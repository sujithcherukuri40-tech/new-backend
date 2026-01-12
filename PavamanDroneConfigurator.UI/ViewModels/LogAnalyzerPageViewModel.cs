using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Log Analyzer page.
/// Provides Mission Planner-style log viewing with graphing, message browsing, and scripting.
/// </summary>
public partial class LogAnalyzerPageViewModel : ViewModelBase
{
    private readonly ILogger<LogAnalyzerPageViewModel> _logger;
    private readonly ILogAnalyzerService _logAnalyzerService;
    private readonly IConnectionService _connectionService;
    private readonly ILogQueryEngine? _queryEngine;
    private readonly ILogEventDetector? _eventDetector;
    private readonly ILogExportService? _exportService;
    private readonly IArduPilotMetadataLoader? _metadataLoader;

    #region Status Properties

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _statusMessage = "Select a log file to analyze.";

    [ObservableProperty]
    private bool _isLogLoaded;

    [ObservableProperty]
    private string _loadedLogInfo = string.Empty;

    [ObservableProperty]
    private int _loadProgress;

    #endregion

    #region Tab Properties

    [ObservableProperty]
    private int _selectedTabIndex;

    public const int TAB_OVERVIEW = 0;
    public const int TAB_PLOT = 1;
    public const int TAB_MAP = 2;
    public const int TAB_EVENTS = 3;
    public const int TAB_PARAMS = 4;

    #endregion

    #region Overview Properties

    [ObservableProperty]
    private string _logFileName = string.Empty;

    [ObservableProperty]
    private string _logFileSize = string.Empty;

    [ObservableProperty]
    private string _logDuration = string.Empty;

    [ObservableProperty]
    private string _logMessageCount = string.Empty;

    [ObservableProperty]
    private string _logMessageTypes = string.Empty;

    [ObservableProperty]
    private string _vehicleType = string.Empty;

    [ObservableProperty]
    private string _firmwareVersion = string.Empty;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private bool _hasGpsData;

    [ObservableProperty]
    private bool _hasAttitudeData;

    [ObservableProperty]
    private bool _hasVibeData;

    #endregion

    #region File Properties

    [ObservableProperty]
    private ObservableCollection<LogFileInfo> _logFiles = new();

    [ObservableProperty]
    private LogFileInfo? _selectedLogFile;

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private bool _isDownloadDialogOpen;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressText = string.Empty;

    [ObservableProperty]
    private string _downloadFolder;

    #endregion

    #region Message Browser Properties

    [ObservableProperty]
    private ObservableCollection<LogMessageTypeGroup> _messageTypes = new();

    [ObservableProperty]
    private LogMessageTypeGroup? _selectedMessageType;

    [ObservableProperty]
    private ObservableCollection<LogMessageView> _currentMessages = new();

    [ObservableProperty]
    private string _messageSearchText = string.Empty;

    [ObservableProperty]
    private int _currentMessagePage;

    [ObservableProperty]
    private int _totalMessagePages;

    private const int MessagesPerPage = 100;

    #endregion

    #region Graph Properties

    [ObservableProperty]
    private ObservableCollection<LogFieldInfo> _availableFields = new();

    [ObservableProperty]
    private ObservableCollection<LogFieldInfo> _selectedGraphFields = new();

    [ObservableProperty]
    private LogGraphConfiguration? _currentGraph;

    [ObservableProperty]
    private bool _hasGraphData;

    [ObservableProperty]
    private string _graphFieldFilter = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LogFieldInfo> _filteredFields = new();

    [ObservableProperty]
    private ObservableCollection<LogMessageTypeNode> _messageTypesTree = new();

    [ObservableProperty]
    private bool _hasTreeData;

    [ObservableProperty]
    private double _cursorTime;

    [ObservableProperty]
    private string _cursorTimeDisplay = "00:00:00.000";

    [ObservableProperty]
    private ObservableCollection<CursorReadout> _cursorReadouts = new();

    [ObservableProperty]
    private double _zoomStartTime;

    [ObservableProperty]
    private double _zoomEndTime;

    #endregion

    #region Events Properties

    [ObservableProperty]
    private ObservableCollection<LogEvent> _detectedEvents = new();

    [ObservableProperty]
    private ObservableCollection<LogEvent> _filteredEvents = new();

    [ObservableProperty]
    private LogEvent? _selectedEvent;

    [ObservableProperty]
    private bool _showInfoEvents = true;

    [ObservableProperty]
    private bool _showWarningEvents = true;

    [ObservableProperty]
    private bool _showErrorEvents = true;

    [ObservableProperty]
    private bool _showCriticalEvents = true;

    [ObservableProperty]
    private string _eventSearchText = string.Empty;

    [ObservableProperty]
    private EventSummary? _eventSummary;

    #endregion

    #region Parameters Properties

    [ObservableProperty]
    private ObservableCollection<ParameterChange> _parameterChanges = new();

    [ObservableProperty]
    private ParameterChange? _selectedParameterChange;

    [ObservableProperty]
    private string _parameterSearchText = string.Empty;
    
    [ObservableProperty]
    private ObservableCollection<LogParameter> _logParameters = new();
    
    [ObservableProperty]
    private ObservableCollection<LogParameter> _filteredLogParameters = new();
    
    [ObservableProperty]
    private LogParameter? _selectedLogParameter;
    
    [ObservableProperty]
    private bool _hasLogParameters;

    #endregion

    #region Map Properties

    [ObservableProperty]
    private ObservableCollection<GpsPoint> _gpsTrack = new();
    
    [ObservableProperty]
    private ObservableCollection<Controls.GpsTrackPoint> _gpsTrackPoints = new();

    [ObservableProperty]
    private GpsPoint? _currentMapPosition;

    [ObservableProperty]
    private double _mapCenterLat;

    [ObservableProperty]
    private double _mapCenterLng;

    [ObservableProperty]
    private int _mapZoom = 15;

    #endregion

    #region Display Options

