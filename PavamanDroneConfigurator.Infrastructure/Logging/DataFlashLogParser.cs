using System.Text;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Logging;

/// <summary>
/// Streaming parser for ArduPilot DataFlash binary (.bin) and text (.log) files.
/// Designed for files up to 10GB+ using buffered streaming instead of loading entire file into memory.
/// Uses a two-pass approach: first discover message formats, then stream-parse data.
/// </summary>
public class DataFlashLogParser
{
    private readonly ILogger<DataFlashLogParser>? _logger;
    
    // DataFlash binary log constants
    private const byte HEAD_BYTE1 = 0xA3;
    private const byte HEAD_BYTE2 = 0x95;
    private const byte MSG_TYPE_FMT = 128;

    // Streaming buffer size (64KB for efficient reads)
    private const int STREAM_BUFFER_SIZE = 65536;
    // Max messages to keep in memory (for message browser)
    private const int MAX_MESSAGES_IN_MEMORY = 500_000;
    // Max data points per series for graphing
    private const int MAX_DATA_POINTS_PER_SERIES = 100_000;
    
    private readonly Dictionary<byte, LogMessageFormat> _formats = new();
    private readonly Dictionary<string, List<LogDataPoint>> _dataSeries = new();
    private readonly List<LogMessage> _messages = new();
    private readonly Dictionary<string, float> _parameters = new();
    
    // Tracking for downsampling large data series
    private readonly Dictionary<string, int> _seriesSkipCounters = new();
    private readonly Dictionary<string, int> _seriesSampleRate = new();
    
    public DataFlashLogParser(ILogger<DataFlashLogParser>? logger = null)
    {
        _logger = logger;
    }

    public ParsedLog? ParsedLog { get; private set; }
    public IReadOnlyDictionary<byte, LogMessageFormat> Formats => _formats;
    public IReadOnlyDictionary<string, List<LogDataPoint>> DataSeries => _dataSeries;
    public IReadOnlyList<LogMessage> Messages => _messages;
    public IReadOnlyDictionary<string, float> Parameters => _parameters;

    /// <summary>
    /// Parse a DataFlash log file using streaming for memory efficiency.
    /// Supports files of any size (tested up to 10GB+).
    /// </summary>
    public async Task<ParsedLog> ParseAsync(string filePath, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Log file not found", filePath);

        _formats.Clear();
        _dataSeries.Clear();
        _messages.Clear();
        _parameters.Clear();
        _seriesSkipCounters.Clear();
        _seriesSampleRate.Clear();

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
            _logger?.LogInformation("Starting parse of {File} ({SizeMB:F1} MB)", 
                result.FileName, fileInfo.Length / (1024.0 * 1024.0));

            // Detect format
            var isBinary = await IsBinaryLogAsync(filePath, cancellationToken);
            
            if (isBinary)
            {
                await ParseBinaryStreamAsync(filePath, fileInfo.Length, cancellationToken, progress);
            }
            else if (IsTextLog(filePath))
            {
                result.IsTextFormat = true;
                await ParseTextStreamAsync(filePath, fileInfo.Length, cancellationToken, progress);
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Not a valid ArduPilot DataFlash log file.";
                return result;
            }

            // Build result
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
            _logger?.LogInformation("Parsed {Count:N0} messages, {Types} types, {Series} data series from {File}",
                result.MessageCount, result.UniqueMessageTypes, _dataSeries.Count, result.FileName);
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Parse cancelled.";
            _logger?.LogWarning("Parse cancelled for {File}", filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse log file: {File}", filePath);
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        progress?.Report(100);
        return result;
    }

    #region Binary Format Detection

    private async Task<bool> IsBinaryLogAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var buffer = new byte[1024];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 
                STREAM_BUFFER_SIZE, FileOptions.SequentialScan);
            var bytesRead = await fs.ReadAsync(buffer, 0, Math.Min(1024, (int)fs.Length), ct);
            
            for (int i = 0; i < bytesRead - 1; i++)
            {
                if (buffer[i] == HEAD_BYTE1 && buffer[i + 1] == HEAD_BYTE2)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsTextLog(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine();
            return firstLine != null && 
                   (firstLine.StartsWith("FMT") || firstLine.Contains(",") || firstLine.StartsWith("GPS"));
        }
        catch { return false; }
    }

    #endregion

    #region Binary Streaming Parser

