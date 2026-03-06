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
        private const byte MAVLINK_MSG_ID_MAG_CAL_PROGRESS = 191;
        private const byte MAVLINK_MSG_ID_MAG_CAL_REPORT = 192;

        // MAV_CMD IDs
        private const ushort MAV_CMD_DO_MOTOR_TEST = 209;
        private const ushort MAV_CMD_PREFLIGHT_CALIBRATION = 241;
        private const ushort MAV_CMD_PREFLIGHT_STORAGE = 245;
        private const ushort MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN = 246;
        private const ushort MAV_CMD_SET_MESSAGE_INTERVAL = 511;
        private const ushort MAV_CMD_COMPONENT_ARM_DISARM = 400;
        private const ushort MAV_CMD_ACCELCAL_VEHICLE_POS = 42429;
        private const ushort MAV_CMD_REQUEST_MESSAGE = 512;
        private const ushort MAV_CMD_DO_START_MAG_CAL = 42424;
        private const ushort MAV_CMD_DO_ACCEPT_MAG_CAL = 42425;
        private const ushort MAV_CMD_DO_CANCEL_MAG_CAL = 42426;

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
        public event EventHandler<MagCalProgressData>? MagCalProgressReceived;
        public event EventHandler<MagCalReportData>? MagCalReportReceived;

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
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
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
                for (int i = 0; i < length; i++)
                {
                    if (_rxBufferPos >= _rxBuffer.Length)
                    {
                        _rxBufferPos = 0;
                    }
                    _rxBuffer[_rxBufferPos++] = data[i];
                }
                ProcessBuffer();
            }
        }

        private void ProcessBuffer()
        {
            while (_rxBufferPos > 0)
            {
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
                    _rxBufferPos = 0;
                    return;
                }

                if (startIdx > 0)
                {
                    Array.Copy(_rxBuffer, startIdx, _rxBuffer, 0, _rxBufferPos - startIdx);
                    _rxBufferPos -= startIdx;
                }

                if (_rxBufferPos < 8)
                    return;

                byte stx = _rxBuffer[0];
                int frameLen;

                if (stx == MAVLINK_STX_V1)
                {
                    byte payloadLen = _rxBuffer[1];
                    frameLen = 8 + payloadLen;
                }
                else
                {
                    if (_rxBufferPos < 12)
                        return;
                    byte payloadLen = _rxBuffer[1];
                    byte incompatFlags = _rxBuffer[2];
                    bool hasSignature = (incompatFlags & 0x01) != 0;
                    frameLen = 12 + payloadLen + (hasSignature ? 13 : 0);
                }

                if (_rxBufferPos < frameLen)
                    return;

                var frame = new byte[frameLen];
                Array.Copy(_rxBuffer, 0, frame, 0, frameLen);
                Array.Copy(_rxBuffer, frameLen, _rxBuffer, 0, _rxBufferPos - frameLen);
                _rxBufferPos -= frameLen;

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

            ushort crcCalc = CalculateCrc(frame, 1, payloadLen + 5, MavlinkProtocol.GetCrcExtra(msgId));
            ushort crcRecv = (ushort)(frame[6 + payloadLen] | (frame[7 + payloadLen] << 8));

            if (crcCalc != crcRecv)
            {
                _logger.LogTrace("V1 CRC mismatch: msg={Msg} calc=0x{Calc:X4} recv=0x{Recv:X4}", msgId, crcCalc, crcRecv);
                return;
            }

            if (!MavlinkProtocol.IsKnownMessage(msgId))
            {
                _logger.LogTrace("Unknown message ID: {MsgId}", msgId);
            }

            var payload = new byte[payloadLen];
            Array.Copy(frame, 6, payload, 0, payloadLen);

            HandleMessage(sysId, compId, msgId, payload);
        }

        private void ProcessMavlink2Frame(byte[] frame)
        {
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

            int msgId = MavlinkProtocol.GetV2MessageId(frame);
            if (msgId < 0)
            {
                _logger.LogWarning("Failed to extract v2 message ID");
                return;
            }

            ushort crcCalc = CalculateCrc(frame, 1, 9 + payloadLen, MavlinkProtocol.GetCrcExtra(msgId));
            int crcOffset = 10 + payloadLen;
            ushort crcRecv = (ushort)(frame[crcOffset] | (frame[crcOffset + 1] << 8));

            if (crcCalc != crcRecv)
            {
                _logger.LogTrace("V2 CRC mismatch: msg={Msg} calc=0x{Calc:X4} recv=0x{Recv:X4}", msgId, crcCalc, crcRecv);
                return;
            }

            if (!MavlinkProtocol.IsKnownMessage(msgId))
            {
                _logger.LogTrace("Unknown v2 message ID: {MsgId}", msgId);
            }

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
                case MAVLINK_MSG_ID_MAG_CAL_PROGRESS:
                    HandleMagCalProgress(payload);
                    break;
                case MAVLINK_MSG_ID_MAG_CAL_REPORT:
                    HandleMagCalReport(payload);
                    break;
            }
        }

        private void HandleHeartbeat(byte sysId, byte compId, byte[] payload)
        {
            if (compId == GCS_COMPONENT_ID || sysId == 0)
                return;

            _targetSystemId = sysId;
            _targetComponentId = compId;
            _lastHeartbeatReceived = DateTime.UtcNow;

            _logger.LogDebug("Heartbeat from FC: sysid={SysId} compid={CompId}", sysId, compId);

            HeartbeatReceived?.Invoke(this, (sysId, compId));

            if (payload.Length >= 9)
            {
                uint customMode = BitConverter.ToUInt32(payload, 0);
                byte vehicleType = payload[4];
                byte autopilot = payload[5];
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
                    IsArmed = (baseMode & 0x80) != 0
                };

                HeartbeatDataReceived?.Invoke(this, heartbeatData);
            }
        }

        private void HandleParamValue(byte[] payload)
        {
            if (payload.Length < 25)
            {
                _logger.LogWarning("PARAM_VALUE payload too short: {Len}", payload.Length);
                return;
            }

            float value = BitConverter.ToSingle(payload, 0);
            ushort paramCount = BitConverter.ToUInt16(payload, 4);
            ushort paramIndex = BitConverter.ToUInt16(payload, 6);
            string name = Encoding.ASCII.GetString(payload, 8, 16).TrimEnd('\0');
            byte paramType = payload[24];

            _logger.LogDebug("PARAM_VALUE: {Name}={Value} [{Index}/{Count}] type={Type}",
                name, value, paramIndex + 1, paramCount, paramType);

            _transactionManager.HandleParameterValue(name, value);
            ParamValueReceived?.Invoke(this, (name, value, paramIndex, paramCount));
        }

        private void HandleCommandAck(byte[] payload)
        {
            if (payload.Length < 3)
            {
                _logger.LogWarning("COMMAND_ACK payload too short: {Len}", payload.Length);
                return;
            }

            ushort command = BitConverter.ToUInt16(payload, 0);
            byte result = payload[2];

            _logger.LogDebug("COMMAND_ACK: cmd={Command} result={Result}", command, result);

            string cmdName = command switch
            {
                MAV_CMD_PREFLIGHT_CALIBRATION => "MAV_CMD_PREFLIGHT_CALIBRATION",
                MAV_CMD_ACCELCAL_VEHICLE_POS => "MAV_CMD_ACCELCAL_VEHICLE_POS",
                MAV_CMD_COMPONENT_ARM_DISARM => "MAV_CMD_COMPONENT_ARM_DISARM",
                MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN => "MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN",
                MAV_CMD_DO_MOTOR_TEST => "MAV_CMD_DO_MOTOR_TEST",
                MAV_CMD_DO_START_MAG_CAL => "MAV_CMD_DO_START_MAG_CAL",
                MAV_CMD_DO_ACCEPT_MAG_CAL => "MAV_CMD_DO_ACCEPT_MAG_CAL",
                MAV_CMD_DO_CANCEL_MAG_CAL => "MAV_CMD_DO_CANCEL_MAG_CAL",
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

            _transactionManager.HandleCommandAck(command, result);
            CommandAckReceived?.Invoke(this, (command, result));
        }

        private void HandleCommandLong(byte sysId, byte compId, byte[] payload)
        {
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
            if (payload.Length < 2)
                return;

            byte severity = payload[0];
            string text = Encoding.ASCII.GetString(payload, 1, Math.Min(50, payload.Length - 1)).TrimEnd('\0');

            _logger.LogDebug("STATUSTEXT: severity={Severity} text={Text}", severity, text);

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
            if (payload.Length < 22)
                return;

            var imuData = new RawImuData
            {
                TimeUsec = BitConverter.ToUInt32(payload, 0) * 1000UL,
                XAcc = BitConverter.ToInt16(payload, 4),
                YAcc = BitConverter.ToInt16(payload, 6),
                ZAcc = BitConverter.ToInt16(payload, 8),
                XGyro = BitConverter.ToInt16(payload, 10),
                YGyro = BitConverter.ToInt16(payload, 12),
                ZGyro = BitConverter.ToInt16(payload, 14),
                XMag = BitConverter.ToInt16(payload, 16),
                YMag = BitConverter.ToInt16(payload, 18),
                ZMag = BitConverter.ToInt16(payload, 20),
                IsScaled = true
            };

            if (payload.Length >= 24)
                imuData.Temperature = BitConverter.ToInt16(payload, 22);

            RawImuReceived?.Invoke(this, imuData);
        }

        private void HandleAutopilotVersion(byte sysId, byte compId, byte[] payload)
        {
            if (payload.Length < 60)
            {
                _logger.LogWarning("AUTOPILOT_VERSION payload too short: {Len}", payload.Length);
                return;
            }

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

            versionData.FlightCustomVersion = new byte[8];
            Array.Copy(payload, 24, versionData.FlightCustomVersion, 0, 8);

            versionData.Uid = new byte[8];
            Array.Copy(payload, 52, versionData.Uid, 0, 8);

            if (payload.Length >= 78)
            {
                versionData.Uid2 = new byte[18];
                Array.Copy(payload, 60, versionData.Uid2, 0, 18);
            }

            DecodeArduPilotVersion(versionData);

            _logger.LogInformation("AUTOPILOT_VERSION: FW={Fw}, Board=0x{Board:X8}, FC_ID={FcId}",
                versionData.FirmwareVersionString, versionData.BoardVersion, versionData.GetFcId());

            AutopilotVersionReceived?.Invoke(this, versionData);
        }

        private void HandleMagCalProgress(byte[] payload)
        {
            // MAG_CAL_PROGRESS message (27 bytes minimum):
            // [0-3]   direction_x (float)
            // [4-7]   direction_y (float)
            // [8-11]  direction_z (float)
            // [12]    compass_id (uint8)
            // [13]    cal_mask (uint8)
            // [14]    cal_status (uint8)
            // [15]    attempt (uint8)
            // [16]    completion_pct (uint8)
            // [17-26] completion_mask (uint8[10])
            if (payload.Length < 27)
                return;

            var progressData = new MagCalProgressData
            {
                DirectionX = BitConverter.ToSingle(payload, 0),
                DirectionY = BitConverter.ToSingle(payload, 4),
                DirectionZ = BitConverter.ToSingle(payload, 8),
                CompassId = payload[12],
                CalMask = payload[13],
                CalStatus = payload[14],
                Attempt = payload[15],
                CompletionPct = payload[16]
            };

            Array.Copy(payload, 17, progressData.CompletionMask, 0, 10);

            _logger.LogDebug("MAG_CAL_PROGRESS: compass={CompassId} status={Status} pct={Pct}%",
                progressData.CompassId, progressData.CalStatus, progressData.CompletionPct);

            MagCalProgressReceived?.Invoke(this, progressData);
        }

        private void HandleMagCalReport(byte[] payload)
        {
            // MAG_CAL_REPORT message (44+ bytes):
            // [0-3]   fitness (float)
            // [4-7]   ofs_x (float)
            // [8-11]  ofs_y (float)
            // [12-15] ofs_z (float)
            // [16-19] diag_x (float)
            // [20-23] diag_y (float)
            // [24-27] diag_z (float)
            // [28-31] offdiag_x (float)
            // [32-35] offdiag_y (float)
            // [36-39] offdiag_z (float)
            // [40]    compass_id (uint8)
            // [41]    cal_mask (uint8)
            // [42]    cal_status (uint8)
            // [43]    autosaved (uint8)
            // Extensions (MAVLink v2):
            // [44]    orientation_confidence (uint8)
            // [45]    old_orientation (uint8)
            // [46]    new_orientation (uint8)
            // [47-50] scale_factor (float)
            if (payload.Length < 44)
                return;

            var reportData = new MagCalReportData
            {
                Fitness = BitConverter.ToSingle(payload, 0),
                OfsX = BitConverter.ToSingle(payload, 4),
                OfsY = BitConverter.ToSingle(payload, 8),
                OfsZ = BitConverter.ToSingle(payload, 12),
                DiagX = BitConverter.ToSingle(payload, 16),
                DiagY = BitConverter.ToSingle(payload, 20),
                DiagZ = BitConverter.ToSingle(payload, 24),
                OffdiagX = BitConverter.ToSingle(payload, 28),
                OffdiagY = BitConverter.ToSingle(payload, 32),
                OffdiagZ = BitConverter.ToSingle(payload, 36),
                CompassId = payload[40],
                CalMask = payload[41],
                CalStatus = payload[42],
                Autosaved = payload[43]
            };

            // MAVLink v2 extensions
            if (payload.Length >= 47)
            {
                reportData.OrientationConfidence = payload[44];
                reportData.OldOrientation = payload[45];
                reportData.NewOrientation = payload[46];
            }
            if (payload.Length >= 51)
            {
                reportData.ScaleFactor = BitConverter.ToSingle(payload, 47);
            }

            _logger.LogInformation("MAG_CAL_REPORT: compass={CompassId} status={Status} fitness={Fitness} autosaved={Autosaved}",
                reportData.CompassId, reportData.CalStatus, reportData.Fitness, reportData.Autosaved);

            MagCalReportReceived?.Invoke(this, reportData);
        }

        private void DecodeArduPilotVersion(AutopilotVersionData data)
        {
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

        #region Send Methods

        public async Task SendParamRequestListAsync(CancellationToken ct = default)
        {
            var payload = new byte[2];
            payload[0] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[1] = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            _logger.LogInformation("Sending PARAM_REQUEST_LIST to sysid={SysId} compid={CompId}", payload[0], payload[1]);
            await SendMessageAsync(MAVLINK_MSG_ID_PARAM_REQUEST_LIST, payload, ct);
        }

        public async Task SendParamRequestReadAsync(ushort paramIndex, CancellationToken ct = default)
        {
            var payload = new byte[20];
            payload[0] = (byte)(paramIndex & 0xFF);
            payload[1] = (byte)((paramIndex >> 8) & 0xFF);
            payload[2] = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            payload[3] = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            await SendMessageAsync(MAVLINK_MSG_ID_PARAM_REQUEST_READ, payload, ct);
        }

        public async Task SendParamSetAsync(string name, float value, CancellationToken ct = default)
        {
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

        public async Task<CommandResult> SendMotorTestAsync(
            int motorInstance, int throttleType, float throttleValue, float timeout,
            int motorCount = 0, int testOrder = 0, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending DO_MOTOR_TEST: motor={Motor} throttle={Throttle} timeout={Timeout}s",
                motorInstance, throttleValue, timeout);

            return await SendCommandLongAsync(MAV_CMD_DO_MOTOR_TEST,
                motorInstance, throttleType, throttleValue, timeout, motorCount, testOrder, 0, ct);
        }

        public async Task SendPreflightCalibrationAsync(
            int gyro = 0, int mag = 0, int groundPressure = 0, int airspeed = 0, int accel = 0,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_CALIBRATION: gyro={Gyro} mag={Mag} baro={Baro} airspeed={Airspeed} accel={Accel}",
                gyro, mag, groundPressure, airspeed, accel);

            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                $"cmd=MAV_CMD_PREFLIGHT_CALIBRATION(241), param1(gyro)={gyro}, param2(mag)={mag}, param3(baro)={groundPressure}, param4(airspeed)={airspeed}, param5(accel)={accel}");

            // Only full 6-axis accelerometer calibration (param5=1) uses fire-and-forget
            // because it is a multi-step process where the FC drives the flow via
            // STATUSTEXT and COMMAND_LONG messages. Level horizon (param5=2) and other
            // simple calibrations must use SendCommandLongAsync so they get retries and
            // the COMMAND_ACK is properly awaited.
            if (accel == 1)
            {
                _logger.LogInformation("[PREFLIGHT_CAL] Full 6-axis accel calibration - sending fire-and-forget (per MissionPlanner)");
                await SendCommandLongFireAndForgetAsync(MAV_CMD_PREFLIGHT_CALIBRATION,
                    gyro, mag, groundPressure, airspeed, accel, 0, 0, ct);
                return;
            }

            await SendCommandLongAsync(MAV_CMD_PREFLIGHT_CALIBRATION,
                gyro, mag, groundPressure, airspeed, accel, 0, 0, ct);
        }

        /// <summary>
        /// Cancel any ongoing preflight calibration by sending MAV_CMD_PREFLIGHT_CALIBRATION with all zeros.
        /// This is how MissionPlanner cancels calibrations in ArduPilot.
        /// </summary>
        public async Task SendCancelPreflightCalibrationAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_CALIBRATION cancel (all params = 0)");

            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                "cmd=MAV_CMD_PREFLIGHT_CALIBRATION(241), CANCEL (all params = 0)");

            // Sending all zeros cancels any ongoing calibration
            await SendCommandLongFireAndForgetAsync(MAV_CMD_PREFLIGHT_CALIBRATION,
                0, 0, 0, 0, 0, 0, 0, ct);
        }

        public async Task SendPreflightRebootAsync(int autopilot = 1, int companion = 0, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN: autopilot={Autopilot} companion={Companion}",
                autopilot, companion);

            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                $"cmd=MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN(246), param1(autopilot)={autopilot}, param2(companion)={companion} [FIRE-AND-FORGET]");

            // Use fire-and-forget since the drone will reboot immediately and cannot send ACK
            // This ensures immediate reboot without waiting for a response that will never come
            await SendCommandLongFireAndForgetAsync(MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN, autopilot, companion, 0, 0, 0, 0, 0, ct);
        }

        public async Task SendArmDisarmAsync(bool arm, bool force = false, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_COMPONENT_ARM_DISARM: arm={Arm} force={Force}", arm, force);

            await SendCommandLongAsync(MAV_CMD_COMPONENT_ARM_DISARM,
                arm ? 1 : 0, force ? 21196 : 0, 0, 0, 0, 0, 0, ct);
        }

        public async Task SendAccelCalVehiclePosAsync(int position, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_ACCELCAL_VEHICLE_POS: position={Position}", position);

            string posName = position switch
            {
                1 => "LEVEL", 2 => "LEFT", 3 => "RIGHT",
                4 => "NOSE_DOWN", 5 => "NOSE_UP", 6 => "BACK",
                _ => position.ToString()
            };
            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                $"cmd=MAV_CMD_ACCELCAL_VEHICLE_POS(42429), param1={position} ({posName})");

            await SendCommandLongFireAndForgetAsync(MAV_CMD_ACCELCAL_VEHICLE_POS, position, 0, 0, 0, 0, 0, 0, ct);
        }

        public void SendPacketRaw(ushort command, float param1, float param2 = 0, float param3 = 0,
            float param4 = 0, float param5 = 0, float param6 = 0, float param7 = 0)
        {
            if (_outputStream == null)
            {
                _logger.LogWarning("SendPacketRaw: Output stream is null");
                return;
            }

            byte targetSysId = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            byte targetCompId = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            _logger.LogInformation("SendPacketRaw: cmd={Command} param1={Param1} -> target_system={TargetSys} target_component={TargetComp}",
                command, param1, targetSysId, targetCompId);

            var payload = new byte[33];
            BitConverter.GetBytes(param1).CopyTo(payload, 0);
            BitConverter.GetBytes(param2).CopyTo(payload, 4);
            BitConverter.GetBytes(param3).CopyTo(payload, 8);
            BitConverter.GetBytes(param4).CopyTo(payload, 12);
            BitConverter.GetBytes(param5).CopyTo(payload, 16);
            BitConverter.GetBytes(param6).CopyTo(payload, 20);
            BitConverter.GetBytes(param7).CopyTo(payload, 24);
            BitConverter.GetBytes(command).CopyTo(payload, 28);
            payload[30] = targetSysId;
            payload[31] = targetCompId;
            payload[32] = 1; // confirmation = 1

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

                    _logger.LogDebug("Sent raw packet: cmd={Cmd} seq={Seq}", command, seq);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending raw packet");
                }
            }
        }

        public void SendAccelCalVehiclePosRaw(int position)
        {
            string posName = position switch
            {
                1 => "LEVEL", 2 => "LEFT", 3 => "RIGHT",
                4 => "NOSE_DOWN", 5 => "NOSE_UP", 6 => "BACK",
                _ => position.ToString()
            };

            byte targetSysId = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            byte targetCompId = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            _logger.LogInformation("SendAccelCalVehiclePosRaw: position={Position} ({Name}) [FIRE-AND-FORGET]",
                position, posName);
            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                $"cmd=MAV_CMD_ACCELCAL_VEHICLE_POS(42429), param1={position} ({posName}) [RAW/NO-ACK]");

            SendPacketRaw(MAV_CMD_ACCELCAL_VEHICLE_POS, position);
        }

        public async Task<CommandResult> SendStartMagCalAsync(
            int magMask = 0, int retryOnFailure = 1, int autosave = 1,
            float delay = 0, int autoreboot = 0, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_DO_START_MAG_CAL: mask={Mask} retry={Retry} autosave={Autosave}",
                magMask, retryOnFailure, autosave);

            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                $"cmd=MAV_CMD_DO_START_MAG_CAL(42424), param1(mask)={magMask}, param2(retry)={retryOnFailure}, param3(autosave)={autosave}, param4(delay)={delay}, param5(autoreboot)={autoreboot}");

            return await SendCommandLongAsync(MAV_CMD_DO_START_MAG_CAL,
                magMask, retryOnFailure, autosave, delay, autoreboot, 0, 0, ct);
        }

        public async Task<CommandResult> SendAcceptMagCalAsync(int magMask = 0, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_DO_ACCEPT_MAG_CAL: mask={Mask}", magMask);
            return await SendCommandLongAsync(MAV_CMD_DO_ACCEPT_MAG_CAL, magMask, 0, 1, 0, 0, 0, 0, ct);
        }

        public async Task<CommandResult> SendCancelMagCalAsync(int magMask = 0, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_DO_CANCEL_MAG_CAL: mask={Mask}", magMask);
            return await SendCommandLongAsync(MAV_CMD_DO_CANCEL_MAG_CAL, magMask, 0, 1, 0, 0, 0, 0, ct);
        }

        public async Task SendResetParametersAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_STORAGE: param1=2 (reset to defaults)");
            await SendCommandLongAsync(MAV_CMD_PREFLIGHT_STORAGE, 2, 0, 0, 0, 0, 0, 0, ct);
        }

        public async Task SendFlashBootloaderAsync(int magicValue, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_FLASH_BOOTLOADER: magic={Magic}", magicValue);
            const ushort MAV_CMD_FLASH_BOOTLOADER = 42650;
            await SendCommandLongAsync(MAV_CMD_FLASH_BOOTLOADER, 0, 0, 0, 0, magicValue, 0, 0, ct);
        }

        public async Task SendSetMessageIntervalAsync(int messageId, int intervalUs, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_SET_MESSAGE_INTERVAL: msgId={MsgId} interval={Interval}us",
                messageId, intervalUs);

            await SendCommandLongAsync(MAV_CMD_SET_MESSAGE_INTERVAL, messageId, intervalUs, 0, 0, 0, 0, 0, ct);
        }

        public async Task SendRequestDataStreamAsync(int streamId, int rateHz, int startStop, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending REQUEST_DATA_STREAM: streamId={StreamId} rate={Rate}Hz start={Start}",
                streamId, rateHz, startStop);

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
        /// Request AUTOPILOT_VERSION message from the flight controller.
        /// This must be called to get the FC's unique identifier (UID/UID2) and firmware version.
        /// Uses MAV_CMD_REQUEST_MESSAGE (512) with param1 = AUTOPILOT_VERSION (148).
        /// </summary>
        public async Task SendRequestAutopilotVersionAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Requesting AUTOPILOT_VERSION message from FC");

            _mavLinkLogger?.LogOutgoing("COMMAND_LONG",
                $"cmd=MAV_CMD_REQUEST_MESSAGE(512), param1=AUTOPILOT_VERSION(148)");

            // MAV_CMD_REQUEST_MESSAGE = 512
            // param1 = message ID to request (AUTOPILOT_VERSION = 148)
            await SendCommandLongFireAndForgetAsync(MAV_CMD_REQUEST_MESSAGE,
                MAVLINK_MSG_ID_AUTOPILOT_VERSION, 0, 0, 0, 0, 0, 0, ct);
        }

        private async Task SendCommandLongFireAndForgetAsync(
            ushort command, float param1, float param2, float param3, float param4,
            float param5, float param6, float param7, CancellationToken ct = default)
        {
            byte targetSysId = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            byte targetCompId = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            var payload = new byte[33];
            BitConverter.GetBytes(param1).CopyTo(payload, 0);
            BitConverter.GetBytes(param2).CopyTo(payload, 4);
            BitConverter.GetBytes(param3).CopyTo(payload, 8);
            BitConverter.GetBytes(param4).CopyTo(payload, 12);
            BitConverter.GetBytes(param5).CopyTo(payload, 16);
            BitConverter.GetBytes(param6).CopyTo(payload, 20);
            BitConverter.GetBytes(param7).CopyTo(payload, 24);
            BitConverter.GetBytes(command).CopyTo(payload, 28);
            payload[30] = targetSysId;
            payload[31] = targetCompId;
            payload[32] = 0;

            await SendMessageAsync(MAVLINK_MSG_ID_COMMAND_LONG, payload, ct);

            _logger.LogInformation("COMMAND_LONG sent (fire-and-forget): cmd={Command} param1={Param1}",
                command, param1);
        }

        private async Task<CommandResult> SendCommandLongAsync(
            ushort command, float param1, float param2, float param3, float param4,
            float param5, float param6, float param7, CancellationToken ct = default)
        {
            var resultTask = _transactionManager.RegisterCommandAsync(command, TimeSpan.FromSeconds(5), maxRetries: 3);

            byte targetSysId = _targetSystemId != 0 ? _targetSystemId : (byte)1;
            byte targetCompId = _targetComponentId != 0 ? _targetComponentId : (byte)1;

            var payload = new byte[33];
            BitConverter.GetBytes(param1).CopyTo(payload, 0);
            BitConverter.GetBytes(param2).CopyTo(payload, 4);
            BitConverter.GetBytes(param3).CopyTo(payload, 8);
            BitConverter.GetBytes(param4).CopyTo(payload, 12);
            BitConverter.GetBytes(param5).CopyTo(payload, 16);
            BitConverter.GetBytes(param6).CopyTo(payload, 20);
            BitConverter.GetBytes(param7).CopyTo(payload, 24);
            BitConverter.GetBytes(command).CopyTo(payload, 28);
            payload[30] = targetSysId;
            payload[31] = targetCompId;
            payload[32] = 0;

            await SendMessageAsync(MAVLINK_MSG_ID_COMMAND_LONG, payload, ct);

            _logger.LogDebug("COMMAND_LONG sent: cmd={Command} param1={Param1}", command, param1);

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

        private async Task SendMessageAsync(byte msgId, byte[] payload, CancellationToken ct)
        {
            if (_outputStream == null)
                return;

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
                    frame[5] = msgId;

                    Array.Copy(payload, 0, frame, 6, payloadLen);

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

            await Task.CompletedTask;
        }

        #endregion

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

        public (double X, double Y, double Z) GetAcceleration()
        {
            if (IsScaled)
            {
                const double MILLI_G_TO_MS2 = 0.00981;
                return (XAcc * MILLI_G_TO_MS2, YAcc * MILLI_G_TO_MS2, ZAcc * MILLI_G_TO_MS2);
            }
            else
            {
                const double RAW_TO_MS2 = 0.00478;
                return (XAcc * RAW_TO_MS2, YAcc * RAW_TO_MS2, ZAcc * RAW_TO_MS2);
            }
        }

        public (double X, double Y, double Z) GetGyro()
        {
            if (IsScaled)
            {
                const double MILLI_RAD_TO_RAD = 0.001;
                return (XGyro * MILLI_RAD_TO_RAD, YGyro * MILLI_RAD_TO_RAD, ZGyro * MILLI_RAD_TO_RAD);
            }
            else
            {
                const double RAW_TO_RAD = 0.0001;
                return (XGyro * RAW_TO_RAD, YGyro * RAW_TO_RAD, ZGyro * RAW_TO_RAD);
            }
        }

        public double GetTemperature()
        {
            return Temperature / 100.0;
        }
    }

    /// <summary>
    /// AUTOPILOT_VERSION data from vehicle
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
        public byte FirmwareMajor { get; set; }
        public byte FirmwareMinor { get; set; }
        public byte FirmwarePatch { get; set; }
        public byte FirmwareType { get; set; }
        public string FirmwareVersionString { get; set; } = "N/A";

        /// <summary>
        /// Gets the Flight Controller ID using the best available identifier.
        /// Priority: UID2 > UID > FlightSwVersion + GitHash
        /// This matches Mission Planner's approach of using uid2 as the primary hardware identifier.
        /// </summary>
        public string GetFcId()
        {
            // Priority 1: Use UID2 if available (18-byte hardware unique ID - most reliable)
            if (Uid2 != null && Uid2.Length > 0)
            {
                bool allZeros = true;
                foreach (byte b in Uid2)
                {
                    if (b != 0)
                    {
                        allZeros = false;
                        break;
                    }
                }
                
                if (!allZeros)
                {
                    // Use first 10 bytes of UID2 for a readable ID
                    var hex = BitConverter.ToString(Uid2, 0, Math.Min(10, Uid2.Length))
                        .Replace("-", "").ToUpperInvariant();
                    return $"FC-{hex}";
                }
            }
            
            // Priority 2: Use UID if available (8-byte hardware ID)
            if (Uid != null && Uid.Length > 0)
            {
                bool allZeros = true;
                foreach (byte b in Uid)
                {
                    if (b != 0)
                    {
                        allZeros = false;
                        break;
                    }
                }
                
                if (!allZeros)
                {
                    var hex = BitConverter.ToString(Uid).Replace("-", "").ToUpperInvariant();
                    return $"FC-{hex}";
                }
            }
            
            // Priority 3: Use firmware version + git hash
            if (FlightSwVersion > 0)
            {
                var gitPrefix = FlightCustomVersion != null && FlightCustomVersion.Length >= 4
                    ? BitConverter.ToString(FlightCustomVersion, 0, Math.Min(4, FlightCustomVersion.Length))
                        .Replace("-", "").ToUpperInvariant()
                    : "0000";
                
                return $"FW-{FlightSwVersion:X8}-{gitPrefix}";
            }
            
            return "FW-UNAVAILABLE";
        }

        /// <summary>
        /// Gets the git hash from FlightCustomVersion
        /// </summary>
        public string GetGitHash()
        {
            if (FlightCustomVersion == null || FlightCustomVersion.Length == 0)
                return "N/A";

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

            var hex = BitConverter.ToString(FlightCustomVersion).Replace("-", "").ToLowerInvariant();
            return hex;
        }
    }

    /// <summary>
    /// COMMAND_LONG data from flight controller
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

    /// <summary>
    /// MAG_CAL_PROGRESS data
    /// </summary>
    public class MagCalProgressData
    {
        public byte CompassId { get; set; }
        public byte CalMask { get; set; }
        public byte CalStatus { get; set; }
        public byte Attempt { get; set; }
        public byte CompletionPct { get; set; }
        public byte[] CompletionMask { get; set; } = new byte[10];
        public float DirectionX { get; set; }
        public float DirectionY { get; set; }
        public float DirectionZ { get; set; }
    }

    /// <summary>
    /// MAG_CAL_REPORT data
    /// </summary>
    public class MagCalReportData
    {
        public byte CompassId { get; set; }
        public byte CalMask { get; set; }
        public byte CalStatus { get; set; }
        public byte Autosaved { get; set; }
        public float Fitness { get; set; }
        public float OfsX { get; set; }
        public float OfsY { get; set; }
        public float OfsZ { get; set; }
        public float DiagX { get; set; }
        public float DiagY { get; set; }
        public float DiagZ { get; set; }
        public float OffdiagX { get; set; }
        public float OffdiagY { get; set; }
        public float OffdiagZ { get; set; }
        public byte OrientationConfidence { get; set; }
        public byte OldOrientation { get; set; }
        public byte NewOrientation { get; set; }
        public float ScaleFactor { get; set; }
    }
}