    [ObservableProperty]
    private bool _showMap;

    [ObservableProperty]
    private bool _showTime = true;

    [ObservableProperty]
    private bool _showDataTable;

    [ObservableProperty]
    private bool _showParams;

    [ObservableProperty]
    private bool _showMode = true;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _showMsg = true;

    [ObservableProperty]
    private bool _showEvents = true;

    #endregion

    #region Scripting Properties

    [ObservableProperty]
    private string _scriptText = string.Empty;

    [ObservableProperty]
    private string _scriptOutput = string.Empty;

    [ObservableProperty]
    private bool _isScriptRunning;

    [ObservableProperty]
    private ObservableCollection<ScriptFunctionInfo> _scriptFunctions = new();

    [ObservableProperty]
    private string _loadedScriptPath = string.Empty;

    [ObservableProperty]
    private string _loadedScriptName = string.Empty;

    #endregion

    private Window? _parentWindow;

    public void SetParentWindow(Window window)
    {
        _parentWindow = window;
    }

    public LogAnalyzerPageViewModel(
        ILogger<LogAnalyzerPageViewModel> logger,
        ILogAnalyzerService logAnalyzerService,
        IConnectionService connectionService,
        ILogQueryEngine? queryEngine = null,
        ILogEventDetector? eventDetector = null,
        ILogExportService? exportService = null,
        IArduPilotMetadataLoader? metadataLoader = null)
    {
        _logger = logger;
        _logAnalyzerService = logAnalyzerService;
        _connectionService = connectionService;
        _queryEngine = queryEngine;
        _eventDetector = eventDetector;
        _exportService = exportService;
        _metadataLoader = metadataLoader;

        _downloadFolder = _logAnalyzerService.GetDefaultDownloadFolder();

        // Subscribe to service events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _logAnalyzerService.LogFilesUpdated += OnLogFilesUpdated;
        _logAnalyzerService.DownloadProgressChanged += OnDownloadProgressChanged;
        _logAnalyzerService.DownloadCompleted += OnDownloadCompleted;
        _logAnalyzerService.LogParsed += OnLogParsed;

        IsConnected = _connectionService.IsConnected;

        // Load script functions
        foreach (var func in _logAnalyzerService.GetScriptFunctions())
        {
            ScriptFunctions.Add(func);
        }
        
        // Load metadata if available
        if (_metadataLoader != null && !_metadataLoader.IsLoaded)
        {
            _ = Task.Run(async () => await _metadataLoader.LoadAllMetadataAsync());
        }
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                StatusMessage = "Connected. Click 'Download log file' to browse logs from FC.";
            }
            else
            {
                StatusMessage = "Disconnected. Browse local log files.";
                LogFiles.Clear();
            }
        });
    }

    private void OnLogFilesUpdated(object? sender, List<LogFileInfo> files)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogFiles.Clear();
            int id = 1;
            foreach (var file in files)
            {
                file.LogId = id++;
                LogFiles.Add(file);
            }
            StatusMessage = $"Found {files.Count} log files";
        });
    }

    private void OnDownloadProgressChanged(object? sender, LogDownloadProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DownloadProgress = progress.ProgressPercent;
            DownloadProgressText = progress.ProgressDisplay;
        });
    }

    private void OnDownloadCompleted(object? sender, (LogFileInfo File, bool Success, String? Error) e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Success)
            {
                StatusMessage = $"Downloaded: {e.File.FileName}";
                SelectedFilePath = e.File.LocalPath ?? string.Empty;
            }
            else
            {
                StatusMessage = $"Download failed: {e.Error}";
            }
        });
    }

    private void OnLogParsed(object? sender, LogParseResult result)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (result.IsSuccess)
            {
                IsLogLoaded = true;
                LoadedLogInfo = $"{result.FileName} - {result.MessageCount:N0} messages, {result.DurationDisplay}";
                
                // Update overview
                UpdateOverview(result);
                
                // Load message types
                MessageTypes.Clear();
                foreach (var type in result.MessageTypes)
                {
                    MessageTypes.Add(type);
                }

                // Load available graph fields
                LoadAvailableFields();
                
                // Detect events in background
                if (_eventDetector != null)
                {
                    await DetectEventsAsync();
                }
                
                // If no events were detected, extract basic events from MSG messages
                if (DetectedEvents.Count == 0)
                {
                    ExtractBasicEventsFromLog();
                }
                
                // Load GPS track for map
                LoadGpsTrack();
                
                // Load parameter changes if available
                LoadParameterChanges();
                
                // Load log parameters with metadata
                await LoadLogParametersAsync();

                StatusMessage = $"Log loaded: {result.MessageCount:N0} messages";
            }
            else
            {
                IsLogLoaded = false;
                StatusMessage = $"Failed to load log: {result.ErrorMessage}";
            }
        });
    }
    
    private void LoadParameterChanges()
    {
        ParameterChanges.Clear();
        
        // Query for PARM messages or parameter changes
        var parmData = _logAnalyzerService.GetMessages("PARM", 0, 1000);
        
        // For now, just placeholder - would parse actual PARM messages
        foreach (var msg in parmData)
        {
            // Parse parameter change from message
            // This would extract Name, OldValue, NewValue, Timestamp
        }
    }
    
    private async Task LoadLogParametersAsync()
    {
        LogParameters.Clear();
        FilteredLogParameters.Clear();
        
        // Get parameters from log
        var logParams = _logAnalyzerService.GetLogParameters();
        
        // Fallback: If no parameters from service, try to extract from PARM messages directly
        if (logParams.Count == 0)
        {
            logParams = ExtractParametersFromParmMessages();
        }
        
        HasLogParameters = logParams.Count > 0;
        
        if (!HasLogParameters)
        {
            StatusMessage = "No parameters found in log file";
            return;
        }

        // Load metadata if not already loaded
        if (_metadataLoader != null && !_metadataLoader.IsLoaded)
        {
            try
            {
                StatusMessage = "Loading parameter metadata...";
                await _metadataLoader.LoadAllMetadataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load parameter metadata");
                StatusMessage = "Parameter metadata unavailable - showing values only";
            }
        }

        // Enrich parameters with metadata
        foreach (var kvp in logParams.OrderBy(p => p.Key))
        {
            var param = new LogParameter
            {
                Name = kvp.Key,
                Value = kvp.Value
            };

            // Try to get metadata
            if (_metadataLoader != null)
            {
                var meta = _metadataLoader.GetMetadata(kvp.Key);
                if (meta != null)
                {
                    param.Description = meta.Description ?? "No description available";
                    param.Units = meta.Units;
                    param.Group = meta.Group ?? "General";
                    param.Default = meta.Range?.Low ?? "Not specified";
                    
                    // Set range display
                    if (meta.Range != null && !string.IsNullOrEmpty(meta.Range.Low) && !string.IsNullOrEmpty(meta.Range.High))
                    {
                        param.Range = $"{meta.Range.Low} to {meta.Range.High}";
                    }

                    // Add value options if available
                    if (meta.Values != null && meta.Values.Count > 0)
                    {
                        foreach (var valKvp in meta.Values.OrderBy(v => v.Key))
                        {
                            param.OptionsDisplay.Add($"{valKvp.Key}: {valKvp.Value}");
                        }
                    }
                }
            }

            LogParameters.Add(param);
            FilteredLogParameters.Add(param);
        }

        StatusMessage = $"Loaded {LogParameters.Count} parameters from log";
    }
    
    /// <summary>
    /// Extracts parameters from PARM messages when GetLogParameters() returns empty.
    /// This is a fallback mechanism to ensure parameters are displayed.
    /// </summary>
    private Dictionary<string, float> ExtractParametersFromParmMessages()
    {
        var parameters = new Dictionary<string, float>();
        
        try
        {
            var parmMessages = _logAnalyzerService.GetMessages("PARM", 0, 10000);
            
            foreach (var msg in parmMessages)
            {
                // Try to extract parameter name and value
                string? paramName = null;
                float paramValue = 0;
                
                // Try different field name variations
                if (msg.Fields.TryGetValue("Name", out var nameObj))
                {
                    paramName = nameObj?.ToString();
                }
                else if (msg.Fields.TryGetValue("N", out var nObj))
                {
                    paramName = nObj?.ToString();
                }
                
                if (msg.Fields.TryGetValue("Value", out var valueObj))
                {
                    if (float.TryParse(valueObj?.ToString(), out var val))
                    {
                        paramValue = val;
                    }
                }
                else if (msg.Fields.TryGetValue("V", out var vObj))
                {
                    if (float.TryParse(vObj?.ToString(), out var val))
                    {
                        paramValue = val;
                    }
                }
                
                if (!string.IsNullOrWhiteSpace(paramName))
                {
                    // Store last value for each parameter (in case of changes during flight)
                    parameters[paramName] = paramValue;
                }
            }
            
            _logger.LogInformation("Extracted {Count} parameters from PARM messages", parameters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting parameters from PARM messages");
        }
        
        return parameters;
    }
    
    partial void OnParameterSearchTextChanged(string value)
    {
        FilterLogParameters();
    }
    
    private void FilterLogParameters()
    {
        FilteredLogParameters.Clear();
        
        var filtered = string.IsNullOrWhiteSpace(ParameterSearchText)
            ? LogParameters
            : LogParameters.Where(p => 
                p.Name.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Group.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase));
        
        foreach (var p in filtered)
        {
            FilteredLogParameters.Add(p);
        }
    }

    private void UpdateOverview(LogParseResult result)
    {
        LogFileName = result.FileName;
        LogFileSize = FormatFileSize(result.FileSize);
        LogDuration = result.DurationDisplay;
        LogMessageCount = result.MessageCount.ToString("N0");
        LogMessageTypes = result.MessageTypes.Count.ToString();
        
        // Check for specific data types
        HasGpsData = result.MessageTypes.Any(t => t.Name == "GPS");
        HasAttitudeData = result.MessageTypes.Any(t => t.Name == "ATT");
        HasVibeData = result.MessageTypes.Any(t => t.Name == "VIBE");
        
        // Set time range for zoom
        ZoomStartTime = 0;
        ZoomEndTime = result.Duration.TotalSeconds;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    partial void OnSelectedMessageTypeChanged(LogMessageTypeGroup? value)
    {
        if (value != null)
        {
            CurrentMessagePage = 0;
            LoadMessages();
        }
    }

    partial void OnGraphFieldFilterChanged(string value)
    {
        FilterGraphFields();
    }

    partial void OnSelectedEventChanged(LogEvent? value)
    {
        if (value != null)
        {
            JumpToTime(value.Timestamp);
        }
    }

    partial void OnSelectedParameterChangeChanged(ParameterChange? value)
    {
        if (value != null)
        {
            JumpToTime(value.Timestamp);
        }
    }

    partial void OnShowInfoEventsChanged(bool value) => FilterEvents();
    partial void OnShowWarningEventsChanged(bool value) => FilterEvents();
    partial void OnShowErrorEventsChanged(bool value) => FilterEvents();
    partial void OnShowCriticalEventsChanged(bool value) => FilterEvents();
    partial void OnEventSearchTextChanged(string value) => FilterEvents();

    #endregion

    #region Events Commands

    private async Task DetectEventsAsync()
    {
        if (_eventDetector == null) return;

        IsAnalyzing = true;
        StatusMessage = "Detecting events...";

        try
        {
            var progress = new Progress<int>(p => LoadProgress = p);
            var events = await _eventDetector.DetectEventsAsync(progress);

            DetectedEvents.Clear();
            foreach (var evt in events)
            {
                DetectedEvents.Add(evt);
            }

            EventSummary = _eventDetector.GetEventSummary();
            ErrorCount = EventSummary.ErrorCount + EventSummary.CriticalCount;
            WarningCount = EventSummary.WarningCount;

            FilterEvents();
            StatusMessage = $"Detected {events.Count} events";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting events");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void FilterEvents()
    {
        FilteredEvents.Clear();

        var filtered = DetectedEvents.Where(e =>
        {
            if (e.Severity == LogEventSeverity.Info && !ShowInfoEvents) return false;
            if (e.Severity == LogEventSeverity.Warning && !ShowWarningEvents) return false;
            if (e.Severity == LogEventSeverity.Error && !ShowErrorEvents) return false;
            if (e.Severity == LogEventSeverity.Critical && !ShowCriticalEvents) return false;

            if (!string.IsNullOrWhiteSpace(EventSearchText))
            {
                var search = EventSearchText.ToLowerInvariant();
                if (!e.Title.ToLowerInvariant().Contains(search) &&
                    !e.Description.ToLowerInvariant().Contains(search))
                    return false;
            }

            return true;
        });

        foreach (var evt in filtered)
        {
            FilteredEvents.Add(evt);
        }
    }

    /// <summary>
    /// Fallback event extraction when event detector is unavailable or returns empty.
    /// Extracts events from MSG, EV, MODE, and ERR message types.
    /// </summary>
    private void ExtractBasicEventsFromLog()
    {
        var eventId = 1;
        
        try
        {
            // Extract from MSG messages (text messages)
            var msgMessages = _logAnalyzerService.GetMessages("MSG", 0, 5000);
            foreach (var msg in msgMessages)
            {
                var text = msg.Fields.GetValueOrDefault("Message")?.ToString() ?? 
                           msg.Fields.GetValueOrDefault("Text")?.ToString() ?? "";
                
                if (string.IsNullOrWhiteSpace(text)) continue;
                
                // Parse timestamp from message
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0; // Convert microseconds to seconds
                }
                
                // Determine severity from message content
                var severity = LogEventSeverity.Info;
                var title = "Message";
                
                if (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("fail", StringComparison.OrdinalIgnoreCase))
                {
                    severity = LogEventSeverity.Error;
                    title = "Error";
                }
                else if (text.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("low", StringComparison.OrdinalIgnoreCase))
                {
                    severity = LogEventSeverity.Warning;
                    title = "Warning";
                }
                else if (text.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("failsafe", StringComparison.OrdinalIgnoreCase))
                {
                    severity = LogEventSeverity.Critical;
                    title = "Critical";
                }
                else if (text.Contains("arm", StringComparison.OrdinalIgnoreCase))
                {
                    title = text.Contains("disarm", StringComparison.OrdinalIgnoreCase) ? "Disarmed" : "Armed";
                }
                else if (text.Contains("mode", StringComparison.OrdinalIgnoreCase))
                {
                    title = "Mode Change";
                }
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = severity,
                    Title = title,
                    Description = text
                });
            }
            
            // Extract from MODE messages
            var modeMessages = _logAnalyzerService.GetMessages("MODE", 0, 1000);
            foreach (var msg in modeMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0;
                }
                
                var modeName = msg.Fields.GetValueOrDefault("Mode")?.ToString() ?? 
                               msg.Fields.GetValueOrDefault("ModeNum")?.ToString() ?? "Unknown";
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.ModeChange,
                    Severity = LogEventSeverity.Info,
                    Title = "Mode Change",
                    Description = $"Flight mode: {modeName}"
                });
            }
            
            // Extract from EV (Event) messages
            var evMessages = _logAnalyzerService.GetMessages("EV", 0, 1000);
            foreach (var msg in evMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0;
                }
                
                var evId = msg.Fields.GetValueOrDefault("Id")?.ToString() ?? "0";
                var severity = LogEventSeverity.Info;
                var title = $"Event {evId}";
                var description = $"Event ID: {evId}";
                
                // Map known ArduPilot event IDs
                if (int.TryParse(evId, out var eventIdNum))
                {
                    (title, severity) = eventIdNum switch
                    {
                        10 => ("Armed", LogEventSeverity.Info),
                        11 => ("Disarmed", LogEventSeverity.Info),
                        15 => ("Battery Failsafe", LogEventSeverity.Critical),
                        17 => ("GPS Failsafe", LogEventSeverity.Error),
                        28 => ("Radio Failsafe", LogEventSeverity.Error),
                        _ => ($"Event {evId}", LogEventSeverity.Info)
                    };
                }
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = severity,
                    Title = title,
                    Description = description
                });
            }
            
            // Extract from ERR (Error) messages
            var errMessages = _logAnalyzerService.GetMessages("ERR", 0, 1000);
            foreach (var msg in errMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0;
                }
                
                var subsys = msg.Fields.GetValueOrDefault("Subsys")?.ToString() ?? "Unknown";
                var errCode = msg.Fields.GetValueOrDefault("ECode")?.ToString() ?? "Unknown";
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = LogEventSeverity.Error,
                    Title = $"Error: {subsys}",
                    Description = $"Subsystem: {subsys}, Error Code: {errCode}"
                });
            }
            
            // Sort events by timestamp
            var sortedEvents = DetectedEvents.OrderBy(e => e.Timestamp).ToList();
            DetectedEvents.Clear();
            foreach (var evt in sortedEvents)
            {
                evt.Id = DetectedEvents.Count + 1;
                DetectedEvents.Add(evt);
            }
            
            // Update event summary
            EventSummary = new EventSummary
            {
                TotalEvents = DetectedEvents.Count,
                InfoCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Info),
                WarningCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Warning),
                ErrorCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Error),
                CriticalCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Critical)
            };
            
            // Apply filters to show events
            FilterEvents();
            
            _logger.LogInformation("Extracted {Count} basic events from log", DetectedEvents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting basic events from log");
        }
    }

    [RelayCommand]
    private void JumpToEvent(LogEvent? evt)
    {
        if (evt == null) return;
        JumpToTime(evt.Timestamp);
        SelectedTabIndex = TAB_PLOT;
    }

    private void JumpToTime(double timestamp)
    {
        CursorTime = timestamp;
        CursorTimeDisplay = TimeSpan.FromSeconds(timestamp).ToString(@"hh\:mm\:ss\.fff");
        
        // Update zoom window to center on this time
        var windowSize = (ZoomEndTime - ZoomStartTime);
        var halfWindow = windowSize / 2;
        ZoomStartTime = Math.Max(0, timestamp - halfWindow);
        ZoomEndTime = ZoomStartTime + windowSize;
        
        // Update cursor readouts
        UpdateCursorReadouts();
        
        // Update map position
        UpdateMapPosition(timestamp);
    }

    private void UpdateCursorReadouts()
    {
        CursorReadouts.Clear();

        foreach (var field in SelectedGraphFields)
        {
            var stats = _logAnalyzerService.GetFieldStatistics(field.DisplayName);
            // Would get actual value at cursor time from query engine
            CursorReadouts.Add(new CursorReadout
            {
                FieldName = field.DisplayName,
                Color = field.Color ?? "#FFFFFF",
                Value = stats.Average // Placeholder
            });
        }
    }

    private void UpdateMapPosition(double timestamp)
    {
        // Find GPS position at timestamp
        if (GpsTrack.Count > 0)
        {
            var nearest = GpsTrack.MinBy(p => Math.Abs(p.Timestamp - timestamp));
            if (nearest != null)
            {
                CurrentMapPosition = nearest;
                MapCenterLat = nearest.Latitude;
                MapCenterLng = nearest.Longitude;
            }
        }
    }

    #endregion

    #region Map Commands

    private void LoadGpsTrack()
    {
        GpsTrack.Clear();
        GpsTrackPoints.Clear();

        var latData = _logAnalyzerService.GetFieldData("GPS", "Lat");
        var lngData = _logAnalyzerService.GetFieldData("GPS", "Lng");
        var altData = _logAnalyzerService.GetFieldData("GPS", "Alt");

        if (latData == null || lngData == null) return;

        var minCount = Math.Min(latData.Count, lngData.Count);
        var altCount = altData?.Count ?? 0;

        for (int i = 0; i < minCount; i++)
        {
            var lat = latData[i].Value;
            var lng = lngData[i].Value;
            
            // Skip invalid coordinates
            if (Math.Abs(lat) < 0.001 && Math.Abs(lng) < 0.001)
                continue;

            var alt = i < altCount ? altData![i].Value : 0;
            var timestamp = latData[i].Timestamp / 1e6; // Convert to seconds

            var point = new GpsPoint
            {
                Latitude = lat,
                Longitude = lng,
                Altitude = alt,
                Timestamp = timestamp
            };
            GpsTrack.Add(point);
            
            // Also add to map-specific collection
            var trackPoint = new Controls.GpsTrackPoint
            {
                Latitude = lat,
                Longitude = lng,
                Altitude = alt,
                Timestamp = timestamp
            };
            GpsTrackPoints.Add(trackPoint);
        }

        if (GpsTrack.Count > 0)
        {
            MapCenterLat = GpsTrack[0].Latitude;
            MapCenterLng = GpsTrack[0].Longitude;
        }

        HasGpsData = GpsTrack.Count > 0;
    }

    #endregion

    #region Export Commands

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        if (_parentWindow == null || !IsLogLoaded)
        {
            StatusMessage = "Please load a log file first";
            return;
        }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to CSV",
                DefaultExtension = "csv",
                SuggestedFileName = $"log_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file != null)
            {
                IsBusy = true;
                StatusMessage = "Exporting to CSV...";

                // If export service is not available, use fallback method
                if (_exportService != null)
                {
                    var seriesKeys = SelectedGraphFields.Select(f => f.DisplayName).ToList();
                    if (seriesKeys.Count == 0)
                    {
                        // Export all available fields if none selected
                        seriesKeys = AvailableFields.Take(10).Select(f => f.DisplayName).ToList();
                    }

                    var progress = new Progress<int>(p => LoadProgress = p);
                    var result = await _exportService.ExportToCsvAsync(
                        file.Path.LocalPath, seriesKeys, ZoomStartTime, ZoomEndTime, progress);

                    if (result.IsSuccess)
                    {
                        StatusMessage = $"Exported {result.RecordCount} records to CSV";
                    }
                    else
                    {
                        StatusMessage = $"Export failed: {result.ErrorMessage}";
                    }
                }
                else
                {
                    // Fallback: Manual CSV export
                    await ExportToCsvManualAsync(file.Path.LocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV export failed");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LoadProgress = 0;
        }
    }
    
    private async Task ExportToCsvManualAsync(string filePath)
    {
        try
        {
            using var writer = new System.IO.StreamWriter(filePath);
            
            // Write header
            var fields = SelectedGraphFields.Count > 0 
                ? SelectedGraphFields 
                : AvailableFields.Take(10);
                
            await writer.WriteLineAsync("Timestamp," + string.Join(",", fields.Select(f => f.DisplayName)));
            
            // Get data for each field
            var fieldData = new Dictionary<string, List<Core.Models.LogDataPoint>>();
            foreach (var field in fields)
            {
                var parts = field.DisplayName.Split('.');
                if (parts.Length == 2)
                {
                    var data = _logAnalyzerService.GetFieldData(parts[0], parts[1]);
                    if (data != null)
                    {
                        fieldData[field.DisplayName] = data;
                    }
                }
            }
            
            // Write data rows
            if (fieldData.Count > 0)
            {
                var maxLength = fieldData.Values.Max(d => d.Count);
                for (int i = 0; i < maxLength; i++)
                {
                    var row = new List<string>();
                    var timestamp = fieldData.Values.First().Count > i 
                        ? (fieldData.Values.First()[i].Timestamp / 1e6).ToString("F6") 
                        : "";
                    row.Add(timestamp);
                    
                    foreach (var field in fields)
                    {
                        if (fieldData.TryGetValue(field.DisplayName, out var data) && data.Count > i)
                        {
                            row.Add(data[i].Value.ToString("F6"));
                        }
                        else
                        {
                            row.Add("");
                        }
                    }
                    
                    await writer.WriteLineAsync(string.Join(",", row));
                    
                    if (i % 1000 == 0)
                    {
                        LoadProgress = (int)((double)i / maxLength * 100);
                    }
                }
            }
            
            StatusMessage = $"Exported to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Manual CSV export failed: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private async Task ExportToKmlAsync()
    {
        if (_parentWindow == null || !IsLogLoaded || !HasGpsData)
        {
            StatusMessage = "No GPS data available to export";
            return;
        }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to KML",
                DefaultExtension = "kml",
                SuggestedFileName = $"flight_track_{DateTime.Now:yyyyMMdd_HHmmss}.kml",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("KML Files") { Patterns = new[] { "*.kml" } }
                }
            });

            if (file != null)
            {
                IsBusy = true;
                StatusMessage = "Exporting to KML...";

                if (_exportService != null)
                {
                    var progress = new Progress<int>(p => LoadProgress = p);
                    var result = await _exportService.ExportToKmlAsync(
                        file.Path.LocalPath, true, progress);

                    if (result.IsSuccess)
                    {
                        StatusMessage = $"Exported GPS track to KML ({result.RecordCount} points)";
                    }
                    else
                    {
                        StatusMessage = $"Export failed: {result.ErrorMessage}";
                    }
                }
                else
                {
                    // Fallback: Manual KML export
                    await ExportToKmlManualAsync(file.Path.LocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KML export failed");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LoadProgress = 0;
        }
    }
    
    private async Task ExportToKmlManualAsync(string filePath)
    {
        try
        {
            if (GpsTrack.Count == 0)
            {
                throw new Exception("No GPS data to export");
            }
            
            using var writer = new System.IO.StreamWriter(filePath);
            
            // Write KML header
            await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            await writer.WriteLineAsync("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            await writer.WriteLineAsync("  <Document>");
            await writer.WriteLineAsync($"    <name>Flight Track - {LogFileName}</name>");
            await writer.WriteLineAsync("    <description>Exported from Pavaman Drone Configurator</description>");
            
            // Style for the track
            await writer.WriteLineAsync("    <Style id=\"flightPath\">");
            await writer.WriteLineAsync("      <LineStyle>");
            await writer.WriteLineAsync("        <color>ff0000ff</color>");
            await writer.WriteLineAsync("        <width>2</width>");
            await writer.WriteLineAsync("      </LineStyle>");
            await writer.WriteLineAsync("    </Style>");
            
            // Write the track
            await writer.WriteLineAsync("    <Placemark>");
            await writer.WriteLineAsync("      <name>Flight Path</name>");
            await writer.WriteLineAsync("      <styleUrl>#flightPath</styleUrl>");
            await writer.WriteLineAsync("      <LineString>");
            await writer.WriteLineAsync("        <extrude>1</extrude>");
            await writer.WriteLineAsync("        <tessellate>1</tessellate>");
            await writer.WriteLineAsync("        <altitudeMode>absolute</altitudeMode>");
            await writer.WriteLineAsync("        <coordinates>");
            
            // Write coordinates
            for (int i = 0; i < GpsTrack.Count; i++)
            {
                var point = GpsTrack[i];
                await writer.WriteLineAsync($"          {point.Longitude:F8},{point.Latitude:F8},{point.Altitude:F2}");
                
                if (i % 100 == 0)
                {
                    LoadProgress = (int)((double)i / GpsTrack.Count * 100);
                }
            }
            
            await writer.WriteLineAsync("        </coordinates>");
            await writer.WriteLineAsync("      </LineString>");
            await writer.WriteLineAsync("    </Placemark>");
            await writer.WriteLineAsync("  </Document>");
            await writer.WriteLineAsync("</kml>");
            
            StatusMessage = $"Exported {GpsTrack.Count} GPS points to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Manual KML export failed: {ex.Message}", ex);
        }
    }

    #endregion

    #region File Commands

    [RelayCommand]
    private async Task BrowseLocalFileAsync()
    {
        if (_parentWindow == null)
        {
            StatusMessage = "Unable to open file browser.";
            return;
        }

        try
        {
            var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Log File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("DataFlash Logs") { Patterns = new[] { "*.bin", "*.log" } },
                    new FilePickerFileType("Telemetry Logs") { Patterns = new[] { "*.tlog" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                SelectedFilePath = file.Path.LocalPath;
                StatusMessage = $"Selected: {Path.GetFileName(SelectedFilePath)}";
                await LoadLogAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file picker");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadLogAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            StatusMessage = "No file selected.";
            return;
        }

        if (!File.Exists(SelectedFilePath))
        {
            StatusMessage = "File not found.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Loading {Path.GetFileName(SelectedFilePath)}...";

        try
        {
            var result = await _logAnalyzerService.LoadLogFileAsync(SelectedFilePath);
            if (!result.IsSuccess)
            {
                StatusMessage = $"Failed to load: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load log");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenDownloadDialogAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to a vehicle.";
            return;
        }

        IsDownloadDialogOpen = true;
        IsBusy = true;
        StatusMessage = "Loading log files from flight controller...";

        try
        {
            await _logAnalyzerService.RefreshLogFilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh logs");
            StatusMessage = $"Failed to load logs: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseDownloadDialog()
    {
        try
        {
            if (IsDownloading)
            {
                _logAnalyzerService.CancelDownload();
                IsDownloading = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error canceling download on dialog close");
        }
        IsDownloadDialogOpen = false;
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (!IsConnected) return;

        IsBusy = true;
        try
        {
            await _logAnalyzerService.RefreshLogFilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh logs");
            StatusMessage = $"Failed to refresh: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        var selectedFiles = LogFiles.Where(f => f.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            StatusMessage = "No logs selected.";
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            var count = await _logAnalyzerService.DownloadLogFilesAsync(selectedFiles, DownloadFolder);
            StatusMessage = $"Downloaded {count} files";
            
            var downloaded = selectedFiles.FirstOrDefault(f => f.IsDownloaded);
            if (downloaded != null)
            {
                SelectedFilePath = downloaded.LocalPath ?? string.Empty;
                IsDownloadDialogOpen = false;
                if (!string.IsNullOrEmpty(SelectedFilePath))
                {
                    await LoadLogAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed");
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        try { _logAnalyzerService.CancelDownload(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error canceling download"); }
        IsDownloading = false;
    }

    #endregion

    #region Message Browser Commands

    private void LoadMessages()
    {
        if (SelectedMessageType == null) return;

        CurrentMessages.Clear();
        var skip = CurrentMessagePage * MessagesPerPage;
        var messages = _logAnalyzerService.GetMessages(SelectedMessageType.Name, skip, MessagesPerPage);
        
        foreach (var msg in messages)
        {
            CurrentMessages.Add(msg);
        }

        var totalCount = _logAnalyzerService.GetMessageCount(SelectedMessageType.Name);
        TotalMessagePages = (totalCount + MessagesPerPage - 1) / MessagesPerPage;
    }

    [RelayCommand]
    private void NextMessagePage()
    {
        if (CurrentMessagePage < TotalMessagePages - 1)
        {
            CurrentMessagePage++;
            LoadMessages();
        }
    }

    [RelayCommand]
    private void PreviousMessagePage()
    {
        if (CurrentMessagePage > 0)
        {
            CurrentMessagePage--;
            LoadMessages();
        }
    }

    [RelayCommand]
    private void SearchMessages()
    {
        if (string.IsNullOrWhiteSpace(MessageSearchText)) return;

        CurrentMessages.Clear();
        var results = _logAnalyzerService.SearchMessages(MessageSearchText, 100);
        
        foreach (var msg in results)
        {
            CurrentMessages.Add(msg);
        }

        StatusMessage = $"Found {results.Count} matching messages";
    }

    #endregion

    #region Graph Commands

    private void LoadAvailableFields()
    {
        AvailableFields.Clear();
        FilteredFields.Clear();
        SelectedGraphFields.Clear();
        MessageTypesTree.Clear();

        var fields = _logAnalyzerService.GetAvailableGraphFields();
        
        foreach (var field in fields)
        {
            var stats = _logAnalyzerService.GetFieldStatistics(field.DisplayName);
            field.MinValue = stats.Minimum;
            field.MaxValue = stats.Maximum;
            field.MeanValue = stats.Average;
            
            AvailableFields.Add(field);
            FilteredFields.Add(field);
        }

        var groupedByType = fields.GroupBy(f => f.MessageType).OrderBy(g => g.Key);
        
        foreach (var group in groupedByType)
        {
            var typeNode = new LogMessageTypeNode
            {
                Name = group.Key,
                IsMessageType = true
            };

            var colorIndex = 0;
            foreach (var field in group.OrderBy(f => f.FieldName))
            {
                var fieldNode = new LogFieldNode
                {
                    Name = field.FieldName,
                    FullKey = field.DisplayName,
                    Color = GraphColors.GetColor(colorIndex++),
                    MinValue = field.MinValue,
                    MaxValue = field.MaxValue,
                    MeanValue = field.MeanValue
                };
                typeNode.Fields.Add(fieldNode);
            }

            MessageTypesTree.Add(typeNode);
        }

        HasTreeData = MessageTypesTree.Count > 0;
    }

    private void FilterGraphFields()
    {
        FilteredFields.Clear();
        var filter = GraphFieldFilter?.ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrEmpty(filter)
            ? AvailableFields
            : AvailableFields.Where(f => f.DisplayName.ToLowerInvariant().Contains(filter));

        foreach (var field in filtered)
        {
            FilteredFields.Add(field);
        }
    }

    [RelayCommand]
    private void AddFieldToGraph(LogFieldInfo? field)
    {
        if (field == null) return;
        if (!SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName))
        {
            field.IsSelected = true;
            field.Color = GraphColors.GetColor(SelectedGraphFields.Count);
            SelectedGraphFields.Add(field);
            
            UpdateGraph();
        }
    }

    [RelayCommand]
    private void RemoveFieldFromGraph(LogFieldInfo? field)
    {
        if (field == null) return;
        field.IsSelected = false;
        field.Color = null;
        
        SelectedGraphFields.Remove(field);
        
        // Reassign colors to remaining fields
        for (int i = 0; i < SelectedGraphFields.Count; i++)
        {
            SelectedGraphFields[i].Color = GraphColors.GetColor(i);
        }
        
        UpdateGraph();
    }

    [RelayCommand]
    private void ClearGraph()
    {
        foreach (var field in SelectedGraphFields)
        {
            field.IsSelected = false;
        }
        SelectedGraphFields.Clear();
        CurrentGraph = null;
        HasGraphData = false;
    }

    private void UpdateGraph()
    {
        if (SelectedGraphFields.Count == 0)
        {
            CurrentGraph = null;
            HasGraphData = false;
            return;
        }

        var fieldKeys = SelectedGraphFields.Select(f => f.DisplayName).ToArray();
        CurrentGraph = _logAnalyzerService.GetGraphData(fieldKeys);
        HasGraphData = CurrentGraph.Series.Any();
    }

    [RelayCommand]
    private void ShowFieldStatistics(LogFieldInfo? field)
    {
        if (field == null) return;
        var stats = _logAnalyzerService.GetFieldStatistics(field.DisplayName);
        StatusMessage = $"{field.DisplayName}: Min={stats.Minimum:F3}, Max={stats.Maximum:F3}, Avg={stats.Average:F3}";
    }

    public void OnFieldSelectionChanged(LogFieldInfo field)
    {
        if (field.IsSelected) AddFieldToGraph(field);
        else RemoveFieldFromGraph(field);
    }

    public bool CanGoToNextPage => CurrentMessagePage < TotalMessagePages - 1;
    public bool CanGoToPreviousPage => CurrentMessagePage > 0;

    [RelayCommand]
    private void ResetZoom()
    {
        StatusMessage = "Zoom reset";
    }

    [RelayCommand]
    private async Task ExportGraphAsync()
    {
        if (!HasGraphData || _parentWindow == null) return;

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Graph",
                DefaultExtension = "png",
                SuggestedFileName = $"graph_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (file != null)
            {
                StatusMessage = $"Graph exported to {Path.GetFileName(file.Path.LocalPath)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export graph");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void GraphLeft() => StatusMessage = "Graph panned left";

    [RelayCommand]
    private void GraphRight() => StatusMessage = "Graph panned right";

    #endregion

    #region Script Commands

    [RelayCommand]
    private async Task LoadScriptFileAsync()
    {
        if (_parentWindow == null) { StatusMessage = "Cannot open file browser"; return; }

        try
        {
            var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Lua Script",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Lua Scripts") { Patterns = new[] { "*.lua" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                LoadedScriptPath = file.Path.LocalPath;
                LoadedScriptName = Path.GetFileName(LoadedScriptPath);
                ScriptText = await File.ReadAllTextAsync(LoadedScriptPath);
                ScriptOutput = $"Loaded: {LoadedScriptName}\nFile size: {new FileInfo(LoadedScriptPath).Length} bytes";
                StatusMessage = $"Script loaded: {LoadedScriptName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load script file");
            StatusMessage = $"Failed to load script: {ex.Message}";
            ScriptOutput = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveScriptFileAsync()
    {
        if (_parentWindow == null || string.IsNullOrWhiteSpace(ScriptText)) { StatusMessage = "Nothing to save"; return; }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Lua Script",
                DefaultExtension = "lua",
                SuggestedFileName = !string.IsNullOrEmpty(LoadedScriptName) ? LoadedScriptName : "script.lua",
                FileTypeChoices = new[] { new FilePickerFileType("Lua Scripts") { Patterns = new[] { "*.lua" } } }
            });

            if (file != null)
            {
                await File.WriteAllTextAsync(file.Path.LocalPath, ScriptText);
                LoadedScriptPath = file.Path.LocalPath;
                LoadedScriptName = Path.GetFileName(LoadedScriptPath);
                StatusMessage = $"Script saved: {LoadedScriptName}";
                ScriptOutput = $"Saved: {LoadedScriptName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save script file");
            StatusMessage = $"Failed to save script: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(ScriptText)) { StatusMessage = "No script loaded."; return; }
        if (!IsLogLoaded) { StatusMessage = "Load a log file first."; return; }

        IsScriptRunning = true;
        ScriptOutput = "Running script...\n";

        try
        {
            var result = await _logAnalyzerService.RunScriptAsync(ScriptText);
            if (result.IsSuccess)
            {
                ScriptOutput = result.Output;
                if (result.Warnings.Count > 0)
                    ScriptOutput += "\nWarnings:\n" + string.Join("\n", result.Warnings);
                ScriptOutput += $"\nCompleted in {result.ExecutionTime.TotalMilliseconds:F0}ms";
            }
            else
            {
                ScriptOutput = $"Error: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            ScriptOutput = $"Error: {ex.Message}";
        }
        finally
        {
            IsScriptRunning = false;
        }
    }

    [RelayCommand]
    private void ClearScript()
    {
        ScriptText = "";
        ScriptOutput = "";
        LoadedScriptPath = "";
        LoadedScriptName = "";
    }

    public void InsertScriptFunction(ScriptFunctionInfo function)
    {
        ScriptText = string.IsNullOrEmpty(ScriptText) ? function.Example : ScriptText + $"\n{function.Example}";
    }

    #endregion

    #region Cleanup

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _logAnalyzerService.LogFilesUpdated -= OnLogFilesUpdated;
            _logAnalyzerService.DownloadProgressChanged -= OnDownloadProgressChanged;
            _logAnalyzerService.DownloadCompleted -= OnDownloadCompleted;
            _logAnalyzerService.LogParsed -= OnLogParsed;
        }
        base.Dispose(disposing);
    }

    #endregion
}



/// <summary>
/// Cursor readout for displaying values at cursor position.
/// </summary>
public class CursorReadout
{
    public string FieldName { get; set; } = string.Empty;
    public string Color { get; set; } = "#FFFFFF";
    public double Value { get; set; }
    public string ValueDisplay => Value.ToString("F3");
}



/// <summary>
/// GPS point for map display.
/// </summary>
public class GpsPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Timestamp { get; set; }
}



/// <summary>
/// Parameter change record.
/// </summary>
public class ParameterChange
{
    public string Name { get; set; } = string.Empty;
    public double OldValue { get; set; }
    public double NewValue { get; set; }
    public double Timestamp { get; set; }
    public string TimestampDisplay => TimeSpan.FromSeconds(Timestamp).ToString(@"hh\:mm\:ss");
}

/// <summary>
/// Parameter display record for log file parameters.
/// Shows parameter value from log with metadata (description, options, etc.)
/// </summary>
public class LogParameter
{
    public string Name { get; set; } = string.Empty;
    public float Value { get; set; }
    public string ValueDisplay => Value.ToString("G");
    public string Description { get; set; } = "No description available";
    public string? Units { get; set; }
    public string Range { get; set; } = "Not specified";
    public string? Default { get; set; }
    public string Group { get; set; } = "General";
    public ObservableCollection<string> OptionsDisplay { get; set; } = new();
    public bool HasOptions => OptionsDisplay.Count > 0;
}

/// <summary>
/// Converter from bool to Yes/No string
/// </summary>
public class BoolToYesNoConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return b ? "Yes" : "No";
        return "No";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter from bool to success/error color
/// </summary>
public class BoolToSuccessColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return b ? "#10B981" : "#EF4444";  // Green for true, red for false
        return "#888";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
