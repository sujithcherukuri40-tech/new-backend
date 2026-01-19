# Accelerometer Calibration Connection Test Script
# Tests FC connection and calibration readiness

Write-Host "=== ACCELEROMETER CALIBRATION CONNECTION TEST ===" -ForegroundColor Cyan
Write-Host ""

# Check if solution builds
Write-Host "[1/5] Building solution..." -ForegroundColor Yellow
$buildResult = dotnet build "C:\Pavaman\Final-repo\PavamanDroneConfigurator.sln" --nologo -v quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ? Build PASSED" -ForegroundColor Green
} else {
    Write-Host "  ? Build FAILED" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}

Write-Host ""
Write-Host "[2/5] Checking calibration service integration..." -ForegroundColor Yellow

# Check if AccelerometerCalibrationService is registered in DI
$appFile = "C:\Pavaman\Final-repo\PavamanDroneConfigurator.UI\App.axaml.cs"
$content = Get-Content $appFile -Raw

if ($content -match "AccelerometerCalibrationService") {
    Write-Host "  ? AccelerometerCalibrationService registered in DI" -ForegroundColor Green
} else {
    Write-Host "  ? AccelerometerCalibrationService NOT registered" -ForegroundColor Red
}

if ($content -match "AccelImuValidator") {
    Write-Host "  ? AccelImuValidator registered in DI" -ForegroundColor Green
} else {
    Write-Host "  ? AccelImuValidator NOT registered" -ForegroundColor Red
}

if ($content -match "AccelStatusTextParser") {
    Write-Host "  ? AccelStatusTextParser registered in DI" -ForegroundColor Green
} else {
    Write-Host "  ? AccelStatusTextParser NOT registered" -ForegroundColor Red
}

Write-Host ""
Write-Host "[3/5] Checking MAVLink command implementations..." -ForegroundColor Yellow

# Check MAVLink wrapper has calibration commands
$mavlinkFile = "C:\Pavaman\Final-repo\PavamanDroneConfigurator.Infrastructure\MAVLink\AsvMavlinkWrapper.cs"
$mavlinkContent = Get-Content $mavlinkFile -Raw

if ($mavlinkContent -match "SendPreflightCalibrationAsync") {
    Write-Host "  ? SendPreflightCalibrationAsync implemented" -ForegroundColor Green
} else {
    Write-Host "  ? SendPreflightCalibrationAsync NOT found" -ForegroundColor Red
}

if ($mavlinkContent -match "SendAccelCalVehiclePosAsync") {
    Write-Host "  ? SendAccelCalVehiclePosAsync implemented" -ForegroundColor Green
} else {
    Write-Host "  ? SendAccelCalVehiclePosAsync NOT found" -ForegroundColor Red
}

