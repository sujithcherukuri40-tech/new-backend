using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.MAVLink
{
    /// <summary>
    /// MAVLink protocol implementation matching Mission Planner's approach.
    /// Handles both MAVLink v1 and v2 frames with proper CRC validation.
    /// </summary>
    public sealed class AsvMavlinkWrapper : IDisposable
    {
        private readonly ILogger _logger;
        private Stream? _inputStream;
        private Stream? _outputStream;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private Task? _heartbeatTask;
        private bool _disposed;

        private readonly byte[] _rxBuffer = new byte[4096];
        private int _rxBufferPos;
        private readonly object _bufferLock = new();
        private readonly object _writeLock = new();

        private byte _targetSystemId = 1;
        private byte _targetComponentId = 1;
        private byte _packetSequence;

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

        // MAV_CMD IDs
        private const ushort MAV_CMD_DO_MOTOR_TEST = 209;
        private const ushort MAV_CMD_PREFLIGHT_CALIBRATION = 241;
        private const ushort MAV_CMD_PREFLIGHT_STORAGE = 245;
        private const ushort MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN = 246;
        private const ushort MAV_CMD_COMPONENT_ARM_DISARM = 400;
        private const ushort MAV_CMD_ACCELCAL_VEHICLE_POS = 42429;

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

        public event EventHandler<(byte SystemId, byte ComponentId)>? HeartbeatReceived;
        public event EventHandler<(string Name, float Value, ushort Index, ushort Count)>? ParamValueReceived;
        public event EventHandler<(ushort Command, byte Result)>? CommandAckReceived;
        public event EventHandler<(byte Severity, string Text)>? StatusTextReceived;
        public event EventHandler<HeartbeatData>? HeartbeatDataReceived;
        public event EventHandler<RcChannelsData>? RcChannelsReceived;
        public event EventHandler<RawImuData>? RawImuReceived;

        public AsvMavlinkWrapper(ILogger logger)
        {
            _logger = logger;
        }

        public void Initialize(Stream inputStream, Stream outputStream)
        {
            _disposed = false;
            _inputStream = inputStream;
            _outputStream = outputStream;
            _rxBufferPos = 0;
            _cts = new CancellationTokenSource();

            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            _heartbeatTask = Task.Run(() => GcsHeartbeatLoopAsync(_cts.Token));

            _logger.LogInformation("MAVLink wrapper initialized with GCS heartbeat");
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
            byte payloadLen = frame[1];
            byte seq = frame[2];
            byte sysId = frame[3];
            byte compId = frame[4];
            byte msgId = frame[5];

            // Verify CRC
            ushort crcCalc = CalculateCrc(frame, 1, payloadLen + 5, GetCrcExtra(msgId));
            ushort crcRecv = (ushort)(frame[6 + payloadLen] | (frame[7 + payloadLen] << 8));

            if (crcCalc != crcRecv)
            {
                _logger.LogTrace("V1 CRC mismatch: msg={Msg} calc=0x{Calc:X4} recv=0x{Recv:X4}", msgId, crcCalc, crcRecv);
                return;
            }

            // Extract payload
            var payload = new byte[payloadLen];
            Array.Copy(frame, 6, payload, 0, payloadLen);

            HandleMessage(sysId, compId, msgId, payload);
        }

        private void ProcessMavlink2Frame(byte[] frame)
        {
            byte payloadLen = frame[1];
            byte incompatFlags = frame[2];
            byte compatFlags = frame[3];
            byte seq = frame[4];
            byte sysId = frame[5];
            byte compId = frame[6];
            int msgId = frame[7] | (frame[8] << 8) | (frame[9] << 16);

            // Verify CRC (for v2, CRC is calculated over bytes 1 to 9+payloadLen)
            ushort crcCalc = CalculateCrc(frame, 1, 9 + payloadLen, GetCrcExtra((byte)(msgId & 0xFF)));
            int crcOffset = 10 + payloadLen;
            ushort crcRecv = (ushort)(frame[crcOffset] | (frame[crcOffset + 1] << 8));

            if (crcCalc != crcRecv)
            {
                _logger.LogTrace("V2 CRC mismatch: msg={Msg} calc=0x{Calc:X4} recv=0x{Recv:X4}", msgId, crcCalc, crcRecv);
                return;
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

                case MAVLINK_MSG_ID_RC_CHANNELS:
                    HandleRcChannels(payload);
                    break;

                case MAVLINK_MSG_ID_RAW_IMU:
                    HandleRawImu(payload);
                    break;

                case MAVLINK_MSG_ID_SCALED_IMU:
                    HandleScaledImu(payload);
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

            _logger.LogDebug("Heartbeat from FC: sysid={SysId} compid={CompId}", sysId, compId);
            HeartbeatReceived?.Invoke(this, (sysId, compId));

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
            CommandAckReceived?.Invoke(this, (command, result));
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
        /// </summary>
        /// <param name="motorInstance">Motor instance (1-based)</param>
        /// <param name="throttleType">Throttle type (0=percent, 1=PWM, 2=pilot)</param>
        /// <param name="throttleValue">Throttle value (percent or PWM)</param>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="motorCount">Motor count (0=single motor test)</param>
        /// <param name="testOrder">Test order (0=default, 1=sequence)</param>
        public async Task SendMotorTestAsync(
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

            await SendCommandLongAsync(
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
        /// </summary>
        /// <param name="gyro">Gyroscope calibration (0=skip, 1=calibrate)</param>
        /// <param name="mag">Magnetometer calibration (0=skip, 1=calibrate)</param>
        /// <param name="groundPressure">Ground pressure/barometer calibration (0=skip, 1=calibrate)</param>
        /// <param name="airspeed">Radio/airspeed calibration (0=skip, 1=calibrate)</param>
        /// <param name="accel">Accelerometer calibration (0=skip, 1=calibrate, 2=board level, 3=simple)</param>
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
        /// </summary>
        /// <param name="position">Vehicle position (1-6 for different orientations)</param>
        public async Task SendAccelCalVehiclePosAsync(int position, CancellationToken ct = default)
        {
            _logger.LogInformation("Sending MAV_CMD_ACCELCAL_VEHICLE_POS: position={Position}", position);
            await SendCommandLongAsync(
                MAV_CMD_ACCELCAL_VEHICLE_POS,
                position,   // param1: position (1-6)
                0, 0, 0, 0, 0, 0,
                ct);
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
        /// Send COMMAND_LONG message
        /// </summary>
        private async Task SendCommandLongAsync(
            ushort command,
            float param1, float param2, float param3, float param4,
            float param5, float param6, float param7,
            CancellationToken ct = default)
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
            // [30]    target_system
            // [31]    target_component
            // [32]    confirmation

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

            await SendMessageAsync(MAVLINK_MSG_ID_COMMAND_LONG, payload, ct);
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
            return msgId switch
            {
                MAVLINK_MSG_ID_HEARTBEAT => CRC_EXTRA_HEARTBEAT,
                MAVLINK_MSG_ID_PARAM_REQUEST_READ => CRC_EXTRA_PARAM_REQUEST_READ,
                MAVLINK_MSG_ID_PARAM_REQUEST_LIST => CRC_EXTRA_PARAM_REQUEST_LIST,
                MAVLINK_MSG_ID_PARAM_VALUE => CRC_EXTRA_PARAM_VALUE,
                MAVLINK_MSG_ID_PARAM_SET => CRC_EXTRA_PARAM_SET,
                MAVLINK_MSG_ID_RAW_IMU => CRC_EXTRA_RAW_IMU,
                MAVLINK_MSG_ID_SCALED_IMU => CRC_EXTRA_SCALED_IMU,
                MAVLINK_MSG_ID_COMMAND_LONG => CRC_EXTRA_COMMAND_LONG,
                (byte)MAVLINK_MSG_ID_COMMAND_ACK => CRC_EXTRA_COMMAND_ACK,
                MAVLINK_MSG_ID_STATUSTEXT => CRC_EXTRA_STATUSTEXT,
                MAVLINK_MSG_ID_RC_CHANNELS => CRC_EXTRA_RC_CHANNELS,
                _ => 0
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _cts?.Cancel();
                _readTask?.Wait(TimeSpan.FromSeconds(2));
                _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MAVLink wrapper");
            }
            finally
            {
                _cts?.Dispose();
                _readTask?.Dispose();
                _heartbeatTask?.Dispose();
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
}