using System.Text.Json;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Loads ArduPilot parameter metadata from official JSON files.
/// Supports the nested group structure found in ArduPilot parameter documentation.
/// Provides caching and search capabilities for efficient parameter lookup.
/// </summary>
public class ArduPilotMetadataLoader : IArduPilotMetadataLoader
{
    private readonly ILogger<ArduPilotMetadataLoader> _logger;
    private readonly string _defaultFilePath;
    
    private List<ArduPilotParameterMetadata> _allMetadata = new();
    private Dictionary<string, ArduPilotParameterMetadata> _metadataByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<ArduPilotParameterMetadata>> _metadataByGroup = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _loadedAt;

    public bool IsLoaded => _allMetadata.Count > 0;
    public int TotalParameters => _allMetadata.Count;
    public string? LoadedFilePath { get; private set; }

    public ArduPilotMetadataLoader(ILogger<ArduPilotMetadataLoader> logger, string? defaultFilePath = null)
    {
        _logger = logger;
        _defaultFilePath = defaultFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "6", "json1.json");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ArduPilotParameterMetadata>> LoadAllMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            _logger.LogDebug("Metadata already loaded with {Count} parameters", _allMetadata.Count);
            return _allMetadata;
        }

        try
        {
            _logger.LogInformation("Loading ArduPilot parameter metadata from {FilePath}", _defaultFilePath);

            if (!File.Exists(_defaultFilePath))
            {
                _logger.LogWarning("Metadata file not found at {FilePath}", _defaultFilePath);
                return _allMetadata;
            }

            var jsonContent = await File.ReadAllTextAsync(_defaultFilePath, cancellationToken);
            await ParseJsonMetadataAsync(jsonContent, cancellationToken);

            LoadedFilePath = _defaultFilePath;
            _loadedAt = DateTime.Now;

            _logger.LogInformation("Successfully loaded {Count} parameters from {Groups} groups", 
                _allMetadata.Count, _metadataByGroup.Count);

            return _allMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ArduPilot parameter metadata from {FilePath}", _defaultFilePath);
            throw;
        }
    }

    /// <summary>
    /// Parses the JSON content which has a nested group structure.
    /// The JSON format is: { "GroupName_": { "ParamName": { metadata } } }
    /// </summary>
    private Task ParseJsonMetadataAsync(string jsonContent, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            // The JSON structure is nested: { "GroupName_": { "ParamName": { metadata } } }
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;

            var tempMetadata = new List<ArduPilotParameterMetadata>();
            var tempByName = new Dictionary<string, ArduPilotParameterMetadata>(StringComparer.OrdinalIgnoreCase);
            var tempByGroup = new Dictionary<string, List<ArduPilotParameterMetadata>>(StringComparer.OrdinalIgnoreCase);

            foreach (var groupProperty in root.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var groupName = groupProperty.Name;
                
                // Handle empty group name (parameters without a group prefix)
                if (string.IsNullOrEmpty(groupName))
                {
                    groupName = "General";
                }
                else if (groupName.EndsWith("_"))
                {
                    // Remove trailing underscore for cleaner display
                    groupName = groupName.TrimEnd('_');
                }

                if (!tempByGroup.ContainsKey(groupName))
                {
                    tempByGroup[groupName] = new List<ArduPilotParameterMetadata>();
                }

                // Each group contains parameter definitions
                if (groupProperty.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var paramProperty in groupProperty.Value.EnumerateObject())
                    {
                        try
                        {
                            var paramName = paramProperty.Name;
                            var metadata = ParseParameterMetadata(paramName, groupName, paramProperty.Value, options);
                            
                            if (metadata != null)
                            {
                                tempMetadata.Add(metadata);
                                tempByName[paramName] = metadata;
                                tempByGroup[groupName].Add(metadata);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse parameter {ParamName} in group {GroupName}", 
                                paramProperty.Name, groupName);
                        }
                    }
                }
            }

            // Sort by parameter name for consistent ordering
            tempMetadata.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            _allMetadata = tempMetadata;
            _metadataByName = tempByName;
            _metadataByGroup = tempByGroup;

        }, cancellationToken);
    }

    /// <summary>
    /// Parses a single parameter's metadata from its JSON element.
    /// </summary>
    private ArduPilotParameterMetadata? ParseParameterMetadata(
        string paramName, 
        string groupName, 
        JsonElement element,
        JsonSerializerOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var metadata = new ArduPilotParameterMetadata
        {
            Name = paramName,
            Group = groupName
        };

        // Parse each property
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "displayname":
                    metadata.DisplayName = property.Value.GetString();
                    break;
                case "description":
                    metadata.Description = property.Value.GetString();
                    break;
                case "units":
                    metadata.Units = property.Value.GetString();
                    break;
                case "user":
                    metadata.User = property.Value.GetString();
                    break;
                case "increment":
                    metadata.Increment = property.Value.GetString();
                    break;
                case "rebootrequired":
                    metadata.RebootRequired = property.Value.GetString();
                    break;
                case "readonly":
                    metadata.ReadOnly = property.Value.GetString();
                    break;
                case "volatile":
                    metadata.Volatile = property.Value.GetString();
                    break;
                case "path":
                    metadata.Path = property.Value.GetString();
                    break;
                case "range":
                    metadata.Range = ParseRange(property.Value);
                    break;
                case "values":
                    metadata.Values = ParseStringDictionary(property.Value);
                    break;
                case "bitmask":
                    metadata.Bitmask = ParseStringDictionary(property.Value);
                    break;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Parses a Range object from JSON element.
    /// </summary>
    private ParameterRange? ParseRange(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var range = new ParameterRange();

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "low":
                    range.Low = property.Value.GetString();
                    break;
                case "high":
                    range.High = property.Value.GetString();
                    break;
            }
        }

        return range;
    }

    /// <summary>
    /// Parses a dictionary of string key-value pairs from JSON element.
    /// </summary>
    private Dictionary<string, string>? ParseStringDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<string, string>();

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.GetString();
            if (value != null)
            {
                dict[property.Name] = value;
            }
        }

        return dict.Count > 0 ? dict : null;
    }

    /// <inheritdoc/>
    public ArduPilotParameterMetadata? GetMetadata(string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName))
            return null;

        _metadataByName.TryGetValue(parameterName, out var metadata);
        return metadata;
    }

    /// <inheritdoc/>
    public IEnumerable<ArduPilotParameterMetadata> Search(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return _allMetadata;

        var searchLower = searchText.ToLowerInvariant();

        return _allMetadata.Where(m =>
            m.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (m.DisplayName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (m.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (m.Group?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetGroups()
    {
        return _metadataByGroup.Keys.OrderBy(g => g);
    }

    /// <inheritdoc/>
    public IEnumerable<ArduPilotParameterMetadata> GetByGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName) || groupName.Equals("All", StringComparison.OrdinalIgnoreCase))
            return _allMetadata;

        if (_metadataByGroup.TryGetValue(groupName, out var groupParams))
            return groupParams;

        return Enumerable.Empty<ArduPilotParameterMetadata>();
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _allMetadata.Clear();
        _metadataByName.Clear();
        _metadataByGroup.Clear();
        _loadedAt = null;
        LoadedFilePath = null;

        await LoadAllMetadataAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public ArduPilotMetadataStatistics GetStatistics()
    {
        return new ArduPilotMetadataStatistics
        {
            TotalParameters = _allMetadata.Count,
            TotalGroups = _metadataByGroup.Count,
            ParametersWithRanges = _allMetadata.Count(m => m.Range != null),
            ParametersWithEnums = _allMetadata.Count(m => m.HasEnumValues),
            ParametersWithBitmasks = _allMetadata.Count(m => m.HasBitmaskValues),
            ParametersRequiringReboot = _allMetadata.Count(m => m.IsRebootRequired),
            ReadOnlyParameters = _allMetadata.Count(m => m.IsReadOnly),
            LoadedAt = _loadedAt
        };
    }
}