if ($mavlinkContent -match "MAV_CMD_ACCELCAL_VEHICLE_POS\s*=\s*42429") {
    Write-Host "  ? MAV_CMD_ACCELCAL_VEHICLE_POS defined correctly" -ForegroundColor Green
} else {
    Write-Host "  ??  MAV_CMD_ACCELCAL_VEHICLE_POS constant check failed" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[4/5] Checking ConnectionService events..." -ForegroundColor Yellow

$connectionFile = "C:\Pavaman\Final-repo\PavamanDroneConfigurator.Infrastructure\Services\ConnectionService.cs"
$connectionContent = Get-Content $connectionFile -Raw

if ($connectionContent -match "event EventHandler<RawImuEventArgs>\? RawImuReceived") {
    Write-Host "  ? RawImuReceived event declared" -ForegroundColor Green
} else {
    Write-Host "  ? RawImuReceived event NOT declared" -ForegroundColor Red
}

if ($connectionContent -match "event EventHandler<CommandAckEventArgs>\? CommandAckReceived") {
    Write-Host "  ? CommandAckReceived event declared" -ForegroundColor Green
} else {
    Write-Host "  ? CommandAckReceived event NOT declared" -ForegroundColor Red
}

if ($connectionContent -match "event EventHandler<StatusTextEventArgs>\? StatusTextReceived") {
    Write-Host "  ? StatusTextReceived event declared" -ForegroundColor Green
} else {
    Write-Host "  ? StatusTextReceived event NOT declared" -ForegroundColor Red
}

if ($connectionContent -match "OnMavlinkRawImu") {
    Write-Host "  ? OnMavlinkRawImu handler implemented" -ForegroundColor Green
} else {
    Write-Host "  ? OnMavlinkRawImu handler NOT found" -ForegroundColor Red
}

Write-Host ""
Write-Host "[5/5] Checking IMU data conversion fix..." -ForegroundColor Yellow

$accelCalFile = "C:\Pavaman\Final-repo\PavamanDroneConfigurator.Infrastructure\Services\AccelerometerCalibrationService.cs"
$accelCalContent = Get-Content $accelCalFile -Raw

if ($accelCalContent -match "MS2_TO_MILLI_G\s*=\s*1000\.0\s*/\s*9\.80665") {
    Write-Host "  ? Correct IMU conversion formula found" -ForegroundColor Green
    Write-Host "     Formula: MS2_TO_MILLI_G = 1000.0 / 9.80665" -ForegroundColor Gray
} else {
    Write-Host "  ? IMU conversion formula NOT correct" -ForegroundColor Red
    if ($accelCalContent -match "e\.AccelX\s*\*\s*MS2_TO_MILLI_G") {
        Write-Host "     Using multiplication (correct)" -ForegroundColor Gray
    } else {
        Write-Host "     ??  Not using multiplication!" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Prerequisites:" -ForegroundColor Yellow
Write-Host "  1. Connect Flight Controller via USB/Serial"
Write-Host "  2. Ensure FC is disarmed"
Write-Host "  3. Launch PavamanDroneConfigurator.UI"
Write-Host "  4. Navigate to Sensors ? Accelerometer tab"
Write-Host ""
Write-Host "Test Procedure:" -ForegroundColor Yellow
Write-Host "  1. Verify 'Accelerometer Available' status shows"
Write-Host "  2. Click 'Calibrate Accelerometer' button"
Write-Host "  3. Wait for FC to send 'Place vehicle level' message"
Write-Host "  4. Place drone FLAT on table (level position)"
Write-Host "  5. Click 'Click When In Position' button"
Write-Host "  6. Verify IMU validation runs (should collect 50 samples)"
Write-Host "  7. Check if validation passes or fails"
Write-Host ""
Write-Host "Expected Results:" -ForegroundColor Yellow
Write-Host "  ? Step 1 indicator turns RED (active/waiting)"
Write-Host "  ? After clicking button, validation starts"
Write-Host "  ? IMU samples collected (AccelZ ? +9.81 m/s² for level)"
Write-Host "  ? Validation passes with message 'Position 1 verified'"
Write-Host "  ? MAV_CMD_ACCELCAL_VEHICLE_POS sent to FC"
Write-Host "  ? Step 1 turns GREEN (complete)"
Write-Host ""
Write-Host "Debug Logs Location:" -ForegroundColor Yellow
Write-Host "  UI: Enable 'Show Logs' toggle in Sensors tab"
Write-Host "  Console: Check application console output"
Write-Host "  Look for: 'Position 1 validation PASSED/FAILED'"
Write-Host ""
Write-Host "Common Issues:" -ForegroundColor Yellow
Write-Host "  ? 'No IMU data' ? Check FC is sending RAW_IMU/SCALED_IMU messages"
Write-Host "  ? 'Wrong magnitude' ? Check IMU conversion formula (should be ×101.97, not ÷0.00981)"
Write-Host "  ? 'Wrong axis' ? Check drone is truly level (use bubble level)"
Write-Host "  ? 'Timeout' ? Check MAVLink connection is stable (heartbeat every 1s)"
Write-Host ""
Write-Host "=== TEST COMPLETE ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Green
Write-Host "  1. Launch the application"
Write-Host "  2. Connect to FC"
Write-Host "  3. Run calibration test"
Write-Host "  4. Report results (pass/fail + logs)"
Write-Host ""
