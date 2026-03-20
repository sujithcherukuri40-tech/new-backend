using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.Infrastructure.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Repositories;

/// <summary>
/// Repository for ArduPilot parameter metadata.
/// Supports loading from ArduPilot XML files (Copter and Plane) or fallback to built-in metadata.
/// </summary>
public class ParameterMetadataRepository : IParameterMetadataRepository
{
    private readonly ILogger<ParameterMetadataRepository> _logger;
    private readonly ArduPilotXmlParser _xmlParser;
    private readonly ArduPilotMetadataDownloader _downloader;
    private Dictionary<string, ParameterMetadata> _metadata;
    private VehicleType _currentVehicleType = VehicleType.Copter;

    public ParameterMetadataRepository(
        ILogger<ParameterMetadataRepository> logger,
        ArduPilotXmlParser xmlParser,
        ArduPilotMetadataDownloader downloader)
    {
        _logger = logger;
        _xmlParser = xmlParser;
        _downloader = downloader;
        _metadata = new Dictionary<string, ParameterMetadata>();

        // Load default metadata (Copter) on construction
        _ = Task.Run(async () => await LoadMetadataAsync(VehicleType.Copter));
    }

    /// <summary>
    /// Loads parameter metadata for specified vehicle type.
    /// Downloads from ArduPilot GitHub or uses cached version.
    /// Falls back to built-in metadata if download fails.
    /// </summary>
    public async Task LoadMetadataAsync(VehicleType vehicleType)
    {
        // Only support Copter and Plane for now
        if (vehicleType != VehicleType.Copter && vehicleType != VehicleType.Plane)
        {
            _logger.LogWarning("Vehicle type {VehicleType} not yet supported, falling back to Copter", vehicleType);
            vehicleType = VehicleType.Copter;
        }

        _currentVehicleType = vehicleType;
        _logger.LogInformation("Loading parameter metadata for {VehicleType}", vehicleType);

        try
        {
            // Try to load from cache first
            var xmlContent = await _downloader.LoadFromCacheAsync(vehicleType);
            var cacheAge = _downloader.GetCacheAge(vehicleType);

            // If cache is missing or older than 7 days, try to download
            if (xmlContent == null || (cacheAge.HasValue && cacheAge.Value.TotalDays > 7))
            {
                _logger.LogInformation("Cache is {Status}, attempting download", 
                    xmlContent == null ? "missing" : $"old ({cacheAge.Value.Days} days)");

                var downloaded = await _downloader.DownloadXmlAsync(vehicleType);
                if (downloaded != null)
                {
                    xmlContent = downloaded;
                    _logger.LogInformation("Successfully downloaded latest parameter metadata");
                }
            }

            // Parse XML if we have it
            if (xmlContent != null)
            {
                _metadata = _xmlParser.ParseXml(xmlContent);
                _logger.LogInformation("Loaded {Count} parameters from XML for {VehicleType}", 
                    _metadata.Count, vehicleType);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading parameter metadata from XML");
        }

        // Fallback #1: bundled metadata shipped with app (Assets/ParameterMetadata.xml)
        try
        {
            var localMetadata = MetadataLoader.Load("ParameterMetadata.xml");
            if (localMetadata.Count > 0)
            {
                _metadata = new Dictionary<string, ParameterMetadata>(localMetadata, StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("Loaded {Count} parameters from bundled local metadata", _metadata.Count);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading bundled local parameter metadata");
        }

        // Fallback #2: built-in minimal metadata
        _logger.LogWarning("Falling back to built-in parameter metadata");
        _metadata = BuildFallbackMetadata();
    }

    public ParameterMetadata? GetByName(string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName)) return null;
        _metadata.TryGetValue(parameterName.ToUpperInvariant(), out var meta);
        return meta;
    }

    public IEnumerable<ParameterMetadata> GetAll() => _metadata.Values;

    public IEnumerable<ParameterMetadata> GetByGroup(string group)
    {
        return _metadata.Values.Where(m => string.Equals(m.Group, group, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> GetAllGroups()
    {
        return _metadata.Values
            .Where(m => !string.IsNullOrEmpty(m.Group))
            .Select(m => m.Group!)
            .Distinct()
            .OrderBy(g => g);
    }

    public bool Exists(string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName)) return false;
        return _metadata.ContainsKey(parameterName.ToUpperInvariant());
    }

    public int GetCount() => _metadata.Count;

    /// <summary>
    /// Gets current vehicle type.
    /// </summary>
    public VehicleType GetCurrentVehicleType() => _currentVehicleType;

    /// <summary>
    /// Fallback metadata with essential parameters (used when XML download fails).
    /// </summary>
    private Dictionary<string, ParameterMetadata> BuildFallbackMetadata()
    {
        var db = new Dictionary<string, ParameterMetadata>(StringComparer.OrdinalIgnoreCase);
        
        // Essential ATC parameters for basic PID tuning
        Add(db, "ATC_ANG_RLL_P", "Roll Angle P", "Attitude Control", "Roll angle controller P gain", 3, 12, 4.5f);
        Add(db, "ATC_ANG_PIT_P", "Pitch Angle P", "Attitude Control", "Pitch angle controller P gain", 3, 12, 4.5f);
        Add(db, "ATC_ANG_YAW_P", "Yaw Angle P", "Attitude Control", "Yaw angle controller P gain", 3, 12, 4.5f);
        Add(db, "ATC_RAT_RLL_P", "Roll Rate P", "Attitude Control", "Roll rate P gain", 0.01f, 0.5f, 0.135f);
        Add(db, "ATC_RAT_RLL_I", "Roll Rate I", "Attitude Control", "Roll rate I gain", 0.01f, 2, 0.135f);
        Add(db, "ATC_RAT_RLL_D", "Roll Rate D", "Attitude Control", "Roll rate D gain", 0, 0.05f, 0.0036f);
        Add(db, "ATC_RAT_PIT_P", "Pitch Rate P", "Attitude Control", "Pitch rate P gain", 0.01f, 0.5f, 0.135f);
        Add(db, "ATC_RAT_PIT_I", "Pitch Rate I", "Attitude Control", "Pitch rate I gain", 0.01f, 2, 0.135f);
        Add(db, "ATC_RAT_PIT_D", "Pitch Rate D", "Attitude Control", "Pitch rate D gain", 0, 0.05f, 0.0036f);
        Add(db, "ATC_RAT_YAW_P", "Yaw Rate P", "Attitude Control", "Yaw rate P gain", 0.01f, 1, 0.18f);
        Add(db, "ATC_RAT_YAW_I", "Yaw Rate I", "Attitude Control", "Yaw rate I gain", 0.01f, 0.5f, 0.018f);
        
        // Battery parameters
        Add(db, "BATT_CAPACITY", "Battery Capacity", "Battery", "Battery capacity in mAh", 0, 100000, 3300, "mAh");
        Add(db, "BATT_LOW_VOLT", "Low Battery Voltage", "Battery", "Voltage that triggers low battery warning", 0, 120, 10.5f, "V");
        Add(db, "BATT_CRT_VOLT", "Critical Battery Voltage", "Battery", "Voltage that triggers critical battery failsafe", 0, 120, 0, "V");
        
        _logger.LogInformation("Built fallback metadata with {Count} essential parameters", db.Count);
        return db;
    }

    private static void Add(Dictionary<string, ParameterMetadata> db, string name, string displayName, string group, string description, float? min = null, float? max = null, float? defaultVal = null, string? units = null, Dictionary<int, string>? values = null)
    {
        db[name] = new ParameterMetadata 
        { 
            Name = name, 
            DisplayName = displayName, 
            Description = description, 
            Group = group, 
            MinValue = min, 
            MaxValue = max, 
            DefaultValue = defaultVal ?? 0, 
            Units = units, 
            Values = values, 
            Range = min.HasValue && max.HasValue ? $"{min} - {max}" : null 
        };
    }
}
