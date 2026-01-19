using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Parser for MAVLink telemetry logs (.tlog files).
/// These are binary logs that contain raw MAVLink messages captured during flight.
/// </summary>
public class TlogParser
{
    private readonly ILogger<TlogParser>? _logger;

    // MAVLink packet constants
    private const byte MAVLINK_V1_STX = 0xFE;
    private const byte MAVLINK_V2_STX = 0xFD;

    // Common MAVLink message IDs
    private const int MAVLINK_MSG_HEARTBEAT = 0;
    private const int MAVLINK_MSG_SYS_STATUS = 1;
    private const int MAVLINK_MSG_GPS_RAW_INT = 24;
    private const int MAVLINK_MSG_ATTITUDE = 30;
    private const int MAVLINK_MSG_GLOBAL_POSITION_INT = 33;
    private const int MAVLINK_MSG_RC_CHANNELS = 65;
    private const int MAVLINK_MSG_VFR_HUD = 74;
    private const int MAVLINK_MSG_COMMAND_LONG = 76;
    private const int MAVLINK_MSG_STATUSTEXT = 253;

    private readonly List<LogMessage> _messages = new();
    private readonly Dictionary<string, List<LogDataPoint>> _dataSeries = new();
    private readonly Dictionary<string, float> _parameters = new();
    private readonly HashSet<string> _messageTypes = new();

    public TlogParser(ILogger<TlogParser>? logger = null)
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
    /// Gets all unique message types in the log.
    /// </summary>
    public IReadOnlyCollection<string> MessageTypes => _messageTypes;

    /// <summary>
    /// Parse a MAVLink telemetry log file.
    /// </summary>
    public async Task<ParsedLog> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Tlog file not found", filePath);

        _messages.Clear();
        _dataSeries.Clear();
        _parameters.Clear();
        _messageTypes.Clear();

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
            
            // Parse MAVLink packets
            ParseMavLinkPackets(data, cancellationToken);

            result.IsSuccess = true;
            result.Messages = _messages;
            result.DataSeries = _dataSeries;
            result.Parameters = new Dictionary<string, float>(_parameters);
            result.MessageCount = _messages.Count;
            result.UniqueMessageTypes = _messageTypes.Count;

            // Calculate duration from timestamps
            if (_messages.Count > 0)
            {
                var firstMsg = _messages.First();
                var lastMsg = _messages.Last();
                result.StartTime = firstMsg.Timestamp;
                result.EndTime = lastMsg.Timestamp;
                result.Duration = result.EndTime - result.StartTime;
            }

            _logger?.LogInformation("Parsed {Count} MAVLink packets, {Types} message types from {File}",
                result.MessageCount, result.UniqueMessageTypes, result.FileName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse tlog file: {File}", filePath);
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private void ParseMavLinkPackets(byte[] data, CancellationToken cancellationToken)
    {
        int pos = 0;
        int packetIndex = 0;
        double currentTimeMs = 0;

        while (pos < data.Length - 8 && !cancellationToken.IsCancellationRequested)
        {
            // Look for MAVLink packet start
            if (data[pos] != MAVLINK_V1_STX && data[pos] != MAVLINK_V2_STX)
            {
                pos++;
                continue;
            }

            bool isV2 = data[pos] == MAVLINK_V2_STX;
            
            try
            {
                if (isV2)
                {
                    ParseMavLinkV2Packet(data, ref pos, packetIndex++, ref currentTimeMs);
                }
                else
                {
                    ParseMavLinkV1Packet(data, ref pos, packetIndex++, ref currentTimeMs);
                }
            }
            catch
            {
                // Skip malformed packet
                pos++;
            }
        }
    }

    private void ParseMavLinkV1Packet(byte[] data, ref int pos, int index, ref double currentTimeMs)
    {
        // MAVLink v1.0 packet structure:
        // STX (1) | LEN (1) | SEQ (1) | SYS_ID (1) | COMP_ID (1) | MSG_ID (1) | PAYLOAD | CRC (2)
        if (pos + 8 > data.Length)
        {
            pos = data.Length;
            return;
        }

        pos++; // Skip STX
        int payloadLen = data[pos++];
        pos++; // Skip sequence
        int sysId = data[pos++];
        int compId = data[pos++];
        int msgId = data[pos++];

        if (pos + payloadLen + 2 > data.Length)
        {
            pos = data.Length;
            return;
        }

        var payload = new byte[payloadLen];
        Array.Copy(data, pos, payload, 0, payloadLen);
        pos += payloadLen + 2; // Skip payload and CRC

        // Estimate time - tlog files may have timestamps prepended
        currentTimeMs += 10; // Assume 10ms between messages as estimate

        ProcessMavLinkMessage(msgId, sysId, compId, payload, index, currentTimeMs);
    }

    private void ParseMavLinkV2Packet(byte[] data, ref int pos, int index, ref double currentTimeMs)
    {
        // MAVLink v2.0 packet structure:
        // STX (1) | LEN (1) | INCOMPAT (1) | COMPAT (1) | SEQ (1) | SYS_ID (1) | COMP_ID (1) | MSG_ID (3) | PAYLOAD | CRC (2) | SIGNATURE (optional 13)
        if (pos + 12 > data.Length)
        {
            pos = data.Length;
            return;
        }

        pos++; // Skip STX
        int payloadLen = data[pos++];
        byte incompat = data[pos++];
        pos++; // Skip compat
        pos++; // Skip sequence
        int sysId = data[pos++];
        int compId = data[pos++];
        
        // 24-bit message ID
        int msgId = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16);
        pos += 3;

        if (pos + payloadLen + 2 > data.Length)
        {
            pos = data.Length;
            return;
        }

        var payload = new byte[payloadLen];
        Array.Copy(data, pos, payload, 0, payloadLen);
        pos += payloadLen + 2; // Skip payload and CRC

        // Skip signature if present
        bool hasSig = (incompat & 0x01) != 0;
        if (hasSig && pos + 13 <= data.Length)
        {
            pos += 13;
        }

        currentTimeMs += 10;

        ProcessMavLinkMessage(msgId, sysId, compId, payload, index, currentTimeMs);
    }

