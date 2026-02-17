using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.IO;
using System.Text.Json;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Stores last successful connection settings for auto-connect functionality.
/// Settings are saved to local app data after successful connection.
/// </summary>
public sealed class ConnectionSettingsStorage
{
    private readonly ILogger<ConnectionSettingsStorage> _logger;
    private readonly string _settingsFilePath;

    public ConnectionSettingsStorage(ILogger<ConnectionSettingsStorage> logger)
    {
        _logger = logger;

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PavamanDroneConfigurator",
            "Connection"
        );

        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "last-connection.json");
    }

    /// <summary>
    /// Save connection settings after successful connection
    /// </summary>
    public void SaveSettings(ConnectionSettings settings, bool enableAutoConnect)
    {
        try
        {
            var storageModel = new ConnectionSettingsStorageModel
            {
                Type = settings.Type,
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                IpAddress = settings.IpAddress,
                Port = settings.Port,
                BluetoothDeviceAddress = settings.BluetoothDeviceAddress,
                BluetoothDeviceName = settings.BluetoothDeviceName,
                EnableAutoConnect = enableAutoConnect,
                LastSuccessfulConnection = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(storageModel, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_settingsFilePath, json);
            _logger.LogInformation("Connection settings saved for auto-connect");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save connection settings");
        }
    }

    /// <summary>
    /// Load last successful connection settings
    /// </summary>
    public (ConnectionSettings? Settings, bool EnableAutoConnect) LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogDebug("No saved connection settings found");
                return (null, false);
            }

            var json = File.ReadAllText(_settingsFilePath);
            var storageModel = JsonSerializer.Deserialize<ConnectionSettingsStorageModel>(json);

            if (storageModel == null)
            {
                return (null, false);
            }

            var settings = new ConnectionSettings
            {
                Type = storageModel.Type,
                PortName = storageModel.PortName,
                BaudRate = storageModel.BaudRate,
                IpAddress = storageModel.IpAddress,
                Port = storageModel.Port,
                BluetoothDeviceAddress = storageModel.BluetoothDeviceAddress,
                BluetoothDeviceName = storageModel.BluetoothDeviceName
            };

            _logger.LogInformation("Loaded saved connection settings: Type={Type}, AutoConnect={AutoConnect}",
                storageModel.Type, storageModel.EnableAutoConnect);

            return (settings, storageModel.EnableAutoConnect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load connection settings");
            return (null, false);
        }
    }

    /// <summary>
    /// Clear saved connection settings
    /// </summary>
    public void ClearSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                File.Delete(_settingsFilePath);
                _logger.LogInformation("Connection settings cleared");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear connection settings");
        }
    }
}

/// <summary>
/// Storage model for connection settings
/// </summary>
internal class ConnectionSettingsStorageModel
{
    public ConnectionType Type { get; set; }
    public string? PortName { get; set; }
    public int BaudRate { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; }
    public string? BluetoothDeviceAddress { get; set; }
    public string? BluetoothDeviceName { get; set; }
    public bool EnableAutoConnect { get; set; }
    public DateTime LastSuccessfulConnection { get; set; }
}
