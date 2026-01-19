using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Parser for PX4 ULog binary format (.ulg files).
/// ULog is the logging format used by PX4 flight controller firmware.
/// </summary>
public class ULogParser
{
    private readonly ILogger<ULogParser>? _logger;

    // ULog file header magic bytes
    private static readonly byte[] ULOG_MAGIC = { 0x55, 0x4C, 0x6F, 0x67 }; // "ULog"
    private const byte ULOG_VERSION = 0x01;

    // ULog message types
    private const byte MSG_TYPE_FORMAT = (byte)'F';       // Format definition
    private const byte MSG_TYPE_DATA = (byte)'D';         // Data message
    private const byte MSG_TYPE_INFO = (byte)'I';         // Information message
    private const byte MSG_TYPE_INFO_MULTIPLE = (byte)'M'; // Multi info message
    private const byte MSG_TYPE_PARAMETER = (byte)'P';    // Parameter message
    private const byte MSG_TYPE_ADD_LOGGED = (byte)'A';   // Add logged message
    private const byte MSG_TYPE_REMOVE_LOGGED = (byte)'R'; // Remove logged message
    private const byte MSG_TYPE_SYNC = (byte)'S';         // Sync message
    private const byte MSG_TYPE_DROPOUT = (byte)'O';      // Dropout (lost data)
    private const byte MSG_TYPE_LOGGING = (byte)'L';      // Logging message (text)

    private readonly Dictionary<ushort, ULogFormat> _formats = new();
    private readonly Dictionary<ushort, string> _subscriptions = new();
    private readonly List<LogMessage> _messages = new();
    private readonly Dictionary<string, List<LogDataPoint>> _dataSeries = new();
    private readonly Dictionary<string, float> _parameters = new();
    private readonly List<ULogInfo> _info = new();

    public ULogParser(ILogger<ULogParser>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets all parsed messages.
    /// </summary>
    public IReadOnlyList<LogMessage> Messages => _messages;

    /// <summary>
    /// Gets all data series for graphing.
    /// </summary>
    public IReadOnlyDictionary<string, List<LogDataPoint>> DataSeries => _dataSeries;

    /// <summary>
    /// Gets all parameters from the log.
    /// </summary>
    public IReadOnlyDictionary<string, float> Parameters => _parameters;

    /// <summary>
    /// Gets log information entries.
    /// </summary>
    public IReadOnlyList<ULogInfo> Info => _info;

    /// <summary>
    /// Parse a PX4 ULog file.
    /// </summary>
    public async Task<ParsedLog> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ULog file not found", filePath);

        _formats.Clear();
        _subscriptions.Clear();
        _messages.Clear();
        _dataSeries.Clear();
        _parameters.Clear();
        _info.Clear();

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

            // Validate header
            if (!ValidateHeader(data))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Invalid ULog file header";
                return result;
            }

            // Parse the log
            ParseULogFile(data, cancellationToken);

            result.IsSuccess = true;
            result.Messages = _messages;
            result.DataSeries = _dataSeries;
            result.Parameters = new Dictionary<string, float>(_parameters);
            result.MessageCount = _messages.Count;
            result.UniqueMessageTypes = _formats.Count;

            // Calculate duration from timestamps
            if (_messages.Count > 0)
            {
                var timestamps = _messages.Select(m => m.Timestamp.TotalSeconds).OrderBy(t => t).ToList();
                result.StartTime = TimeSpan.FromSeconds(timestamps.First());
                result.EndTime = TimeSpan.FromSeconds(timestamps.Last());
                result.Duration = result.EndTime - result.StartTime;
            }

