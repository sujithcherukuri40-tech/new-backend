using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PavamanDroneConfigurator.Infrastructure.Services;

namespace PavamanDroneConfigurator.Infrastructure.MAVLink
{
    /// <summary>
    /// MAVLink protocol implementation matching Mission Planner's approach.
    /// Handles both MAVLink v1 and v2 frames with proper CRC validation.
    /// </summary>
    public sealed class AsvMavlinkWrapper : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IMavLinkMessageLogger? _mavLinkLogger;
        private Stream? _inputStream;
        private Stream? _outputStream;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private Task? _heartbeatTask;
        private Task? _watchdogTask;
        private bool _disposed;

        private readonly byte[] _rxBuffer = new byte[4096];
        private int _rxBufferPos;
        private readonly object _bufferLock = new();
        private readonly object _writeLock = new();

        private byte _targetSystemId = 1;
        private byte _targetComponentId = 1;
        private byte _packetSequence;
        
        // Transaction management (production-grade reliability)
        private readonly MavlinkTransactionManager _transactionManager;
        
        // Heartbeat watchdog
        private DateTime _lastHeartbeatReceived = DateTime.MinValue;
        private const int HEARTBEAT_TIMEOUT_SECONDS = 5;

        // MAVLink constants
        private const byte MAVLINK_STX_V1 = 0xFE;
        private const byte MAVLINK_STX_V2 = 0xFD;
        private const byte GCS_SYSTEM_ID = 255;
        private const byte GCS_COMPONENT_ID = 190;
        private const byte MAV_TYPE_GCS = 6;
        private const byte MAV_AUTOPILOT_INVALID = 8;

        // Message IDs
        private const byte MAVLINK_MSG_ID_HEARTBEAT = 0;
        private const byte MAVLINK_MSG_ID_PARAM_REQUEST_READ = 20;
        private const byte MAVLINK_MSG_ID_PARAM_REQUEST_LIST = 21;
        private const byte MAVLINK_MSG_ID_PARAM_VALUE = 22;
        private const byte MAVLINK_MSG_ID_PARAM_SET = 23;
        private const byte MAVLINK_MSG_ID_RAW_IMU = 27;
        private const byte MAVLINK_MSG_ID_SCALED_IMU = 26;
        private const byte MAVLINK_MSG_ID_COMMAND_LONG = 76;
        private const ushort MAVLINK_MSG_ID_COMMAND_ACK = 77;
        private const byte MAVLINK_MSG_ID_STATUSTEXT = 253;
        private const byte MAVLINK_MSG_ID_RC_CHANNELS = 65;
        private const byte MAVLINK_MSG_ID_AUTOPILOT_VERSION = 148;

        // MAV_CMD IDs
        private const ushort MAV_CMD_DO_MOTOR_TEST = 209;
        private const ushort MAV_CMD_PREFLIGHT_CALIBRATION = 241;
        private const ushort MAV_CMD_PREFLIGHT_STORAGE = 245;
        private const ushort MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN = 246;
        private const ushort MAV_CMD_SET_MESSAGE_INTERVAL = 511;
        private const ushort MAV_CMD_COMPONENT_ARM_DISARM = 400;
        private const ushort MAV_CMD_ACCELCAL_VEHICLE_POS = 42429;
        private const ushort MAV_CMD_REQUEST_MESSAGE = 512;

        // CRC extras from MAVLink message definitions
        private const byte CRC_EXTRA_HEARTBEAT = 50;
        private const byte CRC_EXTRA_PARAM_REQUEST_READ = 214;
        private const byte CRC_EXTRA_PARAM_REQUEST_LIST = 159;
        private const byte CRC_EXTRA_PARAM_VALUE = 220;
        private const byte CRC_EXTRA_PARAM_SET = 168;
        private const byte CRC_EXTRA_RAW_IMU = 144;
        private const byte CRC_EXTRA_SCALED_IMU = 170;
        private const byte CRC_EXTRA_COMMAND_LONG = 152;
        private const byte CRC_EXTRA_COMMAND_ACK = 143;
        private const byte CRC_EXTRA_STATUSTEXT = 83;
        private const byte CRC_EXTRA_RC_CHANNELS = 118;
        private const byte CRC_EXTRA_AUTOPILOT_VERSION = 49;

        public event EventHandler<(byte SystemId, byte ComponentId)>? HeartbeatReceived;
        public event EventHandler<(string Name, float Value, ushort Index, ushort Count)>? ParamValueReceived;
        public event EventHandler<(ushort Command, byte Result)>? CommandAckReceived;
        public event EventHandler<(byte Severity, string Text)>? StatusTextReceived;
        public event EventHandler<HeartbeatData>? HeartbeatDataReceived;
        public event EventHandler<RcChannelsData>? RcChannelsReceived;
        public event EventHandler<RawImuData>? RawImuReceived;
        public event EventHandler<AutopilotVersionData>? AutopilotVersionReceived;
        public event EventHandler<CommandLongData>? CommandLongReceived;
        public event EventHandler? ConnectionLost;

        public AsvMavlinkWrapper(ILogger logger, IMavLinkMessageLogger? mavLinkLogger = null)
        {
            _logger = logger;
            _mavLinkLogger = mavLinkLogger;
            _transactionManager = new MavlinkTransactionManager(logger);
        }

        public void Initialize(Stream inputStream, Stream outputStream)
        {
            _disposed = false;
            _inputStream = inputStream;
            _outputStream = outputStream;
            _rxBufferPos = 0;
            _cts = new CancellationTokenSource();
            _lastHeartbeatReceived = DateTime.UtcNow;

            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            _heartbeatTask = Task.Run(() => GcsHeartbeatLoopAsync(_cts.Token));
            _watchdogTask = Task.Run(() => HeartbeatWatchdogAsync(_cts.Token));

            _logger.LogInformation("MAVLink wrapper initialized with transaction manager and watchdog");
        }
        
        /// <summary>
        /// Heartbeat watchdog - detects connection loss
        /// </summary>
        private async Task HeartbeatWatchdogAsync(CancellationToken token)
        {
            _logger.LogInformation("Heartbeat watchdog started");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, token);

                    if (_lastHeartbeatReceived != DateTime.MinValue)
                    {
                        var elapsed = DateTime.UtcNow - _lastHeartbeatReceived;
                        if (elapsed.TotalSeconds > HEARTBEAT_TIMEOUT_SECONDS)
                        {
                            _logger.LogWarning("Heartbeat timeout: no heartbeat for {Elapsed}s", elapsed.TotalSeconds);
                            ConnectionLost?.Invoke(this, EventArgs.Empty);
                            
                            // Reset to prevent spam
                            _lastHeartbeatReceived = DateTime.MinValue;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Heartbeat watchdog error");
                }
            }

