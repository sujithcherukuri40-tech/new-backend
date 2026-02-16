using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Log Analyzer page.
/// Provides Mission Planner-style log viewing with graphing, message browsing, and scripting.
/// </summary>
public partial class LogAnalyzerPageViewModel : ViewModelBase
{
    private readonly ILogger<LogAnalyzerPageViewModel> _logger;
    private readonly ILogAnalyzerService _logAnalyzerService;
    private readonly IConnectionService _connectionService;
    private readonly ILogQueryEngine? _queryEngine;
    private readonly ILogEventDetector? _eventDetector;
    private readonly ILogExportService? _exportService;
    private readonly IArduPilotMetadataLoader? _metadataLoader;

    #region Status Properties

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _statusMessage = "Select a log file to analyze.";

    [ObservableProperty]
    private bool _isLogLoaded;

    [ObservableProperty]
    private string _loadedLogInfo = string.Empty;

    [ObservableProperty]
    private int _loadProgress;

    #endregion

    #region Tab Properties

    [ObservableProperty]
    private int _selectedTabIndex;

    public const int TAB_OVERVIEW = 0;
    public const int TAB_PLOT = 1;
    public const int TAB_MAP = 2;
    public const int TAB_EVENTS = 3;
    public const int TAB_PARAMS = 4;

    #endregion

    #region Overview Properties

    [ObservableProperty]
    private string _logFileName = string.Empty;

    [ObservableProperty]
    private string _logFileSize = string.Empty;

    [ObservableProperty]
    private string _logDuration = string.Empty;

    [ObservableProperty]
    private string _logMessageCount = string.Empty;

    [ObservableProperty]
    private string _logMessageTypes = string.Empty;

    [ObservableProperty]
    private string _vehicleType = string.Empty;

    [ObservableProperty]
    private string _firmwareVersion = string.Empty;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private bool _hasGpsData;

    [ObservableProperty]
    private bool _hasAttitudeData;

    [ObservableProperty]
    private bool _hasVibeData;

    #endregion

    #region File Properties

    [ObservableProperty]
    private ObservableCollection<LogFileInfo> _logFiles = new();

    [ObservableProperty]
    private LogFileInfo? _selectedLogFile;

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private bool _isDownloadDialogOpen;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressText = string.Empty;

    [ObservableProperty]
    private string _downloadFolder;

    #endregion

    #region Message Browser Properties

    [ObservableProperty]
    private ObservableCollection<LogMessageTypeGroup> _messageTypes = new();

    [ObservableProperty]
    private LogMessageTypeGroup? _selectedMessageType;

    [ObservableProperty]
    private ObservableCollection<LogMessageView> _currentMessages = new();

    [ObservableProperty]
    private string _messageSearchText = string.Empty;

    [ObservableProperty]
    private int _currentMessagePage;

    [ObservableProperty]
    private int _totalMessagePages;

    private const int MessagesPerPage = 100;
    
    // Raw data preview constants
    private const int MaxRawDataRowsPerType = 50;
    private const int MaxRawDataTotalRows = 200;
    private const int MaxMessageTypesForRawData = 10;

    #endregion

    #region Graph Properties

    [ObservableProperty]
    private ObservableCollection<LogFieldInfo> _availableFields = new();

    [ObservableProperty]
    private ObservableCollection<LogFieldInfo> _selectedGraphFields = new();

    [ObservableProperty]
    private LogGraphConfiguration? _currentGraph;

    [ObservableProperty]
    private bool _hasGraphData;

    [ObservableProperty]
    private string _graphFieldFilter = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LogFieldInfo> _filteredFields = new();

    [ObservableProperty]
    private ObservableCollection<LogMessageTypeNode> _messageTypesTree = new();

    [ObservableProperty]
    private bool _hasTreeData;

    [ObservableProperty]
    private double _cursorTime;

    [ObservableProperty]
    private string _cursorTimeDisplay = "00:00:00.000";

    [ObservableProperty]
    private ObservableCollection<CursorReadout> _cursorReadouts = new();

    [ObservableProperty]
    private double _zoomStartTime;

    [ObservableProperty]
    private double _zoomEndTime;

    #endregion

    #region Events Properties

    [ObservableProperty]
    private ObservableCollection<LogEvent> _detectedEvents = new();

    [ObservableProperty]
    private ObservableCollection<LogEvent> _filteredEvents = new();

    [ObservableProperty]
    private LogEvent? _selectedEvent;

    [ObservableProperty]
    private bool _showInfoEvents = true;

    [ObservableProperty]
    private bool _showWarningEvents = true;

    [ObservableProperty]
    private bool _showErrorEvents = true;

    [ObservableProperty]
    private bool _showCriticalEvents = true;

    [ObservableProperty]
    private string _eventSearchText = string.Empty;

    [ObservableProperty]
    private EventSummary? _eventSummary;
    
    /// <summary>
    /// Indicates whether there are filtered events available for export
    /// </summary>
    public bool HasFilteredEvents => FilteredEvents.Count > 0;
    
    /// <summary>
    /// Indicates whether to show the "No events present in this log" message.
    /// Shows when log is loaded but no events were detected.
    /// </summary>
    public bool ShowNoEventsMessage => IsLogLoaded && DetectedEvents.Count == 0;
    
    // Enhanced Events Tab Properties
    [ObservableProperty]
    private EventDisplaySummary _eventDisplaySummary = new();
    
    [ObservableProperty]
    private EventPaginationState _eventPagination = new();
    
    [ObservableProperty]
    private int _selectedTimeRangeIndex = 4; // Default to "All"
    
    [ObservableProperty]
    private string _selectedSeverityFilter = "All";
    
    [ObservableProperty]
    private string _selectedEventTypeFilter = "All";
    
    [ObservableProperty]
    private string _selectedSourceFilter = "All";
    
    [ObservableProperty]
    private ObservableCollection<string> _availableSeverities = new() { "All", "Error", "Warning", "Info", "Critical" };
    
    [ObservableProperty]
    private ObservableCollection<string> _availableEventTypes = new() { "All" };
    
    [ObservableProperty]
    private ObservableCollection<string> _availableSources = new() { "All" };
    
    [ObservableProperty]
    private int _selectedAutoRefreshIndex;
    
    private System.Timers.Timer? _autoRefreshTimer;
    
    public ObservableCollection<string> TimeRangeOptions { get; } = new()
    {
        "Last 1 hour",
        "Last 24 hours", 
        "Last 7 days",
        "Custom",
        "All"
    };
    
    public ObservableCollection<string> AutoRefreshOptions { get; } = new()
    {
        "Off",
        "5 seconds",
        "10 seconds",
        "30 seconds"
    };
    
    // Raw data display for Events tab
    [ObservableProperty]
    private ObservableCollection<LogMessageView> _rawLogMessages = new();
    
    [ObservableProperty]
    private bool _hasRawLogData;
    
    [ObservableProperty]
    private string _rawDataRowCount = "0 rows";

    #endregion

    #region Parameters Properties

    [ObservableProperty]
    private ObservableCollection<ParameterChange> _parameterChanges = new();

    [ObservableProperty]
    private ParameterChange? _selectedParameterChange;

    [ObservableProperty]
    private string _parameterSearchText = string.Empty;
    
    [ObservableProperty]
    private ObservableCollection<LogParameter> _logParameters = new();
    
    [ObservableProperty]
    private ObservableCollection<LogParameter> _filteredLogParameters = new();
    
    [ObservableProperty]
    private LogParameter? _selectedLogParameter;
    
    [ObservableProperty]
    private bool _hasLogParameters;

    #endregion

    #region Map Properties

    [ObservableProperty]
    private ObservableCollection<GpsPoint> _gpsTrack = new();
    
    [ObservableProperty]
    private ObservableCollection<Controls.GpsTrackPoint> _gpsTrackPoints = new();
    
    [ObservableProperty]
    private ObservableCollection<LogEvent> _criticalMapEvents = new();
    
    [ObservableProperty]
    private GpsPoint? _currentMapPosition;

    [ObservableProperty]
    private double _mapCenterLat;

    [ObservableProperty]
    private double _mapCenterLng;

    [ObservableProperty]
    private int _mapZoom = 15;

    #endregion

    #region Display Options

    [ObservableProperty]
    private bool _showMap = true;

    [ObservableProperty]
    private bool _showTime = true;

    [ObservableProperty]
    private bool _showDataTable;

    [ObservableProperty]
    private bool _showParams;

    [ObservableProperty]
    private bool _showMode = true;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _showMsg = true;