    private void ProcessMavLinkMessage(int msgId, int sysId, int compId, byte[] payload, int index, double timeMs)
    {
        var msgTypeName = GetMessageTypeName(msgId);
        _messageTypes.Add(msgTypeName);

        var msg = new LogMessage
        {
            Type = (byte)msgId,
            TypeName = msgTypeName,
            LineNumber = index,
            Timestamp = TimeSpan.FromMilliseconds(timeMs),
            Fields = new Dictionary<string, object>
            {
                ["SysId"] = (double)sysId,
                ["CompId"] = (double)compId
            }
        };

        // Parse common message types
        switch (msgId)
        {
            case MAVLINK_MSG_HEARTBEAT:
                ParseHeartbeat(payload, msg);
                break;
            case MAVLINK_MSG_SYS_STATUS:
                ParseSysStatus(payload, msg);
                break;
            case MAVLINK_MSG_GPS_RAW_INT:
                ParseGpsRawInt(payload, msg);
                break;
            case MAVLINK_MSG_ATTITUDE:
                ParseAttitude(payload, msg);
                break;
            case MAVLINK_MSG_GLOBAL_POSITION_INT:
                ParseGlobalPositionInt(payload, msg);
                break;
            case MAVLINK_MSG_VFR_HUD:
                ParseVfrHud(payload, msg);
                break;
            case MAVLINK_MSG_STATUSTEXT:
                ParseStatusText(payload, msg);
                break;
        }

        _messages.Add(msg);

        // Add numeric fields to data series
        foreach (var field in msg.Fields)
        {
            if (field.Value is double numValue && !double.IsNaN(numValue) && !double.IsInfinity(numValue))
            {
                var seriesKey = $"{msgTypeName}.{field.Key}";
                if (!_dataSeries.ContainsKey(seriesKey))
                    _dataSeries[seriesKey] = new List<LogDataPoint>();

                _dataSeries[seriesKey].Add(new LogDataPoint
                {
                    Index = index,
                    Timestamp = timeMs * 1000, // Convert to microseconds for consistency
                    Value = numValue
                });
            }
        }
    }

    private static string GetMessageTypeName(int msgId)
    {
        return msgId switch
        {
            MAVLINK_MSG_HEARTBEAT => "HEARTBEAT",
            MAVLINK_MSG_SYS_STATUS => "SYS_STATUS",
            MAVLINK_MSG_GPS_RAW_INT => "GPS_RAW_INT",
            MAVLINK_MSG_ATTITUDE => "ATTITUDE",
            MAVLINK_MSG_GLOBAL_POSITION_INT => "GLOBAL_POS",
            MAVLINK_MSG_RC_CHANNELS => "RC_CHANNELS",
            MAVLINK_MSG_VFR_HUD => "VFR_HUD",
            MAVLINK_MSG_COMMAND_LONG => "COMMAND_LONG",
            MAVLINK_MSG_STATUSTEXT => "STATUSTEXT",
            _ => $"MSG_{msgId}"
        };
    }

