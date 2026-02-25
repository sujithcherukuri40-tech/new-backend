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
    private ObservableCollection<LogMessageTypeNode> _filteredMessageTypesTree = new();
    
    [ObservableProperty]
    private string _fieldSearchText = string.Empty;

    [ObservableProperty]
    private bool _hasTreeData;

    // Flag to prevent recursive selection updates
    private bool _isUpdatingFieldSelection;

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

    /// <summary>
    /// Selected fields with their colors for display in the graph legend panel.
    /// Shows field name, color indicator, and statistics (min/max/avg).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GraphLegendItem> _graphLegendItems = new();
    
    /// <summary>
    /// Indicates whether there are selected fields to show in the legend.
    /// </summary>
    public bool HasLegendItems => GraphLegendItems.Count > 0;

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

    /// <summary>
    /// Waypoints extracted from CMD messages in the log file.
    /// Displayed on map when user clicks the "Fit" button.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Controls.WaypointPoint> _mapWaypoints = new();

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
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (e.Success)
            {
                StatusMessage = $"Downloaded: {e.File.FileName}";
                SelectedFilePath = e.File.LocalPath ?? string.Empty;
                await LoadLogAsync();
            }
            else
            {
                StatusMessage = $"Download failed: {e.Error}";
            }
        });
    }

    private void OnLogParsed(object? sender, LogParseResult result)
    {
        // Process log data in optimized parallel manner
        _ = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                if (result.IsSuccess)
                {
                    // Update UI with initial state
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsLogLoaded = true;
                        IsAnalyzing = true;
                        LoadedLogInfo = $"{result.FileName} - {result.MessageCount:N0} messages, {result.DurationDisplay}";
                        StatusMessage = "Loading log data...";
                        
                        // Update overview
                        UpdateOverview(result);
                        
                        // Load message types (fast)
                        MessageTypes.Clear();
                        foreach (var type in result.MessageTypes)
                        {
                            MessageTypes.Add(type);
                        }
                        
                        // Clear all previous data
                        ClearPreviousLogData();
                    });

                    _logger.LogInformation("Starting parallel log analysis...");

                    // Run critical data loading in parallel for speed
                    var gpsTask = Task.Run(() => LoadGpsTrackOptimized());
                    var fieldsTask = Task.Run(() => LoadAvailableFieldsOptimized());
                    var eventsTask = Task.Run(() => ExtractEventsOptimized());
                    var paramsTask = Task.Run(() => ExtractParametersOptimized());

                    // Wait for GPS data first (needed for map)
                    var gpsData = await gpsTask;
                    _logger.LogInformation("GPS track loaded in {Ms}ms: {Count} points", sw.ElapsedMilliseconds, gpsData.Count);
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"GPS loaded ({gpsData.Count} points), loading events...";
                        
                        // Update GPS collections
                        GpsTrack.Clear();
                        GpsTrackPoints.Clear();
                        foreach (var pt in gpsData.Take(10000)) // Limit for performance
                        {
                            GpsTrack.Add(new GpsPoint { Latitude = pt.Lat, Longitude = pt.Lng, Altitude = pt.Alt, Timestamp = pt.Timestamp });
                            GpsTrackPoints.Add(new Controls.GpsTrackPoint { Latitude = pt.Lat, Longitude = pt.Lng, Altitude = pt.Alt, Timestamp = pt.Timestamp, Speed = pt.Speed });
                        }
                        
                        HasGpsData = GpsTrack.Count > 0;
                        if (GpsTrack.Count > 0)
                        {
                            MapCenterLat = GpsTrack[0].Latitude;
                            MapCenterLng = GpsTrack[0].Longitude;
                        }
                        
                        OnPropertyChanged(nameof(HasGpsData));
                        OnPropertyChanged(nameof(GpsTrackPoints));
                    });

                    // Wait for events
                    var events = await eventsTask;
                    _logger.LogInformation("Events extracted in {Ms}ms: {Count} events", sw.ElapsedMilliseconds, events.Count);
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Events loaded ({events.Count}), loading fields...";
                        
                        DetectedEvents.Clear();
                        foreach (var evt in events.Take(1000)) // Limit for performance
                        {
                            DetectedEvents.Add(evt);
                        }
                        
                        // Update summary
                        EventSummary = new EventSummary
                        {
                            TotalEvents = DetectedEvents.Count,
                            InfoCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Info || e.Severity == LogEventSeverity.Notice),
                            WarningCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Warning),
                            ErrorCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Error),
                            CriticalCount = DetectedEvents.Count(e => e.Severity == LogEventSeverity.Critical || e.Severity == LogEventSeverity.Emergency)
                        };
                        
                        ErrorCount = EventSummary.ErrorCount + EventSummary.CriticalCount;
                        WarningCount = EventSummary.WarningCount;
                        
                        PopulateEventFilterDropdowns();
                        FilterEvents();
                        UpdateEventDisplaySummary();
                        PopulateCriticalMapEvents();
                    });

                    // Wait for fields
                    var fields = await fieldsTask;
                    _logger.LogInformation("Fields loaded in {Ms}ms: {Count} fields", sw.ElapsedMilliseconds, fields.Count);
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "Building field tree...";
                        LoadAvailableFieldsFromData(fields);
                        AutoSelectDefaultGraphFields();
                    });

                    // Wait for parameters (can be slower, user may not need immediately)
                    var parameters = await paramsTask;
                    _logger.LogInformation("Parameters loaded in {Ms}ms: {Count} params", sw.ElapsedMilliseconds, parameters.Count);
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LogParameters.Clear();
                        FilteredLogParameters.Clear();
                        foreach (var p in parameters)
                        {
                            var param = new LogParameter { Name = p.Key, Value = p.Value };
                            LogParameters.Add(param);
                            FilteredLogParameters.Add(param);
                        }
                        HasLogParameters = LogParameters.Count > 0;
                    });

                    // Load raw messages sample (fast, limited)
                    await Dispatcher.UIThread.InvokeAsync(LoadRawLogMessages);

                    sw.Stop();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsAnalyzing = false;
                        StatusMessage = $"Log loaded in {sw.ElapsedMilliseconds/1000.0:F1}s: {result.MessageCount:N0} msgs, {GpsTrack.Count} GPS pts, {DetectedEvents.Count} events";
                        
                        OnPropertyChanged(nameof(HasGpsData));
                        OnPropertyChanged(nameof(HasFilteredEvents));
                        OnPropertyChanged(nameof(ShowNoEventsMessage));
                        OnPropertyChanged(nameof(HasLogParameters));
                    });
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing log file");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLogLoaded = false;
                    IsAnalyzing = false;
                    StatusMessage = $"Error: {ex.Message}";
                });
            }
        });
    }
    
    /// <summary>
    /// Optimized GPS track loading - runs on background thread
    /// </summary>
    private List<(double Lat, double Lng, double Alt, double Timestamp, double Speed)> LoadGpsTrackOptimized()
    {
        var results = new List<(double Lat, double Lng, double Alt, double Timestamp, double Speed)>();
        
        try
        {
            var gpsData = _logAnalyzerService.GetFieldData("GPS", "Lat");
            if (gpsData == null || gpsData.Count == 0) return results;
            
            var lngData = _logAnalyzerService.GetFieldData("GPS", "Lng");
            if (lngData == null || lngData.Count == 0) return results;
            
            var altData = _logAnalyzerService.GetFieldData("GPS", "Alt");
            var spdData = _logAnalyzerService.GetFieldData("GPS", "Spd");
            
            var count = Math.Min(gpsData.Count, lngData.Count);
            
            for (int i = 0; i < count; i++)
            {
                var lat = gpsData[i].Value;
                var lng = lngData[i].Value;
                
                // Skip invalid coordinates
                if (Math.Abs(lat) < 0.001 && Math.Abs(lng) < 0.001) continue;
                if (Math.Abs(lat) > 90 || Math.Abs(lng) > 180) continue;
                
                var alt = altData != null && i < altData.Count ? altData[i].Value : 0;
                var spd = spdData != null && i < spdData.Count ? spdData[i].Value : 0;
                var timestamp = gpsData[i].Timestamp / 1_000_000.0;
                
                results.Add((lat, lng, alt, timestamp, spd));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading GPS track");
        }
        
        return results;
    }
    
    /// <summary>
    /// Optimized field loading - runs on background thread
    /// </summary>
    private List<LogFieldInfo> LoadAvailableFieldsOptimized()
    {
        try
        {
            return _logAnalyzerService.GetAvailableGraphFields() ?? new List<LogFieldInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available fields");
            return new List<LogFieldInfo>();
        }
    }
    
    /// <summary>
    /// Loads available fields into UI from pre-loaded data
    /// </summary>
    private void LoadAvailableFieldsFromData(List<LogFieldInfo> fields)
    {
        AvailableFields.Clear();
        FilteredFields.Clear();
        SelectedGraphFields.Clear();
        MessageTypesTree.Clear();
        FilteredMessageTypesTree.Clear();
        GraphLegendItems.Clear();
        
        if (fields.Count == 0)
        {
            HasTreeData = false;
            return;
        }
        
        var fieldLookup = new Dictionary<string, LogFieldInfo>();
        
        foreach (var field in fields)
        {
            AvailableFields.Add(field);
            FilteredFields.Add(field);
            fieldLookup[field.DisplayName] = field;
        }
        
        var groupedByType = fields.GroupBy(f => f.MessageType).OrderBy(g => g.Key);
        
        foreach (var group in groupedByType)
        {
            var typeNode = new LogMessageTypeNode { Name = group.Key, IsMessageType = true };
            var colorIndex = 0;
            
            foreach (var field in group.OrderBy(f => f.FieldName))
            {
                var fieldNode = new LogFieldNode
                {
                    Name = field.FieldName,
                    FullKey = field.DisplayName,
                    Color = GraphColors.GetColor(colorIndex++)
                };
                
                var capturedFieldKey = field.DisplayName;
                fieldNode.PropertyChanged += (s, e) =>
                {
                    if (_isUpdatingFieldSelection) return;
                    if (e.PropertyName == nameof(LogFieldNode.IsSelected) && s is LogFieldNode node)
                    {
                        if (node.FullKey == capturedFieldKey && fieldLookup.TryGetValue(capturedFieldKey, out var matchingField))
                        {
                            if (node.IsSelected && !SelectedGraphFields.Any(f => f.DisplayName == matchingField.DisplayName))
                            {
                                _isUpdatingFieldSelection = true;
                                try
                                {
                                    matchingField.IsSelected = true;
                                    matchingField.Color = GraphColors.GetColor(SelectedGraphFields.Count);
                                    SelectedGraphFields.Add(matchingField);
                                }
                                finally { _isUpdatingFieldSelection = false; }
                                UpdateGraph();
                            }
                            else if (!node.IsSelected)
                            {
                                var existing = SelectedGraphFields.FirstOrDefault(f => f.DisplayName == matchingField.DisplayName);
                                if (existing != null)
                                {
                                    _isUpdatingFieldSelection = true;
                                    try
                                    {
                                        SelectedGraphFields.Remove(existing);
                                        existing.IsSelected = false;
                                        existing.Color = null;
                                        for (int i = 0; i < SelectedGraphFields.Count; i++)
                                            SelectedGraphFields[i].Color = GraphColors.GetColor(i);
                                    }
                                    finally { _isUpdatingFieldSelection = false; }
                                    UpdateGraph();
                                }
                            }
                        }
                    }
                };
                
                typeNode.Fields.Add(fieldNode);
            }
            
            MessageTypesTree.Add(typeNode);
            FilteredMessageTypesTree.Add(typeNode);
        }
        
        HasTreeData = MessageTypesTree.Count > 0;
        OnPropertyChanged(nameof(HasLegendItems));
    }
    
    /// <summary>
    /// Optimized event extraction - runs on background thread
    /// </summary>
    private List<LogEvent> ExtractEventsOptimized()
    {
        var events = new List<LogEvent>();
        var eventId = 1;
        
        try
        {
            // Quick extraction from MSG messages - limit to 500 for speed
            var msgMessages = _logAnalyzerService.GetMessages("MSG", 0, 500);
            foreach (var msg in msgMessages)
            {
                var text = msg.Fields.GetValueOrDefault("Message")?.ToString() ?? 
                           msg.Fields.GetValueOrDefault("Text")?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj, out var timeUs))
                    timestamp = timeUs / 1_000_000.0;
                
                var severity = LogEventSeverity.Info;
                var title = "Message";
                
                var textLower = text.ToLowerInvariant();
                if (textLower.Contains("error") || textLower.Contains("fail"))
                    { severity = LogEventSeverity.Error; title = "Error"; }
                else if (textLower.Contains("warning") || textLower.Contains("low"))
                    { severity = LogEventSeverity.Warning; title = "Warning"; }
                else if (textLower.Contains("critical") || textLower.Contains("crash") || textLower.Contains("failsafe"))
                    { severity = LogEventSeverity.Critical; title = "Critical"; }
                else if (textLower.Contains("arm"))
                    title = textLower.Contains("disarm") ? "Disarmed" : "Armed";
                
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = severity,
                    Title = title,
                    Description = text,
                    Source = "MSG"
                });
            }
            
            // Extract from EV messages - limit to 200
            var evMessages = _logAnalyzerService.GetMessages("EV", 0, 200);
            foreach (var msg in evMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj, out var timeUs))
                    timestamp = timeUs / 1_000_000.0;
                
                var evId = msg.Fields.GetValueOrDefault("Id")?.ToString() ?? "0";
                var (title, severity) = int.TryParse(evId, out var evIdNum) ? evIdNum switch
                {
                    10 => ("Armed", LogEventSeverity.Notice),
                    11 => ("Disarmed", LogEventSeverity.Notice),
                    15 => ("Battery Failsafe", LogEventSeverity.Critical),
                    17 => ("GPS Failsafe", LogEventSeverity.Error),
                    28 => ("Radio Failsafe", LogEventSeverity.Error),
                    _ => ($"Event {evId}", LogEventSeverity.Info)
                } : ($"Event {evId}", LogEventSeverity.Info);
                
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = severity,
                    Title = title,
                    Description = $"Event ID: {evId}",
                    Source = "Autopilot"
                });
            }
            
            // Extract from ERR messages - limit to 200
            var errMessages = _logAnalyzerService.GetMessages("ERR", 0, 200);
            foreach (var msg in errMessages)
            {
                double timestamp = 0;
                if (msg.Fields.TryGetValue("TimeUS", out var timeObj) && double.TryParse(timeObj, out var timeUs))
                    timestamp = timeUs / 1_000_000.0;
                
                var subsys = msg.Fields.GetValueOrDefault("Subsys")?.ToString() ?? "Unknown";
                var errCode = msg.Fields.GetValueOrDefault("ECode")?.ToString() ?? "0";
                
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = timestamp,
                    Type = LogEventType.Custom,
                    Severity = LogEventSeverity.Error,
                    Title = $"Error: {subsys}",
                    Description = $"Subsystem: {subsys}, Code: {errCode}",
                    Source = subsys
                });
            }
            
            // Sort by timestamp
            events = events.OrderBy(e => e.Timestamp).ToList();
            for (int i = 0; i < events.Count; i++)
                events[i].Id = i + 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting events");
        }
        
        return events;
    }
    
    /// <summary>
    /// Optimized parameter extraction - runs on background thread
    /// </summary>
    private Dictionary<string, float> ExtractParametersOptimized()
    {
        var parameters = new Dictionary<string, float>();
        
        try
        {
            // Try service first
            var logParams = _logAnalyzerService.GetLogParameters();
            if (logParams.Count > 0) return logParams;
            
            // Fallback to PARM messages - limit to 5000 for speed
            var parmMessages = _logAnalyzerService.GetMessages("PARM", 0, 5000);
            
            foreach (var msg in parmMessages)
            {
                string? paramName = null;
                if (msg.Fields.TryGetValue("Name", out var nameObj))
                    paramName = nameObj?.Trim();
                else if (msg.Fields.TryGetValue("N", out var nObj))
                    paramName = nObj?.Trim();
                
                if (string.IsNullOrWhiteSpace(paramName)) continue;
                
                if (msg.Fields.TryGetValue("Value", out var valueStr) && float.TryParse(valueStr, out var value))
                    parameters[paramName] = value;
                else if (msg.Fields.TryGetValue("V", out var vStr) && float.TryParse(vStr, out var v))
                    parameters[paramName] = v;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting parameters");
        }
        
        return parameters;
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
    
    partial void OnFieldSearchTextChanged(string value)
    {
        FilterMessageTypesTree();
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

    #region Event Handlers

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
    
    /// <summary>
    /// Filters the message types tree based on the field search text.
    /// Shows only message types and fields that match the search criteria.
    /// </summary>
    private void FilterMessageTypesTree()
    {
        FilteredMessageTypesTree.Clear();
        
        var searchText = FieldSearchText?.ToLowerInvariant()?.Trim() ?? "";
        
        if (string.IsNullOrEmpty(searchText))
        {
            // No search - show all message types
            foreach (var node in MessageTypesTree)
            {
                FilteredMessageTypesTree.Add(node);
            }
            return;
        }
        
        // Filter message types and fields based on search text
        foreach (var node in MessageTypesTree)
        {
            // Check if the message type name matches
            bool messageTypeMatches = node.Name.ToLowerInvariant().Contains(searchText);
            
            // Check if any field name matches
            var matchingFields = node.Fields
                .Where(f => f.Name.ToLowerInvariant().Contains(searchText) || 
                            f.FullKey.ToLowerInvariant().Contains(searchText))
                .ToList();
            
            if (messageTypeMatches || matchingFields.Count > 0)
            {
                // Create a filtered copy of the node
                var filteredNode = new LogMessageTypeNode
                {
                    Name = node.Name,
                    IsMessageType = node.IsMessageType,
                    IsExpanded = true // Expand matching nodes
                };
                
                // Add matching fields (or all fields if the message type name matches)
                IEnumerable<LogFieldNode> fieldsToAdd = messageTypeMatches ? node.Fields : matchingFields;
                foreach (var field in fieldsToAdd)
                {
                    // Create a reference to the original field to maintain selection state
                    filteredNode.Fields.Add(field);
                }
                
                FilteredMessageTypesTree.Add(filteredNode);
            }
        }
    }

    #endregion

    #region Cleanup
    
    /// <summary>
    /// Clears all data from the previous log file to prevent stale data from being displayed.
    /// Must be called on UI thread before loading a new log.
    /// </summary>
    private void ClearPreviousLogData()
    {
        // Clear map data
        GpsTrack.Clear();
        GpsTrackPoints.Clear();
        CriticalMapEvents.Clear();
        MapWaypoints.Clear();
        CurrentMapPosition = null;
        MapCenterLat = 0;
        MapCenterLng = 0;
        HasGpsData = false;
        
        // Clear events data
        DetectedEvents.Clear();
        FilteredEvents.Clear();
        EventSummary = null;
        ErrorCount = 0;
        WarningCount = 0;
        
        // Clear parameters data
        LogParameters.Clear();
        FilteredLogParameters.Clear();
        ParameterChanges.Clear();
        HasLogParameters = false;
        
        // Clear raw log messages
        RawLogMessages.Clear();
        HasRawLogData = false;
        
        _logger.LogInformation("Cleared all previous log data for new log file");
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

    private void JumpToTime(double timestamp)
    {
        CursorTime = timestamp;
        CursorTimeDisplay = TimeSpan.FromSeconds(timestamp).ToString(@"hh\:mm\:ss\.fff");
        
        // Update zoom window to center on this time
        var windowSize = (ZoomEndTime - ZoomStartTime);
        var halfWindow = windowSize / 2;
        ZoomStartTime = Math.Max(0, timestamp - halfWindow);
        ZoomEndTime = ZoomStartTime + windowSize;
        
        // Update map position
        UpdateMapPosition(timestamp);
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

    private void PopulateEventFilterDropdowns()
    {
        AvailableSeverities.Clear();
        AvailableSeverities.Add("All");
        var severities = DetectedEvents.Select(e => e.SeverityDisplay).Distinct().OrderBy(s => s);
        foreach (var sev in severities)
            if (!AvailableSeverities.Contains(sev))
                AvailableSeverities.Add(sev);
        
        AvailableEventTypes.Clear();
        AvailableEventTypes.Add("All");
        var eventTypes = DetectedEvents.Select(e => e.TypeDisplay).Distinct().OrderBy(t => t);
        foreach (var type in eventTypes)
            if (!AvailableEventTypes.Contains(type))
                AvailableEventTypes.Add(type);
        
        AvailableSources.Clear();
        AvailableSources.Add("All");
        var sources = DetectedEvents.Select(e => e.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s);
        foreach (var source in sources)
            if (!AvailableSources.Contains(source))
                AvailableSources.Add(source);
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
        
        OnPropertyChanged(nameof(EventDisplaySummary));
        OnPropertyChanged(nameof(EventPagination));
        OnPropertyChanged(nameof(ShowNoEventsMessage));
        OnPropertyChanged(nameof(HasFilteredEvents));
    }

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

    private void AutoSelectDefaultGraphFields()
    {
        _logger.LogInformation("Auto-selecting graph fields. Available fields count: {Count}", AvailableFields.Count);
        
        if (AvailableFields.Count == 0) return;

        var priorityPatterns = new[]
        {
            new[] { "ATT", "Roll" },
            new[] { "ATT", "Pitch" },
            new[] { "GPS", "Alt" },
            new[] { "BAT", "Volt" },
        };

        var selectedCount = 0;
        const int maxAutoSelectFields = 4;

        _isUpdatingFieldSelection = true;
        try
        {
            foreach (var pattern in priorityPatterns)
            {
                if (selectedCount >= maxAutoSelectFields) break;

                var msgType = pattern[0];
                var fieldName = pattern.Length > 1 ? pattern[1] : null;

                LogFieldInfo? field = null;
                
                if (fieldName != null)
                {
                    field = AvailableFields.FirstOrDefault(f => 
                        f.DisplayName.Equals($"{msgType}.{fieldName}", StringComparison.OrdinalIgnoreCase));
                    
                    if (field == null)
                    {
                        field = AvailableFields.FirstOrDefault(f => 
                            f.MessageType.Equals(msgType, StringComparison.OrdinalIgnoreCase) &&
                            f.FieldName.Contains(fieldName, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (field != null && !SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName))
                {
                    field.IsSelected = true;
                    field.Color = GraphColors.GetColor(selectedCount);
                    SelectedGraphFields.Add(field);
                    UpdateTreeNodeSelection(field.DisplayName, true);
                    selectedCount++;
                }
            }

            if (selectedCount == 0)
            {
                foreach (var field in AvailableFields.Take(maxAutoSelectFields))
                {
                    if (!SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName))
                    {
                        field.IsSelected = true;
                        field.Color = GraphColors.GetColor(selectedCount);
                        SelectedGraphFields.Add(field);
                        UpdateTreeNodeSelection(field.DisplayName, true);
                        selectedCount++;
                    }
                }
            }
        }
        finally
        {
            _isUpdatingFieldSelection = false;
        }

        if (selectedCount > 0)
        {
            _logger.LogInformation("Auto-selected {Count} graph fields for display", selectedCount);
            UpdateGraph();
        }
    }

    private void UpdateTreeNodeSelection(string fieldKey, bool isSelected)
    {
        foreach (var typeNode in MessageTypesTree)
        {
            foreach (var fieldNode in typeNode.Fields)
            {
                if (fieldNode.FullKey == fieldKey)
                {
                    if (fieldNode.IsSelected != isSelected)
                        fieldNode.IsSelected = isSelected;
                    return;
                }
            }
        }
    }

    private void UpdateGraph()
    {
        GraphLegendItems.Clear();
        
        if (SelectedGraphFields.Count == 0)
        {
            CurrentGraph = null;
            HasGraphData = false;
            OnPropertyChanged(nameof(HasLegendItems));
            return;
        }

        try
        {
            var fieldKeys = SelectedGraphFields.Select(f => f.DisplayName).ToArray();
            var newGraphData = _logAnalyzerService.GetGraphData(fieldKeys);
            
            if (newGraphData == null || newGraphData.Series == null || newGraphData.Series.Count == 0)
            {
                CurrentGraph = null;
                HasGraphData = false;
                OnPropertyChanged(nameof(HasLegendItems));
                return;
            }
            
            CurrentGraph = newGraphData;
            HasGraphData = newGraphData.Series.Any(s => s.Points.Count > 0);
            
            // Update legend items
            foreach (var series in CurrentGraph.Series.Where(s => s.IsVisible))
            {
                GraphLegendItems.Add(new GraphLegendItem
                {
                    FieldName = series.Name,
                    Color = series.Color,
                    MinValue = series.MinValue,
                    MaxValue = series.MaxValue,
                    MeanValue = series.Average
                });
            }
            
            OnPropertyChanged(nameof(HasLegendItems));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating graph");
            CurrentGraph = null;
            HasGraphData = false;
            OnPropertyChanged(nameof(HasLegendItems));
        }
    }

    private void LoadRawLogMessages()
    {
        RawLogMessages.Clear();
        
        try
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading raw log messages");
            HasRawLogData = false;
            RawDataRowCount = "Error loading data";
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

    #endregion

    #region Public Methods

    public void OnFieldSelectionChanged(LogFieldInfo field)
    {
        if (_isUpdatingFieldSelection) return;
        
        var isInSelected = SelectedGraphFields.Any(f => f.DisplayName == field.DisplayName);
        
        if (field.IsSelected && !isInSelected)
        {
            _isUpdatingFieldSelection = true;
            try
            {
                field.Color = GraphColors.GetColor(SelectedGraphFields.Count);
                SelectedGraphFields.Add(field);
                UpdateTreeNodeSelection(field.DisplayName, true);
            }
            finally { _isUpdatingFieldSelection = false; }
            UpdateGraph();
        }
        else if (!field.IsSelected && isInSelected)
        {
            _isUpdatingFieldSelection = true;
            try
            {
                SelectedGraphFields.Remove(field);
                field.Color = null;
                UpdateTreeNodeSelection(field.DisplayName, false);
                for (int i = 0; i < SelectedGraphFields.Count; i++)
                    SelectedGraphFields[i].Color = GraphColors.GetColor(i);
            }
            finally { _isUpdatingFieldSelection = false; }
            UpdateGraph();
        }
    }

    public void InsertScriptFunction(ScriptFunctionInfo function)
    {
        ScriptText = string.IsNullOrEmpty(ScriptText) ? function.Example : ScriptText + $"\n{function.Example}";
    }

    #endregion

    #region RelayCommands

    [RelayCommand]
    private void SelectDefaultFields()
    {
        AutoSelectDefaultGraphFields();
    }

    [RelayCommand]
    private void ClearAllFields()
    {
        _isUpdatingFieldSelection = true;
        try
        {
            foreach (var field in SelectedGraphFields.ToList())
            {
                field.IsSelected = false;
                field.Color = null;
                UpdateTreeNodeSelection(field.DisplayName, false);
            }
            SelectedGraphFields.Clear();
        }
        finally
        {
            _isUpdatingFieldSelection = false;
        }
        CurrentGraph = null;
        HasGraphData = false;
        GraphLegendItems.Clear();
        OnPropertyChanged(nameof(HasLegendItems));
    }

    [RelayCommand]
    private void FitToMapWithWaypoints()
    {
        if (!IsLogLoaded)
        {
            StatusMessage = "Please load a log file first";
            return;
        }
        StatusMessage = MapWaypoints.Count > 0
            ? $"Showing {MapWaypoints.Count} waypoints on map"
            : "No waypoint data found in log";
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
        SelectedTimeRangeIndex = 4;
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
                SuggestedFileName = $"events_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            });
            
            if (file != null)
            {
                await using var writer = new StreamWriter(file.Path.LocalPath, false, System.Text.Encoding.UTF8);
                await writer.WriteLineAsync("Timestamp,Severity,Event Type,Source,Message,Details");
                
                foreach (var evt in FilteredEvents)
                {
                    var line = $"\"{evt.TimestampDisplay}\",\"{evt.Severity}\",\"{evt.Title}\",\"{evt.Type}\",\"{evt.Description}\"";
                    await writer.WriteLineAsync(line);
                }
                
                StatusMessage = $"Exported {FilteredEvents.Count} events to CSV";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export events to CSV");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportToKmlAsync()
    {
        if (_parentWindow == null || !HasGpsData)
        {
            StatusMessage = "No GPS data to export";
            return;
        }

        try
        {
            var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to KML",
                DefaultExtension = "kml",
                SuggestedFileName = $"flight_track_{DateTime.Now:yyyyMMdd_HHmmss}.kml"
            });

            if (file != null)
            {
                await using var writer = new StreamWriter(file.Path.LocalPath, false, System.Text.Encoding.UTF8);
                
                await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                await writer.WriteLineAsync("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
                await writer.WriteLineAsync("  <Document><name>Flight Track</name>");
                await writer.WriteLineAsync("    <Placemark><name>Flight Path</name><LineString><coordinates>");
                
                foreach (var point in GpsTrack)
                {
                    await writer.WriteLineAsync($"      {point.Longitude:F8},{point.Latitude:F8},{point.Altitude:F2}");
                }
                
                await writer.WriteLineAsync("    </coordinates></LineString></Placemark>");
                await writer.WriteLineAsync("  </Document></kml>");
                
                StatusMessage = $"Exported {GpsTrack.Count} GPS points to KML";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KML export failed");
            StatusMessage = $"Export failed: {ex.Message}";
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
                SuggestedFileName = $"events_export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            });
            
            if (file != null)
            {
                var exportData = FilteredEvents.Select(e => new { e.Id, e.TimestampDisplay, e.Timestamp, Severity = e.Severity.ToString(), e.Title, e.Description }).ToList();
                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(file.Path.LocalPath, json, System.Text.Encoding.UTF8);
                StatusMessage = $"Exported {FilteredEvents.Count} events to JSON";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export events to JSON");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void JumpToEvent(LogEvent? evt)
    {
        if (evt == null) return;
        JumpToTime(evt.Timestamp);
        SelectedTabIndex = TAB_PLOT;
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
    private void CloseDownloadDialog()
    {
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

    #endregion

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

/// <summary>
/// Represents a legend item for the graph showing field name, color, and statistics.
/// </summary>
public class GraphLegendItem
{
    public string FieldName { get; set; } = string.Empty;
    public string Color { get; set; } = "#FFFFFF";
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double MeanValue { get; set; }
    
    public string MinDisplay => MinValue.ToString("F2");
    public string MaxDisplay => MaxValue.ToString("F2");
    public string MeanDisplay => MeanValue.ToString("F2");
}

