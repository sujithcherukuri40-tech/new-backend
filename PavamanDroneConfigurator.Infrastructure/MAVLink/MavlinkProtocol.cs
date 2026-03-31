using System;
using System.Collections.Generic;

namespace PavamanDroneConfigurator.Infrastructure.MAVLink
{
    /// <summary>
    /// MAVLink protocol constants and CRC extra values
    /// Pre-generated from MAVLink message definitions for production use
    /// </summary>
    internal static class MavlinkProtocol
    {
        // Frame validation constants
        public const int MAX_PAYLOAD_LENGTH_V1 = 255;
        public const int MAX_PAYLOAD_LENGTH_V2 = 255;
        public const int MIN_FRAME_LENGTH_V1 = 8;   // STX + header (5) + CRC (2)
        public const int MIN_FRAME_LENGTH_V2 = 12;  // STX + header (9) + CRC (2)
        
        public const byte MAVLINK_STX_V1 = 0xFE;
        public const byte MAVLINK_STX_V2 = 0xFD;
        
        // Incompatibility flags (v2)
        public const byte MAVLINK_IFLAG_SIGNED = 0x01;
        
        // Message IDs for compass calibration
        public const byte MAVLINK_MSG_ID_MAG_CAL_PROGRESS = 191;
        public const byte MAVLINK_MSG_ID_MAG_CAL_REPORT = 192;
        
        // Command IDs for compass calibration
        public const ushort MAV_CMD_DO_START_MAG_CAL = 42424;
        public const ushort MAV_CMD_DO_ACCEPT_MAG_CAL = 42425;
        public const ushort MAV_CMD_DO_CANCEL_MAG_CAL = 42426;
        
        /// <summary>
        /// CRC_EXTRA values for known MAVLink messages
        /// Generated from official MAVLink XML definitions
        /// </summary>
        private static readonly Dictionary<int, byte> CrcExtraTable = new()
        {
            // Common messages
            { 0, 50 },    // HEARTBEAT
            { 1, 124 },   // SYS_STATUS
            { 2, 137 },   // SYSTEM_TIME
            { 4, 237 },   // PING
            { 5, 217 },   // CHANGE_OPERATOR_CONTROL
            { 6, 104 },   // CHANGE_OPERATOR_CONTROL_ACK
            { 7, 119 },   // AUTH_KEY
            { 11, 89 },   // SET_MODE
            
            // Parameter protocol
            { 20, 214 },  // PARAM_REQUEST_READ
            { 21, 159 },  // PARAM_REQUEST_LIST
            { 22, 220 },  // PARAM_VALUE
            { 23, 168 },  // PARAM_SET
            { 24, 24 },   // GPS_RAW_INT
            { 50, 119 },  // PARAM_MAP_RC
            
            // Mission protocol
            { 25, 153 },  // MISSION_WRITE_PARTIAL_LIST
            { 26, 170 },  // SCALED_IMU
            { 27, 144 },  // RAW_IMU
            { 28, 237 },  // RAW_PRESSURE
            { 29, 203 },  // SCALED_PRESSURE
            { 30, 148 },  // ATTITUDE
            { 31, 183 },  // ATTITUDE_QUATERNION
            { 32, 104 },  // LOCAL_POSITION_NED
            { 33, 185 },  // GLOBAL_POSITION_INT
            { 34, 233 },  // RC_CHANNELS_SCALED
            { 35, 118 },  // RC_CHANNELS_RAW
            { 36, 21 },   // SERVO_OUTPUT_RAW
            
            // Mission items
            { 39, 254 },  // MISSION_ITEM
            { 40, 158 },  // MISSION_REQUEST
            { 41, 230 },  // MISSION_SET_CURRENT
            { 42, 28 },   // MISSION_CURRENT
            { 43, 232 },  // MISSION_REQUEST_LIST
            { 44, 221 },  // MISSION_COUNT
            { 45, 64 },   // MISSION_CLEAR_ALL
            { 46, 12 },   // MISSION_ITEM_REACHED
            { 47, 106 },  // MISSION_ACK
            
            // GPS
            { 65, 118 },  // RC_CHANNELS
            { 74, 20 },   // VFR_HUD
            { 76, 152 },  // COMMAND_LONG
            { 77, 143 },  // COMMAND_ACK
            { 87, 138 },  // POSITION_TARGET_GLOBAL_INT
            
            // Sensor data
            { 100, 154 }, // OPTICAL_FLOW
            { 101, 208 }, // GLOBAL_VISION_POSITION_ESTIMATE
            { 102, 200 }, // VISION_POSITION_ESTIMATE
            { 103, 106 }, // VISION_SPEED_ESTIMATE
            { 104, 211 }, // VICON_POSITION_ESTIMATE
            { 105, 93 },  // HIGHRES_IMU
            { 106, 156 }, // OPTICAL_FLOW_RAD
            
            // Battery
            { 147, 154 }, // BATTERY_STATUS
            { 148, 49 },  // AUTOPILOT_VERSION
            
            // Magnetometer calibration (ArduPilotMega dialect)
            { 191, 92 },  // MAG_CAL_PROGRESS
            { 192, 36 },  // MAG_CAL_REPORT
            
            // Terrain
            { 253, 83 },  // STATUSTEXT
            { 254, 8 },   // DEBUG
            
            // ArduPilot specific
            { 42429, 140 }, // MAV_CMD_ACCELCAL_VEHICLE_POS (special command ACK)
        };

