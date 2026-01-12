using System.Text;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Logging;

/// <summary>
/// Parser for ArduPilot DataFlash binary (.bin) log files.
/// Implements the binary log format used by ArduPilot flight controllers.
/// </summary>
public class DataFlashLogParser
{
    private readonly ILogger<DataFlashLogParser>? _logger;
    
    // DataFlash binary log constants
    private const byte HEAD_BYTE1 = 0xA3;
    private const byte HEAD_BYTE2 = 0x95;
    private const byte FMT_TYPE = 0x80; // Format message type (128)
    
    // Known message type IDs
    private const byte MSG_TYPE_FMT = 128;
    private const byte MSG_TYPE_PARM = 129;
    private const byte MSG_TYPE_GPS = 130;
    private const byte MSG_TYPE_IMU = 131;
    
    private readonly Dictionary<byte, LogMessageFormat> _formats = new();
    private readonly Dictionary<string, List<LogDataPoint>> _dataSeries = new();
    private readonly List<LogMessage> _messages = new();
    private readonly Dictionary<string, float> _parameters = new();
    
    public DataFlashLogParser(ILogger<DataFlashLogParser>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parsed log result containing all data from the log file.
    /// </summary>
    public ParsedLog? ParsedLog { get; private set; }

    /// <summary>
    /// Gets all available message types in the log.
    /// </summary>
    public IReadOnlyDictionary<byte, LogMessageFormat> Formats => _formats;

    /// <summary>
    /// Gets all data series for graphing.
    /// </summary>
    public IReadOnlyDictionary<string, List<LogDataPoint>> DataSeries => _dataSeries;

    /// <summary>
    /// Gets all parsed messages.
    /// </summary>
    public IReadOnlyList<LogMessage> Messages => _messages;

    /// <summary>
    /// Gets all parameters from the log.
    /// </summary>
    public IReadOnlyDictionary<string, float> Parameters => _parameters;

    /// <summary>
    /// Parse a DataFlash binary log file.
    /// </summary>
    public async Task<ParsedLog> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Log file not found", filePath);

        _formats.Clear();
        _dataSeries.Clear();
        _messages.Clear();
        _parameters.Clear();

        var fileInfo = new FileInfo(filePath);
        var result = new ParsedLog
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileSize = fileInfo.Length,
            ParseTime = DateTime.UtcNow
        };

