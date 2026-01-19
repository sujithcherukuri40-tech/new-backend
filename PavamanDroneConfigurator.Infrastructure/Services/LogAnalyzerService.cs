using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Logging;
using PavamanDroneConfigurator.Infrastructure.MAVLink;
using System.Diagnostics;
using System.Text;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service for browsing, downloading, and analyzing flight logs from the flight controller.
/// Implements Mission Planner-style log viewing with graphing and message browsing.
/// </summary>
public sealed class LogAnalyzerService : ILogAnalyzerService, IDisposable
{
    private readonly ILogger<LogAnalyzerService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly ILogEventDetector? _eventDetector;
    
    private MavFtpClient? _ftpClient;
    private CancellationTokenSource? _downloadCts;
    private readonly List<LogFileInfo> _logFiles = new();
    private bool _isDownloading;
    private bool _isAnalyzing;
    private bool _disposed;
    
    // DataFlash log parser for current loaded log
    private DataFlashLogParser? _parser;
    private ParsedLog? _currentLog;
    
    // Common log directories on ArduPilot flight controllers
    private static readonly string[] LogDirectories = new[]
    {
        "/APM/LOGS",
        "/APM/logs",
        "@SYS/logs",
        "/LOGS",
        "/logs"
    };

    public event EventHandler<List<LogFileInfo>>? LogFilesUpdated;
    public event EventHandler<LogDownloadProgress>? DownloadProgressChanged;
    public event EventHandler<(LogFileInfo File, bool Success, string? Error)>? DownloadCompleted;
    public event EventHandler<LogAnalysisResult>? AnalysisCompleted;
    public event EventHandler<LogParseResult>? LogParsed;

    public bool IsDownloading => _isDownloading;
    public bool IsAnalyzing => _isAnalyzing;
    public bool IsFtpAvailable => _connectionService.IsConnected;
    public bool IsLogLoaded => _currentLog != null && _currentLog.IsSuccess;
    public string? LoadedLogPath => _currentLog?.FilePath;