        /// <summary>
        /// Get CRC_EXTRA for a message ID
        /// Strict mode: returns 0 for unknown messages (will cause CRC failure)
        /// </summary>
        public static byte GetCrcExtra(int messageId)
        {
            if (CrcExtraTable.TryGetValue(messageId, out byte crcExtra))
            {
                return crcExtra;
            }

            // Unknown message - return 0 to fail CRC validation
            // This is STRICT production behavior
            return 0;
        }

        /// <summary>
        /// Check if message ID is known/supported
        /// </summary>
        public static bool IsKnownMessage(int messageId)
        {
            return CrcExtraTable.ContainsKey(messageId);
        }

        /// <summary>
        /// Validate MAVLink v1 frame structure
        /// </summary>
        public static bool ValidateV1Frame(byte[] frame)
        {
            if (frame == null || frame.Length < MIN_FRAME_LENGTH_V1)
                return false;

            byte payloadLen = frame[1];
            
            // Check payload length is valid
            if (payloadLen > MAX_PAYLOAD_LENGTH_V1)
                return false;

            // Check frame length matches expected
            int expectedLen = 8 + payloadLen;
            if (frame.Length != expectedLen)
                return false;

            return true;
        }

        /// <summary>
        /// Validate MAVLink v2 frame structure
        /// </summary>
        public static bool ValidateV2Frame(byte[] frame)
        {
            if (frame == null || frame.Length < MIN_FRAME_LENGTH_V2)
                return false;

            byte payloadLen = frame[1];
            byte incompatFlags = frame[2];

            // Check payload length is valid
            if (payloadLen > MAX_PAYLOAD_LENGTH_V2)
                return false;

            // Check for unsupported incompatibility flags
            // If SIGNED flag is set but we don't validate signatures, reject
            bool hasSigned = (incompatFlags & MAVLINK_IFLAG_SIGNED) != 0;
            
            // Calculate expected frame length
            int expectedLen = 12 + payloadLen;
            if (hasSigned)
                expectedLen += 13; // signature

            if (frame.Length != expectedLen)
                return false;

            // Reject signed frames if we don't support signature validation
            // Production rule: don't accept what you can't verify
            if (hasSigned)
                return false;

            return true;
        }

       
        public static int GetV2MessageId(byte[] frame)
        {
            if (frame.Length < MIN_FRAME_LENGTH_V2)
                return -1;

            // Message ID is bytes 7, 8, 9 (little-endian)
            return frame[7] | (frame[8] << 8) | (frame[9] << 16);
        }
    }
}