    [ObservableProperty]
    private bool _showEvents = true;

    #endregion

    #region Scripting Properties

    [ObservableProperty]
    private string _scriptText = string.Empty;

    [ObservableProperty]
    private string _scriptOutput = string.Empty;

    [ObservableProperty]
    private bool _isScriptRunning;

    [ObservableProperty]
    private ObservableCollection<ScriptFunctionInfo> _scriptFunctions = new();

    [ObservableProperty]
    private string _loadedScriptPath = string.Empty;

    [ObservableProperty]
    private string _loadedScriptName = string.Empty;

    #endregion

    private Window? _parentWindow;

    public void SetParentWindow(Window window)
    {
        _parentWindow = window;
    }

    public LogAnalyzerPageViewModel(
        ILogger<LogAnalyzerPageViewModel> logger,
        ILogAnalyzerService logAnalyzerService,
        IConnectionService connectionService,
        ILogQueryEngine? queryEngine = null,
        ILogEventDetector? eventDetector = null,
        ILogExportService? exportService = null,
        IArduPilotMetadataLoader? metadataLoader = null)
    {
        _logger = logger;
        _logAnalyzerService = logAnalyzerService;
        _connectionService = connectionService;
        _queryEngine = queryEngine;
        _eventDetector = eventDetector;
        _exportService = exportService;
        _metadataLoader = metadataLoader;

        _downloadFolder = _logAnalyzerService.GetDefaultDownloadFolder();

        // Subscribe to service events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _logAnalyzerService.LogFilesUpdated += OnLogFilesUpdated;
        _logAnalyzerService.DownloadProgressChanged += OnDownloadProgressChanged;
        _logAnalyzerService.DownloadCompleted += OnDownloadCompleted;
        _logAnalyzerService.LogParsed += OnLogParsed;

        IsConnected = _connectionService.IsConnected;

        // Load script functions
        foreach (var func in _logAnalyzerService.GetScriptFunctions())
        {
            ScriptFunctions.Add(func);
        }
        
        // Load metadata if available
        if (_metadataLoader != null && !_metadataLoader.IsLoaded)
        {
            _ = Task.Run(async () => await _metadataLoader.LoadAllMetadataAsync());
        }
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                StatusMessage = "Connected. Click 'Download log file' to browse logs from FC.";
            }
            else
            {
                StatusMessage = "Disconnected. Browse local log files.";
                LogFiles.Clear();
            }
        });
    }

    private void OnLogFilesUpdated(object? sender, List<LogFileInfo> files)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogFiles.Clear();
            int id = 1;
            foreach (var file in files)
            {
                file.LogId = id++;
                LogFiles.Add(file);
            }
            StatusMessage = $"Found {files.Count} log files";
        });
    }

    private void OnDownloadProgressChanged(object? sender, LogDownloadProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DownloadProgress = progress.ProgressPercent;
            DownloadProgressText = progress.ProgressDisplay;
        });
    }

    private void OnDownloadCompleted(object? sender, (LogFileInfo File, bool Success, String? Error) e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Success)
            {
                StatusMessage = $"Downloaded: {e.File.FileName}";
                SelectedFilePath = e.File.LocalPath ?? string.Empty;
            }
            else
            {
                StatusMessage = $"Download failed: {e.Error}";
            }
        });
    }

    private void OnLogParsed(object? sender, LogParseResult result)
    {
        // Process log data on background thread to avoid blocking UI
        _ = Task.Run(async () =>
        {
            try
            {
                if (result.IsSuccess)
                {
                    // Update UI properties on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsLogLoaded = true;
                        IsAnalyzing = true;
                        LoadedLogInfo = $"{result.FileName} - {result.MessageCount:N0} messages, {result.DurationDisplay}";
                        StatusMessage = "Analyzing log data...";
                        
                        // Update overview
                        UpdateOverview(result);
                        
                        // Load message types
                        MessageTypes.Clear();
                        foreach (var type in result.MessageTypes)
                        {
                            MessageTypes.Add(type);
                        }
                    });

                // Load available graph fields
                LoadAvailableFields();
                
                // Auto-select common fields for initial graph display
                AutoSelectDefaultGraphFields();
                
                // Load GPS track for map FIRST (so map shows immediately)
                LoadGpsTrack();
                
                // Detect events in background
                if (_eventDetector != null)
                {
                    await DetectEventsAsync();
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsLogLoaded = false;
                        IsAnalyzing = false;
                        StatusMessage = $"Failed to load log: {result.ErrorMessage}";
                    });
                }
                
                // Ensure events are filtered and displayed automatically
                FilterEvents();
                UpdateEventDisplaySummary();
                
                // Load parameter changes if available
                LoadParameterChanges();
                
                // Load log parameters with metadata
                await LoadLogParametersAsync();
                
                // Load raw log messages for display
                LoadRawLogMessages();

                StatusMessage = $"Log loaded: {result.MessageCount:N0} messages - {DetectedEvents.Count} events detected";
                
                // Notify all properties changed to ensure UI updates
                OnPropertyChanged(nameof(HasGpsData));
                OnPropertyChanged(nameof(HasFilteredEvents));
                OnPropertyChanged(nameof(ShowNoEventsMessage));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing log file");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLogLoaded = false;
                    IsAnalyzing = false;
                    StatusMessage = $"Error processing log: {ex.Message}";
                });
            }
        });
    }
    
    private void LoadParameterChanges()
    {
        ParameterChanges.Clear();
        
        // Query for PARM messages or parameter changes
        var parmData = _logAnalyzerService.GetMessages("PARM", 0, 1000);
        
        // For now, just placeholder - would parse actual PARM messages
        foreach (var msg in parmData)
        {
            // Parse parameter change from message
            // This would extract Name, OldValue, NewValue, Timestamp
        }
    }
    
    private async Task LoadLogParametersAsync()
    {
        LogParameters.Clear();
        FilteredLogParameters.Clear();
        
        // Get parameters from log
        var logParams = _logAnalyzerService.GetLogParameters();
        
        // Fallback: If no parameters from service, try to extract from PARM messages directly
        if (logParams.Count == 0)
        {
            logParams = ExtractParametersFromParmMessages();
        }
        
        HasLogParameters = logParams.Count > 0;
        
        if (!HasLogParameters)
        {
            StatusMessage = "No parameters found in log file";
            return;
        }

        // Load metadata if not already loaded
        if (_metadataLoader != null && !_metadataLoader.IsLoaded)
        {
            try
            {
                StatusMessage = "Loading parameter metadata...";
                await _metadataLoader.LoadAllMetadataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load parameter metadata");
                StatusMessage = "Parameter metadata unavailable - showing values only";
            }
        }

        // Enrich parameters with metadata
        foreach (var kvp in logParams.OrderBy(p => p.Key))
        {
            var param = new LogParameter
            {
                Name = kvp.Key,
                Value = kvp.Value
            };

            // Try to get metadata
            if (_metadataLoader != null)
            {
                var meta = _metadataLoader.GetMetadata(kvp.Key);
                if (meta != null)
                {
                    param.Description = meta.Description ?? "No description available";
                    param.Units = meta.Units;
                    param.Group = meta.Group ?? "General";
                    param.Default = meta.Range?.Low ?? "Not specified";
                    
                    // Set range display
                    if (meta.Range != null && !string.IsNullOrEmpty(meta.Range.Low) && !string.IsNullOrEmpty(meta.Range.High))
                    {
                        param.Range = $"{meta.Range.Low} to {meta.Range.High}";
                    }

                    // Add value options if available
                    if (meta.Values != null && meta.Values.Count > 0)
                    {
                        foreach (var valKvp in meta.Values.OrderBy(v => v.Key))
                        {
                            param.OptionsDisplay.Add($"{valKvp.Key}: {valKvp.Value}");
                        }
                    }
                }
            }

            LogParameters.Add(param);
            FilteredLogParameters.Add(param);
        }

        StatusMessage = $"Loaded {LogParameters.Count} parameters from log";
    }
    
    /// <summary>
    /// Extracts parameters from PARM messages when GetLogParameters() returns empty.
    /// This is a fallback mechanism to ensure parameters are displayed.
    /// </summary>
    private Dictionary<string, float> ExtractParametersFromParmMessages()
    {
        var parameters = new Dictionary<string, float>();
        
        try
        {
            var parmMessages = _logAnalyzerService.GetMessages("PARM", 0, 10000);
            
            foreach (var msg in parmMessages)
            {
                // Try to extract parameter name and value
                string? paramName = null;
                float? paramValue = null;  // Use nullable to distinguish missing from zero
                
                // Try different field name variations for parameter name
                if (msg.Fields.TryGetValue("Name", out var nameObj))
                {
                    paramName = nameObj?.ToString()?.Trim();
                }
                else if (msg.Fields.TryGetValue("N", out var nObj))
                {
                    paramName = nObj?.ToString()?.Trim();
                }
                
                // Try to extract parameter value
                paramValue = TryExtractParameterValue(msg.Fields, "Value") ?? 
                             TryExtractParameterValue(msg.Fields, "V");
                
                // Only store if we have both a valid name and a successfully parsed value
                if (!string.IsNullOrWhiteSpace(paramName) && paramValue.HasValue)
                {
                    // Store last value for each parameter (in case of changes during flight)
                    parameters[paramName] = paramValue.Value;
                }
            }
            
            _logger.LogInformation("Extracted {Count} parameters from PARM messages", parameters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting parameters from PARM messages");
        }
        
        return parameters;
    }
    
    /// <summary>
    /// Tries to extract a float value from a message field.
    /// Returns null if the field doesn't exist or can't be parsed.
    /// </summary>
    private static float? TryExtractParameterValue(Dictionary<string, string> fields, string fieldName)
    {
        if (fields.TryGetValue(fieldName, out var valueStr))
        {
            if (float.TryParse(valueStr, out var value))
            {
                return value;
            }
        }
        return null;
    }
    
    partial void OnParameterSearchTextChanged(string value)
    {
        FilterLogParameters();
    }
    
    private void FilterLogParameters()
    {
        FilteredLogParameters.Clear();
        
        var filtered = string.IsNullOrWhiteSpace(ParameterSearchText)
            ? LogParameters
            : LogParameters.Where(p => 
                p.Name.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Group.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase));
        
        foreach (var p in filtered)
        {
            FilteredLogParameters.Add(p);
        }
    }
    
    /// <summary>
    /// Loads raw log messages for display in the Events tab data preview.
    /// This loads a limited sample of messages to avoid performance issues with large log files.
    /// The sample is taken from the first few message types to give a representative preview.
    /// </summary>
    private void LoadRawLogMessages()
    {
        RawLogMessages.Clear();
        
        try
        {
            // Get a sample of messages from multiple types to show raw data
            var messageTypes = _logAnalyzerService.GetMessageTypes();
            int totalLoaded = 0;
            
            foreach (var msgType in messageTypes.Take(MaxMessageTypesForRawData))
            {
                if (totalLoaded >= MaxRawDataTotalRows) break;
                
                var messages = _logAnalyzerService.GetMessages(msgType.Name, 0, MaxRawDataRowsPerType);
                foreach (var msg in messages)
                {
                    if (totalLoaded >= MaxRawDataTotalRows) break;
                    RawLogMessages.Add(msg);
                    totalLoaded++;
                }
            }
            
            HasRawLogData = RawLogMessages.Count > 0;
            RawDataRowCount = $"{RawLogMessages.Count:N0} rows (sample)";
            
            _logger.LogInformation("Loaded {Count} raw log messages for display", RawLogMessages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading raw log messages");
            HasRawLogData = false;
            RawDataRowCount = "Error loading data";
        }
    }

    private void UpdateOverview(LogParseResult result)
    {
        LogFileName = result.FileName;
        LogFileSize = FormatFileSize(result.FileSize);
        LogDuration = result.DurationDisplay;
        LogMessageCount = result.MessageCount.ToString("N0");
        LogMessageTypes = result.MessageTypes.Count.ToString();
        
        // Check for specific data types
        HasGpsData = result.MessageTypes.Any(t => t.Name == "GPS");
        HasAttitudeData = result.MessageTypes.Any(t => t.Name == "ATT");
        HasVibeData = result.MessageTypes.Any(t => t.Name == "VIBE");
        
        // Set time range for zoom
        ZoomStartTime = 0;
        ZoomEndTime = result.Duration.TotalSeconds;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    partial void OnSelectedMessageTypeChanged(LogMessageTypeGroup? value)
    {
        if (value != null)
        {
            CurrentMessagePage = 0;
            LoadMessages();
        }
    }

    partial void OnGraphFieldFilterChanged(string value)
    {
        FilterGraphFields();
    }

    partial void OnSelectedEventChanged(LogEvent? value)
    {
        if (value != null)
        {
            JumpToTime(value.Timestamp);
        }
    }

    partial void OnSelectedParameterChangeChanged(ParameterChange? value)
    {
        if (value != null)
        {
            JumpToTime(value.Timestamp);
        }
    }

    partial void OnShowInfoEventsChanged(bool value) => FilterEvents();
    partial void OnShowWarningEventsChanged(bool value) => FilterEvents();
    partial void OnShowErrorEventsChanged(bool value) => FilterEvents();
    partial void OnShowCriticalEventsChanged(bool value) => FilterEvents();
    partial void OnEventSearchTextChanged(string value) => FilterEvents();

    #endregion

    #region Events Commands

    private async Task DetectEventsAsync()
    {
        if (_eventDetector == null) return;

        IsAnalyzing = true;
        StatusMessage = "Detecting events...";

        try
        {
            var progress = new Progress<int>(p => LoadProgress = p);
            var events = await _eventDetector.DetectEventsAsync(progress);

            DetectedEvents.Clear();
            foreach (var evt in events)
            {
                DetectedEvents.Add(evt);
            }

            EventSummary = _eventDetector.GetEventSummary();
            ErrorCount = EventSummary.ErrorCount + EventSummary.CriticalCount + EventSummary.EmergencyCount;
            WarningCount = EventSummary.WarningCount;

            // Populate filter dropdowns based on detected events
            PopulateEventFilterDropdowns();

            FilterEvents();
            UpdateEventDisplaySummary();
            StatusMessage = $"Detected {events.Count} events";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting events");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    /// <summary>
    /// Populates the filter dropdown options based on detected events.
    /// </summary>
    private void PopulateEventFilterDropdowns()
    {
        // Populate severity filter
        AvailableSeverities.Clear();
        AvailableSeverities.Add("All");
        var severities = DetectedEvents.Select(e => e.SeverityDisplay).Distinct().OrderBy(s => s);
        foreach (var sev in severities)
        {
            if (!AvailableSeverities.Contains(sev))
                AvailableSeverities.Add(sev);
        }
        
        // Populate event type filter
        AvailableEventTypes.Clear();
        AvailableEventTypes.Add("All");
        var eventTypes = DetectedEvents.Select(e => e.TypeDisplay).Distinct().OrderBy(t => t);
        foreach (var type in eventTypes)
        {
            if (!AvailableEventTypes.Contains(type))
                AvailableEventTypes.Add(type);
        }
        
        // Populate source filter
        AvailableSources.Clear();
        AvailableSources.Add("All");
        var sources = DetectedEvents.Select(e => e.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s);
        foreach (var source in sources)
        {
            if (!AvailableSources.Contains(source))
                AvailableSources.Add(source);
        }
    }

    private void FilterEvents()
    {
        FilteredEvents.Clear();

        var filtered = DetectedEvents.Where(e =>
        {
            // Apply severity checkbox filters
            if ((e.Severity == LogEventSeverity.Info || e.Severity == LogEventSeverity.Debug || e.Severity == LogEventSeverity.Notice) && !ShowInfoEvents) return false;
            if (e.Severity == LogEventSeverity.Warning && !ShowWarningEvents) return false;
            if (e.Severity == LogEventSeverity.Error && !ShowErrorEvents) return false;
            if ((e.Severity == LogEventSeverity.Critical || e.Severity == LogEventSeverity.Emergency) && !ShowCriticalEvents) return false;

            // Apply dropdown filters
            if (SelectedSeverityFilter != "All" && e.SeverityDisplay != SelectedSeverityFilter) return false;
            if (SelectedEventTypeFilter != "All" && e.TypeDisplay != SelectedEventTypeFilter) return false;
            if (SelectedSourceFilter != "All" && e.Source != SelectedSourceFilter) return false;

            // Apply text search
            if (!string.IsNullOrWhiteSpace(EventSearchText))
            {
                var search = EventSearchText.ToLowerInvariant();
                if (!e.Title.ToLowerInvariant().Contains(search) &&
                    !e.Description.ToLowerInvariant().Contains(search) &&
                    !e.Source.ToLowerInvariant().Contains(search) &&
                    !(e.Details?.ToLowerInvariant().Contains(search) ?? false))
                    return false;
            }

            return true;
        });

        foreach (var evt in filtered.OrderBy(e => e.Timestamp))
        {
            FilteredEvents.Add(evt);
        }
        
        UpdateEventDisplaySummary();
        OnPropertyChanged(nameof(HasFilteredEvents));
    }

    [RelayCommand]
    private void JumpToEvent(LogEvent? evt)
    {
        if (evt == null) return;
        JumpToTime(evt.Timestamp);
        SelectedTabIndex = TAB_PLOT;
    }

    private void JumpToTime(double timestamp)
    {
        CursorTime = timestamp;
        CursorTimeDisplay = TimeSpan.FromSeconds(timestamp).ToString(@"hh\:mm\:ss\.fff");
        
        // Update zoom window to center on this time
        var windowSize = (ZoomEndTime - ZoomStartTime);
        var halfWindow = windowSize / 2;
        ZoomStartTime = Math.Max(0, timestamp - halfWindow);
        ZoomEndTime = ZoomStartTime + windowSize;
        
        // Update cursor readouts
        UpdateCursorReadouts();
        
        // Update map position
        UpdateMapPosition(timestamp);
    }

    private void UpdateCursorReadouts()
    {
        CursorReadouts.Clear();

        foreach (var field in SelectedGraphFields)
        {
            var stats = _logAnalyzerService.GetFieldStatistics(field.DisplayName);
            // Would get actual value at cursor time from query engine
            CursorReadouts.Add(new CursorReadout
            {
                FieldName = field.DisplayName,
                Color = field.Color ?? "#FFFFFF",
                Value = stats.Average // Placeholder
            });
        }
    }

    private void UpdateMapPosition(double timestamp)
    {
        // Find GPS position at timestamp
        if (GpsTrack.Count > 0)
        {
            var nearest = GpsTrack.MinBy(p => Math.Abs(p.Timestamp - timestamp));
            if (nearest != null)
            {
                CurrentMapPosition = nearest;
                MapCenterLat = nearest.Latitude;
                MapCenterLng = nearest.Longitude;
            }
        }
    }

    /// <summary>
    /// Fallback event extraction when event detector is unavailable or returns empty.
    /// Extracts events from MSG, EV, MODE, and ERR message types.
    /// </summary>
    private void ExtractBasicEventsFromLog()
    {
        var eventId = 1;
        
        try
        {
            // Extract from MSG messages (text messages)
            var msgMessages = _logAnalyzerService.GetMessages("MSG", 0, 5000);
            foreach (var msg in msgMessages)
            {
                var text = msg.Fields.GetValueOrDefault("Message")?.ToString() ?? 
                           msg.Fields.GetValueOrDefault("Text")?.ToString() ?? "";
                
                if (string.IsNullOrWhiteSpace(text)) continue;
                
                // Parse timestamp from message
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0; // Convert microseconds to seconds
                }
                
                // Determine severity from message content
                var severity = LogEventSeverity.Info;
                var title = "Message";
                
                if (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("fail", StringComparison.OrdinalIgnoreCase))
                {
                    severity = LogEventSeverity.Error;
                    title = "Error";
                }
                else if (text.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("low", StringComparison.OrdinalIgnoreCase))
                {
                    severity = LogEventSeverity.Warning;
                    title = "Warning";
                }
                else if (text.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("failsafe", StringComparison.OrdinalIgnoreCase))
                {
                    severity = LogEventSeverity.Critical;
                    title = "Critical";
                }
                else if (text.Contains("arm", StringComparison.OrdinalIgnoreCase))
                {
                    title = text.Contains("disarm", StringComparison.OrdinalIgnoreCase) ? "Disarmed" : "Armed";
                }
                else if (text.Contains("mode", StringComparison.OrdinalIgnoreCase))
                {
                    title = "Mode Change";
                }
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = severity,
                    Title = title,
                    Description = text,
                    Source = "MSG",
                    RawMessage = text
                });
            }
            
            // Extract from MODE messages
            var modeMessages = _logAnalyzerService.GetMessages("MODE", 0, 1000);
            foreach (var msg in modeMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0;
                }
                
                var modeName = msg.Fields.GetValueOrDefault("Mode")?.ToString() ?? 
                               msg.Fields.GetValueOrDefault("ModeNum")?.ToString() ?? "Unknown";
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.ModeChange,
                    Severity = LogEventSeverity.Info,
                    Title = "Mode Change",
                    Description = $"Flight mode: {modeName}",
                    Source = "Navigator"
                });
            }
            
            // Extract from EV (Event) messages
            var evMessages = _logAnalyzerService.GetMessages("EV", 0, 1000);
            foreach (var msg in evMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0;
                }
                
                var evId = msg.Fields.GetValueOrDefault("Id")?.ToString() ?? "0";
                var severity = LogEventSeverity.Info;
                var title = $"Event {evId}";
                var description = $"Event ID: {evId}";
                
                // Map known ArduPilot event IDs
                if (int.TryParse(evId, out var eventIdNum))
                {
                    (title, severity) = eventIdNum switch
                    {
                        10 => ("Armed", LogEventSeverity.Notice),
                        11 => ("Disarmed", LogEventSeverity.Notice),
                        15 => ("Battery Failsafe", LogEventSeverity.Critical),
                        17 => ("GPS Failsafe", LogEventSeverity.Error),
                        28 => ("Radio Failsafe", LogEventSeverity.Error),
                        _ => ($"Event {evId}", LogEventSeverity.Info)
                    };
                }
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = severity,
                    Title = title,
                    Description = description,
                    Source = "Autopilot",
                    EventId = int.TryParse(evId, out var eid) ? eid : null
                });
            }
            
            // Extract from ERR (Error) messages
            var errMessages = _logAnalyzerService.GetMessages("ERR", 0, 1000);
            foreach (var msg in errMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj?.ToString(), out var timeUs))
                {
                    timestamp = timeUs / 1_000_000.0;
                }
                
                var subsys = msg.Fields.GetValueOrDefault("Subsys")?.ToString() ?? "Unknown";
                var errCode = msg.Fields.GetValueOrDefault("ECode")?.ToString() ?? "Unknown";
                
                DetectedEvents.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = LogEventSeverity.Error,
                    Title = $"Error: {subsys}",
                    Description = $"Subsystem: {subsys}, Error Code: {errCode}",
                    Source = subsys
                });
            }
            
            // Sort events by timestamp
            var sortedEvents = DetectedEvents.OrderBy(e => e.Timestamp).ToList();
            DetectedEvents.Clear();
            foreach (var evt in sortedEvents)
            {
                evt.Id = DetectedEvents.Count + 1;
                DetectedEvents.Add(evt);
            }
            
            // Update event summary
            EventSummary = new EventSummary
            {
                TotalEvents = DetectedEvents.Count,
                InfoCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Info || e.Severity == LogEventSeverity.Notice || e.Severity == LogEventSeverity.Debug),
                WarningCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Warning),
                ErrorCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Error),
                CriticalCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Critical || e.Severity == LogEventSeverity.Emergency)
            };
            
            // Update the overview counts for display in other tabs
            ErrorCount = EventSummary.ErrorCount + EventSummary.CriticalCount;
            WarningCount = EventSummary.WarningCount;
            
            // Populate filter dropdowns
            PopulateEventFilterDropdowns();
            
            // Apply filters to show events
            FilterEvents();
            
            // Update event display summary to refresh UI statistics
            UpdateEventDisplaySummary();
            
            _logger.LogInformation("Extracted {Count} basic events from log", DetectedEvents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting basic events from log");
        }
    }

    #endregion

    #region Enhanced Events Commands

    partial void OnSelectedAutoRefreshIndexChanged(int value)
    {
        SetupAutoRefresh(value);
    }
    
    private void SetupAutoRefresh(int index)
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        
        int intervalSeconds = index switch
        {
            1 => 5,
            2 => 10,
            3 => 30,
            _ => 0
        };
        
        if (intervalSeconds > 0)
        {
            _autoRefreshTimer = new System.Timers.Timer(intervalSeconds * 1000);
            _autoRefreshTimer.Elapsed += (s, e) =>
            {
                Dispatcher.UIThread.Post(FilterEvents);
            };
            _autoRefreshTimer.Start();
        }
    }
    
    [RelayCommand]
    private void ApplyEventsFilter()
    {
        FilterEvents();
        UpdateEventDisplaySummary();
    }
    
    [RelayCommand]
    private void ClearEventsFilter()
    {
        SelectedTimeRangeIndex = 4; // All
        SelectedSeverityFilter = "All";
        SelectedEventTypeFilter = "All";
        SelectedSourceFilter = "All";
        EventSearchText = string.Empty;
        ShowInfoEvents = true;
        ShowWarningEvents = true;
        ShowErrorEvents = true;
        ShowCriticalEvents = true;
        FilterEvents();
        UpdateEventDisplaySummary();
    }
    
    private void UpdateEventDisplaySummary()
    {
        EventDisplaySummary = new EventDisplaySummary
        {
            TotalEvents = FilteredEvents.Count,
            ErrorCount = FilteredEvents.Count(e => e.Severity == LogEventSeverity.Error),
            WarningCount = FilteredEvents.Count(e => e.Severity == LogEventSeverity.Warning),
            InfoCount = FilteredEvents.Count(e => e.Severity == LogEventSeverity.Info || e.Severity == LogEventSeverity.Notice || e.Severity == LogEventSeverity.Debug),
            CriticalCount = FilteredEvents.Count(e => e.Severity == LogEventSeverity.Critical || e.Severity == LogEventSeverity.Emergency)
        };
        
        EventPagination.TotalItems = FilteredEvents.Count;
        
        // Notify UI of property changes
        OnPropertyChanged(nameof(EventDisplaySummary));
        OnPropertyChanged(nameof(EventPagination));
        OnPropertyChanged(nameof(ShowNoEventsMessage));
        OnPropertyChanged(nameof(HasFilteredEvents));
    }
    
    [RelayCommand]
    private void GoToPreviousEventPage()
    {
        if (EventPagination.CanGoToPreviousPage)
        {
            EventPagination.CurrentPage--;
            OnPropertyChanged(nameof(EventPagination));
        }
    }
    
    [RelayCommand]
    private void GoToNextEventPage()
    {
        if (EventPagination.CanGoToNextPage)
        {
            EventPagination.CurrentPage++;
            OnPropertyChanged(nameof(EventPagination));
        }
    }
    
    [RelayCommand]
    private async Task ExportEventsToCsvAsync()
    {
        if (_parentWindow == null || FilteredEvents.Count == 0)
        {
            StatusMessage = "No events to export";
            return;
        }
        
        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Events to CSV",
                DefaultExtension = "csv",
                SuggestedFileName = $"events_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } }
                }
            });
            
            if (file != null)
            {
                IsBusy = true;
                StatusMessage = "Exporting events to CSV...";
                
                var filePath = file.Path.LocalPath;
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
                
                // Write header
                await writer.WriteLineAsync("Timestamp,Severity,Event Type,Source,Message,Details");
                
                // Write data
                foreach (var evt in FilteredEvents)
                {
                    var line = $"\"{evt.TimestampDisplay}\",\"{evt.Severity}\",\"{EscapeCsv(evt.Title)}\",\"{evt.Type}\",\"{EscapeCsv(evt.Description)}\",\"{EscapeCsv(evt.Details ?? "")}\"";
                    await writer.WriteLineAsync(line);
                }
                
                await writer.FlushAsync();
                StatusMessage = $"Exported {FilteredEvents.Count} events to CSV";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export events to CSV");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    [RelayCommand]
    private async Task ExportEventsToJsonAsync()
    {
        if (_parentWindow == null || FilteredEvents.Count == 0)
        {
            StatusMessage = "No events to export";
            return;
        }
        
        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Events to JSON",
                DefaultExtension = "json",
                SuggestedFileName = $"events_export_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } }
                }
            });
            
            if (file != null)
            {
                IsBusy = true;
                StatusMessage = "Exporting events to JSON...";
                
                var filePath = file.Path.LocalPath;
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var exportData = FilteredEvents.Select(e => new
                {
                    e.Id,
                    e.TimestampDisplay,
                    e.Timestamp,
                    Severity = e.Severity.ToString(),
                    EventType = e.Title,
                    Source = e.Type.ToString(),
                    e.Description,
                    e.Details
                }).ToList();
                
                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(filePath, json, System.Text.Encoding.UTF8);
                
                StatusMessage = $"Exported {FilteredEvents.Count} events to JSON";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export events to JSON");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    private static string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
    }

    #endregion

    #region Map Commands

    private void LoadGpsTrack()
    {
        try
        {
            GpsTrack.Clear();
            GpsTrackPoints.Clear();
            CriticalMapEvents.Clear();

            if (!IsLogLoaded)
            {
                OnPropertyChanged(nameof(HasGpsData));
                return;
            }

            var gpsData = _logAnalyzerService.GetFieldData("GPS", "Lat");
            if (gpsData == null || gpsData.Count == 0)
            {
                _logger.LogInformation("No GPS data found in log");
                OnPropertyChanged(nameof(HasGpsData));
                return;
            }

            var lngData = _logAnalyzerService.GetFieldData("GPS", "Lng");
            var altData = _logAnalyzerService.GetFieldData("GPS", "Alt");

            if (lngData == null)
            {
                _logger.LogWarning("Longitude data missing");
                OnPropertyChanged(nameof(HasGpsData));
                return;
            }

            // Build GPS track
            for (int i = 0; i < Math.Min(gpsData.Count, lngData.Count); i++)
            {
                var lat = gpsData[i].Value;
                var lng = lngData[i].Value;
                var alt = altData != null && i < altData.Count ? altData[i].Value : 0;
                var timestamp = gpsData[i].Timestamp / 1_000_000.0; // Convert microseconds to seconds

                // Skip invalid coordinates
                if (Math.Abs(lat) < 0.001 && Math.Abs(lng) < 0.001)
                    continue;

                GpsTrack.Add(new GpsPoint
                {
                    Latitude = lat,
                    Longitude = lng,
                    Altitude = alt,
                    Timestamp = timestamp
                });

                GpsTrackPoints.Add(new Controls.GpsTrackPoint
                {
                    Latitude = lat,
                    Longitude = lng,
                    Altitude = alt,
                    Timestamp = timestamp
                });
            }

            // Populate critical events for map display after events are detected
            // This will be called again after event detection completes
            PopulateCriticalMapEvents();

            if (GpsTrack.Count > 0)
            {
                var firstPoint = GpsTrack.First();
                MapCenterLat = firstPoint.Latitude;
                MapCenterLng = firstPoint.Longitude;
                MapZoom = 15;

                _logger.LogInformation("GPS track loaded with {Count} points", GpsTrack.Count);
            }
            
            // Notify HasGpsData property changed to update UI bindings
            OnPropertyChanged(nameof(HasGpsData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading GPS track");
            OnPropertyChanged(nameof(HasGpsData));
        }
    }
    
    /// <summary>
    /// Populates critical events for map display.
    /// Should be called after both GPS track and events are loaded.
    /// </summary>
    private void PopulateCriticalMapEvents()
    {
        CriticalMapEvents.Clear();
        
        if (DetectedEvents == null || DetectedEvents.Count == 0)
            return;
            
        var criticalEvents = DetectedEvents
            .Where(e => e.Severity >= LogEventSeverity.Warning && e.HasLocation)
            .OrderBy(e => e.Timestamp)
            .ToList();

        foreach (var evt in criticalEvents)
        {
            CriticalMapEvents.Add(evt);
        }

        _logger.LogInformation("Loaded {Count} critical events for map display", CriticalMapEvents.Count);
    }
    
    #endregion

    #region Export Commands

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        if (_parentWindow == null || !IsLogLoaded)
        {
            StatusMessage = "Please load a log file first";
            return;
        }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to CSV",
                DefaultExtension = "csv",
                SuggestedFileName = $"log_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file != null)
            {
                IsBusy = true;
                StatusMessage = "Exporting to CSV...";

                // If export service is not available, use fallback method
                if (_exportService != null)
                {
                    var seriesKeys = SelectedGraphFields.Select(f => f.DisplayName).ToList();
                    if (seriesKeys.Count == 0)
                    {
                        // Export all available fields if none selected
                        seriesKeys = AvailableFields.Take(10).Select(f => f.DisplayName).ToList();
                    }

                    var progress = new Progress<int>(p => LoadProgress = p);
                    var result = await _exportService.ExportToCsvAsync(
                        file.Path.LocalPath, seriesKeys, ZoomStartTime, ZoomEndTime, progress);

                    if (result.IsSuccess)
                    {
                        StatusMessage = $"Exported {result.RecordCount} records to CSV";
                    }
                    else
                    {
                        StatusMessage = $"Export failed: {result.ErrorMessage}";
                    }
                }
                else
                {
                    // Fallback: Manual CSV export
                    await ExportToCsvManualAsync(file.Path.LocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV export failed");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LoadProgress = 0;
        }
    }
    
    private async Task ExportToCsvManualAsync(string filePath)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await using var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            
            // Write header
            var fields = SelectedGraphFields.Count > 0 
                ? SelectedGraphFields 
                : AvailableFields.Take(10);
                
            await writer.WriteLineAsync("Timestamp," + string.Join(",", fields.Select(f => f.DisplayName)));
            
            // Get data for each field
            var fieldData = new Dictionary<string, List<Core.Models.LogDataPoint>>();
            foreach (var field in fields)
            {
                var parts = field.DisplayName.Split('.');
                if (parts.Length == 2)
                {
                    var data = _logAnalyzerService.GetFieldData(parts[0], parts[1]);
                    if (data != null)
                    {
                        fieldData[field.DisplayName] = data;
                    }
                }
            }
            
            // Write data rows
            if (fieldData.Count > 0)
            {
                var maxLength = fieldData.Values.Max(d => d.Count);
                for (int i = 0; i < maxLength; i++)
                {
                    var row = new List<string>();
                    var timestamp = fieldData.Values.First().Count > i 
                        ? (fieldData.Values.First()[i].Timestamp / 1e6).ToString("F6") 
                        : "";
                    row.Add(timestamp);
                    
                    foreach (var field in fields)
                    {
                        if (fieldData.TryGetValue(field.DisplayName, out var data) && data.Count > i)
                        {
                            row.Add(data[i].Value.ToString("F6"));
                        }
                        else
                        {
                            row.Add("");
                        }
                    }
                    
                    await writer.WriteLineAsync(string.Join(",", row));
                    
                    if (i % 1000 == 0)
                    {
                        LoadProgress = (int)((double)i / maxLength * 100);
                    }
                }
            }
            
            await writer.FlushAsync();
            StatusMessage = $"Exported to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Manual CSV export failed: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private async Task ExportToKmlAsync()
    {
        if (_parentWindow == null || !IsLogLoaded || !HasGpsData)
        {
            StatusMessage = "No GPS data available to export";
            return;
        }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to KML",
                DefaultExtension = "kml",
                SuggestedFileName = $"flight_track_{DateTime.Now:yyyyMMdd_HHmmss}.kml",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("KML Files") { Patterns = new[] { "*.kml" } }
                }
            });

            if (file != null)
            {
                IsBusy = true;
                StatusMessage = "Exporting to KML...";

                if (_exportService != null)
                {
                    var progress = new Progress<int>(p => LoadProgress = p);
                    var result = await _exportService.ExportToKmlAsync(
                        file.Path.LocalPath, true, progress);

                    if (result.IsSuccess)
                    {
                        StatusMessage = $"Exported GPS track to KML ({result.RecordCount} points)";
                    }
                    else
                    {
                        StatusMessage = $"Export failed: {result.ErrorMessage}";
                    }
                }
                else
                {
                    // Fallback: Manual KML export
                    await ExportToKmlManualAsync(file.Path.LocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KML export failed");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LoadProgress = 0;
        }
    }
    
    private async Task ExportToKmlManualAsync(string filePath)
    {
        try
        {
            if (GpsTrack.Count == 0)
            {
                throw new Exception("No GPS data to export");
            }
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await using var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            
            // Write KML header
            await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            await writer.WriteLineAsync("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            await writer.WriteLineAsync("  <Document>");
            await writer.WriteLineAsync($"    <name>Flight Track - {LogFileName}</name>");
            await writer.WriteLineAsync("    <description>Exported from Pavaman Drone Configurator</description>");
            
            // Style for the track
            await writer.WriteLineAsync("    <Style id=\"flightPath\">");
            await writer.WriteLineAsync("      <LineStyle>");
            await writer.WriteLineAsync("        <color>ff0000ff</color>");
            await writer.WriteLineAsync("        <width>2</width>");
            await writer.WriteLineAsync("      </LineStyle>");
            await writer.WriteLineAsync("    </Style>");
            
            // Write the track
            await writer.WriteLineAsync("    <Placemark>");
            await writer.WriteLineAsync("      <name>Flight Path</name>");
            await writer.WriteLineAsync("      <styleUrl>#flightPath</styleUrl>");
            await writer.WriteLineAsync("      <LineString>");
            await writer.WriteLineAsync("        <extrude>1</extrude>");
            await writer.WriteLineAsync("        <tessellate>1</tessellate>");
            await writer.WriteLineAsync("        <altitudeMode>absolute</altitudeMode>");
            await writer.WriteLineAsync("        <coordinates>");
            
            // Write coordinates
            for (int i = 0; i < GpsTrack.Count; i++)
            {
                var point = GpsTrack[i];
                await writer.WriteLineAsync($"          {point.Longitude:F8},{point.Latitude:F8},{point.Altitude:F2}");
                
                if (i % 100 == 0)
                {
                    LoadProgress = (int)((double)i / GpsTrack.Count * 100);
                }
            }
            
            await writer.WriteLineAsync("        </coordinates>");
            await writer.WriteLineAsync("      </LineString>");
            await writer.WriteLineAsync("    </Placemark>");
            await writer.WriteLineAsync("  </Document>");
            await writer.WriteLineAsync("</kml>");
            
            await writer.FlushAsync();
            StatusMessage = $"Exported {GpsTrack.Count} GPS points to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Manual KML export failed: {ex.Message}", ex);
        }
    }

    #endregion

    #region File Commands

    [RelayCommand]
    private async Task BrowseLocalFileAsync()
    {
        if (_parentWindow == null)
        {
            StatusMessage = "Unable to open file browser.";
            return;
        }

        try
        {
            var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Log File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("DataFlash Logs") { Patterns = new[] { "*.bin", "*.log" } },
                    new FilePickerFileType("Telemetry Logs") { Patterns = new[] { "*.tlog" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                SelectedFilePath = file.Path.LocalPath;
                StatusMessage = $"Selected: {Path.GetFileName(SelectedFilePath)}";
                await LoadLogAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file picker");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadLogAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            StatusMessage = "No file selected.";
            return;
        }

        if (!File.Exists(SelectedFilePath))
        {
            StatusMessage = "File not found.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Loading {Path.GetFileName(SelectedFilePath)}...";

        try
        {
            var result = await _logAnalyzerService.LoadLogFileAsync(SelectedFilePath);
            if (!result.IsSuccess)
            {
                StatusMessage = $"Failed to load: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load log");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenDownloadDialogAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to a vehicle.";
            return;
        }

        IsDownloadDialogOpen = true;
        IsBusy = true;
        StatusMessage = "Loading log files from flight controller...";

        try
        {
            await _logAnalyzerService.RefreshLogFilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh logs");
            StatusMessage = $"Failed to load logs: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseDownloadDialog()
    {
        try
        {
            if (IsDownloading)
            {
                _logAnalyzerService.CancelDownload();
                IsDownloading = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error canceling download on dialog close");
        }
        IsDownloadDialogOpen = false;
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (!IsConnected) return;

        IsBusy = true;
        try
        {
            await _logAnalyzerService.RefreshLogFilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh logs");
            StatusMessage = $"Failed to refresh: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        var selectedFiles = LogFiles.Where(f => f.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            StatusMessage = "No logs selected.";
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            var count = await _logAnalyzerService.DownloadLogFilesAsync(selectedFiles, DownloadFolder);
            StatusMessage = $"Downloaded {count} files";
            
            var downloaded = selectedFiles.FirstOrDefault(f => f.IsDownloaded);
            if (downloaded != null)
            {
                SelectedFilePath = downloaded.LocalPath ?? string.Empty;
                IsDownloadDialogOpen = false;
                if (!string.IsNullOrEmpty(SelectedFilePath))
                {
                    await LoadLogAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed");
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        try { _logAnalyzerService.CancelDownload(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error canceling download"); }
        IsDownloading = false;
    }

    #endregion

    #region Message Browser Commands

    private void LoadMessages()
    {
        if (SelectedMessageType == null) return;

        CurrentMessages.Clear();
        var skip = CurrentMessagePage * MessagesPerPage;
        var messages = _logAnalyzerService.GetMessages(SelectedMessageType.Name, skip, MessagesPerPage);
        
        foreach (var msg in messages)
        {
            CurrentMessages.Add(msg);
        }

        var totalCount = _logAnalyzerService.GetMessageCount(SelectedMessageType.Name);
        TotalMessagePages = (totalCount + MessagesPerPage - 1) / MessagesPerPage;
    }

    [RelayCommand]
    private void NextMessagePage()
    {
        if (CurrentMessagePage < TotalMessagePages - 1)
        {
            CurrentMessagePage++;
            LoadMessages();
        }
    }

    [RelayCommand]
    private void PreviousMessagePage()
    {
        if (CurrentMessagePage > 0)
        {
            CurrentMessagePage--;
            LoadMessages();
        }
    }

    [RelayCommand]
    private void SearchMessages()
    {
        if (string.IsNullOrWhiteSpace(MessageSearchText)) return;

        CurrentMessages.Clear();
        var results = _logAnalyzerService.SearchMessages(MessageSearchText, 100);
        
        foreach (var msg in results)
        {
            CurrentMessages.Add(msg);
        }

        StatusMessage = $"Found {results.Count} matching messages";
    }

    #endregion

    #region Graph Commands

    private void LoadAvailableFields()
    {
        AvailableFields.Clear();
        FilteredFields.Clear();
        SelectedGraphFields.Clear();
        MessageTypesTree.Clear();

        var fields = _logAnalyzerService.GetAvailableGraphFields();
        _logger.LogInformation("GetAvailableGraphFields returned {Count} fields", fields?.Count ?? 0);
        
        if (fields == null || fields.Count == 0)
        {
            _logger.LogWarning("No graph fields available from log");
            HasTreeData = false;
            return;
        }
        
        // Build dictionary for O(1) lookup performance
        // Note: This dictionary is populated before event handlers are attached and is read-only after initialization
        var fieldLookup = new Dictionary<string, LogFieldInfo>();
        
        foreach (var field in fields)
        {
            var stats = _logAnalyzerService.GetFieldStatistics(field.DisplayName);
            field.MinValue = stats.Minimum;
            field.MaxValue = stats.Maximum;
            field.MeanValue = stats.Average;
            
            AvailableFields.Add(field);
            FilteredFields.Add(field);
            fieldLookup[field.DisplayName] = field;
        }

        var groupedByType = fields.GroupBy(f => f.MessageType).OrderBy(g => g.Key);
        
        foreach (var group in groupedByType)
        {
            var typeNode = new LogMessageTypeNode
            {
                Name = group.Key,
                IsMessageType = true
            };

            var colorIndex = 0;
            foreach (var field in group.OrderBy(f => f.FieldName))
            {
                var fieldNode = new LogFieldNode
                {
                    Name = field.FieldName,
                    FullKey = field.DisplayName,
                    Color = GraphColors.GetColor(colorIndex++),
                    MinValue = field.MinValue,
                    MaxValue = field.MaxValue,
                    MeanValue = field.MeanValue
                };
                
                // Wire up property changed event to sync with AvailableFields
                // Dictionary lookup provides O(1) performance
                // Event handlers execute on UI thread via OnFieldSelectionChanged which uses Dispatcher
                fieldNode.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LogFieldNode.IsSelected) && s is LogFieldNode node)
                    {
                        if (fieldLookup.TryGetValue(node.FullKey, out var matchingField))
                        {
                            matchingField.IsSelected = node.IsSelected;
                            OnFieldSelectionChanged(matchingField);
                        }
                    }
                };
                
                typeNode.Fields.Add(fieldNode);
            }

            MessageTypesTree.Add(typeNode);
        }

        HasTreeData = MessageTypesTree.Count > 0;
        _logger.LogInformation("Loaded {FieldCount} fields in {TypeCount} message types", 
            AvailableFields.Count, MessageTypesTree.Count);
    }

    /// <summary>
    /// Auto-selects common flight data fields for initial graph display.
    /// This ensures users see meaningful data immediately after loading a log.
    /// </summary>
    private void AutoSelectDefaultGraphFields()
    {
        _logger.LogInformation("Auto-selecting graph fields. Available fields count: {Count}", AvailableFields.Count);
        
        if (AvailableFields.Count == 0)
        {
            _logger.LogWarning("No available fields to auto-select");
            return;
        }

        // Priority list of field patterns to auto-select (in order of preference)
        // Using partial matches for better compatibility with different log formats
        var priorityPatterns = new[]
        {
            // Attitude data (most common)
            new[] { "ATT", "Roll" },
            new[] { "ATT", "Pitch" },
            new[] { "ATT", "Yaw" },
            // Altitude data
            new[] { "GPS", "Alt" },
            new[] { "BARO", "Alt" },
            new[] { "POS", "Alt" },
            // Speed data
            new[] { "GPS", "Spd" },
            new[] { "ARSP", "Airspeed" },
            // Battery data
            new[] { "BAT", "Volt" },
            new[] { "BAT", "Curr" },
            // RC inputs
            new[] { "RCIN", "C1" },
            new[] { "RCIN", "C3" },
            // Motor outputs
            new[] { "RCOU", "C1" },
            // IMU data
            new[] { "IMU", "AccX" },
            new[] { "IMU", "AccZ" },
            // Vibration
            new[] { "VIBE", "VibeX" },
        };

        var selectedCount = 0;
        const int maxAutoSelectFields = 4;

        // Try pattern matching
        foreach (var pattern in priorityPatterns)
        {
            if (selectedCount >= maxAutoSelectFields)
                break;

            var msgType = pattern[0];
            var fieldName = pattern.Length > 1 ? pattern[1] : null;

            // Find matching field
            LogFieldInfo? field = null;
            
            if (fieldName != null)
            {
                // Try exact match first: "ATT.Roll"
                field = AvailableFields.FirstOrDefault(f => 
                    f.DisplayName.Equals($"{msgType}.{fieldName}", StringComparison.OrdinalIgnoreCase));
                
                // Try contains match: field contains both message type and field name
                if (field == null)
                {
                    field = AvailableFields.FirstOrDefault(f => 
                        f.MessageType.Equals(msgType, StringComparison.OrdinalIgnoreCase) &&
                        f.FieldName.Contains(fieldName, StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                // Just match by message type
                field = AvailableFields.FirstOrDefault(f => 
                    f.MessageType.Equals(msgType, StringComparison.OrdinalIgnoreCase));
            }

            if (field != null && !SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName))
            {
                field.IsSelected = true;
                field.Color = GraphColors.GetColor(selectedCount);
                SelectedGraphFields.Add(field);
                selectedCount++;
                _logger.LogDebug("Auto-selected field: {Field}", field.DisplayName);
            }
        }

        // If no priority fields found, select the first few available fields
        if (selectedCount == 0)
        {
            _logger.LogInformation("No priority fields found, selecting first {Count} available fields", maxAutoSelectFields);
            foreach (var field in AvailableFields.Take(maxAutoSelectFields))
            {
                if (!SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName))
                {
                    field.IsSelected = true;
                    field.Color = GraphColors.GetColor(selectedCount);
                    SelectedGraphFields.Add(field);
                    selectedCount++;
                }
            }
        }

        // Update the graph with selected fields
        if (selectedCount > 0)
        {
            _logger.LogInformation("Auto-selected {Count} graph fields for display", selectedCount);
            UpdateGraph();
        }
        else
        {
            _logger.LogWarning("No fields could be auto-selected");
        }
    }

    [RelayCommand]
    private void SelectDefaultFields()
    {
        AutoSelectDefaultGraphFields();
    }

    [RelayCommand]
    private void ClearAllFields()
    {
        // Clear the selected fields collection first, then update individual field properties
        // This ensures the graph update (triggered by UpdateGraph) happens only once
        // Note: Setting IsSelected still triggers individual property notifications, but graph updates are batched
        var fieldsToDeselect = SelectedGraphFields.ToList();
        SelectedGraphFields.Clear();
        
        // Update IsSelected property on each field
        foreach (var field in fieldsToDeselect)
        {
            field.IsSelected = false;
        }
        
        UpdateGraph();
        _logger.LogInformation("Cleared all selected graph fields");
    }



    private void FilterGraphFields()
    {
        FilteredFields.Clear();
        var filter = GraphFieldFilter?.ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrEmpty(filter)
            ? AvailableFields
            : AvailableFields.Where(f => f.DisplayName.ToLowerInvariant().Contains(filter));

        foreach (var field in filtered)
        {
            FilteredFields.Add(field);
        }
    }

    [RelayCommand]
    private void AddFieldToGraph(LogFieldInfo? field)
    {
        if (field == null) return;
        if (!SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName))
        {
            field.IsSelected = true;
            field.Color = GraphColors.GetColor(SelectedGraphFields.Count);
            SelectedGraphFields.Add(field);
            
            UpdateGraph();
        }
    }

    [RelayCommand]
    private void RemoveFieldFromGraph(LogFieldInfo? field)
    {
        if (field == null) return;
        field.IsSelected = false;
        field.Color = null;
        
        SelectedGraphFields.Remove(field);
        
        // Reassign colors to remaining fields
        for (int i = 0; i < SelectedGraphFields.Count; i++)
        {
            SelectedGraphFields[i].Color = GraphColors.GetColor(i);
        }
        
        UpdateGraph();
    }

    [RelayCommand]
    private void ClearGraph()
    {
        foreach (var field in SelectedGraphFields)
        {
            field.IsSelected = false;
        }
        SelectedGraphFields.Clear();
        CurrentGraph = null;
        HasGraphData = false;
    }

    private void UpdateGraph()
    {
        if (SelectedGraphFields.Count == 0)
        {
            CurrentGraph = null;
            HasGraphData = false;
            _logger.LogDebug("No fields selected for graph");
            return;
        }

        try
        {
            var fieldKeys = SelectedGraphFields.Select(f => f.DisplayName).ToArray();
            _logger.LogInformation("Updating graph with {Count} fields: {Fields}", fieldKeys.Length, string.Join(", ", fieldKeys));
            
            CurrentGraph = _logAnalyzerService.GetGraphData(fieldKeys);
            
            if (CurrentGraph == null)
            {
                _logger.LogWarning("GetGraphData returned null");
                HasGraphData = false;
                return;
            }
            
            HasGraphData = CurrentGraph.Series != null && CurrentGraph.Series.Any();
            _logger.LogInformation("Graph updated: HasGraphData={HasData}, SeriesCount={Count}", 
                HasGraphData, CurrentGraph.Series?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating graph");
            HasGraphData = false;
        }
    }

    [RelayCommand]
    private void ShowFieldStatistics(LogFieldInfo? field)
    {
        if (field == null) return;
        var stats = _logAnalyzerService.GetFieldStatistics(field.DisplayName);
        StatusMessage = $"{field.DisplayName}: Min={stats.Minimum:F3}, Max={stats.Maximum:F3}, Avg={stats.Average:F3}";
    }

    public void OnFieldSelectionChanged(LogFieldInfo field)
    {
        if (field.IsSelected) AddFieldToGraph(field);
        else RemoveFieldFromGraph(field);
    }

    public bool CanGoToNextPage => CurrentMessagePage < TotalMessagePages - 1;
    public bool CanGoToPreviousPage => CurrentMessagePage > 0;

    [RelayCommand]
    private void ResetZoom()
    {
        StatusMessage = "Zoom reset";
    }

    [RelayCommand]
    private async Task ExportGraphAsync()
    {
        if (!HasGraphData || _parentWindow == null) return;

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Graph",
                DefaultExtension = "png",
                SuggestedFileName = $"graph_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (file != null)
            {
                StatusMessage = $"Graph exported to {Path.GetFileName(file.Path.LocalPath)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export graph");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void GraphLeft() => StatusMessage = "Graph panned left";

    [RelayCommand]
    private void GraphRight() => StatusMessage = "Graph panned right";

    #endregion

    #region Script Commands

    [RelayCommand]
    private async Task LoadScriptFileAsync()
    {
        if (_parentWindow == null) { StatusMessage = "Cannot open file browser"; return; }

        try
        {
            var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Lua Script",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Lua Scripts") { Patterns = new[] { "*.lua" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                LoadedScriptPath = file.Path.LocalPath;
                LoadedScriptName = Path.GetFileName(LoadedScriptPath);
                ScriptText = await File.ReadAllTextAsync(LoadedScriptPath);
                ScriptOutput = $"Loaded: {LoadedScriptName}\nFile size: {new FileInfo(LoadedScriptPath).Length} bytes";
                StatusMessage = $"Script loaded: {LoadedScriptName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load script file");
            StatusMessage = $"Failed to load script: {ex.Message}";
            ScriptOutput = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveScriptFileAsync()
    {
        if (_parentWindow == null || string.IsNullOrWhiteSpace(ScriptText)) { StatusMessage = "Nothing to save"; return; }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Lua Script",
                DefaultExtension = "lua",
                SuggestedFileName = !string.IsNullOrEmpty(LoadedScriptName) ? LoadedScriptName : "script.lua",
                FileTypeChoices = new[] { new FilePickerFileType("Lua Scripts") { Patterns = new[] { "*.lua" } } }
            });

            if (file != null)
            {
                await File.WriteAllTextAsync(file.Path.LocalPath, ScriptText);
                LoadedScriptPath = file.Path.LocalPath;
                LoadedScriptName = Path.GetFileName(LoadedScriptPath);
                StatusMessage = $"Script saved: {LoadedScriptName}";
                ScriptOutput = $"Saved: {LoadedScriptName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save script file");
            StatusMessage = $"Failed to save script: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(ScriptText)) { StatusMessage = "No script loaded."; return; }
        if (!IsLogLoaded) { StatusMessage = "Load a log file first."; return; }

        IsScriptRunning = true;
        ScriptOutput = "Running script...\n";

        try
        {
            var result = await _logAnalyzerService.RunScriptAsync(ScriptText);
            if (result.IsSuccess)
            {
                ScriptOutput = result.Output;
                if (result.Warnings.Count > 0)
                    ScriptOutput += "\nWarnings:\n" + string.Join("\n", result.Warnings);
                ScriptOutput += $"\nCompleted in {result.ExecutionTime.TotalMilliseconds:F0}ms";
            }
            else
            {
                ScriptOutput = $"Error: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            ScriptOutput = $"Error: {ex.Message}";
        }
        finally
        {
            IsScriptRunning = false;
        }
    }

    [RelayCommand]
    private void ClearScript()
    {
        ScriptText = "";
        ScriptOutput = "";
        LoadedScriptPath = "";
        LoadedScriptName = "";
    }

    public void InsertScriptFunction(ScriptFunctionInfo function)
    {
        ScriptText = string.IsNullOrEmpty(ScriptText) ? function.Example : ScriptText + $"\n{function.Example}";
    }

    #endregion

    #region Cleanup

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _logAnalyzerService.LogFilesUpdated -= OnLogFilesUpdated;
            _logAnalyzerService.DownloadProgressChanged -= OnDownloadProgressChanged;
            _logAnalyzerService.DownloadCompleted -= OnDownloadCompleted;
            _logAnalyzerService.LogParsed -= OnLogParsed;
        }
        base.Dispose(disposing);
    }

    #endregion
}



/// <summary>
/// Cursor readout for displaying values at cursor position.
/// </summary>
public class CursorReadout
{
    public string FieldName { get; set; } = string.Empty;
    public string Color { get; set; } = "#FFFFFF";
    public double Value { get; set; }
    public string ValueDisplay => Value.ToString("F3");
}



/// <summary>
/// GPS point for map display.
/// </summary>
public class GpsPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Timestamp { get; set; }
}



/// <summary>
/// Parameter change record.
/// </summary>
public class ParameterChange
{
    public string Name { get; set; } = string.Empty;
    public double OldValue { get; set; }
    public double NewValue { get; set; }
    public double Timestamp { get; set; }
    public string TimestampDisplay => TimeSpan.FromSeconds(Timestamp).ToString(@"hh\:mm\:ss");
}

/// <summary>
/// Parameter display record for log file parameters.
/// Shows parameter value from log with metadata (description, options, etc.)
/// </summary>
public class LogParameter
{
    public string Name { get; set; } = string.Empty;
    public float Value { get; set; }
    public string ValueDisplay => Value.ToString("G");
    public string Description { get; set; } = "No description available";
    public string? Units { get; set; }
    public string Range { get; set; } = "Not specified";
    public string? Default { get; set; }
    public string Group { get; set; } = "General";
    public ObservableCollection<string> OptionsDisplay { get; set; } = new();
    public bool HasOptions => OptionsDisplay.Count > 0;
}

/// <summary>
/// Converter from bool to Yes/No string
/// </summary>
public class BoolToYesNoConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return b ? "Yes" : "No";
        return "No";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter from bool to success/error color
/// </summary>
public class BoolToSuccessColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return b ? "#10B981" : "#EF4444";  // Green for true, red for false
        return "#888";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
