using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Metadata;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service for providing parameter metadata like Mission Planner.
/// Loads metadata from ArduPilot XML files.
/// </summary>
public class ParameterMetadataService : IParameterMetadataService
{
    private readonly Dictionary<string, ParameterMetadata> _metadata;

    public ParameterMetadataService()
    {
        _metadata = MetadataLoader.Load("ParameterMetadata.xml");
    }

    public ParameterMetadata? GetMetadata(string parameterName)
    {
        return _metadata.TryGetValue(parameterName, out var meta) ? meta : null;
    }

    public IEnumerable<ParameterMetadata> GetAllMetadata()
    {
        return _metadata.Values;
    }

    public IEnumerable<ParameterMetadata> GetParametersByGroup(string group)
    {
        return _metadata.Values.Where(m => m.Group == group);
    }

    public IEnumerable<string> GetGroups()
    {
        return _metadata.Values
            .Select(m => m.Group)
            .Where(g => !string.IsNullOrEmpty(g))
            .Distinct()
            .OrderBy(x => x);
    }

    public void EnrichParameter(DroneParameter parameter)
    {
        if (_metadata.TryGetValue(parameter.Name, out var meta))
        {
            parameter.Description = meta.Description;
            parameter.MinValue = meta.Min;
            parameter.MaxValue = meta.Max;
            parameter.DefaultValue = meta.DefaultValue;
            parameter.Units = meta.Units;
        }
    }

    public bool ValidateParameterValue(string parameterName, float value, out string? errorMessage)
    {
        errorMessage = null;

        if (!_metadata.TryGetValue(parameterName, out var meta))
            return true;

        if (meta.Min.HasValue && value < meta.Min.Value)
        {
            errorMessage = $"Value {value} is below minimum {meta.Min}";
            return false;
        }

        if (meta.Max.HasValue && value > meta.Max.Value)
        {
            errorMessage = $"Value {value} is above maximum {meta.Max}";
            return false;
        }

        return true;
    }

    public string GetValueDescription(string parameterName, float value)
    {
        if (_metadata.TryGetValue(parameterName, out var meta))
        {
            if (meta.Options != null && meta.Options.TryGetValue(value.ToString(), out var desc))
                return desc;
            
            if (meta.Values != null && meta.Values.TryGetValue((int)Math.Round(value), out var legacyDesc))
                return legacyDesc;
        }

        return value.ToString();
    }

    public bool HasMetadata(string parameterName)
    {
        return _metadata.ContainsKey(parameterName);
    }

    public ParameterMetadataStatistics GetStatistics()
    {
        var groups = _metadata.Values
            .Select(x => x.Group)
            .Where(g => !string.IsNullOrEmpty(g))
            .Distinct()
            .ToList();

        return new ParameterMetadataStatistics
        {
            TotalParameters = _metadata.Count,
            TotalGroups = groups.Count,
            GroupNames = groups,
            ParametersWithOptions = _metadata.Values.Count(x => 
                (x.Options != null && x.Options.Count > 0) || 
                (x.Values != null && x.Values.Count > 0)),
            ParametersWithRanges = _metadata.Values.Count(x => x.Min.HasValue || x.Max.HasValue),
        };
    }
}
