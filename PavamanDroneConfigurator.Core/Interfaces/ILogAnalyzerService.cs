using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for browsing, downloading, and analyzing flight logs from the flight controller.
/// Provides Mission Planner-style functionality including graphing and message browsing.
/// </summary>
public interface ILogAnalyzerService
{
    #region Events
    
    /// <summary>
    /// Event raised when log file list is updated.
    /// </summary>
    event EventHandler<List<LogFileInfo>>? LogFilesUpdated;

    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    event EventHandler<LogDownloadProgress>? DownloadProgressChanged;

    /// <summary>
    /// Event raised when a download completes.
    /// </summary>
    event EventHandler<(LogFileInfo File, bool Success, string? Error)>? DownloadCompleted;

    /// <summary>
    /// Event raised when analysis completes.
    /// </summary>
    event EventHandler<LogAnalysisResult>? AnalysisCompleted;

    /// <summary>
    /// Event raised when a log file is loaded and parsed.
    /// </summary>
    event EventHandler<LogParseResult>? LogParsed;

    /// <summary>
    /// Event raised when parse progress changes (0-100 percent).
    /// </summary>
    event EventHandler<int>? ParseProgressChanged;

    #endregion

    #region Status Properties

    /// <summary>
    /// Whether a download is currently in progress.
    /// </summary>
    bool IsDownloading { get; }

    /// <summary>
    /// Whether an analysis is currently in progress.
    /// </summary>
    bool IsAnalyzing { get; }

    /// <summary>
    /// Whether MAVLink FTP is available.
    /// </summary>
    bool IsFtpAvailable { get; }

    /// <summary>
    /// Whether a log file is currently loaded.
    /// </summary>
    bool IsLogLoaded { get; }

    /// <summary>
    /// Current loaded log file path.
    /// </summary>
    string? LoadedLogPath { get; }

    #endregion

    #region FC Log File Operations

