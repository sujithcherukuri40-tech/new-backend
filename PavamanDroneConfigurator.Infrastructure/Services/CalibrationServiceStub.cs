using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Stub implementation of ICalibrationService to prevent application crashes.
/// TODO: Implement full calibration service functionality.
/// This is a temporary placeholder that returns false for all calibration operations
/// and logs warnings when methods are called.
/// </summary>
public class CalibrationServiceStub : ICalibrationService
{
    private readonly ILogger<CalibrationServiceStub> _logger;

    public CalibrationServiceStub(ILogger<CalibrationServiceStub> logger)
    {
        _logger = logger;
        _logger.LogWarning("CalibrationServiceStub initialized - calibration functionality is NOT available");
    }

    public CalibrationStateModel? CurrentState => null;
    public bool IsCalibrating => false;
    public CalibrationStateMachine StateMachineState => CalibrationStateMachine.Idle;
    public CalibrationDiagnostics? CurrentDiagnostics => null;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;

    public Task<bool> StartCalibrationAsync(CalibrationType type)
    {
        _logger.LogWarning("Calibration service stub called: StartCalibrationAsync({Type}) - returning false", type);
        return Task.FromResult(false);
    }

    public Task<bool> CancelCalibrationAsync()
    {
        _logger.LogWarning("Calibration service stub called: CancelCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> AcceptCalibrationStepAsync()
    {
        _logger.LogWarning("Calibration service stub called: AcceptCalibrationStepAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        _logger.LogWarning("Calibration service stub called: StartAccelerometerCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartSimpleAccelerometerCalibrationAsync()
    {
        _logger.LogWarning("Calibration service stub called: StartSimpleAccelerometerCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        _logger.LogWarning("Calibration service stub called: StartCompassCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartGyroscopeCalibrationAsync()
    {
        _logger.LogWarning("Calibration service stub called: StartGyroscopeCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartLevelHorizonCalibrationAsync()
    {
        _logger.LogWarning("Calibration service stub called: StartLevelHorizonCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartBarometerCalibrationAsync()
    {
        _logger.LogWarning("Calibration service stub called: StartBarometerCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> StartAirspeedCalibrationAsync()
    {
        _logger.LogWarning("Calibration service stub called: StartAirspeedCalibrationAsync - returning false");
        return Task.FromResult(false);
    }

    public Task<bool> RebootFlightControllerAsync()
    {
        _logger.LogWarning("Calibration service stub called: RebootFlightControllerAsync - returning false");
        return Task.FromResult(false);
    }
}
