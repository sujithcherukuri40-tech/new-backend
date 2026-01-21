using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for providing parameter metadata like Mission Planner.
/// </summary>
public interface IParameterMetadataService
{
    /// <summary>
    /// Gets metadata for a specific parameter.
    /// </summary>
    ParameterMetadata? GetMetadata(string parameterName);

    /// <summary>
    /// Gets all available parameter metadata.
    /// </summary>
    IEnumerable<ParameterMetadata> GetAllMetadata();

    /// <summary>
    /// Gets parameters in a specific group/category.
    /// </summary>
    IEnumerable<ParameterMetadata> GetParametersByGroup(string group);

    /// <summary>
    /// Gets all parameter groups/categories.
    /// </summary>
    IEnumerable<string> GetGroups();

    /// <summary>
    /// Updates a DroneParameter with metadata (description, range, etc).
    /// </summary>
    void EnrichParameter(DroneParameter parameter);

    /// <summary>
    /// Validates a parameter value against its metadata constraints.
    /// </summary>
    bool ValidateParameterValue(string parameterName, float value, out string? errorMessage);

    /// <summary>
    /// Gets a user-friendly description for a parameter value.
    /// </summary>
    string GetValueDescription(string parameterName, float value);

    /// <summary>
    /// Checks if metadata exists for a parameter.
    /// </summary>
    bool HasMetadata(string parameterName);

    /// <summary>
    /// Gets statistics about the metadata repository.
    /// </summary>
    ParameterMetadataStatistics GetStatistics();
}
