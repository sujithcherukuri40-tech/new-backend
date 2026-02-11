# Drone ID & FC ID Extraction Guide

## ?? Overview

The application extracts Drone ID and Flight Controller (FC) ID using a **multi-source, priority-based approach** to ensure reliable identification.

---

## ?? Drone ID Extraction

**Location:** `DroneInfoService.cs` ? `RefreshDroneInfoAsync()` method (lines 266-286)

### **Priority Order:**

#### 1. **BRD_SERIAL_NUM Parameter** (Highest Priority)
```csharp
var serialNum = await _parameterService.GetParameterAsync("BRD_SERIAL_NUM");
if (serialNum != null && serialNum.Value != 0)
{
    _currentInfo.DroneId = $"P{serialNum.Value:0000000000000000000000}";
}
```
- **Format:** `P` + 22-digit zero-padded number
- **Example:** `P0000000000001234567890`
- **When Available:** If flight controller has set `BRD_SERIAL_NUM`

#### 2. **UID from IMU Parameters** (Fallback)
```csharp
var uid1 = await _parameterService.GetParameterAsync("INS_ACC_ID");
var uid2 = await _parameterService.GetParameterAsync("INS_ACC2_ID");
var uid3 = await _parameterService.GetParameterAsync("INS_GYR_ID");

if (uid1 != null || uid2 != null || uid3 != null)
{
    _currentInfo.DroneId = $"P{(int)(uid1?.Value ?? 0):X8}{(int)(uid2?.Value ?? 0):X8}{(int)(uid3?.Value ?? 0):X8}";
}
```
- **Format:** `P` + 3x 8-character hex strings
- **Example:** `P1A2B3C4D5E6F7890ABCDEF012345678`
- **Parameters Used:**
  - `INS_ACC_ID` - Primary accelerometer ID
  - `INS_ACC2_ID` - Secondary accelerometer ID
  - `INS_GYR_ID` - Gyroscope ID

#### 3. **System ID** (Last Resort)
```csharp
_currentInfo.DroneId = $"SYS{_currentInfo.SystemId:D3}";
```
- **Format:** `SYS` + 3-digit system ID
- **Example:** `SYS001`
- **When Used:** No serial number or IMU IDs available

---

## ??? Flight Controller ID (FC ID) Extraction

**Location:** `AutopilotVersionDataEventArgs.GetFcId()` method (lines 289-330)

### **Priority Order:**

#### 1. **Board Serial Number** (Highest Priority)
```csharp
if (BoardSerialNumber > 0)
{
    return $"FC-{BoardSerialNumber:X16}";
}
```
- **Format:** `FC-` + 16-character hex string
- **Example:** `FC-00000000ABCD1234`
- **Source:** MAVLink `AUTOPILOT_VERSION.board_serial_number` field
- **Most Reliable:** Unique hardware identifier

#### 2. **UID from AUTOPILOT_VERSION** (Backup)
```csharp
if (Uid != null && Uid.Length > 0)
{
    bool allZeros = Uid.All(b => b == 0);
    if (!allZeros)
    {
        var hex = BitConverter.ToString(Uid).Replace("-", "").ToUpperInvariant();
        return $"FC-{hex}";
    }
}
```
- **Format:** `FC-` + hex string of UID bytes
- **Example:** `FC-1A2B3C4D5E6F7890ABCDEF0123456789`
- **Source:** MAVLink `AUTOPILOT_VERSION.uid` field (8-byte array)
- **Note:** Only used if not all zeros

#### 3. **Firmware Version + Git Hash** (Fallback)
```csharp
if (FlightSwVersion > 0)
{
    var gitPrefix = FlightCustomVersion != null && FlightCustomVersion.Length >= 4
        ? BitConverter.ToString(FlightCustomVersion, 0, 4).Replace("-", "").ToUpperInvariant()
        : "0000";
    return $"FW-{FlightSwVersion:X8}-{gitPrefix}";
}
```
- **Format:** `FW-` + 8-char hex version + `-` + 4-char git hash
- **Example:** `FW-040A0B0C-A1B2`
- **Source:** 
  - `AUTOPILOT_VERSION.flight_sw_version` (4 bytes)
  - `AUTOPILOT_VERSION.flight_custom_version` (first 4 bytes)

#### 4. **Unavailable** (No Data)
```csharp
return "FW-UNAVAILABLE";
```
- Used when no identification data is available

---

## ?? Data Flow

### **Sequence Diagram:**

```
???????????????         ????????????????????         ???????????????????
? Autopilot   ?         ? ConnectionService?         ? DroneInfoService?
???????????????         ????????????????????         ???????????????????
       ?                         ?                             ?
       ?  HEARTBEAT Message      ?                             ?
       ?????????????????????????>?                             ?
       ?                         ?  HeartbeatDataReceived      ?
       ?                         ?????????????????????????????>?
       ?                         ?                             ? Extract Basic Info
       ?                         ?                             ? (SystemId, VehicleType)
       ?                         ?                             ?
       ?  AUTOPILOT_VERSION      ?                             ?
       ?????????????????????????>?                             ?
       ?                         ?  AutopilotVersionReceived   ?
       ?                         ?????????????????????????????>?
       ?                         ?                             ? GetFcId()
       ?                         ?                             ? Extract FC ID
       ?                         ?                             ?
       ?  PARAM_VALUE (BRD_*)    ?                             ?
       ?????????????????????????>?                             ?
       ?                         ?  ParameterDownloadCompleted ?
       ?                         ?????????????????????????????>?
       ?                         ?                             ? RefreshDroneInfoAsync()
       ?                         ?                             ? Extract Drone ID
       ?                         ?                             ?
       ?                         ?                             ? NotifyIfChanged()
       ?                         ?                             ? Fire DroneInfoUpdated
```