        try
        {
            var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
            
            // Check if it's a valid DataFlash log
            if (!IsValidDataFlashLog(data))
            {
                // Try parsing as text log
                if (IsTextLog(filePath))
                {
                    return await ParseTextLogAsync(filePath, cancellationToken);
                }
                
                result.IsSuccess = false;
                result.ErrorMessage = "Not a valid ArduPilot DataFlash log file.";
                return result;
            }

            // Parse the binary log
            ParseBinaryLog(data, cancellationToken);

            // Build the result
            result.IsSuccess = true;
            result.Formats = _formats.Values.ToList();
            result.Messages = _messages;
            result.DataSeries = _dataSeries;
            result.Parameters = new Dictionary<string, float>(_parameters);
            result.MessageCount = _messages.Count;
            result.UniqueMessageTypes = _formats.Count;
            
            // Calculate time range
            var timeData = GetTimeSeries();
            if (timeData.Count > 0)
            {
                result.StartTime = TimeSpan.FromMicroseconds(timeData.First().Value);
                result.EndTime = TimeSpan.FromMicroseconds(timeData.Last().Value);
                result.Duration = result.EndTime - result.StartTime;
            }

            ParsedLog = result;
            _logger?.LogInformation("Parsed {Count} messages, {Types} message types from {File}",
                result.MessageCount, result.UniqueMessageTypes, result.FileName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse log file: {File}", filePath);
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private bool IsValidDataFlashLog(byte[] data)
    {
        if (data.Length < 3)
            return false;

        // Check for header bytes anywhere in first 1KB
        var searchLength = Math.Min(data.Length, 1024);
        for (int i = 0; i < searchLength - 1; i++)
        {
            if (data[i] == HEAD_BYTE1 && data[i + 1] == HEAD_BYTE2)
                return true;
        }

        return false;
    }

    private bool IsTextLog(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine();
            // Text logs typically start with FMT, GPS, IMU, etc.
            return firstLine != null && 
                   (firstLine.StartsWith("FMT") || 
                    firstLine.Contains(",") ||
                    firstLine.StartsWith("GPS"));
        }
        catch
        {
            return false;
        }
    }

    private async Task<ParsedLog> ParseTextLogAsync(string filePath, CancellationToken cancellationToken)
    {
        var result = new ParsedLog
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileSize = new FileInfo(filePath).Length,
            ParseTime = DateTime.UtcNow,
            IsTextFormat = true
        };

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            var lineNumber = 0;

            foreach (var line in lines)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    ParseTextLine(line, lineNumber);
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            result.IsSuccess = true;
            result.Formats = _formats.Values.ToList();
            result.Messages = _messages;
            result.DataSeries = _dataSeries;
            result.Parameters = new Dictionary<string, float>(_parameters);
            result.MessageCount = _messages.Count;
            result.UniqueMessageTypes = _formats.Count;

            ParsedLog = result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private void ParseTextLine(string line, int lineNumber)
    {
        var parts = line.Split(',');
        if (parts.Length < 2)
            return;

        var msgType = parts[0].Trim();
        
        // Create format if not exists
        if (!_formats.Values.Any(f => f.Name == msgType))
        {
            var fmt = new LogMessageFormat
            {
                Type = (byte)(_formats.Count + 1),
                Name = msgType,
                FieldNames = new List<string>()
            };
            
            // Add field names (F1, F2, etc.)
            for (int i = 1; i < parts.Length; i++)
            {
                fmt.FieldNames.Add($"F{i}");
            }
            
            _formats[(byte)fmt.Type] = fmt;
        }

        // Parse values
        var format = _formats.Values.First(f => f.Name == msgType);
        var msg = new LogMessage
        {
            Type = format.Type,
            TypeName = msgType,
            LineNumber = lineNumber,
            Fields = new Dictionary<string, object>()
        };

        for (int i = 1; i < parts.Length && i <= format.FieldNames.Count; i++)
        {
            var fieldName = format.FieldNames[i - 1];
            var valueStr = parts[i].Trim();
            
            if (double.TryParse(valueStr, out var numValue))
            {
                msg.Fields[fieldName] = numValue;
                
                // Add to data series
                var seriesKey = $"{msgType}.{fieldName}";
                if (!_dataSeries.ContainsKey(seriesKey))
                    _dataSeries[seriesKey] = new List<LogDataPoint>();
                
                _dataSeries[seriesKey].Add(new LogDataPoint
                {
                    Index = _messages.Count,
                    Value = numValue
                });
            }
            else
            {
                msg.Fields[fieldName] = valueStr;
            }
        }

        _messages.Add(msg);
    }

    private void ParseBinaryLog(byte[] data, CancellationToken cancellationToken)
    {
        int pos = 0;
        int msgIndex = 0;

        while (pos < data.Length - 2 && !cancellationToken.IsCancellationRequested)
        {
            // Find header
            if (data[pos] != HEAD_BYTE1 || data[pos + 1] != HEAD_BYTE2)
            {
                pos++;
                continue;
            }

            pos += 2;
            if (pos >= data.Length)
                break;

            byte msgType = data[pos];
            pos++;

            // Handle FMT message specially
            if (msgType == MSG_TYPE_FMT)
            {
                ParseFormatMessage(data, ref pos);
                continue;
            }

            // Get format for this message type
            if (!_formats.TryGetValue(msgType, out var format))
            {
                // Skip unknown message type
                continue;
            }

            // Parse message using format
            var msg = ParseMessage(data, ref pos, format, msgIndex++);
            if (msg != null)
            {
                _messages.Add(msg);
                
                // Handle special message types - PARM stores parameter values
                if (format.Name == "PARM")
                {
                    string? paramName = null;
                    double? paramValue = null;
                    
                    // Try to get parameter name from various field names
                    if (msg.Fields.TryGetValue("Name", out var nameObj))
                    {
                        paramName = nameObj?.ToString()?.Trim();
                    }
                    else if (msg.Fields.TryGetValue("N", out var nObj))
                    {
                        paramName = nObj?.ToString()?.Trim();
                    }
                    
                    // Try to get parameter value
                    if (msg.Fields.TryGetValue("Value", out var valueObj))
                    {
                        if (valueObj is double d)
                            paramValue = d;
                        else if (double.TryParse(valueObj?.ToString(), out var parsed))
                            paramValue = parsed;
                    }
                    else if (msg.Fields.TryGetValue("V", out var vObj))
                    {
                        if (vObj is double d)
                            paramValue = d;
                        else if (double.TryParse(vObj?.ToString(), out var parsed))
                            paramValue = parsed;
                    }
                    
                    if (!string.IsNullOrWhiteSpace(paramName) && paramValue.HasValue)
                    {
                        _parameters[paramName] = (float)paramValue.Value;
                    }
                }
            }
        }
    }

    private void ParseFormatMessage(byte[] data, ref int pos)
    {
        if (pos + 86 > data.Length)
        {
            pos = data.Length;
            return;
        }

        var format = new LogMessageFormat
        {
            Type = data[pos],
            Length = data[pos + 1]
        };
        pos += 2;

        // Read name (4 bytes, null-terminated)
        format.Name = ReadString(data, ref pos, 4);
        
        // Read format string (16 bytes)
        format.FormatString = ReadString(data, ref pos, 16);
        
        // Read labels (64 bytes, comma-separated)
        var labelsStr = ReadString(data, ref pos, 64);
        format.FieldNames = labelsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();

        _formats[format.Type] = format;
    }

    private LogMessage? ParseMessage(byte[] data, ref int pos, LogMessageFormat format, int index)
    {
        if (format.Length <= 3 || pos + format.Length - 3 > data.Length)
        {
            return null;
        }

        var msg = new LogMessage
        {
            Type = format.Type,
            TypeName = format.Name,
            LineNumber = index,
            Fields = new Dictionary<string, object>()
        };

        // Get timestamp if available
        double timestamp = 0;
        
        int fieldIndex = 0;
        foreach (char formatChar in format.FormatString)
        {
            if (fieldIndex >= format.FieldNames.Count)
                break;

            var fieldName = format.FieldNames[fieldIndex];
            object? value = null;

            try
            {
                switch (formatChar)
                {
                    case 'b': // int8_t
                        value = (double)(sbyte)data[pos];
                        pos += 1;
                        break;
                    case 'B': // uint8_t
                        value = (double)data[pos];
                        pos += 1;
                        break;
                    case 'h': // int16_t
                        value = (double)BitConverter.ToInt16(data, pos);
                        pos += 2;
                        break;
                    case 'H': // uint16_t
                        value = (double)BitConverter.ToUInt16(data, pos);
                        pos += 2;
                        break;
                    case 'i': // int32_t
                        value = (double)BitConverter.ToInt32(data, pos);
                        pos += 4;
                        break;
                    case 'I': // uint32_t
                        value = (double)BitConverter.ToUInt32(data, pos);
                        pos += 4;
                        break;
                    case 'q': // int64_t
                        value = (double)BitConverter.ToInt64(data, pos);
                        pos += 8;
                        break;
                    case 'Q': // uint64_t
                        value = (double)BitConverter.ToUInt64(data, pos);
                        pos += 8;
                        break;
                    case 'f': // float
                        value = (double)BitConverter.ToSingle(data, pos);
                        pos += 4;
                        break;
                    case 'd': // double
                        value = BitConverter.ToDouble(data, pos);
                        pos += 8;
                        break;
                    case 'n': // char[4]
                        value = ReadString(data, ref pos, 4);
                        break;
                    case 'N': // char[16]
                        value = ReadString(data, ref pos, 16);
                        break;
                    case 'Z': // char[64]
                        value = ReadString(data, ref pos, 64);
                        break;
                    case 'c': // int16_t * 100
                        value = BitConverter.ToInt16(data, pos) / 100.0;
                        pos += 2;
                        break;
                    case 'C': // uint16_t * 100
                        value = BitConverter.ToUInt16(data, pos) / 100.0;
                        pos += 2;
                        break;
                    case 'e': // int32_t * 100
                        value = BitConverter.ToInt32(data, pos) / 100.0;
                        pos += 4;
                        break;
                    case 'E': // uint32_t * 100
                        value = BitConverter.ToUInt32(data, pos) / 100.0;
                        pos += 4;
                        break;
                    case 'L': // int32_t latitude/longitude
                        value = BitConverter.ToInt32(data, pos) / 10000000.0;
                        pos += 4;
                        break;
                    case 'M': // uint8_t flight mode
                        value = (double)data[pos];
                        pos += 1;
                        break;
                    default:
                        pos += 1;
                        break;
                }
            }
            catch
            {
                break;
            }

            if (value != null)
            {
                msg.Fields[fieldName] = value;
                
                // Track timestamp
                if (fieldName == "TimeUS" && value is double ts)
                {
                    timestamp = ts;
                    msg.Timestamp = TimeSpan.FromMicroseconds(ts);
                }

                // Add numeric values to data series
                if (value is double numValue && !double.IsNaN(numValue) && !double.IsInfinity(numValue))
                {
                    var seriesKey = $"{format.Name}.{fieldName}";
                    if (!_dataSeries.ContainsKey(seriesKey))
                        _dataSeries[seriesKey] = new List<LogDataPoint>();

                    _dataSeries[seriesKey].Add(new LogDataPoint
                    {
                        Index = index,
                        Timestamp = timestamp,
                        Value = numValue
                    });
                }
            }

            fieldIndex++;
        }

        return msg;
    }

    private string ReadString(byte[] data, ref int pos, int length)
    {
        if (pos + length > data.Length)
        {
            pos = data.Length;
            return string.Empty;
        }

        var bytes = new byte[length];
        Array.Copy(data, pos, bytes, 0, length);
        pos += length;

        // Find null terminator
        var nullIndex = Array.IndexOf(bytes, (byte)0);
        var stringLength = nullIndex >= 0 ? nullIndex : length;

        return Encoding.ASCII.GetString(bytes, 0, stringLength).Trim();
    }

    private List<LogDataPoint> GetTimeSeries()
    {
        // Try common time field names
        if (_dataSeries.TryGetValue("IMU.TimeUS", out var imuTime))
            return imuTime;
        if (_dataSeries.TryGetValue("GPS.TimeUS", out var gpsTime))
            return gpsTime;
        if (_dataSeries.TryGetValue("ATT.TimeUS", out var attTime))
            return attTime;

        // Return first time series found
        var timeKey = _dataSeries.Keys.FirstOrDefault(k => k.EndsWith(".TimeUS"));
        return timeKey != null ? _dataSeries[timeKey] : new List<LogDataPoint>();
    }

    /// <summary>
    /// Get data series for a specific field.
    /// </summary>
    public List<LogDataPoint>? GetDataSeries(string messageType, string fieldName)
    {
        var key = $"{messageType}.{fieldName}";
        return _dataSeries.TryGetValue(key, out var series) ? series : null;
    }

    /// <summary>
    /// Get all available data series names for graphing.
    /// </summary>
    public List<string> GetAvailableDataSeries()
    {
        return _dataSeries.Keys.OrderBy(k => k).ToList();
    }

    /// <summary>
    /// Get messages of a specific type.
    /// </summary>
    public List<LogMessage> GetMessages(string typeName)
    {
        return _messages.Where(m => m.TypeName == typeName).ToList();
    }

    /// <summary>
    /// Get all unique message type names.
    /// </summary>
    public List<string> GetMessageTypes()
    {
        return _formats.Values.Select(f => f.Name).OrderBy(n => n).ToList();
    }
}

/// <summary>
/// Represents a parsed DataFlash log.
/// </summary>
public class ParsedLog
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ParseTime { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsTextFormat { get; set; }
    public string? ErrorMessage { get; set; }
    
    public List<LogMessageFormat> Formats { get; set; } = new();
    public List<LogMessage> Messages { get; set; } = new();
    public Dictionary<string, List<LogDataPoint>> DataSeries { get; set; } = new();
    public Dictionary<string, float> Parameters { get; set; } = new();
    
    public int MessageCount { get; set; }
    public int UniqueMessageTypes { get; set; }
    
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    
    public string DurationDisplay => Duration.TotalSeconds > 0 
        ? $"{Duration.Hours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}" 
        : "Unknown";
}

/// <summary>
/// Represents a message format definition.
/// </summary>
public class LogMessageFormat
{
    public byte Type { get; set; }
    public byte Length { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public List<string> FieldNames { get; set; } = new();
}

/// <summary>
/// Represents a single log message.
/// </summary>
public class LogMessage
{
    public byte Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public TimeSpan Timestamp { get; set; }
    public Dictionary<string, object> Fields { get; set; } = new();

    public string TimestampDisplay => $"{Timestamp.TotalSeconds:F3}s";

    public T? GetField<T>(string name) where T : struct
    {
        if (Fields.TryGetValue(name, out var value))
        {
            if (value is T t)
                return t;
            if (value is double d)
                return (T)Convert.ChangeType(d, typeof(T));
        }
        return null;
    }

    public string? GetStringField(string name)
    {
        return Fields.TryGetValue(name, out var value) ? value?.ToString() : null;
    }
}
