using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

public class AirframeService : IAirframeService
{
    private readonly IParameterService _parameterService;
    private readonly ILogger<AirframeService> _logger;

    public AirframeService(
        IParameterService parameterService,
        ILogger<AirframeService> logger)
    {
        _parameterService = parameterService;
        _logger = logger;
    }

    public async Task<AirframeSettings?> GetAirframeSettingsAsync()
    {
        try
        {
            _logger.LogInformation("Getting airframe settings");

            var settings = new AirframeSettings();

            // Get frame parameters
            settings.FrameClass = (int)await GetParameterValueAsync("FRAME_CLASS");
            settings.FrameType = (int)await GetParameterValueAsync("FRAME_TYPE");
            
            // Determine frame name based on class and type
            settings.FrameName = GetFrameName(settings.FrameClass, settings.FrameType);

            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting airframe settings");
            return null;
        }
    }

    public async Task<bool> UpdateAirframeSettingsAsync(AirframeSettings settings)
    {
        try
        {
            _logger.LogInformation("Updating airframe settings to {FrameName}", settings.FrameName);
            
            bool success = true;
            
            // Update frame class
            var classResult = await _parameterService.SetParameterAsync("FRAME_CLASS", settings.FrameClass);
            if (classResult)
            {
                _logger.LogInformation("? Set FRAME_CLASS = {Value}", settings.FrameClass);
            }
            else
            {
                _logger.LogWarning("? Failed to set FRAME_CLASS");
                success = false;
            }

            // Update frame type
            var typeResult = await _parameterService.SetParameterAsync("FRAME_TYPE", settings.FrameType);
            if (typeResult)
            {
                _logger.LogInformation("? Set FRAME_TYPE = {Value}", settings.FrameType);
            }
            else
            {
                _logger.LogWarning("? Failed to set FRAME_TYPE");
                success = false;
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating airframe settings");
            return false;
        }
    }

    private async Task<float> GetParameterValueAsync(string name)
    {
        var param = await _parameterService.GetParameterAsync(name);
        return param?.Value ?? 0f;
    }

    private string GetFrameName(int frameClass, int frameType)
    {
        // Frame class mapping (ArduCopter)
        var className = frameClass switch
        {
            0 => "Undefined",
            1 => "Quad",
            2 => "Hexa",
            3 => "Octa",
            4 => "OctaQuad",
            5 => "Y6",
            6 => "Heli",
            7 => "Tri",
            8 => "SingleCopter",
            9 => "CoaxCopter",
            10 => "BiCopter",
            11 => "Heli_Dual",
            12 => "DodecaHexa",
            13 => "HeliQuad",
            14 => "Deca",
            _ => $"Unknown ({frameClass})"
        };

        // Frame type mapping (configuration) - Complete ArduPilot FRAME_TYPE list
        var typeName = frameType switch
        {
            0 => "Plus",
            1 => "X",
            2 => "V",
            3 => "H",
            4 => "V-Tail",
            5 => "A-Tail",
            6 => "Y6B (Reversed)",
            7 => "Y6F2",
            8 => "BetaFlight X",
            9 => "DJI X",
            10 => "Y6B",
            11 => "Y6F",
            12 => "ClockWise X",
            13 => "NanoQuad",
            14 => "Deca CW X",
            _ => $"Custom ({frameType})"
        };

        return $"{className} {typeName}";
    }
}
