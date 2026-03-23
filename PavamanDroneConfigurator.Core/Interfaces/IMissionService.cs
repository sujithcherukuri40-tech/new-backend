using PavamanDroneConfigurator.Core.Models;
using System.Collections.ObjectModel;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for mission planning, upload, and download
/// </summary>
public interface IMissionService
{
    /// <summary>
    /// Current mission items
    /// </summary>
    ObservableCollection<MissionItem> MissionItems { get; }
    
    /// <summary>
    /// Home position (first waypoint)
    /// </summary>
    GeoPoint? HomePosition { get; set; }
    
    /// <summary>
    /// Current waypoint index being flown
    /// </summary>
    int CurrentWaypointIndex { get; }
    
    /// <summary>
    /// Total estimated distance in km
    /// </summary>
    double TotalDistanceKm { get; }
    
    /// <summary>
    /// Estimated flight time
    /// </summary>
    TimeSpan EstimatedFlightTime { get; }
    
    /// <summary>
    /// Event raised when mission items change
    /// </summary>
    event EventHandler<MissionItem>? MissionItemAdded;
    event EventHandler<int>? MissionItemRemoved;
    event EventHandler? MissionCleared;
    event EventHandler<int>? CurrentWaypointChanged;
    
    /// <summary>
    /// Add a waypoint at the specified position
    /// </summary>
    MissionItem AddWaypoint(double lat, double lon, float altitude);
    
    /// <summary>
    /// Add a takeoff command
    /// </summary>
    MissionItem AddTakeoff(float altitude);
    
    /// <summary>
    /// Add a land command at position
    /// </summary>
    MissionItem AddLand(double lat, double lon);
    
    /// <summary>
    /// Add RTL command
    /// </summary>
    MissionItem AddRtl();
    
    /// <summary>
    /// Add loiter command
    /// </summary>
    MissionItem AddLoiter(double lat, double lon, float altitude, float durationSeconds);
    
    /// <summary>
    /// Add orbit around a point
    /// </summary>
    MissionItem AddOrbit(double lat, double lon, float altitude, float radiusMeters, int turns);
    
    /// <summary>
    /// Add spray ON command
    /// </summary>
    MissionItem AddSprayOn(int relayNumber = 0);
    
    /// <summary>
    /// Add spray OFF command
    /// </summary>
    MissionItem AddSprayOff(int relayNumber = 0);
    
    /// <summary>
    /// Remove waypoint at index
    /// </summary>
    void RemoveWaypoint(int index);
    
    /// <summary>
    /// Move waypoint to new position
    /// </summary>
    void MoveWaypoint(int index, double lat, double lon);
    
    /// <summary>
    /// Update waypoint altitude
    /// </summary>
    void UpdateWaypointAltitude(int index, float altitude);
    
    /// <summary>
    /// Reorder waypoint
    /// </summary>
    void ReorderWaypoint(int fromIndex, int toIndex);
    
    /// <summary>
    /// Clear all mission items
    /// </summary>
    void ClearMission();
    
    /// <summary>
    /// Upload mission to flight controller
    /// </summary>
    Task UploadMissionAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Download mission from flight controller
    /// </summary>
    Task DownloadMissionAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Start AUTO mission on flight controller
    /// </summary>
    Task StartMissionAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Pause mission (switch to GUIDED/hold)
    /// </summary>
    Task PauseMissionAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Resume mission
    /// </summary>
    Task ResumeMissionAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Abort mission and loiter
    /// </summary>
    Task AbortMissionAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Clear mission on flight controller
    /// </summary>
    Task ClearMissionOnDroneAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Save mission to file
    /// </summary>
    Task SaveMissionAsync(string filePath, CancellationToken ct = default);
    
    /// <summary>
    /// Load mission from file
    /// </summary>
    Task LoadMissionAsync(string filePath, CancellationToken ct = default);
    
    /// <summary>
    /// Generate survey grid from boundary polygon
    /// </summary>
    SurveyResult GenerateSurveyGrid(SurveyConfig config);
    
    /// <summary>
    /// Add survey grid waypoints to mission
    /// </summary>
    void AddSurveyGridToMission(SurveyResult surveyResult, bool insertSprayCommands);
}
