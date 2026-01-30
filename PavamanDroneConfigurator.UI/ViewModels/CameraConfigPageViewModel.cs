using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for Camera configuration page.
/// Manages camera trigger settings, servo configuration, and gimbal settings.
/// </summary>
public partial class CameraConfigPageViewModel : ViewModelBase
{
    private readonly ILogger<CameraConfigPageViewModel> _logger;
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;

    #region Parameter Names
    
    private const string ParamCameraRelayOn = "CAM_RELAY_ON";
    private const string ParamCameraRelayPin = "RELAY_PIN";
    private const string ParamCameraType = "CAM_TRIGG_TYPE";
    private const string ParamCameraServoOn = "CAM_SERVO_ON";
    private const string ParamCameraServoOff = "CAM_SERVO_OFF";
    private const string ParamCameraTriggerDistance = "CAM_TRIGG_DIST";
    private const string ParamCameraDuration = "CAM_DURATION";
    
    // Gimbal parameters
    private const string ParamMntDefltMode = "MNT_DEFLT_MODE";
    private const string ParamMntRcRate = "MNT_RC_RATE";
    private const string ParamMntAngMinTil = "MNT_ANGMIN_TIL";
    private const string ParamMntAngMaxTil = "MNT_ANGMAX_TIL";
    private const string ParamMntRcInTilt = "MNT_RC_IN_TILT";
    
    // Output channel parameters (Servo9 typically used for gimbal)
    private const string ParamServo9Function = "SERVO9_FUNCTION";
    private const string ParamServo9Min = "SERVO9_MIN";
    private const string ParamServo9Max = "SERVO9_MAX";
    private const string ParamServo9Trim = "SERVO9_TRIM";
    private const string ParamServo9Reversed = "SERVO9_REVERSED";
    
    #endregion

    #region Observable Properties - Page State

    [ObservableProperty]
    private bool _isPageEnabled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Downloading parameters...";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    #endregion

    #region Observable Properties - Camera Settings

    [ObservableProperty]
    private CameraRelayOption? _selectedCameraRelay;

    [ObservableProperty]
    private float _cameraRelayPin = -1;

    [ObservableProperty]
    private CameraTriggerTypeOption? _selectedCameraTriggerType;

    [ObservableProperty]
    private float _cameraServoOn = 1900;

    [ObservableProperty]
    private float _cameraServoOff = 1100;

    [ObservableProperty]
    private float _cameraTriggerDistance = 0;

    [ObservableProperty]
    private float _cameraTriggerDuration = 10;

    #endregion

    #region Observable Properties - Gimbal Tilt Settings

    [ObservableProperty]
    private GimbalControlModeOption? _selectedGimbalControlMode;

    [ObservableProperty]
    private float _fixedTiltAngle = -45;

    [ObservableProperty]
    private RcChannelOption? _selectedRcTiltChannel;

    [ObservableProperty]
    private float _gimbalTiltMin = -90;

    [ObservableProperty]
    private float _gimbalTiltMax = 0;

    #endregion

    #region Observable Properties - Output Channel Settings

    [ObservableProperty]
    private OutputChannelOption? _selectedOutputChannel;

    [ObservableProperty]
    private OutputTypeOption? _selectedOutputType;

    [ObservableProperty]
    private float _pwmMin = 1000;

    [ObservableProperty]
    private float _pwmMax = 2000;

    [ObservableProperty]
    private float _pwmNeutral = 1500;

    [ObservableProperty]
    private bool _reverseOutput;

    #endregion

    #region Observable Properties - Validation

    [ObservableProperty]
    private string? _cameraRelayPinError;

    [ObservableProperty]
    private string? _cameraServoOnError;

    [ObservableProperty]
    private string? _cameraServoOffError;

    [ObservableProperty]
    private string? _cameraTriggerDistanceError;

    [ObservableProperty]
    private string? _cameraTriggerDurationError;

    [ObservableProperty]
    private string? _fixedTiltAngleError;

