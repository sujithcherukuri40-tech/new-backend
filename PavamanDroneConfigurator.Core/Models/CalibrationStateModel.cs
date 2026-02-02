using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Calibration state model for UI binding
/// </summary>
public class CalibrationStateModel
{
    /// <summary>
    /// Current calibration type
    /// </summary>
    public CalibrationType Type { get; set; }
    
    /// <summary>
    /// High-level calibration state
    /// </summary>
    public CalibrationState State { get; set; } = CalibrationState.NotStarted;
    
    /// <summary>
    /// Detailed state machine state
    /// </summary>
    public CalibrationStateMachine StateMachine { get; set; } = CalibrationStateMachine.Idle;
    
    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int Progress { get; set; }
    
    /// <summary>
    /// Current status message (from FC or internal)
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// For accelerometer: current position number (1-6)
    /// </summary>
    public int CurrentPosition { get; set; }
    
    /// <summary>
    /// For accelerometer: whether user can click "confirm position"
    /// </summary>
    public bool CanConfirmPosition { get; set; }
    
    /// <summary>
    /// For accelerometer: list of completed position values (1-6)
    /// Used by UI to show green indicators for completed positions.
    /// This is the SINGLE SOURCE OF TRUTH for completed positions.
    /// </summary>
    public List<int> CompletedPositions { get; set; } = new();
    
    /// <summary>
    /// Diagnostics for this calibration session
    /// </summary>
    public CalibrationDiagnostics? Diagnostics { get; set; }
}