---

## ?? MAVLink Message Fields Used

### **AUTOPILOT_VERSION (Message ID: 148)**

| Field | Type | Purpose | Used For |
|-------|------|---------|----------|
| `flight_sw_version` | uint32 | Firmware version | FC ID fallback, Firmware version |
| `flight_custom_version` | uint8[8] | Git hash | FC ID fallback, Git hash display |
| `board_serial_number` | uint64 | Hardware serial | **Primary FC ID** |
| `uid` | uint8[12] | Unique ID | **Secondary FC ID** |
| `capabilities` | uint64 | Autopilot capabilities | Future use |

### **Parameters Used**

| Parameter | Type | Purpose |
|-----------|------|---------|
| `BRD_SERIAL_NUM` | int | Board serial number (primary Drone ID) |
| `INS_ACC_ID` | int | Accelerometer 1 UID |
| `INS_ACC2_ID` | int | Accelerometer 2 UID |
| `INS_GYR_ID` | int | Gyroscope UID |
| `SYSID_SW_MREV` | int | Software version (major.minor.patch) |
| `BRD_TYPE` | int | Board type identification |

---

## ?? Implementation Details

### **Service Initialization**
```csharp
public DroneInfoService(
    ILogger<DroneInfoService> logger,
    IConnectionService connectionService,
    IParameterService parameterService)
{
    // Subscribe to events
    _connectionService.HeartbeatDataReceived += OnHeartbeatDataReceived;
    _connectionService.AutopilotVersionReceived += OnAutopilotVersionReceived;
    _parameterService.ParameterDownloadCompleted += OnParameterDownloadCompleted;
}
```

### **Event Handlers**

#### **OnHeartbeatDataReceived**
- Updates: Vehicle type, autopilot type, flight mode, armed status
- Triggered: Every 1 second (typical heartbeat rate)

#### **OnAutopilotVersionReceived**
- **Updates: FC ID, firmware version, git hash**
- **Triggered: Once after connection**
- **Most Important for FC ID**

#### **OnParameterDownloadCompleted**
- Calls `RefreshDroneInfoAsync()`
- Extracts Drone ID from parameters
- Updates board type and checksums

---

## ?? Key Points

### ? **FC ID Advantages**
- **Unique per hardware** - Board serial or UID
- **Persistent** - Never changes for same hardware
- **Reliable** - From AUTOPILOT_VERSION message

### ? **Drone ID Advantages**
- **Configurable** - Can set BRD_SERIAL_NUM
- **Multiple fallbacks** - IMU IDs, System ID
- **Always available** - At least System ID fallback

### ?? **Important Notes**
1. **FC ID is preferred** for cloud storage keys (unique hardware identifier)
2. **Drone ID is user-facing** for display and grouping
3. **Both IDs are logged** for parameter change tracking
4. **AUTOPILOT_VERSION must be received** for best FC ID (takes ~2-3 seconds after connection)

---

## ?? Usage in Parameter Logging

### **Upload to S3**
```csharp
// File key format
string key = $"param-logs/{userId}/{fcId}/{timestamp}_param_changes.csv";
```

### **Database Entry**
```csharp
new ParamLog
{
    UserId = userId,
    DroneId = droneId,      // User-facing ID
    FcId = fcId,            // Hardware ID (unique)
    FileName = fileName,
    S3Key = s3Key,
    Timestamp = DateTime.UtcNow
}
```

---

## ?? Summary

| ID Type | Format Example | Source | Uniqueness | Best For |
|---------|----------------|--------|------------|----------|
| **FC ID** | `FC-00000000ABCD1234` | AUTOPILOT_VERSION | ????? Hardware unique | Cloud storage keys |
| **Drone ID** | `P0000000000001234567890` | BRD_SERIAL_NUM param | ???? Configurable | User display |
| **Drone ID** | `P1A2B3C4D5E6F789...` | IMU IDs | ??? Hardware-based | Fallback ID |
| **Drone ID** | `SYS001` | System ID | ? Not unique | Last resort |

---

## ?? Troubleshooting

### **"FW-UNAVAILABLE" shown**
- AUTOPILOT_VERSION message not received
- Wait 2-3 seconds after connection
- Check MAVLink connection quality

### **"Pending..." shown**
- Normal during initial connection
- Updates automatically when data arrives

### **Drone ID shows "SYS001"**
- No BRD_SERIAL_NUM set
- No IMU IDs available
- Consider setting BRD_SERIAL_NUM parameter

---

## ?? Related Files

- `DroneInfoService.cs` - Main extraction logic
- `IConnectionService.cs` - AUTOPILOT_VERSION event args
- `AutopilotVersionDataEventArgs.GetFcId()` - FC ID extraction method
- `ParametersPageViewModel.cs` - Uses IDs for parameter logging

---

**Last Updated:** 2024
**Version:** 1.0
