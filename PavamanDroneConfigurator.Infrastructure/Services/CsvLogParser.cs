using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Parser for CSV exported drone flight logs.
/// Supports generic CSV files with common drone telemetry columns.
/// </summary>
public class CsvLogParser
{
    private readonly ILogger<CsvLogParser>? _logger;

    // Common column names for timestamp detection
    private static readonly string[] TimeColumnNames = 
    { 
        "time", "timestamp", "time_us", "timeus", "time_ms", "timems", 
        "datetime", "date_time", "t", "ts", "epoch", "utc_time"
    };

    // Common column patterns for event detection
    private static readonly string[] EventColumnPatterns =
    {
        "mode", "event", "message", "msg", "status", "error", "warning",
        "failsafe", "arm", "disarm", "alert", "notice"
    };

    private readonly List<LogMessage> _messages = new();
    private readonly Dictionary<string, List<LogDataPoint>> _dataSeries = new();
    private readonly List<string> _columnNames = new();

    public CsvLogParser(ILogger<CsvLogParser>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets all parsed messages (one per CSV row).
    /// </summary>
    public IReadOnlyList<LogMessage> Messages => _messages;

    /// <summary>
    /// Gets all data series for graphing.
    /// </summary>
    public IReadOnlyDictionary<string, List<LogDataPoint>> DataSeries => _dataSeries;

    /// <summary>
    /// Gets the column names from the CSV header.
    /// </summary>
    public IReadOnlyList<string> ColumnNames => _columnNames;

    /// <summary>
    /// Parse a CSV log file.
    /// </summary>
    public async Task<ParsedLog> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("CSV file not found", filePath);

        _messages.Clear();
        _dataSeries.Clear();
        _columnNames.Clear();

        var fileInfo = new FileInfo(filePath);
        var result = new ParsedLog
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileSize = fileInfo.Length,
            ParseTime = DateTime.UtcNow,
            IsTextFormat = true
        };

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            
            if (lines.Length == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "CSV file is empty";
                return result;
            }

            // Parse header
            var header = ParseCsvLine(lines[0]);
            _columnNames.AddRange(header);

            // Find time column index
            int timeColumnIndex = FindTimeColumnIndex(header);
            double baseTimestamp = 0;

            // Parse data rows
            for (int i = 1; i < lines.Length && !cancellationToken.IsCancellationRequested; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                try
                {
                    var values = ParseCsvLine(lines[i]);
                    var msg = CreateLogMessage(header, values, i - 1, timeColumnIndex, ref baseTimestamp);
                    if (msg != null)
                    {
                        _messages.Add(msg);
                    }
                }
                catch
                {
                    // Skip malformed rows
                    _logger?.LogWarning("Skipping malformed CSV row {RowNumber}", i);
                }
            }

            // Build data series from messages
            BuildDataSeries(header);

            result.IsSuccess = true;
            result.Messages = _messages;
            result.DataSeries = _dataSeries;
            result.MessageCount = _messages.Count;
            result.UniqueMessageTypes = 1; // CSV typically has one "type" per file

            // Calculate duration
            if (_messages.Count > 0)
            {
                result.StartTime = _messages.First().Timestamp;
                result.EndTime = _messages.Last().Timestamp;
                result.Duration = result.EndTime - result.StartTime;
            }

            _logger?.LogInformation("Parsed {Count} rows from CSV file {File}",
                result.MessageCount, result.FileName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse CSV file: {File}", filePath);
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString().Trim());