    [ObservableProperty]
    private string? _gimbalTiltMinError;

    [ObservableProperty]
    private string? _gimbalTiltMaxError;

    [ObservableProperty]
    private string? _pwmMinError;

    [ObservableProperty]
    private string? _pwmMaxError;

    [ObservableProperty]
    private string? _pwmNeutralError;

    #endregion

    #region Option Collections

    public ObservableCollection<CameraRelayOption> CameraRelayOptions { get; } = new()
    {
        new CameraRelayOption(0, "LOW"),
        new CameraRelayOption(1, "HIGH")
    };

    public ObservableCollection<CameraTriggerTypeOption> CameraTriggerTypeOptions { get; } = new()
    {
        new CameraTriggerTypeOption(0, "Servo"),
        new CameraTriggerTypeOption(1, "Relay")
    };

    public ObservableCollection<GimbalControlModeOption> GimbalControlModeOptions { get; } = new()
    {
        new GimbalControlModeOption(0, "Disabled"),
        new GimbalControlModeOption(1, "RC Controlled"),
        new GimbalControlModeOption(2, "Fixed Angle"),
        new GimbalControlModeOption(3, "Mission Controlled")
    };

    public ObservableCollection<RcChannelOption> RcChannelOptions { get; } = new()
    {
        new RcChannelOption(5, "CH5"),
        new RcChannelOption(6, "CH6"),
        new RcChannelOption(7, "CH7"),
        new RcChannelOption(8, "CH8"),
        new RcChannelOption(9, "CH9"),
        new RcChannelOption(10, "CH10"),
        new RcChannelOption(11, "CH11"),
        new RcChannelOption(12, "CH12")
    };

    public ObservableCollection<OutputChannelOption> OutputChannelOptions { get; } = new()
    {
        new OutputChannelOption(9, "Channel 9 (AUX1)"),
        new OutputChannelOption(10, "Channel 10 (AUX2)"),
        new OutputChannelOption(11, "Channel 11 (AUX3)"),
        new OutputChannelOption(12, "Channel 12 (AUX4)"),
        new OutputChannelOption(13, "Channel 13 (AUX5)"),
        new OutputChannelOption(14, "Channel 14 (AUX6)")
    };

    public ObservableCollection<OutputTypeOption> OutputTypeOptions { get; } = new()
    {
        new OutputTypeOption(0, "Disabled"),
        new OutputTypeOption(1, "PWM Servo"),
        new OutputTypeOption(2, "Brushless Gimbal")
    };

    #endregion

    #region Computed Properties

    public bool IsServoMode => SelectedCameraTriggerType?.Value == 0;

    public bool IsGimbalEnabled => SelectedGimbalControlMode?.Value != 0;

    public bool IsFixedAngleMode => SelectedGimbalControlMode?.Value == 2 || SelectedGimbalControlMode?.Value == 3;

    public bool IsRcControlledMode => SelectedGimbalControlMode?.Value == 1;

    public bool IsPwmServoMode => SelectedOutputType?.Value == 1;

    #endregion

    public CameraConfigPageViewModel(
        ILogger<CameraConfigPageViewModel> logger,
        IParameterService parameterService,
        IConnectionService connectionService)
    {
        _logger = logger;
        _parameterService = parameterService;
        _connectionService = connectionService;

        _parameterService.ParameterDownloadProgressChanged += OnParameterDownloadProgressChanged;
        _parameterService.ParameterUpdated += OnParameterUpdated;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

        InitializeState();
    }

