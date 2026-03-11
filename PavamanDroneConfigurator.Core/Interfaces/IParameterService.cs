using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

public interface IParameterService
{
    Task<List<DroneParameter>> GetAllParametersAsync();
    Task<DroneParameter?> GetParameterAsync(string name);
    Task<bool> SetParameterAsync(string name, float value);
    Task RefreshParametersAsync();
    
    // Event fired when a parameter is updated (provides parameter name)
    event EventHandler<string>? ParameterUpdated;
    event EventHandler? ParameterDownloadStarted;
    event EventHandler<bool>? ParameterDownloadCompleted;
    bool IsParameterDownloadInProgress { get; }
    bool IsParameterDownloadComplete { get; }
    int ReceivedParameterCount { get; }
    int? ExpectedParameterCount { get; }
    event EventHandler? ParameterDownloadProgressChanged;
    void Reset();
    
    /// <summary>
    /// Clears all cached parameters and resets download state.
    /// Should be called when preparing for a fresh connection/reconnection.
    /// </summary>
    void ClearParameters();
}
