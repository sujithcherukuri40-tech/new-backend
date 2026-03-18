using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _isParameterDownloadInProgress;

    [ObservableProperty]
    private bool _isParameterDownloadComplete;

    [ObservableProperty]
    private int _parameterDownloadReceived;

    [ObservableProperty]
    private int? _parameterDownloadExpected;

    [ObservableProperty]
    private string _parameterDownloadStatusText = "Downloading parameters from vehicle...";

    [ObservableProperty]
    private bool _canAccessParameters;

    [ObservableProperty]
    private bool _canAccessAirframe;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private bool _isConnected;

    public ConnectionPageViewModel ConnectionPage { get; }
    public DroneDetailsPageViewModel DroneDetailsPage { get; }
    public AirframePageViewModel AirframePage { get; }
    public ParametersPageViewModel ParametersPage { get; }
    public SafetyPageViewModel SafetyPage { get; }
    public ProfilePageViewModel ProfilePage { get; }
    public FlightModePageViewModel FlightModePage { get; }
    public PowerPageViewModel PowerPage { get; }
    public MotorEscPageViewModel MotorEscPage { get; }
    public PidTuningPageViewModel PidTuningPage { get; }
    public SerialConfigPageViewModel SerialConfigPage { get; }
    public RcCalibrationPageViewModel RcCalibrationPage { get; }
    public SensorsCalibrationPageViewModel SensorsCalibrationPage { get; }
    public LogAnalyzerPageViewModel LogAnalyzerPage { get; }
    public ResetParametersPageViewModel ResetParametersPage { get; }
    public SprayingConfigPageViewModel SprayingConfigPage { get; }
    public CameraConfigPageViewModel CameraConfigPage { get; }
    public AdvancedSettingsPageViewModel AdvancedSettingsPage { get; }
    public FirmwarePageViewModel FirmwarePage { get; }
    public LiveMapPageViewModel LiveMapPage { get; }
    public Admin.AdminPanelViewModel? AdminPanelPage { get; private set; }
    public Admin.AdminDashboardViewModel? AdminDashboardPage { get; private set; }
    public Admin.FirmwareManagementViewModel? FirmwareManagementPage { get; private set; }
    public Admin.ParamLogsViewModel? ParamLogsPage { get; private set; }

    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    
    public bool IsAdmin { get; }

    /// <summary>
    /// Event raised when disconnect is requested and app should navigate back to entry page.
    /// </summary>
    public event EventHandler? DisconnectRequested;

    public MainWindowViewModel(
        ConnectionPageViewModel connectionPage,
        DroneDetailsPageViewModel droneDetailsPage,
        AirframePageViewModel airframePage,
        ParametersPageViewModel parametersPage,
        SafetyPageViewModel safetyPage,
        ProfilePageViewModel profilePage,
        FlightModePageViewModel flightModePage,
        PowerPageViewModel powerPage,
        MotorEscPageViewModel motorEscPage,
        PidTuningPageViewModel pidTuningPage,
        SerialConfigPageViewModel serialConfigPage,
        RcCalibrationPageViewModel rcCalibrationPage,
        SensorsCalibrationPageViewModel sensorsCalibrationPage,
        LogAnalyzerPageViewModel logAnalyzerPage,
        ResetParametersPageViewModel resetParametersPage,
        SprayingConfigPageViewModel sprayingConfigPage,
        CameraConfigPageViewModel cameraConfigPage,
        AdvancedSettingsPageViewModel advancedSettingsPage,
        FirmwarePageViewModel firmwarePage,
        LiveMapPageViewModel liveMapPage,
        IParameterService parameterService,
        IConnectionService connectionService,
        Auth.AuthSessionViewModel authSession)
    {
        ConnectionPage = connectionPage;
        DroneDetailsPage = droneDetailsPage;
        AirframePage = airframePage;
        ParametersPage = parametersPage;
        SafetyPage = safetyPage;
        ProfilePage = profilePage;
        FlightModePage = flightModePage;
        PowerPage = powerPage;
        MotorEscPage = motorEscPage;
        PidTuningPage = pidTuningPage;
        SerialConfigPage = serialConfigPage;
        RcCalibrationPage = rcCalibrationPage;
        SensorsCalibrationPage = sensorsCalibrationPage;
        LogAnalyzerPage = logAnalyzerPage;
        ResetParametersPage = resetParametersPage;
        SprayingConfigPage = sprayingConfigPage;
        CameraConfigPage = cameraConfigPage;
        AdvancedSettingsPage = advancedSettingsPage;
        FirmwarePage = firmwarePage;
        LiveMapPage = liveMapPage;
        _parameterService = parameterService;
        _connectionService = connectionService;

        // Determine if user is admin from auth session
        IsAdmin = authSession.CurrentState.User?.IsAdmin ?? false;

        // Create admin panel and dashboard only if user is admin
        if (IsAdmin && App.Services != null)
        {
            try
            {
                AdminDashboardPage = App.Services.GetService<Admin.AdminDashboardViewModel>();
                if (AdminDashboardPage != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await AdminDashboardPage.InitializeAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Admin dashboard initialization failed: {ex.Message}");
                        }
                    });
                }

                AdminPanelPage = App.Services.GetService<Admin.AdminPanelViewModel>();
                FirmwareManagementPage = App.Services.GetService<Admin.FirmwareManagementViewModel>();
                ParamLogsPage = App.Services.GetService<Admin.ParamLogsViewModel>();
            }
            catch (Exception ex)
            {
                // Admin features not available - gracefully degrade
                Console.WriteLine($"Failed to initialize admin features: {ex.Message}");
                AdminDashboardPage = null;
                AdminPanelPage = null;
            }
        }

        _parameterService.ParameterDownloadStarted += OnParameterDownloadStarted;
        _parameterService.ParameterDownloadCompleted += OnParameterDownloadCompleted;
        _parameterService.ParameterUpdated += OnParameterUpdated;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        InitializeFromServices();

        // Start on the Airframe page (connection already completed before MainWindow)
        _currentPage = airframePage;
        
        // Initialize connection state
        IsConnected = _connectionService.IsConnected;
    }

    /// <summary>
    /// Disconnect from the drone immediately.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!_connectionService.IsConnected) return;
        
        await _connectionService.DisconnectAsync();
        DisconnectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnParameterDownloadStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsParameterDownloadInProgress = true;
            IsParameterDownloadComplete = false;
            UpdateProgress();
            UpdateAccessPermissions();
        });
    }

    private void OnParameterDownloadCompleted(object? sender, bool completedSuccessfully)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsParameterDownloadInProgress = false;
            IsParameterDownloadComplete = completedSuccessfully;
            UpdateProgress();
            UpdateAccessPermissions();
        });
    }

    private void OnParameterUpdated(object? sender, string parameterName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsParameterDownloadInProgress)
            {
                UpdateProgress();
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            UpdateAccessPermissions();
            
            // Auto-navigate to connection tab on disconnect
            if (!connected)
            {
                // Disconnect detected - navigate to Connection page
                CurrentPage = ConnectionPage;
                // Also update view if MainWindow has a method to set view
            }
        });
    }

    private void InitializeFromServices()
    {
        IsParameterDownloadInProgress = _parameterService.IsParameterDownloadInProgress;
        IsParameterDownloadComplete = _parameterService.IsParameterDownloadComplete;
        IsConnected = _connectionService.IsConnected;
        UpdateProgress();
        UpdateAccessPermissions();
    }

    private void UpdateProgress()
    {
        ParameterDownloadReceived = _parameterService.ReceivedParameterCount;
        ParameterDownloadExpected = _parameterService.ExpectedParameterCount;

        if (ParameterDownloadExpected.HasValue && ParameterDownloadExpected.Value > 0)
        {
            ParameterDownloadStatusText = $"{ParameterDownloadReceived} / {ParameterDownloadExpected.Value}";
        }
        else if (ParameterDownloadReceived > 0)
        {
            ParameterDownloadStatusText = $"{ParameterDownloadReceived} parameters received...";
        }
        else
        {
            ParameterDownloadStatusText = "Requesting parameters...";
        }
    }

    private void UpdateAccessPermissions()
    {
        var connected = _connectionService.IsConnected;
        var parametersReady = _parameterService.IsParameterDownloadComplete;
        CanAccessParameters = connected && parametersReady;
        CanAccessAirframe = connected && parametersReady;
    }

    public void SetCurrentPage(ViewModelBase page, object view)
    {
        CurrentPage = page;
        CurrentView = view;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _parameterService.ParameterDownloadStarted -= OnParameterDownloadStarted;
            _parameterService.ParameterDownloadCompleted -= OnParameterDownloadCompleted;
            _parameterService.ParameterUpdated -= OnParameterUpdated;
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
        }
        base.Dispose(disposing);
    }
}