    /// <summary>
    /// Gets the list of log files from the flight controller.
    /// </summary>
    Task<List<LogFileInfo>> GetLogFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the log file list from the flight controller.
    /// </summary>
    Task RefreshLogFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a log file from the flight controller.
    /// </summary>
    Task<bool> DownloadLogFileAsync(LogFileInfo logFile, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads multiple log files.
    /// </summary>
    Task<int> DownloadLogFilesAsync(IEnumerable<LogFileInfo> logFiles, string destinationFolder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the current download operation.
    /// </summary>
    void CancelDownload();

    /// <summary>
    /// Deletes a log file from the flight controller.
    /// </summary>
    Task<bool> DeleteLogFileAsync(LogFileInfo logFile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all log files from the flight controller.
    /// </summary>
    Task<int> DeleteAllLogFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available log directories on the flight controller.
    /// </summary>
    Task<List<FtpDirectoryEntry>> GetDirectoryListingAsync(string path, CancellationToken cancellationToken = default);

    #endregion

    #region Log Parsing and Loading

    /// <summary>
    /// Loads and parses a log file for viewing and graphing.
    /// </summary>
    /// <param name="logFilePath">Path to the log file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parse result with available message types and fields.</returns>
    Task<LogParseResult> LoadLogFileAsync(string logFilePath, CancellationToken cancellationToken = default,
        IProgress<int>? parseProgress = null);

    /// <summary>
    /// Unloads the current log file.
    /// </summary>
    void UnloadLog();

    #endregion

    #region Message Browsing (Mission Planner style)

    /// <summary>
    /// Gets all available message types in the loaded log.
    /// </summary>
    List<LogMessageTypeGroup> GetMessageTypes();

    /// <summary>
    /// Gets messages of a specific type.
    /// </summary>
    /// <param name="messageType">Message type name (e.g., "GPS", "ATT").</param>
    /// <param name="skip">Number of messages to skip.</param>
    /// <param name="take">Number of messages to take.</param>
    /// <returns>List of messages.</returns>
    List<LogMessageView> GetMessages(string messageType, int skip = 0, int take = 1000);

    /// <summary>
    /// Gets the total count of messages for a type.
    /// </summary>
    int GetMessageCount(string messageType);

    /// <summary>
    /// Searches messages for a value.
    /// </summary>
    List<LogMessageView> SearchMessages(string searchText, int maxResults = 100);

    #endregion

    #region Graphing (Mission Planner style)

    /// <summary>
    /// Gets all available data fields for graphing.
    /// </summary>
    List<LogFieldInfo> GetAvailableGraphFields();

    /// <summary>
    /// Gets graph data for the specified fields.
    /// </summary>
    /// <param name="fieldKeys">Field keys in format "MessageType.FieldName".</param>
    /// <returns>Graph configuration with data.</returns>
    LogGraphConfiguration GetGraphData(params string[] fieldKeys);

    /// <summary>
    /// Gets graph data for a time range.
    /// </summary>
    /// <param name="startTime">Start time in seconds.</param>
    /// <param name="endTime">End time in seconds.</param>
    /// <param name="fieldKeys">Field keys to graph.</param>
    /// <returns>Graph configuration with data.</returns>
    LogGraphConfiguration GetGraphData(double startTime, double endTime, params string[] fieldKeys);

    /// <summary>
    /// Gets statistical summary for a field.
    /// </summary>
    LogFieldStatistics GetFieldStatistics(string fieldKey);
    
    /// <summary>
    /// Gets raw data series for a specific field (for custom processing).
    /// </summary>
    /// <param name="messageType">Message type (e.g., "GPS", "ATT").</param>
    /// <param name="fieldName">Field name (e.g., "Lat", "Roll").</param>
    /// <returns>List of data points, or null if not found.</returns>
    List<LogDataPoint>? GetFieldData(string messageType, string fieldName);

    #endregion

    #region Legacy Analysis

    /// <summary>
    /// Analyzes a downloaded log file (legacy simple analysis).
    /// </summary>
    Task<LogAnalysisResult> AnalyzeLogFileAsync(string logFilePath, CancellationToken cancellationToken = default);

    #endregion

    #region Parameters from Log

    /// <summary>
    /// Gets parameters stored in the log file.
    /// </summary>
    Dictionary<string, float> GetLogParameters();

    #endregion

    #region Scripting

    /// <summary>
    /// Runs a log analysis script.
    /// </summary>
    /// <param name="script">Script content.</param>
    /// <returns>Script execution result.</returns>
    Task<ScriptExecutionResult> RunScriptAsync(string script, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available script functions.
    /// </summary>
    List<ScriptFunctionInfo> GetScriptFunctions();

    #endregion

    #region Utility

    /// <summary>
    /// Opens the log file in the default application.
    /// </summary>
    void OpenLogFile(string logFilePath);

    /// <summary>
    /// Opens the folder containing the log file.
    /// </summary>
    void OpenLogFolder(string logFilePath);

    /// <summary>
    /// Gets the default download folder for logs.
    /// </summary>
    string GetDefaultDownloadFolder();

    #endregion
}

/// <summary>
/// Result of parsing a log file.
/// </summary>
public class LogParseResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int MessageCount { get; set; }
    public int MessageTypeCount { get; set; }
    public TimeSpan Duration { get; set; }
    public string DurationDisplay => Duration.TotalSeconds > 0 
        ? $"{Duration.Hours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}" 
        : "N/A";
    public List<LogMessageTypeGroup> MessageTypes { get; set; } = new();
    public Dictionary<string, float> Parameters { get; set; } = new();
}

/// <summary>
/// Represents a message for display in the UI.
/// </summary>
public class LogMessageView
{
    public int Index { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public Dictionary<string, string> Fields { get; set; } = new();
    public string FieldsDisplay => string.Join(", ", Fields.Select(f => $"{f.Key}={f.Value}"));
}

/// <summary>
/// Statistics for a log field.
/// </summary>
public class LogFieldStatistics
{
    public string FieldKey { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public double Average { get; set; }
    public double StandardDeviation { get; set; }
    public double Median { get; set; }
}

/// <summary>
/// Result of running a script.
/// </summary>
public class ScriptExecutionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Output { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Information about a script function.
/// </summary>
public class ScriptFunctionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
}
