using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// MAG_CAL_PROGRESS message data (MAVLink message ID 191).
/// Reports progress of compass calibration from ArduPilot.
/// </summary>
public class MagCalProgressData
{
    /// <summary>Compass being calibrated (0, 1, 2)</summary>
    public byte CompassId { get; set; }
    
    /// <summary>Bitmask of compasses being calibrated</summary>
    public byte CalMask { get; set; }
    
    /// <summary>Calibration status</summary>
    public MagCalStatus CalStatus { get; set; }
    
    /// <summary>Attempt number</summary>
    public byte Attempt { get; set; }
    
    /// <summary>Completion percentage (0-100)</summary>
    public byte CompletionPct { get; set; }
    
    /// <summary>Bitmask of sphere sections completed (10 bytes)</summary>
    public byte[] CompletionMask { get; set; } = new byte[10];
    
    /// <summary>Body frame direction vector X for display</summary>
    public float DirectionX { get; set; }
    
    /// <summary>Body frame direction vector Y for display</summary>
    public float DirectionY { get; set; }
    
    /// <summary>Body frame direction vector Z for display</summary>
    public float DirectionZ { get; set; }
}

/// <summary>
/// MAG_CAL_REPORT message data (MAVLink message ID 192).
/// Contains final calibration results with offsets.
/// </summary>
public class MagCalReportData
{
    /// <summary>Compass being reported (0, 1, 2)</summary>
    public byte CompassId { get; set; }
    
    /// <summary>Bitmask of compasses being calibrated</summary>
    public byte CalMask { get; set; }
    
    /// <summary>Calibration status</summary>
    public MagCalStatus CalStatus { get; set; }
    
    /// <summary>Whether calibration was autosaved (1 = autosaved)</summary>
    public byte Autosaved { get; set; }
    
    /// <summary>Fitness value (lower is better, typically &lt; 50)</summary>
    public float Fitness { get; set; }
    
    /// <summary>X offset</summary>
    public float OfsX { get; set; }
    
    /// <summary>Y offset</summary>
    public float OfsY { get; set; }
    
    /// <summary>Z offset</summary>
    public float OfsZ { get; set; }
    
    /// <summary>X diagonal element</summary>
    public float DiagX { get; set; }
    
    /// <summary>Y diagonal element</summary>
    public float DiagY { get; set; }
    
    /// <summary>Z diagonal element</summary>
    public float DiagZ { get; set; }
    
    /// <summary>X off-diagonal element</summary>
    public float OffdiagX { get; set; }
    
    /// <summary>Y off-diagonal element</summary>
    public float OffdiagY { get; set; }
    
    /// <summary>Z off-diagonal element</summary>
    public float OffdiagZ { get; set; }
    
    /// <summary>Compass orientation enum value</summary>
    public byte OrientationConfidence { get; set; }
    
    /// <summary>Old compass orientation before calibration</summary>
    public byte OldOrientation { get; set; }
    
    /// <summary>New compass orientation after calibration</summary>
    public byte NewOrientation { get; set; }
    
    /// <summary>Scale factor (for soft iron correction)</summary>
    public float ScaleFactor { get; set; }
    
    /// <summary>Whether calibration is considered acceptable</summary>
    public bool IsAcceptable => CalStatus == MagCalStatus.Success && Fitness < 50.0f;
    
    /// <summary>
    /// Get formatted offset string for display
    /// </summary>
    public string GetOffsetString() => $"X: {OfsX:F1}, Y: {OfsY:F1}, Z: {OfsZ:F1}";
    
    /// <summary>
    /// Get maximum absolute offset value
    /// </summary>
    public float GetMaxAbsOffset() => Math.Max(Math.Max(Math.Abs(OfsX), Math.Abs(OfsY)), Math.Abs(OfsZ));
}

/// <summary>
/// State model for compass calibration UI
/// </summary>
public class CompassCalibrationStateModel
{
    /// <summary>Overall calibration state</summary>
    public CompassCalibrationState State { get; set; } = CompassCalibrationState.Idle;
    
    /// <summary>Status message for display</summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>Whether calibration is in progress</summary>
    public bool IsCalibrating => State == CompassCalibrationState.RunningSphereFit ||
                                  State == CompassCalibrationState.RunningEllipsoidFit ||
                                  State == CompassCalibrationState.Starting;
    
    /// <summary>Whether user can accept the calibration</summary>
    public bool CanAccept => State == CompassCalibrationState.WaitingForAccept;
    
    /// <summary>Whether user can cancel the calibration</summary>
    public bool CanCancel => IsCalibrating || State == CompassCalibrationState.WaitingForAccept;
    
    /// <summary>Progress per compass (compass_id -> progress percent)</summary>
    public Dictionary<byte, int> CompassProgress { get; set; } = new();
    
    /// <summary>Reports per compass (compass_id -> report data)</summary>
    public Dictionary<byte, MagCalReportData> CompassReports { get; set; } = new();
    
    /// <summary>Number of compasses being calibrated</summary>
    public int CompassCount => CompassProgress.Count;
    
    /// <summary>Number of compasses completed</summary>
    public int CompletedCount => CompassReports.Count(r => r.Value.Autosaved == 1 || r.Value.CalStatus == MagCalStatus.Success);
}

/// <summary>
/// Compass offset thresholds for UI color coding (from MissionPlanner)
/// </summary>
public static class CompassOffsetThresholds
{
    /// <summary>Offset above this is RED (bad calibration)</summary>
    public const int Red = 600;
    
    /// <summary>Offset above this is YELLOW (marginal calibration)</summary>
    public const int Yellow = 400;
    
    /// <summary>
    /// Get color for offset value
    /// </summary>
    public static string GetColorForOffset(float maxOffset)
    {
        var absOffset = Math.Abs(maxOffset);
        if (absOffset > Red) return "#EF4444"; // Red
        if (absOffset > Yellow) return "#EAB308"; // Yellow
        if (absOffset == 0) return "#EF4444"; // Red - not calibrated
        return "#10B981"; // Green
    }
}
