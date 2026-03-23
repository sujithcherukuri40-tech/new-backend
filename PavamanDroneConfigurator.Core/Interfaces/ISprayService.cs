using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for spray system control and monitoring
/// </summary>
public interface ISprayService
{
    /// <summary>
    /// Current spray configuration
    /// </summary>
    SprayConfig Config { get; }
    
    /// <summary>
    /// Current spray state
    /// </summary>
    SprayState State { get; }
    
    /// <summary>
    /// Whether spray pump is currently active
    /// </summary>
    bool IsPumpActive { get; }
    
    /// <summary>
    /// Event raised when spray state changes
    /// </summary>
    event EventHandler<SprayStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// Event raised when tank level is low (<15%)
    /// </summary>
    event EventHandler? TankLowWarning;
    
    /// <summary>
    /// Event raised when tank level is critical (<5%)
    /// </summary>
    event EventHandler? TankCriticalWarning;
    
    /// <summary>
    /// Initialize spray system with configuration
    /// </summary>
    Task InitializeAsync(SprayConfig config, CancellationToken ct = default);
    
    /// <summary>
    /// Update spray configuration
    /// </summary>
    Task UpdateConfigAsync(SprayConfig config, CancellationToken ct = default);
    
    /// <summary>
    /// Activate spray pump
    /// </summary>
    Task SetPumpOnAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Deactivate spray pump
    /// </summary>
    Task SetPumpOffAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Set pump speed (0-100%)
    /// </summary>
    Task SetPumpSpeedAsync(double percentage, CancellationToken ct = default);
    
    /// <summary>
    /// Prime nozzles (short pump burst)
    /// </summary>
    Task PrimeNozzlesAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Configure spray parameters on flight controller
    /// </summary>
    Task ConfigureSprayParamsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Calculate required flow rate for given parameters
    /// Formula: FlowRate (L/min) = (AppRate × Speed × Width) / 166.67
    /// </summary>
    double CalculateRequiredFlowRate(double appRateLPerHa, double speedMs, double widthM);
    
    /// <summary>
    /// Estimate coverage from current tank level
    /// </summary>
    SprayCoverageEstimate EstimateCoverage();
    
    /// <summary>
    /// Set current tank level manually
    /// </summary>
    void SetTankLevel(double liters);
    
    /// <summary>
    /// Reset total sprayed counter
    /// </summary>
    void ResetSprayedCounter();
    
    /// <summary>
    /// Start flow rate calibration
    /// </summary>
    Task StartCalibrationAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Complete flow rate calibration with measured volume
    /// </summary>
    Task CompleteCalibrationAsync(double measuredLiters, TimeSpan duration, CancellationToken ct = default);
}
