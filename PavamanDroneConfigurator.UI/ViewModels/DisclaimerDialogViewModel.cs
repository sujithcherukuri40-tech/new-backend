using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the disclaimer dialog shown before parameter changes or calibration.
/// </summary>
public partial class DisclaimerDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Important Notice";

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _warningText = string.Empty;

    [ObservableProperty]
    private bool _isCalibrationDisclaimer;

    [ObservableProperty]
    private bool _isParameterDisclaimer;

    [ObservableProperty]
    private bool _userAcknowledged;

    [ObservableProperty]
    private string _confirmButtonText = "I Understand, Proceed";

    [ObservableProperty]
    private string _cancelButtonText = "Cancel";

    public bool? DialogResult { get; private set; }

    /// <summary>
    /// Creates a disclaimer for parameter changes.
    /// </summary>
    public static DisclaimerDialogViewModel CreateForParameterChange(int parameterCount)
    {
        var vm = new DisclaimerDialogViewModel();
        vm._title = "Parameter Change Warning";
        vm._isParameterDisclaimer = true;
        vm._isCalibrationDisclaimer = false;
        vm._message = $"You are about to modify {parameterCount} parameter(s) on your flight controller.\n\n" +
                      "Incorrect parameter values can cause:\n" +
                      "• Unstable flight behavior\n" +
                      "• Loss of vehicle control\n" +
                      "• Damage to the vehicle or property\n" +
                      "• Personal injury";
        vm._warningText = "IMPORTANT: Always verify parameter values before flight. " +
                          "Test changes in a safe environment. " +
                          "Pavaman Drones is not responsible for any damage or injury " +
                          "resulting from parameter modifications.";
        vm._confirmButtonText = "I Understand the Risks, Apply Changes";
        vm._cancelButtonText = "Cancel";
        return vm;
    }

    /// <summary>
    /// Creates a disclaimer for calibration operations.
    /// </summary>
    public static DisclaimerDialogViewModel CreateForCalibration(string calibrationType)
    {
        var specificWarnings = calibrationType.ToLowerInvariant() switch
        {
            "accelerometer" or "accel" => 
                "• Keep the vehicle stationary during each position\n" +
                "• Ensure the surface is level and stable\n" +
                "• Do not interrupt the calibration process",
            "compass" => 
                "• Move away from metal objects and electronic devices\n" +
                "• Rotate the vehicle slowly and smoothly\n" +
                "• Complete all rotation axes as instructed",
            "level" or "level horizon" => 
                "• Place the vehicle on a perfectly level surface\n" +
                "• Do not touch the vehicle during calibration\n" +
                "• Ensure the vehicle is in its normal flight attitude",
            "barometer" or "pressure" => 
                "• Keep the vehicle stationary\n" +
                "• Avoid sudden pressure changes (doors, wind)\n" +
                "• Wait for the calibration to complete",
            _ => 
                "• Follow all on-screen instructions carefully\n" +
                "• Do not interrupt the calibration process\n" +
                "• Ensure stable conditions during calibration"
        };

        var vm = new DisclaimerDialogViewModel();
        vm._title = $"{calibrationType} Calibration";
        vm._isCalibrationDisclaimer = true;
        vm._isParameterDisclaimer = false;
        vm._message = $"You are about to start {calibrationType} calibration.\n\n" +
                      "Important guidelines:\n" +
                      specificWarnings + "\n\n" +
                      "Improper calibration can result in:\n" +
                      "• Inaccurate sensor readings\n" +
                      "• Unstable flight behavior\n" +
                      "• Unexpected flight path deviations";
        vm._warningText = "WARNING: Calibration affects flight safety. " +
                          "Always perform a pre-flight check after calibration. " +
                          "Test in a safe, open area before normal operations. " +
                          "Pavaman Drones is not responsible for any issues arising from calibration procedures.";
        vm._confirmButtonText = "I Understand, Start Calibration";
        vm._cancelButtonText = "Cancel";
        return vm;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (_userAcknowledged)
        {
            DialogResult = true;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