            _logger.LogInformation("Heartbeat watchdog ended");
        }

        private async Task GcsHeartbeatLoopAsync(CancellationToken token)
        {
            _logger.LogInformation("GCS heartbeat loop started");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await SendGcsHeartbeatAsync(token);
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sending GCS heartbeat");
                }
            }

            _logger.LogInformation("GCS heartbeat loop ended");
        }

        private async Task SendGcsHeartbeatAsync(CancellationToken ct)
        {
            // HEARTBEAT payload (9 bytes):
            // [0-3] custom_mode (uint32)
            // [4]   type (uint8) - MAV_TYPE_GCS = 6
            // [5]   autopilot (uint8) - MAV_AUTOPILOT_INVALID = 8
            // [6]   base_mode (unit8)
            // [7]   system_status (uint8)
            // [8]   mavlink_version (uint8)

            var payload = new byte[9];
            payload[0] = 0; payload[1] = 0; payload[2] = 0; payload[3] = 0; // custom_mode = 0
            payload[4] = MAV_TYPE_GCS;
            payload[5] = MAV_AUTOPILOT_INVALID;
            payload[6] = 0; // base_mode
            payload[7] = 0; // system_status
            payload[8] = 3; // mavlink_version

            await SendMessageAsync(MAVLINK_MSG_ID_HEARTBEAT, payload, ct);
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            var buffer = new byte[1024];
            try
            {
                _logger.LogInformation("MAVLink read loop started");
                while (!token.IsCancellationRequested && _inputStream != null)
                {
                    try
                    {
                        int bytesRead = await _inputStream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead > 0)
                        {
                            ProcessBytes(buffer, bytesRead);
                        }
                        else
                        {
                            await Task.Delay(10, token);
                        }
                    }
                    catch (IOException)
                    {
                        // Connection closed
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MAVLink read loop error");
            }
            _logger.LogInformation("MAVLink read loop ended");
        }

        private void ProcessBytes(byte[] data, int length)
        {
            lock (_bufferLock)
            {
                // Add new data to buffer
                for (int i = 0; i < length; i++)
                {
                    if (_rxBufferPos >= _rxBuffer.Length)
                    {
                        // Buffer overflow - reset
                        _rxBufferPos = 0;
                    }
                    _rxBuffer[_rxBufferPos++] = data[i];
                }

                // Process complete packets
                ProcessBuffer();
            }
        }

        private void ProcessBuffer()
        {
            while (_rxBufferPos > 0)
            {
                // Find start byte
                int startIdx = -1;
                for (int i = 0; i < _rxBufferPos; i++)
                {
                    if (_rxBuffer[i] == MAVLINK_STX_V1 || _rxBuffer[i] == MAVLINK_STX_V2)
                    {
                        startIdx = i;
                        break;
                    }
                }

                if (startIdx < 0)
                {
                    // No start byte found - clear buffer
                    _rxBufferPos = 0;
                    return;
                }

                // Remove garbage before start byte
                if (startIdx > 0)
                {
                    Array.Copy(_rxBuffer, startIdx, _rxBuffer, 0, _rxBufferPos - startIdx);
                    _rxBufferPos -= startIdx;
                }

                // Check if we have enough data for header
                if (_rxBufferPos < 8)
                    return;

                byte stx = _rxBuffer[0];
                int frameLen;

                if (stx == MAVLINK_STX_V1)
                {
                    // MAVLink v1: STX + LEN + SEQ + SYSID + COMPID + MSGID + PAYLOAD + CRC(2)
                    byte payloadLen = _rxBuffer[1];
                    frameLen = 8 + payloadLen;
                }
                else // MAVLINK_STX_V2
                {
                    // MAVLink v2: STX + LEN + INCOMPAT + COMPAT + SEQ + SYSID + COMPID + MSGID(3) + PAYLOAD + CRC(2) + [SIG(13)]
                    if (_rxBufferPos < 12)
                        return;
                    byte payloadLen = _rxBuffer[1];
                    byte incompatFlags = _rxBuffer[2];
                    bool hasSignature = (incompatFlags & 0x01) != 0;
                    frameLen = 12 + payloadLen + (hasSignature ? 13 : 0);
                }

                // Check if we have complete frame
                if (_rxBufferPos < frameLen)
                    return;

                // Extract and process frame
                var frame = new byte[frameLen];
                Array.Copy(_rxBuffer, 0, frame, 0, frameLen);

                // Remove processed frame from buffer
                Array.Copy(_rxBuffer, frameLen, _rxBuffer, 0, _rxBufferPos - frameLen);
                _rxBufferPos -= frameLen;

                // Process the frame
                try
                {
                    if (stx == MAVLINK_STX_V1)
                        ProcessMavlink1Frame(frame);
                    else
                        ProcessMavlink2Frame(frame);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing MAVLink frame");
                }
            }
        }

        private void ProcessMavlink1Frame(byte[] frame)
        {
            // Strict validation before processing
            if (!MavlinkProtocol.ValidateV1Frame(frame))
            {
                _logger.LogWarning("Invalid MAVLink v1 frame structure");
                return;
            }

            byte payloadLen = frame[1];
            byte seq = frame[2];
            byte sysId = frame[3];
            byte compId = frame[4];
            byte msgId = frame[5];

            // Verify CRC with proper CRC_EXTRA
            ushort crcCalc = CalculateCrc(frame, 1, payloadLen + 5, MavlinkProtocol.GetCrcExtra(msgId));
            ushort crcRecv = (ushort)(frame[6 + payloadLen] | (frame[7 + payloadLen] << 8));

            if (crcCalc != crcRecv)
            {
                _logger.LogTrace("V1 CRC mismatch: msg={Msg} calc=0x{Calc:X4} recv=0x{Recv:X4}", msgId, crcCalc, crcRecv);
                return;
            }
            
            // Warn about unknown messages (CRC_EXTRA = 0)
            if (!MavlinkProtocol.IsKnownMessage(msgId))
            {
                _logger.LogTrace("Unknown message ID: {MsgId} (CRC validation may be unreliable)", msgId);
            }

            // Extract payload
            var payload = new byte[payloadLen];
            Array.Copy(frame, 6, payload, 0, payloadLen);

            HandleMessage(sysId, compId, msgId, payload);
        }

        private void ProcessMavlink2Frame(byte[] frame)
        {
            // Strict validation before processing
            if (!MavlinkProtocol.ValidateV2Frame(frame))
            {
                _logger.LogWarning("Invalid MAVLink v2 frame structure or unsupported features");
                return;
            }

            byte payloadLen = frame[1];
            byte incompatFlags = frame[2];
            byte compatFlags = frame[3];
            byte seq = frame[4];
            byte sysId = frame[5];
            byte compId = frame[6];
            
            // Use proper 24-bit message ID extraction
            int msgId = MavlinkProtocol.GetV2MessageId(frame);
            if (msgId < 0)
            {
                _logger.LogWarning("Failed to extract v2 message ID");
                return;
            }

            // Verify CRC (for v2, CRC is calculated over bytes 1 to 9+payloadLen)
            ushort crcCalc = CalculateCrc(frame, 1, 9 + payloadLen, MavlinkProtocol.GetCrcExtra(msgId));
            int crcOffset = 10 + payloadLen;
            ushort crcRecv = (ushort)(frame[crcOffset] | (frame[crcOffset + 1] << 8));

            if (crcCalc != crcRecv)
            {
                _logger.LogTrace("V2 CRC mismatch: msg={Msg} calc=0x{Calc:X4} recv=0x{Recv:X4}", msgId, crcCalc, crcRecv);
                return;
            }
            
            // Warn about unknown messages
            if (!MavlinkProtocol.IsKnownMessage(msgId))
            {
                _logger.LogTrace("Unknown v2 message ID: {MsgId}", msgId);
            }

            // Extract payload
            var payload = new byte[payloadLen];
            Array.Copy(frame, 10, payload, 0, payloadLen);

            HandleMessage(sysId, compId, (byte)msgId, payload);
        }

        private void HandleMessage(byte sysId, byte compId, byte msgId, byte[] payload)
        {
            switch (msgId)
            {
                case MAVLINK_MSG_ID_HEARTBEAT:
                    HandleHeartbeat(sysId, compId, payload);
                    break;

                case MAVLINK_MSG_ID_PARAM_VALUE:
                    HandleParamValue(payload);
                    break;

                case (byte)MAVLINK_MSG_ID_COMMAND_ACK:
                    HandleCommandAck(payload);
                    break;

                case MAVLINK_MSG_ID_STATUSTEXT:
                    HandleStatusText(payload);
                    break;

                case MAVLINK_MSG_ID_COMMAND_LONG:
                    HandleCommandLong(sysId, compId, payload);
                    break;

                case MAVLINK_MSG_ID_RC_CHANNELS:
                    HandleRcChannels(payload);
                    break;

                case MAVLINK_MSG_ID_RAW_IMU:
                    HandleRawImu(payload);
                    break;

                case MAVLINK_MSG_ID_SCALED_IMU:
                    HandleScaledImu(payload);
                    break;
                    
                case MAVLINK_MSG_ID_AUTOPILOT_VERSION:
                    HandleAutopilotVersion(sysId, compId, payload);
                    break;
            }
        }

        private void HandleHeartbeat(byte sysId, byte compId, byte[] payload)
        {
            // Skip GCS heartbeats
            if (compId == GCS_COMPONENT_ID || sysId == 0)
                return;

            _targetSystemId = sysId;
            _targetComponentId = compId;
            
            // Update watchdog timestamp
            _lastHeartbeatReceived = DateTime.UtcNow;

            _logger.LogDebug("Heartbeat from FC: sysid={SysId} compid={CompId}", sysId, compId);
            
            // Don't log HEARTBEAT to MAVLink logger - too noisy (every 1 second)
            // _mavLinkLogger?.LogIncoming("HEARTBEAT", $"sysid={sysId}, compid={compId}");
            
            HeartbeatReceived?.Invoke(this, (sysId, compId));

            // Update last heartbeat timestamp
            _lastHeartbeatReceived = DateTime.UtcNow;

            // Parse heartbeat payload for flight mode info and vehicle type
            // HEARTBEAT payload:
            // [0-3] custom_mode (uint32)
            // [4]   type (uint8) - MAV_TYPE
            // [5]   autopilot (uint8) - MAV_AUTOPILOT
            // [6]   base_mode (uint8)
            // [7]   system_status (uint8)
            // [8]   mavlink_version (uint8)
            if (payload.Length >= 9)
            {
                uint customMode = BitConverter.ToUInt32(payload, 0);
                byte vehicleType = payload[4];  // MAV_TYPE
                byte autopilot = payload[5];    // MAV_AUTOPILOT
                byte baseMode = payload[6];
                byte systemStatus = payload[7];
                byte mavlinkVersion = payload[8];

                var heartbeatData = new HeartbeatData
                {
                    SystemId = sysId,
                    ComponentId = compId,
                    CustomMode = customMode,
                    VehicleType = vehicleType,
                    Autopilot = autopilot,
                    BaseMode = baseMode,
                    SystemStatus = systemStatus,
                    MavlinkVersion = mavlinkVersion,
                    IsArmed = (baseMode & 0x80) != 0 // MAV_MODE_FLAG_SAFETY_ARMED
                };

                HeartbeatDataReceived?.Invoke(this, heartbeatData);
            }
        }

        private void HandleParamValue(byte[] payload)
        {
            // PARAM_VALUE payload (25 bytes):
            // [0-3]   param_value (float)
            // [4-5]   param_count (uint16)
            // [6-7]   param_index (uint16)
            // [8-23]  param_id (char[16])
            // [24]    param_type (uint8)

            if (payload.Length < 25)
            {
                _logger.LogWarning("PARAM_VALUE payload too short: {Len}", payload.Length);
                return;
            }

            float value = BitConverter.ToSingle(payload, 0);
            ushort paramCount = BitConverter.ToUInt16(payload, 4);
            ushort paramIndex = BitConverter.ToUInt16(payload, 6);

            // Extract param name (null-terminated string)
            string name = Encoding.ASCII.GetString(payload, 8, 16).TrimEnd('\0');
            byte paramType = payload[24];

            _logger.LogDebug("PARAM_VALUE: {Name}={Value} [{Index}/{Count}] type={Type}",
                name, value, paramIndex + 1, paramCount, paramType);

            // Feed transaction manager
            _transactionManager.HandleParameterValue(name, value);

            ParamValueReceived?.Invoke(this, (name, value, paramIndex, paramCount));
        }

        private void HandleCommandAck(byte[] payload)
        {
            // COMMAND_ACK payload:
            // [0-1] command (uint16)
            // [2]   result (uint8)
            if (payload.Length < 3)
            {
                _logger.LogWarning("COMMAND_ACK payload too short: {Len}", payload.Length);
                return;
            }

            ushort command = BitConverter.ToUInt16(payload, 0);
            byte result = payload[2];

            _logger.LogDebug("COMMAND_ACK: cmd={Command} result={Result}", command, result);
            
            // Log to MAVLink logger with command name and number
            string cmdName = command switch
            {
                MAV_CMD_PREFLIGHT_CALIBRATION => "MAV_CMD_PREFLIGHT_CALIBRATION",
                MAV_CMD_ACCELCAL_VEHICLE_POS => "MAV_CMD_ACCELCAL_VEHICLE_POS",
                MAV_CMD_COMPONENT_ARM_DISARM => "MAV_CMD_COMPONENT_ARM_DISARM",
                MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN => "MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN",
                MAV_CMD_DO_MOTOR_TEST => "MAV_CMD_DO_MOTOR_TEST",
                _ => command.ToString()
            };
            string resultName = result switch
            {
                0 => "ACCEPTED",
                1 => "TEMPORARILY_REJECTED",
                2 => "DENIED",
                3 => "UNSUPPORTED",
                4 => "FAILED",
                5 => "IN_PROGRESS",
                _ => result.ToString()
            };
            _mavLinkLogger?.LogIncoming("COMMAND_ACK", $"cmd={command} ({cmdName}), result={resultName}");
            
            // Feed transaction manager
            _transactionManager.HandleCommandAck(command, result);
            
            CommandAckReceived?.Invoke(this, (command, result));
        }

        private void HandleCommandLong(byte sysId, byte compId, byte[] payload)
        {
            // COMMAND_LONG payload (33 bytes):
            // [0-3]   param1 (float)
            // [4-7]   param2 (float)
            // [8-11]  param3 (float)
            // [12-15] param4 (float)
            // [16-19] param5 (float)
            // [20-23] param6 (float)
            // [24-27] param7 (float)
            // [28-29] command (uint16)
            // [30]    target_system (uint8)
            // [31]    target_component (uint8)
            // [32]    confirmation (uint8)
            
            if (payload.Length < 33)
            {
                _logger.LogWarning("COMMAND_LONG payload too short: {Len}", payload.Length);
                return;
            }

            float param1 = BitConverter.ToSingle(payload, 0);
            float param2 = BitConverter.ToSingle(payload, 4);
            float param3 = BitConverter.ToSingle(payload, 8);
            float param4 = BitConverter.ToSingle(payload, 12);
            float param5 = BitConverter.ToSingle(payload, 16);
            float param6 = BitConverter.ToSingle(payload, 20);
            float param7 = BitConverter.ToSingle(payload, 24);
            ushort command = BitConverter.ToUInt16(payload, 28);
            byte targetSystem = payload[30];
            byte targetComponent = payload[31];
            byte confirmation = payload[32];

            _logger.LogDebug("COMMAND_LONG: cmd={Command} param1={Param1} from sysid={SysId}", 
                command, param1, sysId);
            
            // Log to MAVLink logger for specific commands we care about
            if (command == MAV_CMD_ACCELCAL_VEHICLE_POS)
            {
                string posName = ((int)param1) switch
                {
                    1 => "LEVEL",
                    2 => "LEFT",
                    3 => "RIGHT",
                    4 => "NOSEDOWN",
                    5 => "NOSEUP",
                    6 => "BACK",
                    _ => param1.ToString()
                };
                _mavLinkLogger?.LogIncoming("COMMAND_LONG", 
                    $"MAV_CMD_ACCELCAL_VEHICLE_POS: position={posName} (param1={param1})");
            }
            
            // Raise event with parsed data
            var commandLongData = new CommandLongData
            {
                SystemId = sysId,
                ComponentId = compId,
                Command = command,
                Param1 = param1,
                Param2 = param2,
                Param3 = param3,
                Param4 = param4,
                Param5 = param5,
                Param6 = param6,
                Param7 = param7,
                TargetSystem = targetSystem,
                TargetComponent = targetComponent,
                Confirmation = confirmation
            };
            
            CommandLongReceived?.Invoke(this, commandLongData);
        }

        private void HandleStatusText(byte[] payload)
        {
            // STATUSTEXT payload:
            // [0]     severity (uint8)
            // [1-50]  text (char[50])
            if (payload.Length < 2)
                return;

            byte severity = payload[0];
            string text = System.Text.Encoding.ASCII.GetString(payload, 1, Math.Min(50, payload.Length - 1)).TrimEnd('\0');

            _logger.LogDebug("STATUSTEXT: severity={Severity} text={Text}", severity, text);
            
            // Log to MAVLink logger
            string severityName = severity switch
            {
                0 => "EMERGENCY",
                1 => "ALERT",
                2 => "CRITICAL",
                3 => "ERROR",
                4 => "WARNING",
                5 => "NOTICE",
                6 => "INFO",
                7 => "DEBUG",
                _ => severity.ToString()
            };
            _mavLinkLogger?.LogIncoming("STATUSTEXT", $"[{severityName}] {text}");
            
            StatusTextReceived?.Invoke(this, (severity, text));
        }

        private void HandleRcChannels(byte[] payload)
        {
            // RC_CHANNELS payload (42 bytes):
            // [0-3]   time_boot_ms (uint32)
            // [4-5]   chan1_raw (uint16)
            // [6-7]   chan2_raw (uint16)
            // ... up to chan18
            // [38]    chancount (uint8)
            // [39]    rssi (uint8)
            if (payload.Length < 40)
                return;

            var rcData = new RcChannelsData
            {
                TimeBootMs = BitConverter.ToUInt32(payload, 0),
                Channel1 = BitConverter.ToUInt16(payload, 4),
                Channel2 = BitConverter.ToUInt16(payload, 6),
                Channel3 = BitConverter.ToUInt16(payload, 8),
                Channel4 = BitConverter.ToUInt16(payload, 10),
                Channel5 = BitConverter.ToUInt16(payload, 12),
                Channel6 = BitConverter.ToUInt16(payload, 14),
                Channel7 = BitConverter.ToUInt16(payload, 16),
                Channel8 = BitConverter.ToUInt16(payload, 18),
                ChannelCount = payload[38],
                Rssi = payload[39]
            };

            RcChannelsReceived?.Invoke(this, rcData);
        }

        private void HandleRawImu(byte[] payload)
        {
            // RAW_IMU payload (29 bytes):
            // [0-7]   time_usec (uint64)
            // [8-9]   xacc (int16) - raw X acceleration
            // [10-11] yacc (int16) - raw Y acceleration
            // [12-13] zacc (int16) - raw Z acceleration
            // [14-15] xgyro (int16) - raw X gyro
            // [16-17] ygyro (int16) - raw Y gyro
            // [18-19] zgyro (int16) - raw Z gyro
            // [20-21] xmag (int16) - raw X magnetometer
            // [22-23] ymag (int16) - raw Y magnetometer
            // [24-25] zmag (int16) - raw Z magnetometer
            // [26]    id (uint8) - IMU ID
            // [27-28] temperature (int16) - temperature in cdegC (optional)
            if (payload.Length < 26)
                return;

            var imuData = new RawImuData
            {
                TimeUsec = BitConverter.ToUInt64(payload, 0),
                XAcc = BitConverter.ToInt16(payload, 8),
                YAcc = BitConverter.ToInt16(payload, 10),
                ZAcc = BitConverter.ToInt16(payload, 12),
                XGyro = BitConverter.ToInt16(payload, 14),
                YGyro = BitConverter.ToInt16(payload, 16),
                ZGyro = BitConverter.ToInt16(payload, 18),
                XMag = BitConverter.ToInt16(payload, 20),
                YMag = BitConverter.ToInt16(payload, 22),
                ZMag = BitConverter.ToInt16(payload, 24)
            };

            if (payload.Length >= 27)
                imuData.Id = payload[26];
            
            if (payload.Length >= 29)
                imuData.Temperature = BitConverter.ToInt16(payload, 27);

            RawImuReceived?.Invoke(this, imuData);
        }

        private void HandleScaledImu(byte[] payload)
        {
            // SCALED_IMU payload (24 bytes):
            // [0-3]   time_boot_ms (uint32)
            // [4-5]   xacc (int16) - milli-g
            // [6-7]   yacc (int16) - milli-g
            // [8-9]   zacc (int16) - milli-g
            // [10-11] xgyro (int16) - milli-rad/s
            // [12-13] ygyro (int16) - milli-rad/s
            // [14-15] zgyro (int16) - milli-rad/s
            // [16-17] xmag (int16) - milli-Gauss
            // [18-19] ymag (int16) - milli-Gauss
            // [20-21] zmag (int16) - milli-Gauss
            // [22-23] temperature (int16) - cdegC (optional)
            if (payload.Length < 22)
                return;

            // Convert to same format as RAW_IMU for consistency
            var imuData = new RawImuData
            {
                TimeUsec = BitConverter.ToUInt32(payload, 0) * 1000UL, // Convert ms to us
                XAcc = BitConverter.ToInt16(payload, 4),
                YAcc = BitConverter.ToInt16(payload, 6),
                ZAcc = BitConverter.ToInt16(payload, 8),
                XGyro = BitConverter.ToInt16(payload, 10),
                YGyro = BitConverter.ToInt16(payload, 12),
                ZGyro = BitConverter.ToInt16(payload, 14),
                XMag = BitConverter.ToInt16(payload, 16),
                YMag = BitConverter.ToInt16(payload, 18),
                ZMag = BitConverter.ToInt16(payload, 20),
                IsScaled = true // Flag to indicate this is SCALED_IMU
            };

            if (payload.Length >= 24)
                imuData.Temperature = BitConverter.ToInt16(payload, 22);

            RawImuReceived?.Invoke(this, imuData);
        }

        private void HandleAutopilotVersion(byte sysId, byte compId, byte[] payload)
        {
            // AUTOPILOT_VERSION payload (60 bytes):
            // [0-7]     capabilities (uint64)
            // [8-11]    flight_sw_version (uint32)
            // [12-15]   middleware_sw_version (uint32)
            // [16-19]   os_sw_version (uint32)
            // [20-23]   board_version (uint32)
            // [24-31]   flight_custom_version (uint8[8])
            // [32-39]   middleware_custom_version (uint8[8])
            // [40-47]   os_custom_version (uint8[8])
            // [48-49]   vendor_id (uint16)
            // [50-51]   product_id (uint16)
            // [52-59]   uid (uint64)
            // [60-77]   uid2 (uint8[18]) - MAVLink v2 extension
            
            if (payload.Length < 60)
            {
                _logger.LogWarning("AUTOPILOT_VERSION payload too short: {Len}", payload.Length);
                return;
            }

            // Only accept from autopilot component (compId = 1)
            if (compId != 1)
            {
                _logger.LogDebug("Ignoring AUTOPILOT_VERSION from non-autopilot component {CompId}", compId);
                return;
            }

            var versionData = new AutopilotVersionData
            {
                SystemId = sysId,
                ComponentId = compId,
                Capabilities = BitConverter.ToUInt64(payload, 0),
                FlightSwVersion = BitConverter.ToUInt32(payload, 8),
                MiddlewareSwVersion = BitConverter.ToUInt32(payload, 12),
                OsSwVersion = BitConverter.ToUInt32(payload, 16),
                BoardVersion = BitConverter.ToUInt32(payload, 20),
                VendorId = BitConverter.ToUInt16(payload, 48),
                ProductId = BitConverter.ToUInt16(payload, 50)
            };

            // Extract flight_custom_version (git hash)
            versionData.FlightCustomVersion = new byte[8];
            Array.Copy(payload, 24, versionData.FlightCustomVersion, 0, 8);

            // Extract uid
            versionData.Uid = new byte[8];
            Array.Copy(payload, 52, versionData.Uid, 0, 8);

            // Extract uid2 (MAVLink v2 extension) - THE FC ID SOURCE
            if (payload.Length >= 78)
            {
                versionData.Uid2 = new byte[18];
                Array.Copy(payload, 60, versionData.Uid2, 0, 18);
            }

            // Decode ArduPilot version
            DecodeArduPilotVersion(versionData);

            _logger.LogInformation("AUTOPILOT_VERSION: FW={Fw}, Board=0x{Board:X8}, FC_ID={FcId}",
                versionData.FirmwareVersionString, versionData.BoardVersion, versionData.GetFcId());

            AutopilotVersionReceived?.Invoke(this, versionData);
        }

        /// <summary>
        /// Decode ArduPilot firmware version from flight_sw_version field
        /// Format: MAJOR.MINOR.PATCH (RELEASE_TYPE)
        /// </summary>
        private void DecodeArduPilotVersion(AutopilotVersionData data)
        {
            // ArduPilot encoding (little-endian):
            // Byte 0: patch version (0-255)
            // Byte 1: minor version (0-255)
            // Byte 2: major version (0-255)
            // Byte 3: firmware type (0=stable, 64=beta, 255=dev)
            
            byte patch = (byte)(data.FlightSwVersion & 0xFF);
            byte minor = (byte)((data.FlightSwVersion >> 8) & 0xFF);
            byte major = (byte)((data.FlightSwVersion >> 16) & 0xFF);
            byte firmwareType = (byte)((data.FlightSwVersion >> 24) & 0xFF);

            data.FirmwareMajor = major;
            data.FirmwareMinor = minor;
            data.FirmwarePatch = patch;
            data.FirmwareType = firmwareType;

            string releaseType = firmwareType switch
            {
                0 => "Stable",
                64 => "Beta",
                255 => "Dev",
                _ => $"Type{firmwareType}"
            };

            data.FirmwareVersionString = $"{major}.{minor}.{patch} ({releaseType})";
        }

        /// <summary>
        /// Validate uid2 is not all zeros
        /// </summary>
        private static bool IsValidUid2(byte[] uid2)
        {
            if (uid2 == null || uid2.Length != 18)
                return false;

            // Check if all bytes are zero
            foreach (byte b in uid2)
            {
                if (b != 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Convert uid2 to FC ID string
        /// Format: FC-{UID2_HEX_UPPERCASE_NO_SEPARATORS}
        /// Example: FC-4A9F3C01A2887E5500119C4D3A01FF88902B
        /// </summary>
        private static string ConvertUid2ToFcId(byte[] uid2)
        {
            if (!IsValidUid2(uid2))
                return "FC-INVALID";

            var hex = BitConverter.ToString(uid2).Replace("-", "").ToUpperInvariant();
            return $"FC-{hex}";
        }

        public async Task SendParamRequestListAsync(CancellationToken ct = default)
        {
            // PARAM_REQUEST_LIST payload (2 bytes):
            // [0] target_system
            // [1] target_component
            var payload = new byte[2];
            payload[0] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[1] = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            _logger.LogInformation("Sending PARAM_REQUEST_LIST to sysid={SysId} compid={CompId}", payload[0], payload[1]);
            await SendMessageAsync(MAVLINK_MSG_ID_PARAM_REQUEST_LIST, payload, ct);
        }

        public async Task SendParamRequestReadAsync(ushort paramIndex, CancellationToken ct = default)
        {
            // PARAM_REQUEST_READ payload (20 bytes):
            // [0-1]   param_index (int16)
            // [2]     target_system
            // [3]     target_component
            // [4-19]  param_id (char[16]) - empty when using index
            var payload = new byte[20];

            // Write param_index as int16
            payload[0] = (byte)(paramIndex & 0xFF);
            payload[1] = (byte)((paramIndex >> 8) & 0xFF);
            payload[2] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[3] = _targetComponentId != 0 ? _targetComponentId : (byte)1;
            // param_id bytes 4-19 stay zero when requesting by index

            await SendMessageAsync(MAVLINK_MSG_ID_PARAM_REQUEST_READ, payload, ct);
        }

        public async Task SendParamSetAsync(string name, float value, CancellationToken ct = default)
        {
            // PARAM_SET payload (23 bytes):
            // [0-3]   param_value (float)
            // [4]     target_system
            // [5]     target_component
            // [6-21]  param_id (char[16])
            // [22]    param_type (uint8) - 9 = MAV_PARAM_TYPE_REAL32
            var payload = new byte[23];

            BitConverter.GetBytes(value).CopyTo(payload, 0);
            payload[4] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[5] = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            var nameBytes = Encoding.ASCII.GetBytes(name);
            Array.Copy(nameBytes, 0, payload, 6, Math.Min(16, nameBytes.Length));

            payload[22] = 9; // MAV_PARAM_TYPE_REAL32

            _logger.LogInformation("Sending PARAM_SET: {Name}={Value}", name, value);
            await SendMessageAsync(MAVLINK_MSG_ID_PARAM_SET, payload, ct);
        }

        /// <summary>
        /// Send DO_MOTOR_TEST command (MAV_CMD = 209)
        /// Returns CommandResult to indicate success/failure
        /// </summary>
        /// <param name="motorInstance">Motor instance (1-based)</param>
        /// <param name="throttleType">Throttle type (0=percent, 1=PWM, 2=pilot)</param>
        /// <param name="throttleValue">Throttle value (percent or PWM)</param>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="motorCount">Motor count (0=single motor test)</param>
        /// <param name="testOrder">Test order (0=default, 1=sequence)</param>
        public async Task<CommandResult> SendMotorTestAsync(
            int motorInstance,
            int throttleType,
            float throttleValue,
            float timeout,
            int motorCount = 0,
            int testOrder = 0,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Sending DO_MOTOR_TEST: motor={Motor} throttle={Throttle} timeout={Timeout}s",
                motorInstance, throttleValue, timeout);

            return await SendCommandLongAsync(
                MAV_CMD_DO_MOTOR_TEST,
                motorInstance,      // param1: motor instance
                throttleType,       // param2: throttle type
                throttleValue,      // param3: throttle value
                timeout,            // param4: timeout
                motorCount,         // param5: motor count
                testOrder,          // param6: test order
                0,                  // param7: empty
                ct);
        }

        /// <summary>
        /// Send MAV_CMD_PREFLIGHT_CALIBRATION command
        /// NOTE: For accel calibration (accel >= 1), MissionPlanner does NOT wait for ACK!
        /// </summary>
        public async Task SendPreflightCalibrationAsync(
            int gyro = 0,
            int mag = 0,
            int groundPressure = 0,
            int airspeed = 0,
            int accel = 0,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_CALIBRATION: gyro={Gyro} mag={Mag} baro={Baro} airspeed={Airspeed} accel={Accel}",
                gyro, mag, groundPressure, airspeed, accel);

            // Log to MAVLink logger
            _mavLinkLogger?.LogOutgoing("COMMAND_LONG", 
                $"cmd=MAV_CMD_PREFLIGHT_CALIBRATION(241), param1(gyro)={gyro}, param2(mag)={mag}, param3(baro)={groundPressure}, param4(airspeed)={airspeed}, param5(accel)={accel}");

            // CRITICAL: MissionPlanner behavior for accel calibration:
            // "imu calib take a little while" - it does NOT wait for ACK!
            // It returns immediately and relies on STATUSTEXT/COMMAND_LONG subscriptions.
            // This applies to ALL accel calibration modes (1, 2, 4, etc.)
            if (accel >= 1)
            {
                _logger.LogInformation("[PREFLIGHT_CAL] Accel calibration (param5={Accel}) - sending fire-and-forget (no ACK wait, per MissionPlanner)", accel);
                await SendCommandLongFireAndForgetAsync(
                    MAV_CMD_PREFLIGHT_CALIBRATION,
                    gyro, mag, groundPressure, airspeed, accel, 0, 0, ct);
                return;
            }

            // For non-accel calibrations, wait for ACK normally
            await SendCommandLongAsync(
                MAV_CMD_PREFLIGHT_CALIBRATION,
                gyro,           // param1: gyroscope
                mag,            // param2: magnetometer
                groundPressure, // param3: ground pressure / barometer
                airspeed,       // param4: radio / airspeed
                accel,          // param5: accelerometer
                0,              // param6: compmot / none
                0,              // param7: none
                ct);
        }

        /// <summary>
        /// Send MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN command
        /// </summary>
        /// <param name="autopilot">0=do nothing, 1=reboot autopilot, 2=shutdown autopilot, 3=reboot to bootloader</param>
        /// <param name="companion">0=do nothing, 1=reboot companion, 2=shutdown companion</param>
        public async Task SendPreflightRebootAsync(int autopilot = 1, int companion = 0, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN: autopilot={Autopilot} companion={Companion}",
                autopilot, companion);

            await SendCommandLongAsync(
                MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN,
                autopilot,  // param1: autopilot
                companion,  // param2: companion
                0, 0, 0, 0, 0,
                ct);
        }

        /// <summary>
        /// Send arm/disarm command
        /// </summary>
        /// <param name="arm">True to arm, false to disarm</param>
        /// <param name="force">True to force arm/disarm</param>
        public async Task SendArmDisarmAsync(bool arm, bool force = false, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_COMPONENT_ARM_DISARM: arm={Arm} force={Force}", arm, force);

            await SendCommandLongAsync(
                MAV_CMD_COMPONENT_ARM_DISARM,
                arm ? 1 : 0,        // param1: 1=arm, 0=disarm
                force ? 21196 : 0,  // param2: 21196=force arm/disarm
                0, 0, 0, 0, 0,
                ct);
        }

        /// <summary>
        /// Send MAV_CMD_ACCELCAL_VEHICLE_POS command for accelerometer calibration
        /// MissionPlanner sends this as fire-and-forget (no ACK wait)
        /// </summary>
        /// <param name="position">Vehicle position (1-6 for different orientations)</param>
        public async Task SendAccelCalVehiclePosAsync(int position, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_ACCELCAL_VEHICLE_POS: position={Position}", position);
            
            // Log to MAVLink logger
            string posName = position switch
            {
                1 => "LEVEL",
                2 => "LEFT",
                3 => "RIGHT",
                4 => "NOSE_DOWN",
                5 => "NOSE_UP",
                6 => "BACK",
                _ => position.ToString()
            };
            _mavLinkLogger?.LogOutgoing("COMMAND_LONG", 
                $"cmd=MAV_CMD_ACCELCAL_VEHICLE_POS(42429), param1(position)={position} ({posName})");
            
            // MissionPlanner sends this as fire-and-forget
            // sendPacket(mavlink_command_long_t { param1=pos, command=ACCELCAL_VEHICLE_POS })
            await SendCommandLongFireAndForgetAsync(
                MAV_CMD_ACCELCAL_VEHICLE_POS,
                position,   // param1: position (1-6)
                0, 0, 0, 0, 0, 0,
                ct);
        }

        /// <summary>
        /// Send a raw COMMAND_LONG packet without any ACK handling.
        /// This matches Mission Planner's sendPacket() behavior.
        /// Used for: MAV_CMD_ACCELCAL_VEHICLE_POS during accel calibration
        /// SYNCHRONOUS - does not wait for anything
        /// </summary>
        public void SendPacketRaw(ushort command, float param1, float param2 = 0, float param3 = 0, 
            float param4 = 0, float param5 = 0, float param6 = 0, float param7 = 0)
        {
            if (_outputStream == null)
            {
                _logger.LogWarning("SendPacketRaw: Output stream is null");
                return;
            }

            _logger.LogInformation("SendPacketRaw: cmd={Command} param1={Param1}", command, param1);

            // COMMAND_LONG payload (33 bytes)
            var payload = new byte[33];

            BitConverter.GetBytes(param1).CopyTo(payload, 0);
            BitConverter.GetBytes(param2).CopyTo(payload, 4);
            BitConverter.GetBytes(param3).CopyTo(payload, 8);
            BitConverter.GetBytes(param4).CopyTo(payload, 12);
            BitConverter.GetBytes(param5).CopyTo(payload, 16);
            BitConverter.GetBytes(param6).CopyTo(payload, 20);
            BitConverter.GetBytes(param7).CopyTo(payload, 24);
            BitConverter.GetBytes(command).CopyTo(payload, 28);
            payload[30] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[31] = _targetComponentId != 0 ? _targetComponentId : (byte)1;
            payload[32] = 0; // confirmation

            // Send synchronously, no waiting for ACK
            lock (_writeLock)
            {
                try
                {
                    byte payloadLen = (byte)payload.Length;
                    byte seq = _packetSequence++;

                    int frameLen = 8 + payloadLen;
                    var frame = new byte[frameLen];

                    frame[0] = MAVLINK_STX_V1;
                    frame[1] = payloadLen;
                    frame[2] = seq;
                    frame[3] = GCS_SYSTEM_ID;
                    frame[4] = GCS_COMPONENT_ID;
                    frame[5] = MAVLINK_MSG_ID_COMMAND_LONG;

                    Array.Copy(payload, 0, frame, 6, payloadLen);

                    ushort crc = CalculateCrc(frame, 1, 5 + payloadLen, CRC_EXTRA_COMMAND_LONG);
                    frame[6 + payloadLen] = (byte)(crc & 0xFF);
                    frame[7 + payloadLen] = (byte)((crc >> 8) & 0xFF);

                    _outputStream.Write(frame, 0, frameLen);
                    _outputStream.Flush();

                    _logger.LogTrace("Sent raw packet: cmd={Cmd} seq={Seq}", command, seq);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending raw packet");
                }
            }
        }

        /// <summary>
        /// Send MAV_CMD_ACCELCAL_VEHICLE_POS - Mission Planner style (fire and forget)
        /// This is the EXACT behavior of Mission Planner's sendPacket() method.
        /// Does NOT wait for COMMAND_ACK - FC responds via STATUSTEXT/COMMAND_LONG
        /// </summary>
        public void SendAccelCalVehiclePosRaw(int position)
        {
            string posName = position switch
            {
                1 => "LEVEL",
                2 => "LEFT",
                3 => "RIGHT",
                4 => "NOSE_DOWN",
                5 => "NOSE_UP",
                6 => "BACK",
                _ => position.ToString()
            };
            
            _logger.LogInformation("SendAccelCalVehiclePosRaw: position={Position} ({Name}) [FIRE-AND-FORGET]", position, posName);
            _mavLinkLogger?.LogOutgoing("COMMAND_LONG", 
                $"cmd=MAV_CMD_ACCELCAL_VEHICLE_POS(42429), param1={position} ({posName}) [RAW/NO-ACK]");

            SendPacketRaw(MAV_CMD_ACCELCAL_VEHICLE_POS, position);
        }

        private async Task SendMessageAsync(byte msgId, byte[] payload, CancellationToken ct)
        {
            if (_outputStream == null)
                return;

            lock (_writeLock)
            {
                try
                {
                    // Build MAVLink v1 frame
                    byte payloadLen = (byte)payload.Length;
                    byte seq = _packetSequence++;

                    // Frame: STX + LEN + SEQ + SYSID + COMPID + MSGID + PAYLOAD + CRC(2)
                    int frameLen = 8 + payloadLen;
                    var frame = new byte[frameLen];

                    frame[0] = MAVLINK_STX_V1;
                    frame[1] = payloadLen;
                    frame[2] = seq;
                    frame[3] = GCS_SYSTEM_ID;
                    frame[4] = GCS_COMPONENT_ID;
                    frame[5] = msgId;

                    Array.Copy(payload, 0, frame, 6, payloadLen);

                    // Calculate and append CRC
                    ushort crc = CalculateCrc(frame, 1, 5 + payloadLen, GetCrcExtra(msgId));
                    frame[6 + payloadLen] = (byte)(crc & 0xFF);
                    frame[7 + payloadLen] = (byte)((crc >> 8) & 0xFF);

                    _outputStream.Write(frame, 0, frameLen);
                    _outputStream.Flush();

                    _logger.LogTrace("Sent MAVLink message: msgId={MsgId} len={Len} seq={Seq}", msgId, payloadLen, seq);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending MAVLink message");
                    throw;
                }
            }
        }

        private ushort CalculateCrc(byte[] buffer, int offset, int length, byte crcExtra)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i < length; i++)
            {
                byte b = buffer[offset + i];
                b ^= (byte)(crc & 0xFF);
                b ^= (byte)(b << 4);
                crc = (ushort)((crc >> 8) ^ (b << 8) ^ (b << 3) ^ (b >> 4));
            }

            // Add CRC_EXTRA
            byte extra = crcExtra;
            extra ^= (byte)(crc & 0xFF);
            extra ^= (byte)(extra << 4);
            crc = (ushort)((crc >> 8) ^ (extra << 8) ^ (extra << 3) ^ (extra >> 4));

            return crc;
        }

        private byte GetCrcExtra(byte msgId)
        {
            return MavlinkProtocol.GetCrcExtra(msgId);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                // Clear all pending transactions
                _transactionManager.ClearAll();
                
                _cts?.Cancel();
                _readTask?.Wait(TimeSpan.FromSeconds(2));
                _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
                _watchdogTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MAVLink wrapper");
            }
            finally
            {
                _transactionManager.Dispose();
                _cts?.Dispose();
                _readTask?.Dispose();
                _heartbeatTask?.Dispose();
                _watchdogTask?.Dispose();
            }

            _logger.LogInformation("MAVLink wrapper disposed");
        }

        /// <summary>
        /// Send MAV_CMD_PREFLIGHT_STORAGE command to reset all parameters to default
        /// param1 = 0: Read params from storage
        /// param1 = 1: Write params to storage
        /// param1 = 2: Reset params to default
        /// </summary>
        public async Task SendResetParametersAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_STORAGE: param1=2 (reset to defaults)");

            await SendCommandLongAsync(
                MAV_CMD_PREFLIGHT_STORAGE,
                2,  // param1: 2 = reset all params to defaults
                0,  // param2: mission storage (not used)
                0,  // param3: logging rate (not used)
                0, 0, 0, 0,
                ct);
        }

        /// <summary>
        /// Send MAV_CMD_FLASH_BOOTLOADER command to update the bootloader
        /// Magic value 290876 confirms the operation
        /// </summary>
        /// <param name="magicValue">Magic confirmation value (290876)</param>
        public async Task SendFlashBootloaderAsync(int magicValue, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_FLASH_BOOTLOADER: magic={Magic}", magicValue);

            const ushort MAV_CMD_FLASH_BOOTLOADER = 42650;

            await SendCommandLongAsync(
                MAV_CMD_FLASH_BOOTLOADER,
                0,          // param1: unused
                0,          // param2: unused
                0,          // param3: unused
                0,          // param4: unused
                magicValue, // param5: magic number (290876)
                0,          // param6: unused
                0,          // param7: unused
                ct);
        }

        /// <summary>
        /// Send MAV_CMD_SET_MESSAGE_INTERVAL to request specific message rate
        /// Used to force IMU messages at 50Hz during accelerometer calibration
        /// </summary>
        /// <param name="messageId">MAVLink message ID (27=RAW_IMU, 26=SCALED_IMU)</param>
        /// <param name="intervalUs">Interval in microseconds (20000 = 50Hz, -1 = default rate, 0 = stop)</param>
        public async Task SendSetMessageIntervalAsync(int messageId, int intervalUs, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_SET_MESSAGE_INTERVAL: msgId={MsgId} interval={Interval}us ({Hz}Hz)",
                messageId, intervalUs, intervalUs > 0 ? 1000000.0 / intervalUs : 0);

            await SendCommandLongAsync(
                MAV_CMD_SET_MESSAGE_INTERVAL,
                messageId,      // param1: message ID
                intervalUs,     // param2: interval in microseconds
                0, 0, 0, 0, 0,
                ct);
        }

        /// <summary>
        /// Send REQUEST_DATA_STREAM (legacy fallback for older firmware)
        /// Used when SET_MESSAGE_INTERVAL is not supported
        /// </summary>
        /// <param name="streamId">Stream ID (1=RAW_SENSORS includes IMU)</param>
        /// <param name="rateHz">Rate in Hz (50 = 50Hz)</param>
        /// <param name="startStop">1=start, 0=stop</param>
        public async Task SendRequestDataStreamAsync(int streamId, int rateHz, int startStop, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending REQUEST_DATA_STREAM: streamId={StreamId} rate={Rate}Hz start={Start}",
                streamId, rateHz, startStop);

            // REQUEST_DATA_STREAM payload (6 bytes):
            const byte MAVLINK_MSG_ID_REQUEST_DATA_STREAM = 66;

            var payload = new byte[6];
            payload[0] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[1] = _targetComponentId != 0 ? _targetComponentId : (byte)1;
            payload[2] = (byte)streamId;
            BitConverter.GetBytes((ushort)rateHz).CopyTo(payload, 3);
            payload[5] = (byte)startStop;

            await SendMessageAsync(MAVLINK_MSG_ID_REQUEST_DATA_STREAM, payload, ct);
        }

        /// <summary>
        /// Send COMMAND_LONG message WITHOUT waiting for ACK (fire-and-forget).
        /// Used for accel calibration commands per MissionPlanner behavior.
        /// </summary>
        private async Task SendCommandLongFireAndForgetAsync(
            ushort command,
            float param1, float param2, float param3, float param4,
            float param5, float param6, float param7,
            CancellationToken ct = default)
        {
            // COMMAND_LONG payload (33 bytes)
            var payload = new byte[33];

            BitConverter.GetBytes(param1).CopyTo(payload, 0);
            BitConverter.GetBytes(param2).CopyTo(payload, 4);
            BitConverter.GetBytes(param3).CopyTo(payload, 8);
            BitConverter.GetBytes(param4).CopyTo(payload, 12);
            BitConverter.GetBytes(param5).CopyTo(payload, 16);
            BitConverter.GetBytes(param6).CopyTo(payload, 20);
            BitConverter.GetBytes(param7).CopyTo(payload, 24);
            BitConverter.GetBytes(command).CopyTo(payload, 28);
            payload[30] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[31] = _targetComponentId != 0 ? _targetComponentId : (byte)1;
            payload[32] = 0; // confirmation

            // Send command - NO ACK WAIT
            await SendMessageAsync(MAVLINK_MSG_ID_COMMAND_LONG, payload, ct);
            
            _logger.LogInformation("COMMAND_LONG sent (fire-and-forget): cmd={Command} param1={Param1}", command, param1);
        }

        /// <summary>
        /// Send COMMAND_LONG message with transaction tracking and retry logic
        /// Production-grade reliability
        /// </summary>
        private async Task<CommandResult> SendCommandLongAsync(
            ushort command,
            float param1, float param2, float param3, float param4,
            float param5, float param6, float param7,
            CancellationToken ct = default)
        {
            // Register transaction for ACK tracking
            var resultTask = _transactionManager.RegisterCommandAsync(command, TimeSpan.FromSeconds(5), maxRetries: 3);

            // COMMAND_LONG payload (33 bytes)
            var payload = new byte[33];

            BitConverter.GetBytes(param1).CopyTo(payload, 0);
            BitConverter.GetBytes(param2).CopyTo(payload, 4);
            BitConverter.GetBytes(param3).CopyTo(payload, 8);
            BitConverter.GetBytes(param4).CopyTo(payload, 12);
            BitConverter.GetBytes(param5).CopyTo(payload, 16);
            BitConverter.GetBytes(param6).CopyTo(payload, 20);
            BitConverter.GetBytes(param7).CopyTo(payload, 24);
            BitConverter.GetBytes(command).CopyTo(payload, 28);
            payload[30] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[31] = _targetComponentId != 0 ? _targetComponentId : (byte)1;
            payload[32] = 0; // confirmation

            // Send command
            await SendMessageAsync(MAVLINK_MSG_ID_COMMAND_LONG, payload, ct);
            
            // Wait for ACK with retry logic
            try
            {
                var result = await resultTask;
                
                if (result != CommandResult.Accepted)
                {
                    _logger.LogWarning("Command {Command} result: {Result}", command, result);
                }
                
                return result;
            }
            catch (TimeoutException ex)
            {
                _logger.LogError("Command {Command} timed out: {Message}", command, ex.Message);
                throw;
            }
        }
    }

    /// <summary>
    /// Heartbeat data from vehicle
    /// </summary>
    public class HeartbeatData
    {
        public byte SystemId { get; set; }
        public byte ComponentId { get; set; }
        public uint CustomMode { get; set; }
        public byte VehicleType { get; set; }
        public byte Autopilot { get; set; }
        public byte BaseMode { get; set; }
        public byte SystemStatus { get; set; }
        public byte MavlinkVersion { get; set; }
        public bool IsArmed { get; set; }
    }

    /// <summary>
    /// RC channels data
    /// </summary>
    public class RcChannelsData
    {
        public uint TimeBootMs { get; set; }
        public ushort Channel1 { get; set; }
        public ushort Channel2 { get; set; }
        public ushort Channel3 { get; set; }
        public ushort Channel4 { get; set; }
        public ushort Channel5 { get; set; }
        public ushort Channel6 { get; set; }
        public ushort Channel7 { get; set; }
        public ushort Channel8 { get; set; }
        public byte ChannelCount { get; set; }
        public byte Rssi { get; set; }
    }

    /// <summary>
    /// Raw IMU data from vehicle
    /// </summary>
    public class RawImuData
    {
        public ulong TimeUsec { get; set; }
        public short XAcc { get; set; }
        public short YAcc { get; set; }
        public short ZAcc { get; set; }
        public short XGyro { get; set; }
        public short YGyro { get; set; }
        public short ZGyro { get; set; }
        public short XMag { get; set; }
        public short YMag { get; set; }
        public short ZMag { get; set; }
        public byte Id { get; set; }
        public short Temperature { get; set; }
        public bool IsScaled { get; set; }

        /// <summary>
        /// Get acceleration in m/s² (scaled)
        /// </summary>
        public (double X, double Y, double Z) GetAcceleration()
        {
            if (IsScaled)
            {
                // SCALED_IMU: values are in milli-g, convert to m/s²
                const double MILLI_G_TO_MS2 = 0.00981; // 1 milli-g = 0.00981 m/s²
                return (XAcc * MILLI_G_TO_MS2, YAcc * MILLI_G_TO_MS2, ZAcc * MILLI_G_TO_MS2);
            }
            else
            {
                // RAW_IMU: values are raw ADC, typical scale is 1/1000 g per LSB for most IMUs
                // This varies by sensor, typical MPU6000/9250: 16-bit, ±16g range = 32g / 65536 = 0.000488 g/LSB
                const double RAW_TO_MS2 = 0.00478; // Approximate conversion for ±16g range
                return (XAcc * RAW_TO_MS2, YAcc * RAW_TO_MS2, ZAcc * RAW_TO_MS2);
            }
        }

        /// <summary>
        /// Get gyro in rad/s (scaled)
        /// </summary>
        public (double X, double Y, double Z) GetGyro()
        {
            if (IsScaled)
            {
                // SCALED_IMU: values are in milli-rad/s
                const double MILLI_RAD_TO_RAD = 0.001;
                return (XGyro * MILLI_RAD_TO_RAD, YGyro * MILLI_RAD_TO_RAD, ZGyro * MILLI_RAD_TO_RAD);
            }
            else
            {
                // RAW_IMU: values are raw ADC
                const double RAW_TO_RAD = 0.0001; // Approximate
                return (XGyro * RAW_TO_RAD, YGyro * RAW_TO_RAD, ZGyro * RAW_TO_RAD);
            }
        }

        /// <summary>
        /// Get temperature in °C
        /// </summary>
        public double GetTemperature()
        {
            return Temperature / 100.0; // Temperature is in centi-degrees
        }
    }

    /// <summary>
    /// AUTOPILOT_VERSION data from vehicle
    /// Contains FC ID (from uid2), firmware version, and board information
    /// </summary>
    public class AutopilotVersionData
    {
        public byte SystemId { get; set; }
        public byte ComponentId { get; set; }
        public ulong Capabilities { get; set; }
        public uint FlightSwVersion { get; set; }
        public uint MiddlewareSwVersion { get; set; }
        public uint OsSwVersion { get; set; }
        public uint BoardVersion { get; set; }
        public byte[]? FlightCustomVersion { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public byte[]? Uid { get; set; }
        public byte[]? Uid2 { get; set; }
        
        // Decoded firmware version fields
        public byte FirmwareMajor { get; set; }
        public byte FirmwareMinor { get; set; }
        public byte FirmwarePatch { get; set; }
        public byte FirmwareType { get; set; }
        public string FirmwareVersionString { get; set; } = "N/A";

        /// <summary>
        /// Get FC ID from uid2 field.
        /// This is the ONLY reliable source for FC ID.
        /// Format: FC-{UID2_HEX_UPPERCASE}
        /// </summary>
        public string GetFcId()
        {
            if (Uid2 == null || Uid2.Length != 18)
                return "FC-UNAVAILABLE";

            // Check if all zeros (invalid)
            bool allZeros = true;
            foreach (byte b in Uid2)
            {
                if (b != 0)
                {
                    allZeros = false;
                    break;
                }
            }

            if (allZeros)
                return "FC-UNAVAILABLE";

            var hex = BitConverter.ToString(Uid2).Replace("-", "").ToUpperInvariant();
            return $"FC-{hex}";
        }

        /// <summary>
        /// Get git hash from flight_custom_version field.
        /// First 8 bytes of git commit hash.
        /// </summary>
        public string GetGitHash()
        {
            if (FlightCustomVersion == null || FlightCustomVersion.Length == 0)
                return "N/A";

            // Check if all zeros
            bool allZeros = true;
            foreach (byte b in FlightCustomVersion)
            {
                if (b != 0)
                {
                    allZeros = false;
                    break;
                }
            }

            if (allZeros)
                return "N/A";

            // Convert to hex string (first 8 bytes of git hash)
            var hex = BitConverter.ToString(FlightCustomVersion).Replace("-", "").ToLowerInvariant();
            return hex;
        }
    }

    /// <summary>
    /// COMMAND_LONG data from flight controller
    /// Used to receive position requests during accelerometer calibration
    /// </summary>
    public class CommandLongData
    {
        public byte SystemId { get; set; }
        public byte ComponentId { get; set; }
        public ushort Command { get; set; }
        public float Param1 { get; set; }
        public float Param2 { get; set; }
        public float Param3 { get; set; }
        public float Param4 { get; set; }
        public float Param5 { get; set; }
        public float Param6 { get; set; }
        public float Param7 { get; set; }
        public byte TargetSystem { get; set; }
        public byte TargetComponent { get; set; }
        public byte Confirmation { get; set; }
    }
}