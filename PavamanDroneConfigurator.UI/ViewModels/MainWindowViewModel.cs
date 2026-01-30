using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public ConnectionPageViewModel ConnectionPage { get; }
    public DroneDetailsPageViewModel DroneDetailsPage { get; }
    public AirframePageViewModel AirframePage { get; }
    public ParametersPageViewModel ParametersPage { get; }
    public SafetyPageViewModel SafetyPage { get; }
    public ProfilePageViewModel ProfilePage { get; }
    public FlightModePageViewModel FlightModesPage { get; }
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
    public Admin.AdminPanelViewModel? AdminPanelPage { get; private set; }

    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    
    public bool IsAdmin { get; }

    private bool _navigatedAfterConnect;

    public MainWindowViewModel(
        ConnectionPageViewModel connectionPage,
        DroneDetailsPageViewModel droneDetailsPage,
        AirframePageViewModel airframePage,
        ParametersPageViewModel parametersPage,
        SafetyPageViewModel safetyPage,
        ProfilePageViewModel profilePage,
        FlightModePageViewModel flightModesPage,
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
        FlightModesPage = flightModesPage;
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
        _parameterService = parameterService;
        _connectionService = connectionService;

        // Determine if user is admin from auth session
        IsAdmin = authSession.CurrentState.User?.IsAdmin ?? false;

        // Create admin panel only if user is admin
        if (IsAdmin && App.Services != null)
        {
            try
            {
                AdminPanelPage = App.Services.GetService<Admin.AdminPanelViewModel>();
                if (AdminPanelPage != null)
                {
                    _ = AdminPanelPage.InitializeAsync();
                }
            }
            catch
            {
                // Admin panel not available - gracefully degrade
                AdminPanelPage = null;
            }
        }

        _parameterService.ParameterDownloadStarted += OnParameterDownloadStarted;
        _parameterService.ParameterDownloadCompleted += OnParameterDownloadCompleted;
        _parameterService.ParameterUpdated += OnParameterUpdated;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        InitializeFromServices();

        _currentPage = connectionPage; // ensure connection page is the first page after splash
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
            UpdateAccessPermissions();
            UpdateNavigationForConnectionState(connected);
        });
    }

    private void InitializeFromServices()
    {
        IsParameterDownloadInProgress = _parameterService.IsParameterDownloadInProgress;
        IsParameterDownloadComplete = _parameterService.IsParameterDownloadComplete;
        UpdateProgress();
        UpdateAccessPermissions();
        UpdateNavigationForConnectionState(_connectionService.IsConnected);
    }

    private void UpdateNavigationForConnectionState(bool connected)
    {
        if (connected)
        {
            if (!_navigatedAfterConnect)
            {
                CurrentPage = ParametersPage; // open main application experience after connect
                _navigatedAfterConnect = true;
            }
        }
        else
        {
            // return to connection page and reset navigation state on disconnect
            CurrentPage = ConnectionPage;
            _navigatedAfterConnect = false;
        }
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