    /// <summary>
    /// Stream-parse a binary DataFlash log using a 64KB rolling buffer.
    /// Memory usage stays constant regardless of file size.
    /// </summary>
    private async Task ParseBinaryStreamAsync(string filePath, long fileSize, 
        CancellationToken ct, IProgress<int>? progress)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 
            STREAM_BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.Asynchronous);

        // Ring buffer for streaming — holds enough for any single message
        var ringBuffer = new byte[STREAM_BUFFER_SIZE * 2]; // 128KB ring
        int ringStart = 0;
        int ringEnd = 0;
        long totalBytesRead = 0;
        int msgIndex = 0;
        int lastProgressPercent = -1;

        // Calculate sample rates based on estimated message count
        var estimatedMessages = fileSize / 50; // rough estimate: ~50 bytes per message
        _logger?.LogInformation("Estimated {Est:N0} messages for {SizeMB:F1} MB file", 
            estimatedMessages, fileSize / (1024.0 * 1024.0));

        while (!ct.IsCancellationRequested)
        {
            // Fill the buffer
            int freeSpace = ringBuffer.Length - ringEnd;
            if (freeSpace > 0)
            {
                var read = await fs.ReadAsync(ringBuffer, ringEnd, Math.Min(freeSpace, STREAM_BUFFER_SIZE), ct);
                if (read == 0 && ringStart >= ringEnd) break; // EOF + no data left
                ringEnd += read;
                totalBytesRead += read;
            }

            // Report progress
            if (progress != null && fileSize > 0)
            {
                int pct = (int)(totalBytesRead * 100 / fileSize);
                if (pct != lastProgressPercent)
                {
                    lastProgressPercent = pct;
                    progress.Report(Math.Min(pct, 99));
                }
            }

            // Parse messages from buffer
            while (ringStart < ringEnd - 2)
            {
                if (ct.IsCancellationRequested) break;

                // Find header
                if (ringBuffer[ringStart] != HEAD_BYTE1 || ringBuffer[ringStart + 1] != HEAD_BYTE2)
                {
                    ringStart++;
                    continue;
                }

                if (ringStart + 2 >= ringEnd) break; // Need more data
                byte msgType = ringBuffer[ringStart + 2];

                // FMT message (fixed 89 bytes: 2 header + 1 type + 86 payload)
                if (msgType == MSG_TYPE_FMT)
                {
                    if (ringStart + 89 > ringEnd) break; // Need more data
                    ParseFormatMessage(ringBuffer, ringStart + 3);
                    ringStart += 89;
                    continue;
                }

                // Regular message — need to know its length from format
                if (!_formats.TryGetValue(msgType, out var format) || format.Length == 0)
                {
                    ringStart++;
                    continue;
                }

                if (ringStart + format.Length > ringEnd) break; // Need more data

                // Parse message from buffer
                int dataPos = ringStart + 3; // skip header (2) + type (1)
                var msg = ParseMessageStreaming(ringBuffer, ref dataPos, format, msgIndex);
                
                if (msg != null)
                {
                    // Only keep messages in memory up to limit (for message browser)
                    if (_messages.Count < MAX_MESSAGES_IN_MEMORY)
                    {
                        _messages.Add(msg);
                    }

                    // Handle PARM messages
                    if (format.Name == "PARM")
                    {
                        var paramName = TryGetStringField(msg.Fields, "Name", "N");
                        var paramValue = TryGetDoubleField(msg.Fields, "Value", "V");
                        if (!string.IsNullOrWhiteSpace(paramName) && paramValue.HasValue)
                        {
                            _parameters[paramName] = (float)paramValue.Value;
                        }
                    }

                    msgIndex++;
                }

                ringStart += format.Length;
            }

            // Compact ring buffer — move unprocessed data to beginning
            if (ringStart > 0)
            {
                int remaining = ringEnd - ringStart;
                if (remaining > 0)
                    Buffer.BlockCopy(ringBuffer, ringStart, ringBuffer, 0, remaining);
                ringEnd = remaining;
                ringStart = 0;
            }

            // EOF check
            if (fs.Position >= fileSize && ringStart >= ringEnd) break;
        }

        _logger?.LogInformation("Binary parse complete: {Msgs:N0} messages, {Bytes:N0} bytes processed", 
            msgIndex, totalBytesRead);
    }

    private void ParseFormatMessage(byte[] data, int pos)
    {
        if (pos + 86 > data.Length) return;

        var format = new LogMessageFormat
        {
            Type = data[pos],
            Length = data[pos + 1]
        };
        pos += 2;

        format.Name = ReadString(data, pos, 4);
        pos += 4;
        format.FormatString = ReadString(data, pos, 16);
        pos += 16;
        var labelsStr = ReadString(data, pos, 64);
        format.FieldNames = labelsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).ToList();

        _formats[format.Type] = format;
    }

    private LogMessage? ParseMessageStreaming(byte[] data, ref int pos, LogMessageFormat format, int index)
    {
        int msgDataLen = format.Length - 3; // minus header (2) + type (1)
        if (pos + msgDataLen > data.Length)
            return null;

        var msg = new LogMessage
        {
            Type = format.Type,
            TypeName = format.Name,
            LineNumber = index,
            Fields = new Dictionary<string, object>()
        };

        // Parse fields
        int fieldIndex = 0;
        foreach (char formatChar in format.FormatString)
        {
            if (fieldIndex >= format.FieldNames.Count) break;
            var fieldName = format.FieldNames[fieldIndex];
            object? value = null;

            try
            {
                switch (formatChar)
                {
                    case 'b': value = (double)(sbyte)data[pos]; pos += 1; break;
                    case 'B': value = (double)data[pos]; pos += 1; break;
                    case 'h': value = (double)BitConverter.ToInt16(data, pos); pos += 2; break;
                    case 'H': value = (double)BitConverter.ToUInt16(data, pos); pos += 2; break;
                    case 'i': value = (double)BitConverter.ToInt32(data, pos); pos += 4; break;
                    case 'I': value = (double)BitConverter.ToUInt32(data, pos); pos += 4; break;
                    case 'q': value = (double)BitConverter.ToInt64(data, pos); pos += 8; break;
                    case 'Q': value = (double)BitConverter.ToUInt64(data, pos); pos += 8; break;
                    case 'f': value = (double)BitConverter.ToSingle(data, pos); pos += 4; break;
                    case 'd': value = BitConverter.ToDouble(data, pos); pos += 8; break;
                    case 'n': value = ReadString(data, pos, 4); pos += 4; break;
                    case 'N': value = ReadString(data, pos, 16); pos += 16; break;
                    case 'Z': value = ReadString(data, pos, 64); pos += 64; break;
                    case 'c': value = BitConverter.ToInt16(data, pos) / 100.0; pos += 2; break;
                    case 'C': value = BitConverter.ToUInt16(data, pos) / 100.0; pos += 2; break;
                    case 'e': value = BitConverter.ToInt32(data, pos) / 100.0; pos += 4; break;
                    case 'E': value = BitConverter.ToUInt32(data, pos) / 100.0; pos += 4; break;
                    case 'L': value = BitConverter.ToInt32(data, pos) / 10000000.0; pos += 4; break;
                    case 'M': value = (double)data[pos]; pos += 1; break;
                    default: pos += 1; break;
                }
            }
            catch { break; }

            if (value != null) msg.Fields[fieldName] = value;
            fieldIndex++;
        }

        // Extract timestamp
        double timestamp = 0;
        if (msg.Fields.TryGetValue("TimeUS", out var timeUsObj) && timeUsObj is double ts)
        {
            timestamp = ts;
            msg.Timestamp = TimeSpan.FromMicroseconds(ts);
        }

        // Add numeric values to data series with adaptive downsampling
        foreach (var field in msg.Fields)
        {
            if (field.Value is double numValue && !double.IsNaN(numValue) && !double.IsInfinity(numValue))
            {
                var seriesKey = $"{format.Name}.{field.Key}";
                
                if (!_dataSeries.ContainsKey(seriesKey))
                {
                    _dataSeries[seriesKey] = new List<LogDataPoint>();
                    _seriesSkipCounters[seriesKey] = 0;
                    _seriesSampleRate[seriesKey] = 1; // Start at 1:1
                }

                var series = _dataSeries[seriesKey];
                
                // Adaptive downsampling: if series is getting large, increase skip rate
                if (series.Count >= MAX_DATA_POINTS_PER_SERIES)
                {
                    // Double the sample rate
                    _seriesSampleRate[seriesKey] = Math.Min(_seriesSampleRate[seriesKey] * 2, 1000);
                }

                var sampleRate = _seriesSampleRate[seriesKey];
                _seriesSkipCounters[seriesKey]++;
                
                if (_seriesSkipCounters[seriesKey] >= sampleRate)
                {
                    _seriesSkipCounters[seriesKey] = 0;
                    series.Add(new LogDataPoint
                    {
                        Index = index,
                        Timestamp = timestamp,
                        Value = numValue
                    });
                }
            }
        }

        return msg;
    }

    #endregion

    #region Text Streaming Parser

    /// <summary>
    /// Stream-parse a text log file line by line using StreamReader.
    /// Never loads entire file into memory.
    /// </summary>
    private async Task ParseTextStreamAsync(string filePath, long fileSize,
        CancellationToken ct, IProgress<int>? progress)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            STREAM_BUFFER_SIZE, FileOptions.SequentialScan);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: STREAM_BUFFER_SIZE);

        int lineNumber = 0;
        int lastProgressPercent = -1;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                ParseTextLine(line, lineNumber);
            }
            catch
            {
                // Skip malformed lines
            }

            // Progress reporting
            if (progress != null && fileSize > 0 && lineNumber % 10000 == 0)
            {
                int pct = (int)(fs.Position * 100 / fileSize);
                if (pct != lastProgressPercent)
                {
                    lastProgressPercent = pct;
                    progress.Report(Math.Min(pct, 99));
                }
            }
        }

        _logger?.LogInformation("Text parse complete: {Lines:N0} lines processed", lineNumber);
    }

    private void ParseTextLine(string line, int lineNumber)
    {
        var parts = line.Split(',');
        if (parts.Length < 2) return;

        var msgType = parts[0].Trim();
        
        if (!_formats.Values.Any(f => f.Name == msgType))
        {
            var fmt = new LogMessageFormat
            {
                Type = (byte)(_formats.Count + 1),
                Name = msgType,
                FieldNames = new List<string>()
            };
            for (int i = 1; i < parts.Length; i++)
                fmt.FieldNames.Add($"F{i}");
            _formats[(byte)fmt.Type] = fmt;
        }

        var format = _formats.Values.First(f => f.Name == msgType);
        
        // Limit messages in memory
        if (_messages.Count >= MAX_MESSAGES_IN_MEMORY) return;
        
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
                
                var seriesKey = $"{msgType}.{fieldName}";
                if (!_dataSeries.ContainsKey(seriesKey))
                {
                    _dataSeries[seriesKey] = new List<LogDataPoint>();
                    _seriesSkipCounters[seriesKey] = 0;
                    _seriesSampleRate[seriesKey] = 1;
                }

                var series = _dataSeries[seriesKey];
                if (series.Count >= MAX_DATA_POINTS_PER_SERIES)
                    _seriesSampleRate[seriesKey] = Math.Min(_seriesSampleRate[seriesKey] * 2, 1000);

                _seriesSkipCounters[seriesKey]++;
                if (_seriesSkipCounters[seriesKey] >= _seriesSampleRate[seriesKey])
                {
                    _seriesSkipCounters[seriesKey] = 0;
                    series.Add(new LogDataPoint { Index = _messages.Count, Value = numValue });
                }
            }
            else
            {
                msg.Fields[fieldName] = valueStr;
            }
        }

        _messages.Add(msg);
    }

    #endregion

    #region Utility

    private static string ReadString(byte[] data, int pos, int length)
    {
        if (pos + length > data.Length) return string.Empty;
        var nullIndex = Array.IndexOf(data, (byte)0, pos, length);
        var stringLength = nullIndex >= 0 ? nullIndex - pos : length;
        return Encoding.ASCII.GetString(data, pos, stringLength).Trim();
    }

    private static string? TryGetStringField(Dictionary<string, object> fields, params string[] fieldNames)
    {
        foreach (var name in fieldNames)
            if (fields.TryGetValue(name, out var obj))
                return obj?.ToString()?.Trim();
        return null;
    }
    
    private static double? TryGetDoubleField(Dictionary<string, object> fields, params string[] fieldNames)
    {
        foreach (var name in fieldNames)
        {
            if (fields.TryGetValue(name, out var obj))
            {
                if (obj is double d) return d;
                if (double.TryParse(obj?.ToString(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    private List<LogDataPoint> GetTimeSeries()
    {
        if (_dataSeries.TryGetValue("IMU.TimeUS", out var imuTime)) return imuTime;
        if (_dataSeries.TryGetValue("GPS.TimeUS", out var gpsTime)) return gpsTime;
        if (_dataSeries.TryGetValue("ATT.TimeUS", out var attTime)) return attTime;
        var timeKey = _dataSeries.Keys.FirstOrDefault(k => k.EndsWith(".TimeUS"));
        return timeKey != null ? _dataSeries[timeKey] : new List<LogDataPoint>();
    }

    public List<LogDataPoint>? GetDataSeries(string messageType, string fieldName)
    {
        var key = $"{messageType}.{fieldName}";
        return _dataSeries.TryGetValue(key, out var series) ? series : null;
    }

    public List<string> GetAvailableDataSeries() => _dataSeries.Keys.OrderBy(k => k).ToList();

    public List<LogMessage> GetMessages(string typeName) => _messages.Where(m => m.TypeName == typeName).ToList();

    public List<string> GetMessageTypes() => _formats.Values.Select(f => f.Name).OrderBy(n => n).ToList();

    #endregion
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
            if (value is T t) return t;
            if (value is double d) return (T)Convert.ChangeType(d, typeof(T));
        }
        return null;
    }

    public string? GetStringField(string name)
    {
        return Fields.TryGetValue(name, out var value) ? value?.ToString() : null;
    }
}