            _logger?.LogInformation("Parsed {Count} messages, {Types} formats from ULog file {File}",
                result.MessageCount, result.UniqueMessageTypes, result.FileName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse ULog file: {File}", filePath);
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private bool ValidateHeader(byte[] data)
    {
        if (data.Length < 16)
            return false;

        // Check magic bytes
        for (int i = 0; i < ULOG_MAGIC.Length; i++)
        {
            if (data[i] != ULOG_MAGIC[i])
                return false;
        }

        // Check version (byte 4 should be 0x01 for ULog version 1)
        if (data[4] != 0x01)
            return false;

        return true;
    }

    private void ParseULogFile(byte[] data, CancellationToken cancellationToken)
    {
        int pos = 16; // Skip header (16 bytes)
        int msgIndex = 0;
        ulong baseTimestamp = 0;

        while (pos < data.Length - 3 && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Read message header: size (2) + type (1)
                ushort msgSize = BitConverter.ToUInt16(data, pos);
                pos += 2;
                byte msgType = data[pos++];

                if (pos + msgSize > data.Length)
                    break;

                var payload = new byte[msgSize];
                Array.Copy(data, pos, payload, 0, msgSize);
                pos += msgSize;

                ProcessULogMessage(msgType, payload, msgIndex++, ref baseTimestamp);
            }
            catch
            {
                // Skip malformed message
                pos++;
            }
        }
    }

    private void ProcessULogMessage(byte msgType, byte[] payload, int index, ref ulong baseTimestamp)
    {
        switch ((char)msgType)
        {
            case 'F': // Format definition
                ParseFormatMessage(payload);
                break;
            case 'I': // Info
                ParseInfoMessage(payload);
                break;
            case 'P': // Parameter
                ParseParameterMessage(payload);
                break;
            case 'A': // Add logged message
                ParseAddLoggedMessage(payload);
                break;
            case 'D': // Data message
                ParseDataMessage(payload, index, ref baseTimestamp);
                break;
            case 'L': // Logging (text)
                ParseLoggingMessage(payload, index, baseTimestamp);
                break;
        }
    }

    private void ParseFormatMessage(byte[] payload)
    {
        if (payload.Length < 2)
            return;

        // Format: format_string (null-terminated)
        int nullIndex = Array.IndexOf(payload, (byte)0);
        if (nullIndex < 0) nullIndex = payload.Length;

        var formatStr = System.Text.Encoding.ASCII.GetString(payload, 0, nullIndex);

        // Parse format string: "message_name:field_type1 field_name1;field_type2 field_name2;..."
        var colonIndex = formatStr.IndexOf(':');
        if (colonIndex < 0) return;

        var msgName = formatStr.Substring(0, colonIndex);
        var fieldsStr = formatStr.Substring(colonIndex + 1);

        var format = new ULogFormat
        {
            Name = msgName,
            Fields = new List<(string Type, string Name)>()
        };

        foreach (var fieldDef in fieldsStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = fieldDef.Trim().Split(' ', 2);
            if (parts.Length == 2)
            {
                format.Fields.Add((parts[0], parts[1]));
            }
        }

        // Use format count as ID since ULog doesn't have explicit format IDs
        var formatId = (ushort)_formats.Count;
        _formats[formatId] = format;
    }

    private void ParseInfoMessage(byte[] payload)
    {
        if (payload.Length < 2)
            return;

        byte keyLen = payload[0];
        if (keyLen + 1 >= payload.Length)
            return;

        var key = System.Text.Encoding.ASCII.GetString(payload, 1, keyLen);
        var valueBytes = new byte[payload.Length - keyLen - 1];
        Array.Copy(payload, keyLen + 1, valueBytes, 0, valueBytes.Length);

        // Try to interpret as string
        var value = System.Text.Encoding.ASCII.GetString(valueBytes).TrimEnd('\0');

        _info.Add(new ULogInfo { Key = key, Value = value });
    }

    private void ParseParameterMessage(byte[] payload)
    {
        if (payload.Length < 2)
            return;

        byte keyLen = payload[0];
        if (keyLen + 5 > payload.Length)
            return;

        var key = System.Text.Encoding.ASCII.GetString(payload, 1, keyLen);
        
        // Parameter value starts after key
        int valueOffset = keyLen + 1;
        if (valueOffset + 4 <= payload.Length)
        {
            var value = BitConverter.ToSingle(payload, valueOffset);
            _parameters[key] = value;
        }
    }

