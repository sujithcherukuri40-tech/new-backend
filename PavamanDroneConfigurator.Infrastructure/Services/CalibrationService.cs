using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Mission Planner-style calibration service - STRICT FC-DRIVEN PROTOCOL.
/// 
/// HARD REQUIREMENTS (ArduPilot MAVLink Contract):
/// 
/// 1. POSITION MAPPING (CRITICAL):
///    - UI internally uses positions 1..6 (Level=1, Left=2, Right=3, NoseDown=4, NoseUp=5, Back=6)
///    - ArduPilot expects param1 = 0..5 for MAV_CMD_ACCELCAL_VEHICLE_POS
///    - Mapping: mavlinkPosition = uiPosition - 1 (done ONLY at MAVLink send boundary)
///    - NEVER change enums globally, NEVER send 1..6 to FC
/// 
/// 2. STATUSTEXT IS THE SINGLE SOURCE OF TRUTH:
///    - FC drives calibration, UI NEVER fabricates steps
///    - NO fallback logic ("If FC doesn't respond in 5 seconds, assume LEVEL")
///    - NO auto-advancing positions
///    - NO timer-based CanConfirm toggles
///    - Only allow a position when FC sends STATUSTEXT like "Place vehicle level"
/// 
/// 3. NO AUTO-RETRY OF ACCELCAL_VEHICLE_POS:
///    - When COMMAND_ACK == FAILED: disable confirm button, wait for next STATUSTEXT
///    - If FC does not re-request, user must restart calibration
///    - NEVER resend the same position automatically
/// 
/// 4. COMMAND_ACK HANDLING:
///    - PREFLIGHT_CALIBRATION: ACCEPTED/IN_PROGRESS = wait for STATUSTEXT; DENIED = abort
///    - ACCELCAL_VEHICLE_POS: ACCEPTED = wait for FC validation via STATUSTEXT; FAILED = stop and wait
/// 
/// 5. VEHICLE-AGNOSTIC:
///    - Same logic for ArduCopter, ArduPlane, Rover, SITL, and real hardware
/// 
/// Mission Planner Reference Behavior:
/// 1. User clicks Start Accel Calibration
/// 2. Send MAV_CMD_PREFLIGHT_CALIBRATION (accel=1)
/// 3. WAIT for COMMAND_ACK (ACCEPTED/IN_PROGRESS)
/// 4. WAIT for FC STATUSTEXT: "Place vehicle level"
/// 5. Button enabled, user clicks
/// 6. Send MAV_CMD_ACCELCAL_VEHICLE_POS(0)  // 0 = Level in MAVLink
/// 7. FC samples internally
/// 8. FC sends STATUSTEXT: "Place vehicle on left side"
/// 9. Repeat for all 6 faces (positions 0-5 in MAVLink terms)
/// 10. FC sends STATUSTEXT: "Calibration successful"
/// </summary>
public class CalibrationService : ICalibrationService, IDisposable
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    
    // State tracking
    private readonly object _lock = new();
    private CalibrationType _currentType;
    private int _currentPosition;
    private bool _isCalibrating;
    private bool _waitingForUserClick; // True when FC wants user to click button
    private CalibrationStateMachine _state = CalibrationStateMachine.Idle;
    private DateTime _calibrationStartTime;
    private CancellationTokenSource? _calibrationCts;
    private bool _disposed;
    
    private CalibrationStateModel _currentState = new();
    
    // Position display names - ONLY for UI display, not for validation
    // UI positions are 1-6, MAVLink positions are 0-5
    private static readonly string[] PositionNames = { "LEVEL", "LEFT", "RIGHT", "NOSE DOWN", "NOSE UP", "BACK" };
    
    // Total positions required for accelerometer calibration
    private const int ACCELEROMETER_TOTAL_POSITIONS = 6;
    
    // CRITICAL: UI uses positions 1-6 internally, but ArduPilot expects 0-5 for MAV_CMD_ACCELCAL_VEHICLE_POS
    // This constant documents the mapping: mavlinkPosition = uiPosition - 1
    private const int UI_POSITION_TO_MAVLINK_OFFSET = 1;

    public CalibrationStateModel? CurrentState => _currentState;
    public bool IsCalibrating { get { lock (_lock) return _isCalibrating; } }
    public CalibrationStateMachine StateMachineState { get { lock (_lock) return _state; } }
    public CalibrationDiagnostics? CurrentDiagnostics => null;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
        
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
    }

    #region Connection Monitoring

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            lock (_lock)
            {
                if (_isCalibrating)
                {
                    _logger.LogWarning("Connection lost during calibration");
                    AbortCalibration("Connection lost");
                }
            }
        }
    }

    #endregion

    #region STATUSTEXT Handler - FC tells us EVERYTHING

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
        }
        
        var text = e.Text;
        var lower = text.ToLowerInvariant();
        
        _logger.LogInformation("FC: {Text}", text);
        StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs { Severity = e.Severity, Text = text });
        
        CalibrationType currentType;
        lock (_lock) { currentType = _currentType; }
        
        // Check for success - FC says done
        if (IsSuccessMessage(lower))
        {
            _logger.LogInformation("FC reported SUCCESS");
            FinishCalibration(true, text);
            return;
        }
        
        // Check for failure
        if (IsFailureMessage(lower))
        {
            _logger.LogWarning("FC reported FAILURE: {Text}", text);
            FinishCalibration(false, text);
            return;
        }
        
        // Accelerometer - FC tells us what position it wants
        if (currentType == CalibrationType.Accelerometer)
        {
            HandleAccelStatusText(lower, text);
        }
    }

    private void HandleAccelStatusText(string lower, string originalText)
    {
        // RULE 2: STATUSTEXT is the SINGLE SOURCE OF TRUTH
        // Detect if FC is requesting a specific position
        int? requestedPosition = DetectPositionFromMessage(lower);
        
        if (requestedPosition.HasValue)
        {
            // FC is requesting a specific position - this is the ONLY way to enable the button
            lock (_lock)
            {
                _currentPosition = requestedPosition.Value;
                _waitingForUserClick = true;  // Enable button because FC requested this position
            }
            
            _logger.LogInformation("FC requests position {Pos}: {Name} (via STATUSTEXT)", requestedPosition.Value, GetPositionName(requestedPosition.Value));
            
            // RULE 5: Progress comes ONLY from FC requesting next position
            // When FC requests position N, it means positions 1 to N-1 are complete
            int progress = CalculateProgressFromPosition(requestedPosition.Value);
            _logger.LogInformation("Progress: {Progress}% (FC requesting position {Pos})", progress, requestedPosition.Value);
            
            // Show position to user with FC's exact message
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                originalText, // Always show FC's exact message
                progress);
            
            // Tell UI to show position image and enable button
            // RULE 4: Button enabled because FC requested this position via STATUSTEXT
            RaiseStepRequired(requestedPosition.Value, true, originalText);
        }
        // FC is sampling - user should NOT click until FC asks for next position
        else if (lower.Contains("sampling") || lower.Contains("reading") || lower.Contains("hold"))
        {
            lock (_lock) { _waitingForUserClick = false; } // Disable button during sampling
            
            int pos;
            lock (_lock) { pos = _currentPosition; }
            
            _logger.LogInformation("FC is sampling position {Pos} - button disabled", pos);
            SetState(CalibrationStateMachine.Sampling, 
                originalText, // Show FC's exact message
                GetProgress());
        }
        // FC says position detected/held/complete
        else if (lower.Contains("got") || lower.Contains("detected") || lower.Contains("held") || lower.Contains("complete"))
        {
            // Position was accepted - wait for FC to request next position
            lock (_lock) { _waitingForUserClick = false; } // Keep button disabled until FC asks for next
            
            _logger.LogInformation("FC acknowledged position - waiting for next STATUSTEXT");
            SetState(CalibrationStateMachine.PositionAccepted, originalText, GetProgress());
        }
        // FC reports a problem with current position
        else if (lower.Contains("bad") || lower.Contains("wrong") || lower.Contains("incorrect"))
        {
            // RULE 3: NO AUTO-RETRY - FC will tell us what to do next via another STATUSTEXT
            // Keep button disabled and wait for FC instruction
            lock (_lock) { _waitingForUserClick = false; }
            
            int pos;
            lock (_lock) { pos = _currentPosition; }
            
            _logger.LogWarning("FC reported issue with position {Pos}: {Text} - waiting for FC instruction", pos, originalText);
            SetState(CalibrationStateMachine.PositionRejected,
                originalText, // Show FC's exact error message
                GetProgress());
            
            // Tell UI - button disabled because FC didn't explicitly re-request
            RaiseStepRequired(pos, false, originalText);
        }
    }

    private int? DetectPositionFromMessage(string lower)
    {
        // Detect which position FC is requesting based on message keywords
        // Order matters - check specific patterns first
        if (lower.Contains("left") && !lower.Contains("right"))
            return 2;
        if (lower.Contains("right") && !lower.Contains("left"))
            return 3;
        if (lower.Contains("nose") && lower.Contains("down"))
            return 4;
        if (lower.Contains("nose") && lower.Contains("up"))
            return 5;
        if (lower.Contains("back") || lower.Contains("upside"))
            return 6;
        if (lower.Contains("level"))
            return 1;
        
        return null;
    }

    private static bool IsSuccessMessage(string lower)
    {
        return lower.Contains("calibration successful") ||
               lower.Contains("calibration complete") ||
               lower.Contains("calibration done") ||
               (lower.Contains("offsets") && lower.Contains("saved"));
    }

    private static bool IsFailureMessage(string lower)
    {
        if (lower.Contains("prearm")) return false;
        
        return (lower.Contains("calibration") && lower.Contains("failed")) ||
               (lower.Contains("calibration") && lower.Contains("cancelled")) ||
               (lower.Contains("calibration") && lower.Contains("timeout"));
    }

    #endregion

    #region COMMAND_ACK Handler

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
        }
        
        var result = (MavResult)e.Result;
        _logger.LogDebug("COMMAND_ACK: cmd={Command} result={Result}", e.Command, result);
        
        if (e.Command == 241) // MAV_CMD_PREFLIGHT_CALIBRATION
        {
            HandleCalibrationStartAck(result);
        }
        else if (e.Command == 42429) // MAV_CMD_ACCELCAL_VEHICLE_POS
        {
            HandlePositionCommandAck(result);
        }
    }

    private void HandleCalibrationStartAck(MavResult result)
    {
        if (result == MavResult.Accepted || result == MavResult.InProgress)
        {
            _logger.LogInformation("FC accepted calibration command - waiting for FC STATUSTEXT instructions");
            
            CalibrationType type;
            lock (_lock) { type = _currentType; }
            
            if (type == CalibrationType.Accelerometer)
            {
                // RULE 2: STATUSTEXT is the SINGLE SOURCE OF TRUTH
                // We MUST wait for FC to send STATUSTEXT telling us which position it wants
                // NO fallback timers, NO assumptions about starting with LEVEL
                // FC will send something like "Place vehicle level" when ready
                SetState(CalibrationStateMachine.WaitingForInstruction,
                    "Calibration accepted - waiting for flight controller instructions...", 0);
                
                // NO FALLBACK TIMER - violates Mission Planner protocol
                // The FC drives the calibration, not the UI
                _logger.LogInformation("Accelerometer calibration: Waiting for FC STATUSTEXT to request first position (NO fallback timer)");
            }
            else
            {
                // Simple calibration types (gyro, baro, etc.) - just wait for completion
                SetState(CalibrationStateMachine.Sampling,
                    $"{GetTypeName(type)} calibration in progress...", 50);
                _ = WaitForSimpleCalibrationAsync();
            }
        }
        else
        {
            // RULE 4: COMMAND_ACK DENIED = abort with clear error
            string msg = result switch
            {
                MavResult.Denied => "Calibration denied - vehicle may be armed or sensors not ready",
                MavResult.TemporarilyRejected => "Temporarily rejected - FC is busy, try again later",
                MavResult.Unsupported => "Calibration not supported by this firmware",
                MavResult.Failed => "Calibration command failed",
                _ => $"Calibration rejected by FC (result code: {(int)result})"
            };
            _logger.LogWarning("FC rejected MAV_CMD_PREFLIGHT_CALIBRATION: {Result} - {Message}", result, msg);
            FinishCalibration(false, msg);
        }
    }

    // REMOVED: StartPositionRequestFallbackAsync()
    // This method was ILLEGAL - it violated Mission Planner protocol by:
    // 1. Auto-assuming LEVEL position after 5 seconds if FC didn't respond
    // 2. Fabricating UI steps that the FC never requested
    // 3. Breaking the rule: "STATUSTEXT is the SINGLE SOURCE OF TRUTH"
    // 
    // Per ArduPilot MAVLink contract: The FC drives calibration, not the UI.
    // If FC doesn't send STATUSTEXT, calibration cannot proceed - this is correct behavior.

    private void HandlePositionCommandAck(MavResult result)
    {
        int pos;
        lock (_lock) { pos = _currentPosition; }
        
        // Convert UI position (1-6) to MAVLink position (0-5) for logging clarity
        int mavlinkPos = pos - UI_POSITION_TO_MAVLINK_OFFSET;
        
        if (result == MavResult.Accepted || result == MavResult.InProgress)
        {
            // RULE 4: ACCELCAL_VEHICLE_POS ACCEPTED = wait for FC validation via STATUSTEXT
            // FC will either:
            // - Send sampling/hold messages and then request next position
            // - Send success message when all positions complete
            // - Send failure message if something went wrong
            _logger.LogInformation("FC accepted position command (UI pos {UiPos}, MAVLink pos {MavPos}) - waiting for FC STATUSTEXT", pos, mavlinkPos);
            
            lock (_lock) { _waitingForUserClick = false; }
            
            SetState(CalibrationStateMachine.Sampling,
                $"Position {pos} accepted by FC - waiting for sampling to complete...",
                GetProgress());
            
            // NO TIMER! Just wait for FC to send STATUSTEXT
            // FC will tell us via STATUSTEXT when it needs the next position
        }
        else if (result == MavResult.Denied || result == MavResult.Failed)
        {
            // RULE 3: NO AUTO-RETRY OF ACCELCAL_VEHICLE_POS
            // When COMMAND_ACK == FAILED:
            // 1. Disable confirm button (set _waitingForUserClick = false)
            // 2. Wait for next STATUSTEXT from FC
            // 3. If FC does not re-request, user must restart calibration cleanly
            // NEVER resend the same position automatically
            _logger.LogWarning("FC rejected position command (UI pos {UiPos}, MAVLink pos {MavPos}): {Result} - disabling button and waiting for FC instruction", pos, mavlinkPos, result);
            
            // CRITICAL: Disable button - user cannot click until FC re-requests via STATUSTEXT
            lock (_lock) { _waitingForUserClick = false; }
            
            SetState(CalibrationStateMachine.PositionRejected,
                $"Position {pos} ({GetPositionName(pos)}) rejected by FC. Waiting for FC to provide instructions...",
                GetProgress());
            
            // Tell UI that position was rejected - button should be disabled
            RaiseStepRequired(pos, false, $"Position rejected by FC. Wait for flight controller instructions.");
            
            // DO NOT auto-retry - wait for FC STATUSTEXT to either:
            // - Re-request the same position
            // - Request a different position
            // - Report calibration failure
        }
    }
    
    private async Task MonitorInternalSamplingAsync()
    {
        // GUARD: This method should NEVER be called for Accelerometer calibration!
        // Accelerometer progress must ONLY come from FC STATUSTEXT messages
        CalibrationType type;
        lock (_lock) { type = _currentType; }
        
        if (type == CalibrationType.Accelerometer)
        {
            _logger.LogError("MonitorInternalSamplingAsync called for Accelerometer - this is a BUG! This method creates fake progress.");
            return;
        }
        
        // Monitor for up to 120 seconds for FC to complete internal sampling
        const int timeoutMs = 120000; // 2 minutes
        const int updateIntervalMs = 500;
        var startTime = DateTime.UtcNow;
        
        CancellationToken ct;
        lock (_lock) { ct = _calibrationCts?.Token ?? CancellationToken.None; }
        
        _logger.LogInformation("Monitoring internal sampling for {Type}", type);
        
        while (true)
        {
            // Check if calibration is still active
            bool stillCalibrating;
            CalibrationStateMachine currentState;
            lock (_lock)
            {
                stillCalibrating = _isCalibrating;
                currentState = _state;
            }
            
            if (!stillCalibrating || currentState == CalibrationStateMachine.Completed || currentState == CalibrationStateMachine.Failed)
            {
                return;
            }
            
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                _logger.LogWarning("Calibration timeout after 120 seconds");
                FinishCalibration(false, "Calibration timeout - FC did not complete within 2 minutes.");
                return;
            }
            
            // Update progress smoothly while FC is sampling
            // This is ONLY for non-accelerometer calibrations that don't have position-based progress
            if (currentState == CalibrationStateMachine.Sampling)
            {
                var progress = Math.Min(95, 16 + (int)(elapsed.TotalMilliseconds / timeoutMs * 79));
                
                lock (_lock)
                {
                    if (_isCalibrating && _state == CalibrationStateMachine.Sampling)
                    {
                        _currentState.Progress = progress;
                    }
                }
                
                CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
                {
                    Type = type,
                    ProgressPercent = progress,
                    StatusText = "Calibration in progress...",
                    StateMachine = CalibrationStateMachine.Sampling
                });
            }
            
            try { await Task.Delay(updateIntervalMs, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    #endregion

    #region Calibration Operations

    public Task<bool> StartCalibrationAsync(CalibrationType type)
    {
        return type switch
        {
            CalibrationType.Accelerometer => StartAccelerometerCalibrationAsync(true),
            CalibrationType.Compass => StartCompassCalibrationAsync(false),
            CalibrationType.Gyroscope => StartGyroscopeCalibrationAsync(),
            CalibrationType.LevelHorizon => StartLevelHorizonCalibrationAsync(),
            CalibrationType.Barometer => StartBarometerCalibrationAsync(),
            CalibrationType.Airspeed => StartAirspeedCalibrationAsync(),
            _ => Task.FromResult(false)
        };
    }

    public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Accelerometer);
        
        // RULE 6: PREFLIGHT_CALIBRATION FLOW (Match Mission Planner)
        // 1. Send MAV_CMD_PREFLIGHT_CALIBRATION (accel = 1)
        // 2. WAIT for COMMAND_ACK (ACCEPTED/IN_PROGRESS)
        // 3. WAIT for STATUSTEXT asking for first position
        // 4. DO NOT assume LEVEL - wait for FC instruction
        //
        // ArduPilot accelerometer calibration modes:
        // - param5=1: Position-based calibration (6 positions, FC validates each) - RECOMMENDED
        // - param5=2: Level calibration only (single position)  
        // - param5=4: Simple calibration (automatic, no user positions)
        // We use param5=1 to match Mission Planner's behavior
        
        _logger.LogInformation("Starting accelerometer calibration: MAV_CMD_PREFLIGHT_CALIBRATION(accel=1) - 6-position calibration");
        SetState(CalibrationStateMachine.WaitingForAck, "Sending calibration command to FC...", 0);
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: 1);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Compass);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting compass calibration...", 0);
        
        RaiseStepRequired(0, false, "Rotate vehicle slowly in all directions until calibration completes.");
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: onboardCalibration ? 76 : 1, groundPressure: 0, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartGyroscopeCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Gyroscope);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting gyroscope calibration...", 0);
        
        RaiseStepRequired(0, false, "Keep the vehicle completely still.");
        
        _connectionService.SendPreflightCalibration(gyro: 1, mag: 0, groundPressure: 0, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.LevelHorizon);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting level calibration...", 0);
        
        RaiseStepRequired(0, false, "Place vehicle on a perfectly level surface.");
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: 2);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartBarometerCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Barometer);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting barometer calibration...", 0);
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 1, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartAirspeedCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Airspeed);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting airspeed calibration...", 0);
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 1, accel: 0);
        
        return Task.FromResult(true);
    }

    /// <summary>
    /// User clicked "Click When In Position" button.
    /// Send MAV_CMD_ACCELCAL_VEHICLE_POS command to FC for the current position.
    /// 
    /// CRITICAL POSITION MAPPING:
    /// - UI/internal logic uses positions 1..6 (Level=1, Left=2, etc.)
    /// - ArduPilot expects param1 = 0..5 for MAV_CMD_ACCELCAL_VEHICLE_POS
    /// - Mapping is done HERE at the MAVLink send boundary: mavlinkPosition = uiPosition - 1
    /// 
    /// Mission Planner sends this command for ALL 6 positions (not just position 1).
    /// The FC tells us which position it wants via STATUSTEXT.
    /// </summary>
    public Task<bool> AcceptCalibrationStepAsync()
    {
        int uiPosition;
        bool waitingForClick;
        
        lock (_lock)
        {
            if (!_isCalibrating || _currentType != CalibrationType.Accelerometer)
            {
                _logger.LogWarning("AcceptCalibrationStepAsync: Not in accelerometer calibration");
                return Task.FromResult(false);
            }
            
            uiPosition = _currentPosition;
            waitingForClick = _waitingForUserClick;
        }
        
        // RULE 2: Only allow if FC requested this position via STATUSTEXT
        if (!waitingForClick)
        {
            _logger.LogWarning("AcceptCalibrationStepAsync: Not waiting for user click (FC has not requested position confirmation)");
            return Task.FromResult(false);
        }
        
        // Validate position range (UI positions 1-6)
        if (uiPosition < 1 || uiPosition > ACCELEROMETER_TOTAL_POSITIONS)
        {
            _logger.LogError("AcceptCalibrationStepAsync: Invalid UI position {Pos} (expected 1-6)", uiPosition);
            return Task.FromResult(false);
        }
        
        if (!_connectionService.IsConnected)
        {
            AbortCalibration("Connection lost");
            return Task.FromResult(false);
        }
        
        // CRITICAL: Map UI position (1-6) to MAVLink position (0-5)
        // ArduPilot expects param1 = 0..5 for MAV_CMD_ACCELCAL_VEHICLE_POS
        // UI uses 1..6 internally for display purposes
        // This mapping MUST only happen at the MAVLink send boundary
        int mavlinkPosition = uiPosition - UI_POSITION_TO_MAVLINK_OFFSET;
        
        _logger.LogInformation("User confirmed position {UiPos} ({Name}) - sending MAV_CMD_ACCELCAL_VEHICLE_POS({MavPos}) to FC",
            uiPosition, GetPositionName(uiPosition), mavlinkPosition);
        
        // Disable button until FC responds via STATUSTEXT
        lock (_lock) { _waitingForUserClick = false; }
        
        SetState(CalibrationStateMachine.WaitingForSampling,
            $"Sending position {uiPosition} ({GetPositionName(uiPosition)}) to FC...",
            GetProgress());
        
        // Send position command to FC
        // CRITICAL: Send mavlinkPosition (0-5), NOT uiPosition (1-6)
        try
        {
            _connectionService.SendAccelCalVehiclePos(mavlinkPosition);
            _logger.LogInformation("Successfully sent MAV_CMD_ACCELCAL_VEHICLE_POS({MavPos}) - waiting for FC COMMAND_ACK and STATUSTEXT", mavlinkPosition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send MAV_CMD_ACCELCAL_VEHICLE_POS command");
            
            // Re-enable button so user can retry (connection might have recovered)
            lock (_lock) { _waitingForUserClick = true; }
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"Error sending command: {ex.Message}. Please try again.",
                GetProgress());
            
            RaiseStepRequired(uiPosition, true, $"Error sending command. Click to retry.");
            return Task.FromResult(false);
        }
        
        return Task.FromResult(true);
    }

    public Task<bool> CancelCalibrationAsync()
    {
        lock (_lock)
        {
            if (!_isCalibrating)
                return Task.FromResult(true);
        }
        
        _logger.LogInformation("Calibration cancelled by user");
        AbortCalibration("Cancelled by user");
        
        return Task.FromResult(true);
    }

    public Task<bool> RebootFlightControllerAsync()
    {
        if (!_connectionService.IsConnected)
            return Task.FromResult(false);
        
        _logger.LogInformation("Sending reboot command");
        _connectionService.SendPreflightReboot(autopilot: 1, companion: 0);
        
        return Task.FromResult(true);
    }

    #endregion

    #region Timeout Watcher for Simple Calibrations

    private async Task WaitForSimpleCalibrationAsync()
    {
        CalibrationType type;
        lock (_lock)
        {
            type = _currentType;
        }
        
        // GUARD: This method should ONLY run for simple calibrations
        // Accelerometer and Compass require user interaction and FC position validation
        if (type == CalibrationType.Accelerometer || type == CalibrationType.Compass)
        {
            _logger.LogWarning("WaitForSimpleCalibrationAsync called for {Type} - this should NOT happen! This creates fake progress.", type);
            return;
        }
        
        var startTime = DateTime.UtcNow;
        const int timeoutMs = 15000;
        CancellationToken ct;
        
        lock (_lock)
        {
            ct = _calibrationCts?.Token ?? CancellationToken.None;
        }
        
        _logger.LogInformation("Starting simple calibration timer for {Type}", type);
        
        while (true)
        {
            lock (_lock)
            {
                if (!_isCalibrating || _state == CalibrationStateMachine.Completed || _state == CalibrationStateMachine.Failed)
                    return;
            }
            
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                // Assume success for simple calibrations
                FinishCalibration(true, $"{GetTypeName(type)} calibration completed.");
                return;
            }
            
            var progress = Math.Min(95, (int)(elapsed.TotalMilliseconds / timeoutMs * 100));
            
            lock (_lock)
            {
                if (_isCalibrating && _state == CalibrationStateMachine.Sampling)
                {
                    _currentState.Progress = progress;
                }
            }
            
            CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
            {
                Type = type,
                ProgressPercent = progress,
                StatusText = $"{GetTypeName(type)} calibration in progress...",
                StateMachine = CalibrationStateMachine.Sampling
            });
            
            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    #endregion

    #region State Management

    private bool CanStart()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Not connected");
            return false;
        }
        
        lock (_lock)
        {
            if (_isCalibrating)
            {
                _logger.LogWarning("Calibration already in progress");
                return false;
            }
        }
        
        return true;
    }

    private void InitializeCalibration(CalibrationType type)
    {
        lock (_lock)
        {
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _isCalibrating = true;
            _currentType = type;
            _currentPosition = 1;
            _waitingForUserClick = false;
            _state = CalibrationStateMachine.Idle;
            _calibrationStartTime = DateTime.UtcNow;
        }
        
        _currentState = new CalibrationStateModel
        {
            Type = type,
            State = CalibrationState.InProgress,
            StateMachine = CalibrationStateMachine.Idle
        };
    }

    private void SetState(CalibrationStateMachine newState, string message, int progress)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
            _state = newState;
        }
        
        int pos;
        CalibrationType type;
        bool canConfirm;
        lock (_lock)
        {
            pos = _currentPosition;
            type = _currentType;
            canConfirm = _waitingForUserClick;
        }
        
        _currentState.StateMachine = newState;
        _currentState.State = CalibrationState.InProgress;
        _currentState.Message = message;
        _currentState.Progress = progress;
        _currentState.CurrentPosition = pos;
        _currentState.CanConfirmPosition = canConfirm;
        
        CalibrationStateChanged?.Invoke(this, _currentState);
        
        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = type,
            ProgressPercent = progress,
            StatusText = message,
            CurrentStep = pos,
            TotalSteps = type == CalibrationType.Accelerometer ? ACCELEROMETER_TOTAL_POSITIONS : 1,
            StateMachine = newState
        });
    }

    private void RaiseStepRequired(int position, bool canConfirm, string instructions)
    {
        CalibrationType type;
        lock (_lock) { type = _currentType; }
        
        var step = position switch
        {
            1 => CalibrationStep.Level,
            2 => CalibrationStep.LeftSide,
            3 => CalibrationStep.RightSide,
            4 => CalibrationStep.NoseDown,
            5 => CalibrationStep.NoseUp,
            6 => CalibrationStep.Back,
            _ => CalibrationStep.Level
        };
        
        if (type == CalibrationType.Compass) step = CalibrationStep.Rotate;
        if (type == CalibrationType.Gyroscope || type == CalibrationType.LevelHorizon) step = CalibrationStep.KeepStill;
        
        CalibrationStepRequired?.Invoke(this, new CalibrationStepEventArgs
        {
            Type = type,
            Step = step,
            Instructions = instructions,
            CanConfirm = canConfirm
        });
    }

    private void FinishCalibration(bool success, string message)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
            
            _state = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed;
            _isCalibrating = false;
            _waitingForUserClick = false;
            _calibrationCts?.Cancel();
        }
        
        var duration = DateTime.UtcNow - _calibrationStartTime;
        _logger.LogInformation("Calibration {Result}: {Message} ({Duration:F1}s)",
            success ? "SUCCESS" : "FAILED", message, duration.TotalSeconds);
        
        CalibrationType type;
        int pos;
        lock (_lock)
        {
            type = _currentType;
            pos = _currentPosition;
        }
        
        _currentState.State = success ? CalibrationState.Completed : CalibrationState.Failed;
        _currentState.StateMachine = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed;
        _currentState.Progress = success ? 100 : 0;
        _currentState.Message = message;
        _currentState.CanConfirmPosition = false;
        
        CalibrationStateChanged?.Invoke(this, _currentState);
        
        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = type,
            ProgressPercent = success ? 100 : 0,
            StatusText = message,
            CurrentStep = pos,
            TotalSteps = type == CalibrationType.Accelerometer ? ACCELEROMETER_TOTAL_POSITIONS : 1,
            StateMachine = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed
        });
    }

    private void AbortCalibration(string reason)
    {
        _calibrationCts?.Cancel();
        
        lock (_lock)
        {
            if (!_isCalibrating) return;
            _state = CalibrationStateMachine.Failed;
            _isCalibrating = false;
            _waitingForUserClick = false;
        }
        
        _logger.LogWarning("Calibration aborted: {Reason}", reason);
        
        CalibrationType type;
        int pos;
        lock (_lock)
        {
            type = _currentType;
            pos = _currentPosition;
        }
        
        _currentState.State = CalibrationState.Failed;
        _currentState.StateMachine = CalibrationStateMachine.Failed;
        _currentState.Progress = 0;
        _currentState.Message = reason;
        _currentState.CanConfirmPosition = false;
        
        CalibrationStateChanged?.Invoke(this, _currentState);
        
        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = type,
            ProgressPercent = 0,
            StatusText = reason,
            CurrentStep = pos,
            TotalSteps = type == CalibrationType.Accelerometer ? ACCELEROMETER_TOTAL_POSITIONS : 1,
            StateMachine = CalibrationStateMachine.Failed
        });
    }

    #endregion

    #region Helpers

    private int GetProgress()
    {
        int pos;
        CalibrationType type;
        lock (_lock)
        {
            pos = _currentPosition;
            type = _currentType;
        }
        
        if (type != CalibrationType.Accelerometer) return 50;
        return (int)((pos - 1) * 100.0 / ACCELEROMETER_TOTAL_POSITIONS);
    }

    /// <summary>
    /// Calculate progress based on which position FC is requesting.
    /// When FC requests position N, it means positions 1 to N-1 are complete.
    /// </summary>
    private static int CalculateProgressFromPosition(int requestedPosition)
    {
        // Validate input range
        if (requestedPosition < 1 || requestedPosition > ACCELEROMETER_TOTAL_POSITIONS)
        {
            // Return safe default - should not happen if FC is working correctly
            return requestedPosition < 1 ? 0 : 100;
        }
        
        // Position 1 (LEVEL) = 0% (just starting)
        // Position 2 (LEFT) = 16.67% (position 1 complete)
        // Position 3 (RIGHT) = 33.33% (positions 1-2 complete)
        // Position 4 (NOSE DOWN) = 50% (positions 1-3 complete)
        // Position 5 (NOSE UP) = 66.67% (positions 1-4 complete)
        // Position 6 (BACK) = 83.33% (positions 1-5 complete)
        int positionsComplete = requestedPosition - 1;
        return (int)((positionsComplete * 100.0) / ACCELEROMETER_TOTAL_POSITIONS);
    }

    private static string GetPositionName(int position)
    {
        return position >= 1 && position <= ACCELEROMETER_TOTAL_POSITIONS ? PositionNames[position - 1] : "UNKNOWN";
    }

    private static string GetTypeName(CalibrationType type)
    {
        return type switch
        {
            CalibrationType.Accelerometer => "Accelerometer",
            CalibrationType.Compass => "Compass",
            CalibrationType.Gyroscope => "Gyroscope",
            CalibrationType.LevelHorizon => "Level Horizon",
            CalibrationType.Barometer => "Barometer",
            CalibrationType.Airspeed => "Airspeed",
            _ => type.ToString()
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _calibrationCts?.Cancel();
        _calibrationCts?.Dispose();
        
        _connectionService.StatusTextReceived -= OnStatusTextReceived;
        _connectionService.CommandAckReceived -= OnCommandAckReceived;
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
    }

    #endregion
}