        return values.ToArray();
    }

    private static int FindTimeColumnIndex(string[] header)
    {
        for (int i = 0; i < header.Length; i++)
        {
            var colName = header[i].ToLowerInvariant();
            foreach (var timeName in TimeColumnNames)
            {
                if (colName.Contains(timeName))
                    return i;
            }
        }
        return -1;
    }

    private LogMessage? CreateLogMessage(string[] header, string[] values, int rowIndex, int timeColumnIndex, ref double baseTimestamp)
    {
        if (values.Length == 0)
            return null;

        var msg = new LogMessage
        {
            Type = 0,
            TypeName = "CSV",
            LineNumber = rowIndex,
            Fields = new Dictionary<string, object>()
        };

        // Parse timestamp
        double timestamp = 0;
        if (timeColumnIndex >= 0 && timeColumnIndex < values.Length)
        {
            var timeStr = values[timeColumnIndex];
            timestamp = ParseTimestamp(timeStr, baseTimestamp);
            
            if (rowIndex == 0)
                baseTimestamp = timestamp;
        }
        else
        {
            // Use row index as pseudo-timestamp
            timestamp = rowIndex * 0.1; // 100ms per row estimate
        }

        msg.Timestamp = TimeSpan.FromSeconds(timestamp - baseTimestamp);

        // Add all columns as fields
        for (int i = 0; i < Math.Min(header.Length, values.Length); i++)
        {
            var colName = header[i];
            var valueStr = values[i];

            // Try to parse as number
            if (double.TryParse(valueStr, out var numValue))
            {
                msg.Fields[colName] = numValue;
            }
            else
            {
                msg.Fields[colName] = valueStr;
            }
        }

        return msg;
    }

    private static double ParseTimestamp(string timeStr, double baseTimestamp)
    {
        // Try various timestamp formats
        
        // Unix timestamp in seconds
        if (double.TryParse(timeStr, out var unixTimestamp))
        {
            // Check if it's in milliseconds (very large number)
            if (unixTimestamp > 1e12)
                return unixTimestamp / 1000.0;
            // Microseconds
            if (unixTimestamp > 1e15)
                return unixTimestamp / 1e6;
            return unixTimestamp;
        }

        // ISO datetime format
        if (DateTime.TryParse(timeStr, out var dateTime))
        {
            return (dateTime - DateTime.UnixEpoch).TotalSeconds;
        }

        // Timespan format (HH:mm:ss.fff)
        if (TimeSpan.TryParse(timeStr, out var timeSpan))
        {
            return baseTimestamp + timeSpan.TotalSeconds;
        }

        return 0;
    }

    private void BuildDataSeries(string[] header)
    {
        foreach (var msg in _messages)
        {
            foreach (var field in msg.Fields)
            {
                if (field.Value is double numValue && !double.IsNaN(numValue) && !double.IsInfinity(numValue))
                {
                    var seriesKey = $"CSV.{field.Key}";
                    if (!_dataSeries.ContainsKey(seriesKey))
                        _dataSeries[seriesKey] = new List<LogDataPoint>();

                    _dataSeries[seriesKey].Add(new LogDataPoint
                    {
                        Index = msg.LineNumber,
                        Timestamp = msg.Timestamp.TotalSeconds * 1e6, // Convert to microseconds
                        Value = numValue
                    });
                }
            }
        }
    }

    /// <summary>
    /// Detects if a column might contain event data.
    /// </summary>
    public bool HasEventColumn()
    {
        return _columnNames.Any(col => 
            EventColumnPatterns.Any(pattern => 
                col.ToLowerInvariant().Contains(pattern)));
    }

    /// <summary>
    /// Extracts events from columns that might contain event data.
    /// </summary>
    public List<(double Timestamp, string EventType, string Message)> ExtractEvents()
    {
        var events = new List<(double, string, string)>();

        // Find event-related columns
        var eventColumns = _columnNames
            .Where(col => EventColumnPatterns.Any(pattern => 
                col.ToLowerInvariant().Contains(pattern)))
            .ToList();

        foreach (var msg in _messages)
        {
            foreach (var col in eventColumns)
            {
                if (msg.Fields.TryGetValue(col, out var value))
                {
                    var valueStr = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(valueStr) && valueStr != "0" && valueStr.ToLower() != "false")
                    {
                        events.Add((msg.Timestamp.TotalSeconds, col, valueStr));
                    }
                }
            }
        }

        return events;
    }

    /// <summary>
    /// Get messages (rows) from the CSV.
    /// </summary>
    public List<LogMessage> GetMessages(string typeName)
    {
        return _messages.ToList();
    }

    /// <summary>
    /// Get all available data series names for graphing.
    /// </summary>
    public List<string> GetAvailableDataSeries()
    {
        return _dataSeries.Keys.OrderBy(k => k).ToList();
    }
}