    private void ParseAddLoggedMessage(byte[] payload)
    {
        if (payload.Length < 3)
            return;

        byte multi_id = payload[0];
        ushort msgId = BitConverter.ToUInt16(payload, 1);

        // Message name follows (null-terminated)
        var nameBytes = payload.Skip(3).TakeWhile(b => b != 0).ToArray();
        var msgName = System.Text.Encoding.ASCII.GetString(nameBytes);

        _subscriptions[msgId] = msgName;
    }

    private void ParseDataMessage(byte[] payload, int index, ref ulong baseTimestamp)
    {
        if (payload.Length < 10)
            return;

        ushort msgId = BitConverter.ToUInt16(payload, 0);
        
        if (!_subscriptions.TryGetValue(msgId, out var msgName))
        {
            msgName = $"MSG_{msgId}";
        }

        // Timestamp is first field (8 bytes, microseconds)
        ulong timestamp = BitConverter.ToUInt64(payload, 2);
        
        if (baseTimestamp == 0)
            baseTimestamp = timestamp;

        var relativeTimestamp = (timestamp - baseTimestamp) / 1_000_000.0; // Convert to seconds

        var msg = new LogMessage
        {
            Type = (byte)(msgId & 0xFF),
            TypeName = msgName,
            LineNumber = index,
            Timestamp = TimeSpan.FromSeconds(relativeTimestamp),
            Fields = new Dictionary<string, object>
            {
                ["TimeUS"] = (double)timestamp
            }
        };

        // Try to parse fields if we have format info
        // For now, just store the raw data indicator
        msg.Fields["DataLength"] = (double)(payload.Length - 10);

        _messages.Add(msg);

        // Add to data series
        var seriesKey = $"{msgName}.TimeUS";
        if (!_dataSeries.ContainsKey(seriesKey))
            _dataSeries[seriesKey] = new List<LogDataPoint>();

        _dataSeries[seriesKey].Add(new LogDataPoint
        {
            Index = index,
            Timestamp = timestamp,
            Value = relativeTimestamp
        });
    }

    private void ParseLoggingMessage(byte[] payload, int index, ulong baseTimestamp)
    {
        if (payload.Length < 9)
            return;

        byte logLevel = payload[0];
        ulong timestamp = BitConverter.ToUInt64(payload, 1);

        var textBytes = payload.Skip(9).TakeWhile(b => b != 0).ToArray();
        var text = System.Text.Encoding.ASCII.GetString(textBytes);

        var relativeTimestamp = baseTimestamp > 0 
            ? (timestamp - baseTimestamp) / 1_000_000.0 
            : timestamp / 1_000_000.0;

        var msg = new LogMessage
        {
            Type = MSG_TYPE_LOGGING,
            TypeName = "LOG",
            LineNumber = index,
            Timestamp = TimeSpan.FromSeconds(relativeTimestamp),
            Fields = new Dictionary<string, object>
            {
                ["Level"] = (double)logLevel,
                ["Message"] = text,
                ["TimeUS"] = (double)timestamp
            }
        };

        _messages.Add(msg);
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
        return _subscriptions.Values.Distinct().OrderBy(n => n).ToList();
    }

    /// <summary>
    /// Get all available data series names for graphing.
    /// </summary>
    public List<string> GetAvailableDataSeries()
    {
        return _dataSeries.Keys.OrderBy(k => k).ToList();
    }
}

/// <summary>
/// ULog format definition.
/// </summary>
public class ULogFormat
{
    public string Name { get; set; } = string.Empty;
    public List<(string Type, string Name)> Fields { get; set; } = new();
}

/// <summary>
/// ULog info entry.
/// </summary>
public class ULogInfo
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
