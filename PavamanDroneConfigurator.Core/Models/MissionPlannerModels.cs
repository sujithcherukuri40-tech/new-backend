namespace PavamanDroneConfigurator.Core.Models.MissionPlanner;

public enum MavFrame
{
    GlobalInt = 0,
    GlobalRelAlt = 3,
    GlobalAbsAlt = 6
}

public enum LandMode
{
    Normal = 0,
    Precision = 1
}

public enum YawBehaviour
{
    FixedForward = 0,
    FollowPath = 1,
    Free = 2
}

public enum UploadStatus
{
    Idle = 0,
    InProgress = 1,
    Success = 2,
    Failed = 3
}

public enum Severity
{
    Hard,
    Soft
}

public record GeoPoint(double Lat, double Lon, float AltM = 0f);

public record Obstacle(GeoPoint Centre, float RadiusM, float HeightM);

public record ValidationResult(
    string RuleId,
    Severity Severity,
    string Message,
    int? AffectedSeq,
    Action? QuickFix
);

public abstract record MissionItem
{
    public int Seq { get; init; }
    public MavFrame Frame { get; init; } = MavFrame.GlobalRelAlt;
    public abstract ushort Command { get; }
    public double Lat { get; init; }
    public double Lon { get; init; }
    public float AltM { get; init; }
}

public record Takeoff : MissionItem
{
    public Takeoff(int seq, double lat, double lon, float altM)
    {
        Seq = seq;
        Lat = lat;
        Lon = lon;
        AltM = altM;
    }

    public override ushort Command => 22;
}

public record Waypoint : MissionItem
{
    public Waypoint(
        int seq,
        double lat,
        double lon,
        float altM,
        float acceptanceRadiusM,
        float yawDeg,
        float holdTimeSec,
        float speedMs)
    {
        Seq = seq;
        Lat = lat;
        Lon = lon;
        AltM = altM;
        AcceptanceRadiusM = acceptanceRadiusM;
        YawDeg = yawDeg;
        HoldTimeSec = holdTimeSec;
        SpeedMs = speedMs;
    }

    public float AcceptanceRadiusM { get; init; }
    public float YawDeg { get; init; }
    public float HoldTimeSec { get; init; }
    public float SpeedMs { get; init; }
    public override ushort Command => 16;
}

public record Loiter : MissionItem
{
    public Loiter(int seq, double lat, double lon, float altM, float radiusM, int turns)
    {
        Seq = seq;
        Lat = lat;
        Lon = lon;
        AltM = altM;
        RadiusM = radiusM;
        Turns = turns;
    }

    public float RadiusM { get; init; }
    public int Turns { get; init; }
    public override ushort Command => 19;
}

public record Land : MissionItem
{
    public Land(int seq, double lat, double lon, float altM, float abortAltM, LandMode landMode)
    {
        Seq = seq;
        Lat = lat;
        Lon = lon;
        AltM = altM;
        AbortAltM = abortAltM;
        LandMode = landMode;
    }

    public float AbortAltM { get; init; }
    public LandMode LandMode { get; init; }
    public override ushort Command => 21;
}

public record Roi : MissionItem
{
    public Roi(int seq, double lat, double lon, float altM)
    {
        Seq = seq;
        Lat = lat;
        Lon = lon;
        AltM = altM;
    }

    public override ushort Command => 195;
}

public record SurveyGrid : MissionItem
{
    public SurveyGrid(
        int seq,
        double lat,
        double lon,
        float altM,
        IReadOnlyList<GeoPoint> polygon,
        float laneSpacingM,
        float triggerDistM,
        float gimbalPitchDeg)
    {
        Seq = seq;
        Lat = lat;
        Lon = lon;
        AltM = altM;
        Polygon = polygon;
        LaneSpacingM = laneSpacingM;
        TriggerDistM = triggerDistM;
        GimbalPitchDeg = gimbalPitchDeg;
    }

    public IReadOnlyList<GeoPoint> Polygon { get; init; }
    public float LaneSpacingM { get; init; }
    public float TriggerDistM { get; init; }
    public float GimbalPitchDeg { get; init; }
    public override ushort Command => 16;
}

public record CameraTrigger : MissionItem
{
    public CameraTrigger(int seq, float distanceM)
    {
        Seq = seq;
        Lat = 0;
        Lon = 0;
        AltM = 0;
        DistanceM = distanceM;
    }

    public float DistanceM { get; init; }
    public override ushort Command => 206;
}

public record GimbalControl : MissionItem
{
    public GimbalControl(int seq, float pitchDeg, float yawDeg)
    {
        Seq = seq;
        Lat = 0;
        Lon = 0;
        AltM = 0;
        PitchDeg = pitchDeg;
        YawDeg = yawDeg;
    }

    public float PitchDeg { get; init; }
    public float YawDeg { get; init; }
    public override ushort Command => 205;
}

public record PlannerSettings
{
    public float DefaultAltM { get; init; } = 50f;
    public float CruiseSpeedMs { get; init; } = 10f;
    public float RtlAltM { get; init; } = 30f;
    public float MaxAltM { get; init; } = 120f;
    public MavFrame Frame { get; init; } = MavFrame.GlobalRelAlt;
    public float AcceptanceRadiusM { get; init; } = 5f;
    public YawBehaviour YawBehaviour { get; init; } = YawBehaviour.FixedForward;
    public float CameraTrigDistM { get; init; } = 0f;
    public float GimbalPitchDeg { get; init; } = -90f;
    public int MinBatteryPct { get; init; } = 20;
    public int MinSatCount { get; init; } = 8;
}

public record MissionState
{
    public GeoPoint Home { get; init; } = new(0, 0, 0);
    public IReadOnlyList<MissionItem> Items { get; init; } = [];
    public IReadOnlyList<GeoPoint> Fence { get; init; } = [];
    public IReadOnlyList<Obstacle> Obstacles { get; init; } = [];
    public PlannerSettings Settings { get; init; } = new();
    public UploadStatus UploadStatus { get; init; } = UploadStatus.Idle;
    public IReadOnlyList<ValidationResult> ValidationResults { get; init; } = [];
    /// <summary>
    /// Indicates mission state has unsaved changes.
    /// </summary>
    public bool Dirty { get; init; }
}
