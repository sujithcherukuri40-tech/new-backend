namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Spray system configuration
/// </summary>
public class SprayConfig
{
    public double TankCapacityLiters { get; set; } = 16.0;
    public double ApplicationRateLPerHa { get; set; } = 10.0;
    public double SprayWidthMeters { get; set; } = 4.0;
    public int NozzleCount { get; set; } = 4;
    public string NozzleType { get; set; } = "Flat Fan";
    public string DropletSize { get; set; } = "Medium";
    
    // Pump configuration
    public string PumpType { get; set; } = "Brushless";
    public int PumpPwm { get; set; } = 1500;
    public int PumpChannel { get; set; } = 9; // RC9
    public int PumpRelayNumber { get; set; } = 0;
    
    // ArduPilot parameters
    public int SprayEnable { get; set; } = 1;
    public int SprayPumpRatePercent { get; set; } = 80;
    public int SpraySpinnerRpm { get; set; } = 1200;
    public double SpraySpeedMinMs { get; set; } = 1.0;
    
    // Target flow
    public double TargetFlowRateLPerMin { get; set; } = 1.0;
    public double TargetPressureBar { get; set; } = 3.0;
}

/// <summary>
/// Real-time spray telemetry state
/// </summary>
public class SprayState
{
    public bool IsPumpActive { get; set; }
    public double FlowRateLPerMin { get; set; }
    public double TankLevelLiters { get; set; }
    public double TankPercentage { get; set; }
    public double TotalSprayedLiters { get; set; }
    public double SprayPressureBar { get; set; }
    public double SprayCoverageHa { get; set; }
    public DateTime LastUpdateTime { get; set; }
}

/// <summary>
/// Coverage estimate based on tank level
/// </summary>
public class SprayCoverageEstimate
{
    public double AreaHectares { get; set; }
    public double DistanceKm { get; set; }
    public double TimeMinutes { get; set; }
    public double RemainingLiters { get; set; }
}

/// <summary>
/// Event args for spray state changes
/// </summary>
public class SprayStateChangedEventArgs : EventArgs
{
    public SprayState State { get; }
    
    public SprayStateChangedEventArgs(SprayState state)
    {
        State = state;
    }
}