    private void ParseHeartbeat(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 9)
        {
            msg.Fields["Type"] = (double)payload[4];
            msg.Fields["Autopilot"] = (double)payload[5];
            msg.Fields["BaseMode"] = (double)payload[6];
            msg.Fields["SystemStatus"] = (double)payload[8];
        }
    }

    private void ParseSysStatus(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 31)
        {
            msg.Fields["VoltageBattery"] = BitConverter.ToUInt16(payload, 14) / 1000.0;
            msg.Fields["CurrentBattery"] = BitConverter.ToInt16(payload, 16) / 100.0;
            msg.Fields["BatteryRemaining"] = (double)(sbyte)payload[30];
        }
    }

    private void ParseGpsRawInt(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 30)
        {
            msg.Fields["Lat"] = BitConverter.ToInt32(payload, 8) / 1e7;
            msg.Fields["Lon"] = BitConverter.ToInt32(payload, 12) / 1e7;
            msg.Fields["Alt"] = BitConverter.ToInt32(payload, 16) / 1000.0;
            msg.Fields["EPH"] = BitConverter.ToUInt16(payload, 20) / 100.0;
            msg.Fields["EPV"] = BitConverter.ToUInt16(payload, 22) / 100.0;
            msg.Fields["Vel"] = BitConverter.ToUInt16(payload, 24) / 100.0;
            msg.Fields["NSats"] = (double)payload[29];
            msg.Fields["FixType"] = (double)payload[28];
        }
    }

    private void ParseAttitude(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 28)
        {
            msg.Fields["TimeBootMs"] = (double)BitConverter.ToUInt32(payload, 0);
            msg.Fields["Roll"] = BitConverter.ToSingle(payload, 4) * 180 / Math.PI;
            msg.Fields["Pitch"] = BitConverter.ToSingle(payload, 8) * 180 / Math.PI;
            msg.Fields["Yaw"] = BitConverter.ToSingle(payload, 12) * 180 / Math.PI;
            msg.Fields["RollSpeed"] = BitConverter.ToSingle(payload, 16);
            msg.Fields["PitchSpeed"] = BitConverter.ToSingle(payload, 20);
            msg.Fields["YawSpeed"] = BitConverter.ToSingle(payload, 24);
        }
    }

    private void ParseGlobalPositionInt(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 28)
        {
            msg.Fields["TimeBootMs"] = (double)BitConverter.ToUInt32(payload, 0);
            msg.Fields["Lat"] = BitConverter.ToInt32(payload, 4) / 1e7;
            msg.Fields["Lon"] = BitConverter.ToInt32(payload, 8) / 1e7;
            msg.Fields["Alt"] = BitConverter.ToInt32(payload, 12) / 1000.0;
            msg.Fields["RelAlt"] = BitConverter.ToInt32(payload, 16) / 1000.0;
            msg.Fields["Vx"] = BitConverter.ToInt16(payload, 20) / 100.0;
            msg.Fields["Vy"] = BitConverter.ToInt16(payload, 22) / 100.0;
            msg.Fields["Vz"] = BitConverter.ToInt16(payload, 24) / 100.0;
            msg.Fields["Hdg"] = BitConverter.ToUInt16(payload, 26) / 100.0;
        }
    }

    private void ParseVfrHud(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 20)
        {
            msg.Fields["Airspeed"] = (double)BitConverter.ToSingle(payload, 0);
            msg.Fields["Groundspeed"] = (double)BitConverter.ToSingle(payload, 4);
            msg.Fields["Heading"] = (double)BitConverter.ToInt16(payload, 8);
            msg.Fields["Throttle"] = (double)BitConverter.ToUInt16(payload, 10);
            msg.Fields["Alt"] = (double)BitConverter.ToSingle(payload, 12);
            msg.Fields["Climb"] = (double)BitConverter.ToSingle(payload, 16);
        }
    }

    private void ParseStatusText(byte[] payload, LogMessage msg)
    {
        if (payload.Length >= 1)
        {
            msg.Fields["Severity"] = (double)payload[0];
            
            // Extract text (remaining bytes, null-terminated)
            int textLen = Math.Min(50, payload.Length - 1);
            var textBytes = new byte[textLen];
            Array.Copy(payload, 1, textBytes, 0, textLen);
            var nullIndex = Array.IndexOf(textBytes, (byte)0);
            if (nullIndex >= 0) textLen = nullIndex;
            
            var text = System.Text.Encoding.ASCII.GetString(textBytes, 0, textLen).Trim();
            msg.Fields["Text"] = text;
        }
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
        return _messageTypes.OrderBy(n => n).ToList();
    }
}