    private void InitializeState()
    {
        IsPageEnabled = _parameterService.IsParameterDownloadComplete && _connectionService.IsConnected;
        StatusMessage = IsPageEnabled ? "Camera parameters loaded." : "Downloading parameters...";

        if (IsPageEnabled)
        {
            _ = LoadParametersAsync();
        }
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPageEnabled = connected && _parameterService.IsParameterDownloadComplete;
            if (!connected)
            {
                StatusMessage = "Disconnected from vehicle";
            }
        });
    }

    private void OnParameterDownloadProgressChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_parameterService.IsParameterDownloadInProgress)
            {
                StatusMessage = $"Downloading parameters... ({_parameterService.ReceivedParameterCount}/{_parameterService.ExpectedParameterCount ?? 0})";
                IsPageEnabled = false;
            }
            else if (_parameterService.IsParameterDownloadComplete)
            {
                StatusMessage = "Camera parameters loaded.";
                IsPageEnabled = _connectionService.IsConnected;
                _ = LoadParametersAsync();
            }
        });
    }

    private void OnParameterUpdated(object? sender, string paramName)
    {
        // Handle real-time parameter updates from FC
        Dispatcher.UIThread.Post(() =>
        {
            _ = LoadSingleParameterAsync(paramName);
        });
    }

    #endregion

    #region Parameter Loading

    private async Task LoadParametersAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading camera configuration...";

            // Load camera settings
            var cameraRelay = await _parameterService.GetParameterAsync(ParamCameraRelayOn);
            if (cameraRelay != null)
            {
                SelectedCameraRelay = CameraRelayOptions.FirstOrDefault(o => o.Value == (int)cameraRelay.Value);
            }

            var relayPin = await _parameterService.GetParameterAsync(ParamCameraRelayPin);
            if (relayPin != null)
            {
                CameraRelayPin = relayPin.Value;
            }

            var triggerType = await _parameterService.GetParameterAsync(ParamCameraType);
            if (triggerType != null)
            {
                SelectedCameraTriggerType = CameraTriggerTypeOptions.FirstOrDefault(o => o.Value == (int)triggerType.Value);
            }

            var servoOn = await _parameterService.GetParameterAsync(ParamCameraServoOn);
            if (servoOn != null)
            {
                CameraServoOn = servoOn.Value;
            }

            var servoOff = await _parameterService.GetParameterAsync(ParamCameraServoOff);
            if (servoOff != null)
            {
                CameraServoOff = servoOff.Value;
            }

            var triggerDist = await _parameterService.GetParameterAsync(ParamCameraTriggerDistance);
            if (triggerDist != null)
            {
                CameraTriggerDistance = triggerDist.Value;
            }

            var duration = await _parameterService.GetParameterAsync(ParamCameraDuration);
            if (duration != null)
            {
                CameraTriggerDuration = duration.Value;
            }

            // Load gimbal tilt settings
            var gimbalMode = await _parameterService.GetParameterAsync(ParamMntDefltMode);
            if (gimbalMode != null)
            {
                SelectedGimbalControlMode = GimbalControlModeOptions.FirstOrDefault(o => o.Value == (int)gimbalMode.Value);
            }

            var tiltMin = await _parameterService.GetParameterAsync(ParamMntAngMinTil);
            if (tiltMin != null)
            {
                GimbalTiltMin = tiltMin.Value;
            }

            var tiltMax = await _parameterService.GetParameterAsync(ParamMntAngMaxTil);
            if (tiltMax != null)
            {
                GimbalTiltMax = tiltMax.Value;
            }

            var rcTiltChannel = await _parameterService.GetParameterAsync(ParamMntRcInTilt);
            if (rcTiltChannel != null)
            {
                SelectedRcTiltChannel = RcChannelOptions.FirstOrDefault(o => o.Value == (int)rcTiltChannel.Value);
            }

            // Load output channel settings (using SERVO9 as default gimbal output)
            SelectedOutputChannel = OutputChannelOptions.FirstOrDefault(o => o.Value == 9);

            var servo9Function = await _parameterService.GetParameterAsync(ParamServo9Function);
            if (servo9Function != null)
            {
                // Function 6 = Mount Tilt Servo
                SelectedOutputType = servo9Function.Value == 6 ? OutputTypeOptions.FirstOrDefault(o => o.Value == 1) : OutputTypeOptions.First();
            }

            var servo9Min = await _parameterService.GetParameterAsync(ParamServo9Min);
            if (servo9Min != null)
            {
                PwmMin = servo9Min.Value;
            }

            var servo9Max = await _parameterService.GetParameterAsync(ParamServo9Max);
            if (servo9Max != null)
            {
                PwmMax = servo9Max.Value;
            }

            var servo9Trim = await _parameterService.GetParameterAsync(ParamServo9Trim);
            if (servo9Trim != null)
            {
                PwmNeutral = servo9Trim.Value;
            }

            var servo9Reversed = await _parameterService.GetParameterAsync(ParamServo9Reversed);
            if (servo9Reversed != null)
            {
                ReverseOutput = servo9Reversed.Value == 1;
            }

            HasUnsavedChanges = false;
            StatusMessage = "Camera configuration loaded successfully.";
            _logger.LogInformation("Camera configuration loaded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading camera parameters");
            StatusMessage = $"Error loading parameters: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSingleParameterAsync(string paramName)
    {
        try
        {
            switch (paramName)
            {
                case ParamCameraRelayOn:
                    var relay = await _parameterService.GetParameterAsync(paramName);
                    if (relay != null)
                        SelectedCameraRelay = CameraRelayOptions.FirstOrDefault(o => o.Value == (int)relay.Value);
                    break;

                case ParamCameraRelayPin:
                    var pin = await _parameterService.GetParameterAsync(paramName);
                    if (pin != null)
                        CameraRelayPin = pin.Value;
                    break;

                case ParamCameraType:
                    var type = await _parameterService.GetParameterAsync(paramName);
                    if (type != null)
                        SelectedCameraTriggerType = CameraTriggerTypeOptions.FirstOrDefault(o => o.Value == (int)type.Value);
                    break;

                case ParamCameraServoOn:
                    var servoOn = await _parameterService.GetParameterAsync(paramName);
                    if (servoOn != null)
                        CameraServoOn = servoOn.Value;
                    break;

                case ParamCameraServoOff:
                    var servoOff = await _parameterService.GetParameterAsync(paramName);
                    if (servoOff != null)
                        CameraServoOff = servoOff.Value;
                    break;

                case ParamCameraTriggerDistance:
                    var dist = await _parameterService.GetParameterAsync(paramName);
                    if (dist != null)
                        CameraTriggerDistance = dist.Value;
                    break;

                case ParamCameraDuration:
                    var dur = await _parameterService.GetParameterAsync(paramName);
                    if (dur != null)
                        CameraTriggerDuration = dur.Value;
                    break;

                case ParamMntDefltMode:
                    var mode = await _parameterService.GetParameterAsync(paramName);
                    if (mode != null)
                        SelectedGimbalControlMode = GimbalControlModeOptions.FirstOrDefault(o => o.Value == (int)mode.Value);
                    break;

                case ParamMntAngMinTil:
                    var min = await _parameterService.GetParameterAsync(paramName);
                    if (min != null)
                        GimbalTiltMin = min.Value;
                    break;

                case ParamMntAngMaxTil:
                    var max = await _parameterService.GetParameterAsync(paramName);
                    if (max != null)
                        GimbalTiltMax = max.Value;
                    break;

                case ParamMntRcInTilt:
                    var rcChannel = await _parameterService.GetParameterAsync(paramName);
                    if (rcChannel != null)
                        SelectedRcTiltChannel = RcChannelOptions.FirstOrDefault(o => o.Value == (int)rcChannel.Value);
                    break;

                case ParamServo9Function:
                    var func = await _parameterService.GetParameterAsync(paramName);
                    if (func != null)
                        SelectedOutputType = func.Value == 6 ? OutputTypeOptions.FirstOrDefault(o => o.Value == 1) : OutputTypeOptions.First();
                    break;

                case ParamServo9Min:
                    var min9 = await _parameterService.GetParameterAsync(paramName);
                    if (min9 != null)
                        PwmMin = min9.Value;
                    break;

                case ParamServo9Max:
                    var max9 = await _parameterService.GetParameterAsync(paramName);
                    if (max9 != null)
                        PwmMax = max9.Value;
                    break;

                case ParamServo9Trim:
                    var trim = await _parameterService.GetParameterAsync(paramName);
                    if (trim != null)
                        PwmNeutral = trim.Value;
                    break;

                case ParamServo9Reversed:
                    var rev = await _parameterService.GetParameterAsync(paramName);
                    if (rev != null)
                        ReverseOutput = rev.Value == 1;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading parameter {ParamName}", paramName);
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnSelectedCameraTriggerTypeChanged(CameraTriggerTypeOption? value)
    {
        OnPropertyChanged(nameof(IsServoMode));
        HasUnsavedChanges = true;
    }

    partial void OnSelectedCameraRelayChanged(CameraRelayOption? value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnCameraRelayPinChanged(float value)
    {
        ValidateCameraRelayPin();
        HasUnsavedChanges = true;
    }

    partial void OnCameraServoOnChanged(float value)
    {
        ValidateCameraServoOn();
        HasUnsavedChanges = true;
    }

    partial void OnCameraServoOffChanged(float value)
    {
        ValidateCameraServoOff();
        HasUnsavedChanges = true;
    }

    partial void OnCameraTriggerDistanceChanged(float value)
    {
        ValidateCameraTriggerDistance();
        HasUnsavedChanges = true;
    }

    partial void OnCameraTriggerDurationChanged(float value)
    {
        ValidateCameraTriggerDuration();
        HasUnsavedChanges = true;
    }

    partial void OnSelectedGimbalControlModeChanged(GimbalControlModeOption? value)
    {
        OnPropertyChanged(nameof(IsGimbalEnabled));
        OnPropertyChanged(nameof(IsFixedAngleMode));
        OnPropertyChanged(nameof(IsRcControlledMode));
        HasUnsavedChanges = true;
    }

    partial void OnFixedTiltAngleChanged(float value)
    {
        ValidateFixedTiltAngle();
        HasUnsavedChanges = true;
    }

    partial void OnGimbalTiltMinChanged(float value)
    {
        ValidateGimbalTiltMin();
        HasUnsavedChanges = true;
    }

    partial void OnGimbalTiltMaxChanged(float value)
    {
        ValidateGimbalTiltMax();
        HasUnsavedChanges = true;
    }

    partial void OnSelectedRcTiltChannelChanged(RcChannelOption? value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnSelectedOutputChannelChanged(OutputChannelOption? value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnSelectedOutputTypeChanged(OutputTypeOption? value)
    {
        OnPropertyChanged(nameof(IsPwmServoMode));
        HasUnsavedChanges = true;
    }

    partial void OnPwmMinChanged(float value)
    {
        ValidatePwmMin();
        HasUnsavedChanges = true;
    }

    partial void OnPwmMaxChanged(float value)
    {
        ValidatePwmMax();
        HasUnsavedChanges = true;
    }

    partial void OnPwmNeutralChanged(float value)
    {
        ValidatePwmNeutral();
        HasUnsavedChanges = true;
    }

    partial void OnReverseOutputChanged(bool value)
    {
        HasUnsavedChanges = true;
    }

    #endregion

    #region Validation

    private void ValidateCameraRelayPin()
    {
        if (CameraRelayPin < -1 || CameraRelayPin > 50)
        {
            CameraRelayPinError = "Value must be between -1 and 50";
        }
        else
        {
            CameraRelayPinError = null;
        }
    }

    private void ValidateCameraServoOn()
    {
        if (CameraServoOn < 1000 || CameraServoOn > 2000)
        {
            CameraServoOnError = "PWM value must be between 1000 and 2000";
        }
        else
        {
            CameraServoOnError = null;
        }
    }

    private void ValidateCameraServoOff()
    {
        if (CameraServoOff < 1000 || CameraServoOff > 2000)
        {
            CameraServoOffError = "PWM value must be between 1000 and 2000";
        }
        else
        {
            CameraServoOffError = null;
        }
    }

    private void ValidateCameraTriggerDistance()
    {
        if (CameraTriggerDistance < 0 || CameraTriggerDistance > 1000)
        {
            CameraTriggerDistanceError = "Value must be between 0 and 1000 meters";
        }
        else
        {
            CameraTriggerDistanceError = null;
        }
    }

    private void ValidateCameraTriggerDuration()
    {
        if (CameraTriggerDuration < 0 || CameraTriggerDuration > 50)
        {
            CameraTriggerDurationError = "Value must be between 0 and 50 deciseconds";
        }
        else
        {
            CameraTriggerDurationError = null;
        }
    }

    private void ValidateFixedTiltAngle()
    {
        if (FixedTiltAngle < -90 || FixedTiltAngle > 0)
        {
            FixedTiltAngleError = "Angle must be between -90 and 0 degrees";
        }
        else
        {
            FixedTiltAngleError = null;
        }
    }

    private void ValidateGimbalTiltMin()
    {
        if (GimbalTiltMin < -90 || GimbalTiltMin > 0)
        {
            GimbalTiltMinError = "Min angle must be between -90 and 0 degrees";
        }
        else
        {
            GimbalTiltMinError = null;
        }
    }

    private void ValidateGimbalTiltMax()
    {
        if (GimbalTiltMax < -90 || GimbalTiltMax > 0)
        {
            GimbalTiltMaxError = "Max angle must be between -90 and 0 degrees";
        }
        else
        {
            GimbalTiltMaxError = null;
        }
    }

    private void ValidatePwmMin()
    {
        if (PwmMin < 1000 || PwmMin > 2000)
        {
            PwmMinError = "PWM Min must be between 1000 and 2000";
        }
        else
        {
            PwmMinError = null;
        }
    }

    private void ValidatePwmMax()
    {
        if (PwmMax < 1000 || PwmMax > 2000)
        {
            PwmMaxError = "PWM Max must be between 1000 and 2000";
        }
        else
        {
            PwmMaxError = null;
        }
    }

    private void ValidatePwmNeutral()
    {
        if (PwmNeutral < 1000 || PwmNeutral > 2000)
        {
            PwmNeutralError = "PWM Neutral must be between 1000 and 2000";
        }
        else
        {
            PwmNeutralError = null;
        }
    }

    private bool ValidateAll()
    {
        ValidateCameraRelayPin();
        ValidateCameraServoOn();
        ValidateCameraServoOff();
        ValidateCameraTriggerDistance();
        ValidateCameraTriggerDuration();
        ValidateFixedTiltAngle();
        ValidateGimbalTiltMin();
        ValidateGimbalTiltMax();
        ValidatePwmMin();
        ValidatePwmMax();
        ValidatePwmNeutral();

        return CameraRelayPinError == null &&
               CameraServoOnError == null &&
               CameraServoOffError == null &&
               CameraTriggerDistanceError == null &&
               CameraTriggerDurationError == null &&
               FixedTiltAngleError == null &&
               GimbalTiltMinError == null &&
               GimbalTiltMaxError == null &&
               PwmMinError == null &&
               PwmMaxError == null &&
               PwmNeutralError == null;
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task SaveParametersAsync()
    {
        if (!IsPageEnabled || IsBusy)
            return;

        if (!ValidateAll())
        {
            StatusMessage = "Please fix validation errors before saving";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Saving camera parameters...";

            var success = true;

            // Save camera settings
            if (SelectedCameraRelay != null)
                success &= await _parameterService.SetParameterAsync(ParamCameraRelayOn, SelectedCameraRelay.Value);

            success &= await _parameterService.SetParameterAsync(ParamCameraRelayPin, CameraRelayPin);

            if (SelectedCameraTriggerType != null)
                success &= await _parameterService.SetParameterAsync(ParamCameraType, SelectedCameraTriggerType.Value);

            success &= await _parameterService.SetParameterAsync(ParamCameraServoOn, CameraServoOn);
            success &= await _parameterService.SetParameterAsync(ParamCameraServoOff, CameraServoOff);
            success &= await _parameterService.SetParameterAsync(ParamCameraTriggerDistance, CameraTriggerDistance);
            success &= await _parameterService.SetParameterAsync(ParamCameraDuration, CameraTriggerDuration);

            // Save gimbal tilt settings
            if (SelectedGimbalControlMode != null)
                success &= await _parameterService.SetParameterAsync(ParamMntDefltMode, SelectedGimbalControlMode.Value);

            success &= await _parameterService.SetParameterAsync(ParamMntAngMinTil, GimbalTiltMin);
            success &= await _parameterService.SetParameterAsync(ParamMntAngMaxTil, GimbalTiltMax);

            if (SelectedRcTiltChannel != null)
                success &= await _parameterService.SetParameterAsync(ParamMntRcInTilt, SelectedRcTiltChannel.Value);

            // Save output channel settings (SERVO9)
            if (SelectedOutputType != null)
            {
                // Function 6 = Mount Tilt Servo, 0 = Disabled
                var function = SelectedOutputType.Value == 1 ? 6 : 0;
                success &= await _parameterService.SetParameterAsync(ParamServo9Function, function);
            }

            success &= await _parameterService.SetParameterAsync(ParamServo9Min, PwmMin);
            success &= await _parameterService.SetParameterAsync(ParamServo9Max, PwmMax);
            success &= await _parameterService.SetParameterAsync(ParamServo9Trim, PwmNeutral);
            success &= await _parameterService.SetParameterAsync(ParamServo9Reversed, ReverseOutput ? 1 : 0);

            if (success)
            {
                HasUnsavedChanges = false;
                StatusMessage = "Camera parameters saved successfully";
                _logger.LogInformation("Camera parameters saved");
            }
            else
            {
                StatusMessage = "Failed to save some parameters";
                _logger.LogWarning("Failed to save some camera parameters");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving camera parameters");
            StatusMessage = $"Error saving parameters: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        if (!IsPageEnabled || IsBusy)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = "Resetting to defaults...";

            // Camera defaults
            SelectedCameraRelay = CameraRelayOptions.First();
            CameraRelayPin = -1;
            SelectedCameraTriggerType = CameraTriggerTypeOptions.First();
            CameraServoOn = 1900;
            CameraServoOff = 1100;
            CameraTriggerDistance = 0;
            CameraTriggerDuration = 10;

            // Gimbal tilt defaults
            SelectedGimbalControlMode = GimbalControlModeOptions.First();
            FixedTiltAngle = -45;
            SelectedRcTiltChannel = RcChannelOptions.FirstOrDefault(o => o.Value == 6);
            GimbalTiltMin = -90;
            GimbalTiltMax = 0;

            // Output channel defaults
            SelectedOutputChannel = OutputChannelOptions.FirstOrDefault(o => o.Value == 9);
            SelectedOutputType = OutputTypeOptions.First();
            PwmMin = 1000;
            PwmMax = 2000;
            PwmNeutral = 1500;
            ReverseOutput = false;

            HasUnsavedChanges = true;
            StatusMessage = "Reset to defaults. Click Save to apply.";
            _logger.LogInformation("Camera parameters reset to defaults");
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsPageEnabled || IsBusy)
            return;

        await LoadParametersAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _parameterService.ParameterDownloadProgressChanged -= OnParameterDownloadProgressChanged;
            _parameterService.ParameterUpdated -= OnParameterUpdated;
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
        }
        base.Dispose(disposing);
    }

    #endregion
}

#region Option Models

public record CameraRelayOption(int Value, string Label);
public record CameraTriggerTypeOption(int Value, string Label);
public record GimbalControlModeOption(int Value, string Label);
public record RcChannelOption(int Value, string Label);
public record OutputChannelOption(int Value, string Label);
public record OutputTypeOption(int Value, string Label);

#endregion
