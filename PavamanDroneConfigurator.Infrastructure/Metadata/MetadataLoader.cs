using System.Xml.Linq;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Metadata;

/// <summary>
/// Loads ArduPilot parameter metadata from XML files.
/// Correctly interprets ArduPilot XML field meanings.
/// </summary>
public static class MetadataLoader
{
    /// <summary>
    /// Loads parameter metadata from an ArduPilot XML file.
    /// </summary>
    /// <param name="filename">XML filename in Assets folder</param>
    /// <returns>Dictionary of parameter name to metadata</returns>
    public static Dictionary<string, ParameterMetadata> Load(string filename)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", filename);
        var result = new Dictionary<string, ParameterMetadata>();

        if (!File.Exists(path))
            return result;

        var doc = XDocument.Load(path);

        foreach (var param in doc.Descendants("param"))
        {
            var meta = new ParameterMetadata
            {
                Name = ((string?)param.Attribute("name"))?.Split(':').Last() ?? "",
                DisplayName = (string?)param.Attribute("humanName") ?? "",
                Description = (string?)param.Attribute("documentation") ?? "",
                Group = (string?)param.Attribute("user") ?? "Standard",
                Units = null,
                UnitsText = null,
                Min = null,
                Max = null,
                StepSize = null,
                DefaultValue = 0 // Default to 0 if not specified in XML
            };

            foreach (var field in param.Elements("field"))
            {
                string? fieldName = (string?)field.Attribute("name");
                string? fieldValue = field.Value;

                switch (fieldName)
                {
                    case "Units":
                        meta.Units = fieldValue;
                        break;

                    case "UnitText":
                        meta.UnitsText = fieldValue;
                        break;

                    case "Range":
                        var parts = fieldValue?.Split(' ');
                        if (parts?.Length == 2)
                        {
                            if (float.TryParse(parts[0], out float min))
                                meta.Min = min;
                            if (float.TryParse(parts[1], out float max))
                                meta.Max = max;
                            meta.Range = fieldValue;
                        }
                        break;

                    case "Increment":
                        // Increment is STEP SIZE for editing, not default value!
                        if (fieldValue != null && float.TryParse(fieldValue, out float step))
                        {
                            if (!float.IsNaN(step) && !float.IsInfinity(step) && step > 0)
                            {
                                meta.StepSize = step;
                            }
                        }
                        break;

                    case "Default":
                        // This is the actual default value
                        if (fieldValue != null && float.TryParse(fieldValue, out float def))
                        {
                            if (!float.IsNaN(def) && !float.IsInfinity(def))
                            {
                                meta.DefaultValue = def;
                            }
                        }
                        break;

                    case "Values":
                        // Enum options (single-select)
                        if (!string.IsNullOrWhiteSpace(fieldValue))
                        {
                            meta.Values ??= new Dictionary<int, string>();
                            
                            var valueParts = fieldValue.Split(',');
                            foreach (var item in valueParts)
                            {
                                var kv = item.Split(':');
                                if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int code))
                                {
                                    meta.Values[code] = kv[1].Trim();
                                }
                            }
                        }
                        break;

                    case "Bitmask":
                        // Bitmask options (multiple-select)
                        if (!string.IsNullOrWhiteSpace(fieldValue))
                        {
                            meta.Bitmask = fieldValue;
                            meta.Values ??= new Dictionary<int, string>();

                            var bitmaskParts = fieldValue.Split(',');
                            foreach (var item in bitmaskParts)
                            {
                                var kv = item.Split(':');
                                if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int code))
                                {
                                    meta.Values[code] = kv[1].Trim();
                                }
                            }
                        }
                        break;

                    case "ReadOnly":
                        meta.ReadOnly = fieldValue?.Equals("True", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;

                    case "RebootRequired":
                        meta.RebootRequired = fieldValue?.Equals("True", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                }
            }

            result[meta.Name] = meta;
        }

        return result;
    }
}
