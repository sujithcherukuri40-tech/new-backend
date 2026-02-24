using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service interface for parsing drone log files and extracting waypoints, GPS tracks, and flight data.
/// Supports ArduPilot DataFlash logs (.bin) and text logs (.log).
/// </summary>
public interface ILogParserService
{
    /// <summary>
    /// Event raised when parsing progress changes
    /// </summary>
    event EventHandler<int>? ParseProgressChanged;
    
    /// <summary>
    /// Whether a log file is currently loaded
    /// </summary>
    bool IsLogLoaded { get; }
    
    /// <summary>
    /// Current loaded log file path
    /// </summary>
    string? LoadedFilePath { get; }
    
    /// <summary>
    /// Parse a log file asynchronously
    /// </summary>
    /// <param name="filePath">Path to the log file (.bin or .log)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if parsing succeeded</returns>
    Task<bool> ParseLogFileAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract waypoints from CMD messages in the log
    /// </summary>
    /// <returns>List of extracted waypoints</returns>
    List<DroneWaypoint> ExtractWaypoints();
    
    /// <summary>
    /// Calculate flight summary statistics from the loaded log
    /// </summary>
    /// <returns>Flight summary with distance, duration, altitude, speed, etc.</returns>
    FlightSummary CalculateFlightSummary();
    
    /// <summary>
    /// Get the home position (first GPS fix or SET_HOME command)
    /// </summary>
    /// <returns>Home position waypoint or null</returns>
    DroneWaypoint? GetHomePosition();
    
    /// <summary>
    /// Get field data for graphing
    /// </summary>
    /// <param name="messageType">Message type (e.g., "ATT", "GPS")</param>
    /// <param name="fieldName">Field name (e.g., "Roll", "Alt")</param>
    /// <returns>List of data points</returns>
    List<LogDataPoint>? GetFieldData(string messageType, string fieldName);
    
    /// <summary>
    /// Get all available data series names for graphing
    /// </summary>
    /// <returns>List of series names in "MessageType.FieldName" format</returns>
    List<string> GetAvailableDataSeries();
    
    /// <summary>
    /// Get all unique message type names
    /// </summary>
    List<string> GetMessageTypes();
    
    /// <summary>
    /// Get total count of messages of a specific type
    /// </summary>
    int GetMessageCount(string messageType);
    
    /// <summary>
    /// Get parameters from the log file
    /// </summary>
    /// <returns>Dictionary of parameter name to value</returns>
    Dictionary<string, float> GetLogParameters();
    
    /// <summary>
    /// Clear the current log and release resources
    /// </summary>
    void ClearLog();
}
