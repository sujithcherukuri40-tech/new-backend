namespace PavamanDroneConfigurator.Core.Enums;

public enum CalibrationState
{
    Idle,
    NotStarted,
    InProgress,
    /// <summary>
    /// Calibration is waiting for user action (e.g., placing vehicle in position)
    /// </summary>
    WaitingForUserAction,
    Completed,
    Failed
}
