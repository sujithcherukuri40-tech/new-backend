using System.Text;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Logging;

/// <summary>
/// Streaming parser for ArduPilot DataFlash binary (.bin) and text (.log) files.
/// Optimized for performance: O(1) message lookup, hard memory caps, no quadratic scans.
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

    // Hard caps — keeps memory bounded and avoids GC thrashing
    private const int MAX_MESSAGES_PER_TYPE = 50_000;   // per type, not total
    private const int MAX_DATA_POINTS_PER_SERIES = 50_000;
    private const int MAX_MESSAGE_TYPES = 256;

    // Format dictionary: byte -> format
    private readonly Dictionary<byte, LogMessageFormat> _formats = new();
    // Data series: "TYPE.FIELD" -> datapoints
    private readonly Dictionary<string, List<LogDataPoint>> _dataSeries = new(256);
    // Messages bucketed by type for O(1) lookup — avoids full-scan on GetMessages()
    private readonly Dictionary<string, List<LogMessage>> _messagesByType = new(64);
    // Parameters extracted from PARM messages
    private readonly Dictionary<string, float> _parameters = new(256);
    // Name -> format lookup for O(1) text parsing (avoids .Any() + .First() O(n) scans)
    private readonly Dictionary<string, LogMessageFormat> _formatsByName = new(64);

    // Adaptive downsampling counters
    private readonly Dictionary<string, int> _seriesSkipCounters = new(256);
    private readonly Dictionary<string, int> _seriesSampleRate = new(256);
    // Per-type message counters for cap enforcement
    private readonly Dictionary<string, int> _messageCountByType = new(64);

    public DataFlashLogParser(ILogger<DataFlashLogParser>? logger = null)
    {
        _logger = logger;
    }

    public ParsedLog? ParsedLog { get; private set; }
    public IReadOnlyDictionary<byte, LogMessageFormat> Formats => _formats;
    public IReadOnlyDictionary<string, List<LogDataPoint>> DataSeries => _dataSeries;

    /// <summary>All messages flattened. Use GetMessages(typeName) for type-specific access.</summary>
    public IReadOnlyList<LogMessage> Messages
    {
        get
        {
            // Flatten only when needed — callers should prefer GetMessages(typeName)
            var all = new List<LogMessage>();
            foreach (var bucket in _messagesByType.Values)
                all.AddRange(bucket);
            return all;
        }
    }

    public IReadOnlyDictionary<string, float> Parameters => _parameters;

    /// <summary>
    /// Parse a DataFlash log file using streaming. Supports files of any size.
    /// </summary>
    public async Task<ParsedLog> ParseAsync(string filePath, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Log file not found", filePath);

        _formats.Clear();
        _formatsByName.Clear();
        _dataSeries.Clear();
        _messagesByType.Clear();
        _parameters.Clear();
        _seriesSkipCounters.Clear();
        _seriesSampleRate.Clear();
        _messageCountByType.Clear();

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

            var isBinary = await IsBinaryLogAsync(filePath, cancellationToken);

            if (isBinary)
                await ParseBinaryStreamAsync(filePath, fileInfo.Length, cancellationToken, progress);
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

            result.IsSuccess = true;
            result.Formats = _formats.Values.ToList();

            // Populate Messages list (flattened for compatibility) with total count
            int totalMessages = _messagesByType.Values.Sum(b => b.Count);
            result.MessageCount = totalMessages;
            result.UniqueMessageTypes = _formats.Count;
            result.DataSeries = _dataSeries;
            result.Parameters = new Dictionary<string, float>(_parameters);

            // Build flat messages list for result (bounded)
            var allMessages = new List<LogMessage>(Math.Min(totalMessages, 200_000));
            foreach (var bucket in _messagesByType.Values)
                allMessages.AddRange(bucket);
            result.Messages = allMessages;

            // Calculate time range
            var timeData = GetTimeSeries();
            if (timeData.Count > 0)
            {
                result.StartTime = TimeSpan.FromMicroseconds(timeData.First().Timestamp);
                result.EndTime = TimeSpan.FromMicroseconds(timeData.Last().Timestamp);
                result.Duration = result.EndTime - result.StartTime;
            }

            ParsedLog = result;
            _logger?.LogInformation("Parsed {Count:N0} messages total, {Types} types, {Series} series from {File}",
                totalMessages, result.UniqueMessageTypes, _dataSeries.Count, result.FileName);
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
                if (buffer[i] == HEAD_BYTE1 && buffer[i + 1] == HEAD_BYTE2)
                    return true;
            return false;
        }
        catch { return false; }
    }

    private static bool IsTextLog(string filePath)
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
    /// Stream-parse a binary DataFlash log. Uses a 128KB ring buffer.
    /// Fixes the critical EOF bug: tracks fileEOF separately from buffer state.
    /// </summary>
    private async Task ParseBinaryStreamAsync(string filePath, long fileSize,
        CancellationToken ct, IProgress<int>? progress)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            STREAM_BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.Asynchronous);

        var ringBuffer = new byte[STREAM_BUFFER_SIZE * 2]; // 128KB ring
        int ringStart = 0;
        int ringEnd = 0;
        long totalBytesRead = 0;
        int msgIndex = 0;
        int lastProgressPercent = -1;
        bool fileEOF = false; // ← CRITICAL: track EOF separately from buffer empty

        while (!ct.IsCancellationRequested)
        {
            // Fill the buffer if not at EOF
            if (!fileEOF)
            {
                int freeSpace = ringBuffer.Length - ringEnd;
                if (freeSpace > 0)
                {
                    var bytesToRead = Math.Min(freeSpace, STREAM_BUFFER_SIZE);
                    var read = await fs.ReadAsync(ringBuffer, ringEnd, bytesToRead, ct);
                    if (read == 0)
                    {
                        fileEOF = true; // No more data from file
                    }
                    else
                    {
                        ringEnd += read;
                        totalBytesRead += read;
                    }
                }
            }

            // Report progress based on bytes read
            if (progress != null && fileSize > 0)
            {
                int pct = (int)Math.Min(totalBytesRead * 99 / fileSize, 99);
                if (pct != lastProgressPercent)
                {
                    lastProgressPercent = pct;
                    progress.Report(pct);
                }
            }

            // Parse messages from buffer
            bool parsedAny = false;
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
                    parsedAny = true;
                    continue;
                }

                // Regular message — need its length from format
                if (!_formats.TryGetValue(msgType, out var format) || format.Length == 0)
                {
                    ringStart++;
                    continue;
                }

                if (ringStart + format.Length > ringEnd) break; // Need more data

                // Parse message
                int dataPos = ringStart + 3; // skip header(2) + type(1)
                var msg = ParseMessageBinary(ringBuffer, ref dataPos, format, msgIndex);

                if (msg != null)
                {
                    AddMessageToBucket(msg, format.Name);
                    msgIndex++;
                }

                ringStart += format.Length;
                parsedAny = true;
            }

            // Compact ring buffer
            if (ringStart > 0)
            {
                int remaining = ringEnd - ringStart;
                if (remaining > 0)
                    Buffer.BlockCopy(ringBuffer, ringStart, ringBuffer, 0, remaining);
                ringEnd = remaining;
                ringStart = 0;
            }

            // Exit when file is done AND buffer is empty
            if (fileEOF && ringEnd == 0) break;

            // If file is EOF but buffer still has data - only exit if we couldn't parse anything
            // (means remaining bytes are incomplete/corrupt)
            if (fileEOF && !parsedAny) break;

            // Yield to prevent UI thread starvation on large files
            if (msgIndex % 100_000 == 0 && msgIndex > 0)
                await Task.Yield();
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
        _formatsByName[format.Name] = format; // O(1) name lookup
    }

    private LogMessage? ParseMessageBinary(byte[] data, ref int pos, LogMessageFormat format, int index)
    {
        int msgDataLen = format.Length - 3;
        if (pos + msgDataLen > data.Length)
            return null;

        var fields = new Dictionary<string, object>(format.FieldNames.Count);
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

            if (value != null) fields[fieldName] = value;
            fieldIndex++;
        }

        // Extract timestamp
        double timestamp = 0;
        TimeSpan timestampSpan = default;
        if (fields.TryGetValue("TimeUS", out var timeUsObj) && timeUsObj is double ts)
        {
            timestamp = ts;
            timestampSpan = TimeSpan.FromMicroseconds(ts);
        }

        // Store data series (numeric fields only)
        foreach (var field in fields)
        {
            if (field.Value is double numValue && !double.IsNaN(numValue) && !double.IsInfinity(numValue))
            {
                StoreDataPoint($"{format.Name}.{field.Key}", index, timestamp, numValue);
            }
        }

        // Extract PARM
        if (format.Name == "PARM")
        {
            var paramName = TryGetStringField(fields, "Name", "N");
            var paramValue = TryGetDoubleField(fields, "Value", "V");
            if (!string.IsNullOrWhiteSpace(paramName) && paramValue.HasValue)
                _parameters[paramName] = (float)paramValue.Value;
        }

        return new LogMessage
        {
            Type = format.Type,
            TypeName = format.Name,
            LineNumber = index,
            Timestamp = timestampSpan,
            Fields = fields
        };
    }

    private void AddMessageToBucket(LogMessage msg, string typeName)
    {
        if (!_messagesByType.TryGetValue(typeName, out var bucket))
        {
            bucket = new List<LogMessage>(128);
            _messagesByType[typeName] = bucket;
            _messageCountByType[typeName] = 0;
        }

        // Enforce per-type cap to prevent any one type dominating memory
        if (_messageCountByType[typeName] < MAX_MESSAGES_PER_TYPE)
        {
            bucket.Add(msg);
            _messageCountByType[typeName]++;
        }
    }

    private void StoreDataPoint(string seriesKey, int index, double timestamp, double value)
    {
        if (!_dataSeries.TryGetValue(seriesKey, out var series))
        {
            series = new List<LogDataPoint>(512);
            _dataSeries[seriesKey] = series;
            _seriesSkipCounters[seriesKey] = 0;
            _seriesSampleRate[seriesKey] = 1;
        }

        // Check if we need to start downsampling BEFORE reaching the cap
        if (series.Count >= MAX_DATA_POINTS_PER_SERIES)
        {
            // Increase sampling rate — but don't add more points
            if (_seriesSampleRate[seriesKey] < 1000)
                _seriesSampleRate[seriesKey] = Math.Min(_seriesSampleRate[seriesKey] * 2, 1000);
            return; // Hard cap reached — stop adding
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
                Value = value
            });
        }
    }

    #endregion

    #region Text Streaming Parser

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

            // Progress + yield every 5000 lines
            if (lineNumber % 5000 == 0)
            {
                if (progress != null && fileSize > 0)
                {
                    int pct = (int)Math.Min(fs.Position * 99 / fileSize, 99);
                    if (pct != lastProgressPercent)
                    {
                        lastProgressPercent = pct;
                        progress.Report(pct);
                    }
                }
                await Task.Yield(); // Don't starve UI thread
            }
        }

        _logger?.LogInformation("Text parse complete: {Lines:N0} lines processed", lineNumber);
    }

    private void ParseTextLine(string line, int lineNumber)
    {
        var parts = line.Split(',');
        if (parts.Length < 2) return;

        var msgType = parts[0].Trim();

        // O(1) format lookup by name — avoids the O(n) .Any() + .First() scan
        if (!_formatsByName.TryGetValue(msgType, out var format))
        {
            if (_formatsByName.Count >= MAX_MESSAGE_TYPES) return;

            format = new LogMessageFormat
            {
                Type = (byte)(_formats.Count % 256 + 1),
                Name = msgType,
                FieldNames = new List<string>()
            };
            for (int i = 1; i < parts.Length; i++)
                format.FieldNames.Add($"F{i}");

            // Avoid byte key collisions
            byte key = format.Type;
            while (_formats.ContainsKey(key)) key++;
            format.Type = key;

            _formats[format.Type] = format;
            _formatsByName[msgType] = format;
        }

        // Enforce per-type cap
        _messageCountByType.TryGetValue(msgType, out var count);
        if (count >= MAX_MESSAGES_PER_TYPE) return;

        var fields = new Dictionary<string, object>(parts.Length);
        double timestamp = 0;

        for (int i = 1; i < parts.Length && i <= format.FieldNames.Count; i++)
        {
            var fieldName = format.FieldNames[i - 1];
            var valueStr = parts[i].Trim();

            if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var numValue))
            {
                fields[fieldName] = numValue;
                StoreDataPoint($"{msgType}.{fieldName}", lineNumber, timestamp, numValue);

                if (fieldName == "TimeUS") timestamp = numValue;
            }
            else
            {
                fields[fieldName] = valueStr;
            }
        }

        var msg = new LogMessage
        {
            Type = format.Type,
            TypeName = msgType,
            LineNumber = lineNumber,
            Timestamp = TimeSpan.FromMicroseconds(timestamp),
            Fields = fields
        };

        AddMessageToBucket(msg, msgType);
    }

    #endregion

    #region Public Query API

    /// <summary>Get messages for a specific type. O(1) — no full scan.</summary>
    public List<LogMessage> GetMessages(string typeName)
    {
        return _messagesByType.TryGetValue(typeName, out var bucket)
            ? bucket
            : new List<LogMessage>();
    }

    public List<LogDataPoint>? GetDataSeries(string messageType, string fieldName)
    {
        var key = $"{messageType}.{fieldName}";
        return _dataSeries.TryGetValue(key, out var series) ? series : null;
    }

    public List<string> GetAvailableDataSeries() => _dataSeries.Keys.OrderBy(k => k).ToList();

    public List<string> GetMessageTypes() => _formatsByName.Keys.OrderBy(n => n).ToList();

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

    #endregion
}

/// <summary>Represents a parsed DataFlash log.</summary>
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

/// <summary>Represents a message format definition.</summary>
public class LogMessageFormat
{
    public byte Type { get; set; }
    public byte Length { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public List<string> FieldNames { get; set; } = new();
}

/// <summary>Represents a single log message.</summary>
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