    public LogAnalyzerService(
        ILogger<LogAnalyzerService> logger,
        IConnectionService connectionService,
        ILogEventDetector? eventDetector = null)
    {
        _logger = logger;
        _connectionService = connectionService;
        _eventDetector = eventDetector;
        
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            _ftpClient?.Dispose();
            _ftpClient = null;
            _logFiles.Clear();
        }
    }

    private void EnsureFtpClient()
    {
        if (_ftpClient != null) return;
        
        var stream = _connectionService.GetTransportStream();
        if (stream == null)
        {
            throw new InvalidOperationException("No connection available for FTP operations");
        }

        _ftpClient = new MavFtpClient(_logger);
        _ftpClient.Initialize(stream, stream);
        _ftpClient.ProgressChanged += OnFtpProgressChanged;
    }

    private void OnFtpProgressChanged(object? sender, (long BytesDownloaded, long TotalBytes) e)
    {
        var progress = new LogDownloadProgress
        {
            BytesDownloaded = e.BytesDownloaded,
            TotalBytes = e.TotalBytes
        };
        DownloadProgressChanged?.Invoke(this, progress);
    }

    #region FC Log File Operations

    public async Task<List<LogFileInfo>> GetLogFilesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_logFiles.ToList());
    }

    public async Task RefreshLogFilesAsync(CancellationToken cancellationToken = default)
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot refresh logs - not connected");
            return;
        }

        try
        {
            EnsureFtpClient();
            
            _logFiles.Clear();
            _logger.LogInformation("Refreshing log file list from flight controller");

            foreach (var dir in LogDirectories)
            {
                try
                {
                    var entries = await _ftpClient!.ListDirectoryAsync(dir, cancellationToken);
                    
                    foreach (var entry in entries.Where(e => !e.IsDirectory))
                    {
                        var logFile = CreateLogFileInfo(entry);
                        if (logFile != null)
                        {
                            _logFiles.Add(logFile);
                        }
                    }

                    if (_logFiles.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} log files in {Dir}", _logFiles.Count, dir);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Directory {Dir} not accessible: {Error}", dir, ex.Message);
                }
            }

            _logFiles.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.Ordinal));

            LogFilesUpdated?.Invoke(this, _logFiles.ToList());
            _logger.LogInformation("Log refresh complete: {Count} files found", _logFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh log files");
            throw;
        }
    }

    private LogFileInfo? CreateLogFileInfo(FtpDirectoryEntry entry)
    {
        var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
        
        var fileType = ext switch
        {
            ".bin" => LogFileType.DataFlashLog,
            ".log" => LogFileType.DataFlashLog,
            ".tlog" => LogFileType.TelemetryLog,
            ".param" => LogFileType.ParamFile,
            ".lua" => LogFileType.ScriptFile,
            ".waypoints" => LogFileType.WaypointFile,
            _ => LogFileType.Unknown
        };

        if (fileType == LogFileType.Unknown)
            return null;

        return new LogFileInfo
        {
            FileName = entry.Name,
            FilePath = entry.FullPath,
            FileSize = entry.Size,
            FileType = fileType,
            DownloadStatus = LogDownloadStatus.Pending
        };
    }

    public async Task<bool> DownloadLogFileAsync(LogFileInfo logFile, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot download - not connected");
            return false;
        }

        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isDownloading = true;

        try
        {
            EnsureFtpClient();
            
            logFile.DownloadStatus = LogDownloadStatus.Downloading;
            logFile.DownloadProgress = 0;

            var progress = new LogDownloadProgress
            {
                CurrentFile = logFile.FileName,
                TotalBytes = logFile.FileSize
            };
            
            var startTime = DateTime.UtcNow;
            
            _ftpClient!.ProgressChanged += (s, e) =>
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                progress.BytesDownloaded = e.BytesDownloaded;
                progress.BytesPerSecond = elapsed > 0 ? (long)(e.BytesDownloaded / elapsed) : 0;
                
                if (progress.BytesPerSecond > 0)
                {
                    var remaining = (progress.TotalBytes - progress.BytesDownloaded) / progress.BytesPerSecond;
                    progress.EstimatedTimeRemaining = TimeSpan.FromSeconds(remaining);
                }

                logFile.DownloadProgress = progress.ProgressPercent;
                DownloadProgressChanged?.Invoke(this, progress);
            };

            var success = await _ftpClient.DownloadFileAsync(logFile.FilePath, destinationPath, _downloadCts.Token);

            if (success)
            {
                logFile.DownloadStatus = LogDownloadStatus.Completed;
                logFile.LocalPath = destinationPath;
                logFile.DownloadProgress = 100;
                _logger.LogInformation("Downloaded: {File}", logFile.FileName);
            }
            else
            {
                logFile.DownloadStatus = LogDownloadStatus.Failed;
                _logger.LogWarning("Download failed: {File}", logFile.FileName);
            }

            DownloadCompleted?.Invoke(this, (logFile, success, success ? null : "Download failed"));
            return success;
        }
        catch (OperationCanceledException)
        {
            logFile.DownloadStatus = LogDownloadStatus.Cancelled;
            DownloadCompleted?.Invoke(this, (logFile, false, "Cancelled"));
            return false;
        }
        catch (Exception ex)
        {
            logFile.DownloadStatus = LogDownloadStatus.Failed;
            _logger.LogError(ex, "Error downloading {File}", logFile.FileName);
            DownloadCompleted?.Invoke(this, (logFile, false, ex.Message));
            return false;
        }
        finally
        {
            _isDownloading = false;
        }
    }

    public async Task<int> DownloadLogFilesAsync(IEnumerable<LogFileInfo> logFiles, string destinationFolder, CancellationToken cancellationToken = default)
    {
        var files = logFiles.ToList();
        int successCount = 0;

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var destPath = Path.Combine(destinationFolder, file.FileName);
            if (await DownloadLogFileAsync(file, destPath, cancellationToken))
            {
                successCount++;
            }
        }

        return successCount;
    }

    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    public async Task<bool> DeleteLogFileAsync(LogFileInfo logFile, CancellationToken cancellationToken = default)
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot delete - not connected");
            return false;
        }

        try
        {
            EnsureFtpClient();
            
            var success = await _ftpClient!.DeleteFileAsync(logFile.FilePath, cancellationToken);
            
            if (success)
            {
                _logFiles.Remove(logFile);
                LogFilesUpdated?.Invoke(this, _logFiles.ToList());
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting {File}", logFile.FileName);
            return false;
        }
    }

    public async Task<int> DeleteAllLogFilesAsync(CancellationToken cancellationToken = default)
    {
        var files = _logFiles.ToList();
        int deleteCount = 0;

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (await DeleteLogFileAsync(file, cancellationToken))
            {
                deleteCount++;
            }
        }

        return deleteCount;
    }

    public async Task<List<FtpDirectoryEntry>> GetDirectoryListingAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_connectionService.IsConnected)
        {
            return new List<FtpDirectoryEntry>();
        }

        try
        {
            EnsureFtpClient();
            return await _ftpClient!.ListDirectoryAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list directory: {Path}", path);
            return new List<FtpDirectoryEntry>();
        }
    }

    #endregion

    #region Log Parsing and Loading

    public async Task<LogParseResult> LoadLogFileAsync(string logFilePath, CancellationToken cancellationToken = default)
    {
        var result = new LogParseResult
        {
            FilePath = logFilePath,
            FileName = Path.GetFileName(logFilePath)
        };

        try
        {
            _logger.LogInformation("Loading log file: {Path}", logFilePath);

            if (!File.Exists(logFilePath))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Log file not found";
                return result;
            }

            result.FileSize = new FileInfo(logFilePath).Length;

            // Detect log file type automatically
            var typeDetector = new LogFileTypeDetector();
            var detectionResult = typeDetector.DetectFileType(logFilePath);
            
            _logger.LogInformation("Detected log format: {Format} (confidence: {Confidence}%)", 
                detectionResult.Format, detectionResult.Confidence);

            // Create parser and parse the file based on detected format
            _parser = new DataFlashLogParser();
            _currentLog = await _parser.ParseAsync(logFilePath, cancellationToken);

            if (!_currentLog.IsSuccess)
            {
                result.IsSuccess = false;
                result.ErrorMessage = _currentLog.ErrorMessage ?? "Failed to parse log file";
                return result;
            }

            result.IsSuccess = true;
            result.MessageCount = _currentLog.MessageCount;
            result.MessageTypeCount = _currentLog.UniqueMessageTypes;
            result.Duration = _currentLog.Duration;
            result.Parameters = _currentLog.Parameters;

            // Set log data for event detector
            if (_eventDetector is LogEventDetector detector)
            {
                detector.SetLogData(_parser, _currentLog);
            }

            // Build message type groups
            foreach (var format in _currentLog.Formats)
            {
                var group = new LogMessageTypeGroup
                {
                    Name = format.Name,
                    MessageCount = _parser.GetMessages(format.Name).Count
                };

                foreach (var fieldName in format.FieldNames)
                {
                    var seriesKey = $"{format.Name}.{fieldName}";
                    if (_currentLog.DataSeries.TryGetValue(seriesKey, out var series))
                    {
                        group.Fields.Add(new LogFieldInfo
                        {
                            MessageType = format.Name,
                            FieldName = fieldName,
                            DataPointCount = series.Count
                        });
                    }
                }

                if (group.Fields.Count > 0)
                {
                    result.MessageTypes.Add(group);
                }
            }

            _logger.LogInformation("Loaded log: {Count} messages, {Types} types, {Duration}",
                result.MessageCount, result.MessageTypeCount, result.DurationDisplay);

            LogParsed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load log file");
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public void UnloadLog()
    {
        _parser = null;
        _currentLog = null;
        _logger.LogInformation("Log unloaded");
    }

    #endregion

    #region Message Browsing

    public List<LogMessageTypeGroup> GetMessageTypes()
    {
        if (_parser == null || _currentLog == null)
            return new List<LogMessageTypeGroup>();

        var groups = new List<LogMessageTypeGroup>();

        foreach (var typeName in _parser.GetMessageTypes())
        {
            var messages = _parser.GetMessages(typeName);
            if (messages.Count == 0) continue;

            var format = _currentLog.Formats.FirstOrDefault(f => f.Name == typeName);
            if (format == null) continue;

            var group = new LogMessageTypeGroup
            {
                Name = typeName,
                MessageCount = messages.Count
            };

            foreach (var fieldName in format.FieldNames)
            {
                var seriesKey = $"{typeName}.{fieldName}";
                if (_currentLog.DataSeries.TryGetValue(seriesKey, out var series))
                {
                    group.Fields.Add(new LogFieldInfo
                    {
                        MessageType = typeName,
                        FieldName = fieldName,
                        DataPointCount = series.Count
                    });
                }
            }

            groups.Add(group);
        }

        return groups.OrderBy(g => g.Name).ToList();
    }

    public List<LogMessageView> GetMessages(string messageType, int skip = 0, int take = 1000)
    {
        if (_parser == null)
            return new List<LogMessageView>();

        var messages = _parser.GetMessages(messageType);
        
        return messages
            .Skip(skip)
            .Take(take)
            .Select((m, i) => new LogMessageView
            {
                Index = skip + i,
                TypeName = m.TypeName,
                Timestamp = m.TimestampDisplay,
                Fields = m.Fields.ToDictionary(
                    f => f.Key,
                    f => f.Value?.ToString() ?? "")
            })
            .ToList();
    }

    public int GetMessageCount(string messageType)
    {
        if (_parser == null)
            return 0;

        return _parser.GetMessages(messageType).Count;
    }

    public List<LogMessageView> SearchMessages(string searchText, int maxResults = 100)
    {
        if (_parser == null || string.IsNullOrWhiteSpace(searchText))
            return new List<LogMessageView>();

        var results = new List<LogMessageView>();
        var searchLower = searchText.ToLowerInvariant();

        foreach (var msg in _parser.Messages)
        {
            if (results.Count >= maxResults)
                break;

            // Search in type name
            if (msg.TypeName.ToLowerInvariant().Contains(searchLower))
            {
                results.Add(CreateMessageView(msg, results.Count));
                continue;
            }

            // Search in field values
            foreach (var field in msg.Fields)
            {
                var valueStr = field.Value?.ToString()?.ToLowerInvariant() ?? "";
                if (field.Key.ToLowerInvariant().Contains(searchLower) ||
                    valueStr.Contains(searchLower))
                {
                    results.Add(CreateMessageView(msg, results.Count));
                    break;
                }
            }
        }

        return results;
    }

    private LogMessageView CreateMessageView(LogMessage msg, int index)
    {
        return new LogMessageView
        {
            Index = index,
            TypeName = msg.TypeName,
            Timestamp = msg.TimestampDisplay,
            Fields = msg.Fields.ToDictionary(
                f => f.Key,
                f => f.Value?.ToString() ?? "")
        };
    }

    #endregion

    #region Graphing

    public List<LogFieldInfo> GetAvailableGraphFields()
    {
        if (_parser == null || _currentLog == null)
            return new List<LogFieldInfo>();

        var fields = new List<LogFieldInfo>();

        foreach (var seriesKey in _parser.GetAvailableDataSeries())
        {
            var parts = seriesKey.Split('.');
            if (parts.Length != 2) continue;

            if (_currentLog.DataSeries.TryGetValue(seriesKey, out var series))
            {
                fields.Add(new LogFieldInfo
                {
                    MessageType = parts[0],
                    FieldName = parts[1],
                    DataPointCount = series.Count
                });
            }
        }

        return fields.OrderBy(f => f.DisplayName).ToList();
    }

    public LogGraphConfiguration GetGraphData(params string[] fieldKeys)
    {
        var config = new LogGraphConfiguration();
        
        if (_parser == null || _currentLog == null || fieldKeys.Length == 0)
            return config;

        int colorIndex = 0;
        foreach (var key in fieldKeys)
        {
            if (!_currentLog.DataSeries.TryGetValue(key, out var dataPoints))
                continue;

            var series = new LogGraphSeries
            {
                Name = key,
                MessageType = key.Split('.').FirstOrDefault() ?? "",
                FieldName = key.Split('.').LastOrDefault() ?? "",
                Color = GraphColors.GetColor(colorIndex++),
                Points = dataPoints.Select(p => new GraphPoint(
                    p.Timestamp / 1_000_000.0, // Convert microseconds to seconds
                    p.Value
                )).ToList()
            };

            config.Series.Add(series);
        }

        config.Title = fieldKeys.Length == 1 ? fieldKeys[0] : $"{fieldKeys.Length} series";

        return config;
    }

    public LogGraphConfiguration GetGraphData(double startTime, double endTime, params string[] fieldKeys)
    {
        var fullData = GetGraphData(fieldKeys);

        // Filter points by time range
        foreach (var series in fullData.Series)
        {
            series.Points = series.Points
                .Where(p => p.X >= startTime && p.X <= endTime)
                .ToList();
        }

        return fullData;
    }

    public LogFieldStatistics GetFieldStatistics(string fieldKey)
    {
        var stats = new LogFieldStatistics { FieldKey = fieldKey };

        if (_currentLog == null || !_currentLog.DataSeries.TryGetValue(fieldKey, out var dataPoints))
            return stats;

        if (dataPoints.Count == 0)
            return stats;

        var values = dataPoints.Select(p => p.Value).ToList();
        values.Sort();

        stats.SampleCount = values.Count;
        stats.Minimum = values.First();
        stats.Maximum = values.Last();
        stats.Average = values.Average();
        stats.Median = values[values.Count / 2];

        // Standard deviation
        var avg = stats.Average;
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        stats.StandardDeviation = Math.Sqrt(sumSquares / values.Count);

        return stats;
    }
    
    public List<LogDataPoint>? GetFieldData(string messageType, string fieldName)
    {
        if (_parser == null)
            return null;
            
        return _parser.GetDataSeries(messageType, fieldName);
    }

    #endregion

    #region Parameters from Log

    public Dictionary<string, float> GetLogParameters()
    {
        return _currentLog?.Parameters ?? new Dictionary<string, float>();
    }

    #endregion

    #region Scripting

    public async Task<ScriptExecutionResult> RunScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        var result = new ScriptExecutionResult();
        var sw = Stopwatch.StartNew();

        try
        {
            if (_parser == null || _currentLog == null)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "No log file loaded. Load a log file first.";
                return result;
            }

            // Simple script interpreter
            var output = new StringBuilder();
            var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#") || trimmedLine.StartsWith("//"))
                    continue;

                try
                {
                    var lineResult = ExecuteScriptLine(trimmedLine);
                    if (!string.IsNullOrEmpty(lineResult))
                    {
                        output.AppendLine(lineResult);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Line error: {trimmedLine} - {ex.Message}");
                }
            }

            result.IsSuccess = true;
            result.Output = output.ToString();
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.ExecutionTime = sw.Elapsed;
        }

        return await Task.FromResult(result);
    }

    private string ExecuteScriptLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";

        var command = parts[0].ToUpperInvariant();

        switch (command)
        {
            case "INFO":
                return $"Log: {_currentLog?.FileName}, Messages: {_currentLog?.MessageCount}, Duration: {_currentLog?.DurationDisplay}";

            case "TYPES":
                var types = _parser?.GetMessageTypes();
                return types != null && types.Count > 0 ? string.Join(", ", types) : "No message types found";

            case "COUNT":
                if (parts.Length > 1)
                {
                    var count = GetMessageCount(parts[1]);
                    return $"{parts[1]}: {count} messages";
                }
                return "Usage: COUNT <MessageType>";

            case "STATS":
                if (parts.Length > 1)
                {
                    var stats = GetFieldStatistics(parts[1]);
                    return $"{parts[1]}: Min={stats.Minimum:F3}, Max={stats.Maximum:F3}, Avg={stats.Average:F3}, StdDev={stats.StandardDeviation:F3}";
                }
                return "Usage: STATS <MessageType.Field>";

            case "PARAMS":
                var paramsList = GetLogParameters().Take(20);
                return string.Join("\n", paramsList.Select(p => $"{p.Key}={p.Value}"));

            case "PRINT":
                if (parts.Length > 1)
                {
                    var messages = GetMessages(parts[1], 0, 10);
                    return string.Join("\n", messages.Select(m => m.FieldsDisplay));
                }
                return "Usage: PRINT <MessageType>";

            default:
                return $"Unknown command: {command}";
        }
    }

    public List<ScriptFunctionInfo> GetScriptFunctions()
    {
        return new List<ScriptFunctionInfo>
        {
            new() { Name = "INFO", Description = "Show log file information", Signature = "INFO", Example = "INFO" },
            new() { Name = "TYPES", Description = "List all message types", Signature = "TYPES", Example = "TYPES" },
            new() { Name = "COUNT", Description = "Count messages of a type", Signature = "COUNT <MessageType>", Example = "COUNT GPS" },
            new() { Name = "STATS", Description = "Show statistics for a field", Signature = "STATS <MessageType.Field>", Example = "STATS GPS.Alt" },
            new() { Name = "PARAMS", Description = "Show parameters from log", Signature = "PARAMS", Example = "PARAMS" },
            new() { Name = "PRINT", Description = "Print first 10 messages of a type", Signature = "PRINT <MessageType>", Example = "PRINT ATT" }
        };
    }

    #endregion

    #region Legacy Analysis

    public async Task<LogAnalysisResult> AnalyzeLogFileAsync(string logFilePath, CancellationToken cancellationToken = default)
    {
        _isAnalyzing = true;
        var result = new LogAnalysisResult
        {
            LogFilePath = logFilePath,
            LogFileName = Path.GetFileName(logFilePath),
            AnalysisTime = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Analyzing log file: {Path}", logFilePath);

            // First load the log properly
            var parseResult = await LoadLogFileAsync(logFilePath, cancellationToken);
            
            if (!parseResult.IsSuccess)
            {
                result.IsSuccess = false;
                result.ErrorMessage = parseResult.ErrorMessage ?? "Failed to parse log";
                return result;
            }

            result.IsSuccess = true;
            result.Summary = new FlightSummary
            {
                FlightDuration = parseResult.Duration
            };

            // Add info about log content
            result.Issues.Add(new LogAnalysisIssue
            {
                Severity = LogMessageSeverity.Info,
                Category = LogAnalysisCategory.General,
                Title = "Log Loaded Successfully",
                Description = $"Parsed {parseResult.MessageCount:N0} messages, {parseResult.MessageTypeCount} types. Duration: {parseResult.DurationDisplay}"
            });

            // Calculate health score based on actual data
            result.HealthScore = 85;

            // Check for GPS quality
            var gpsStats = GetFieldStatistics("GPS.NSats");
            if (gpsStats.SampleCount > 0)
            {
                if (gpsStats.Minimum < 6)
                {
                    result.Issues.Add(new LogAnalysisIssue
                    {
                        Severity = LogMessageSeverity.Warning,
                        Category = LogAnalysisCategory.GpsSignal,
                        Title = "Low GPS Satellite Count",
                        Description = $"Minimum satellites: {gpsStats.Minimum:F0}. Average: {gpsStats.Average:F1}",
                        Suggestion = "Ensure clear sky view for GPS antenna"
                    });
                    result.HealthScore -= 10;
                }
            }

            // Check for vibration issues
            var vibX = GetFieldStatistics("VIBE.VibeX");
            var vibY = GetFieldStatistics("VIBE.VibeY");
            var vibZ = GetFieldStatistics("VIBE.VibeZ");
            
            if (vibX.SampleCount > 0 && (vibX.Maximum > 60 || vibY.Maximum > 60 || vibZ.Maximum > 60))
            {
                result.Issues.Add(new LogAnalysisIssue
                {
                    Severity = LogMessageSeverity.Warning,
                    Category = LogAnalysisCategory.Vibration,
                    Title = "High Vibration Detected",
                    Description = $"Max vibration - X: {vibX.Maximum:F1}, Y: {vibY.Maximum:F1}, Z: {vibZ.Maximum:F1}",
                    Suggestion = "Check propeller balance and motor mounts"
                });
                result.HealthScore -= 15;
            }

            // Check battery voltage
            var battV = GetFieldStatistics("BAT.Volt");
            if (battV.SampleCount > 0 && battV.Minimum < 10.5)
            {
                result.Issues.Add(new LogAnalysisIssue
                {
                    Severity = LogMessageSeverity.Warning,
                    Category = LogAnalysisCategory.BatteryVoltage,
                    Title = "Low Battery Voltage",
                    Description = $"Minimum voltage: {battV.Minimum:F2}V",
                    Suggestion = "Check battery health and capacity"
                });
                result.HealthScore -= 10;
            }

            // Check for errors in ERR messages
            var errCount = GetMessageCount("ERR");
            if (errCount > 0)
            {
                result.Issues.Add(new LogAnalysisIssue
                {
                    Severity = LogMessageSeverity.Warning,
                    Category = LogAnalysisCategory.General,
                    Title = "Error Messages Found",
                    Description = $"Found {errCount} error messages in the log",
                    Suggestion = "Review error messages for details"
                });
                result.HealthScore -= 5;
            }

            result.HealthScore = Math.Max(0, Math.Min(100, result.HealthScore));

            AnalysisCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing log file");
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            _isAnalyzing = false;
        }

        return result;
    }

    #endregion

    #region Utility

    public void OpenLogFile(string logFilePath)
    {
        try
        {
            if (File.Exists(logFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logFilePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log file");
        }
    }

    public void OpenLogFolder(string logFilePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder");
        }
    }

    public string GetDefaultDownloadFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PavamanDroneConfigurator",
            "Logs"
        );
        
        Directory.CreateDirectory(folder);
        return folder;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _ftpClient?.Dispose();
    }
}
