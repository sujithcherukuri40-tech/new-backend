using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Monitors serial port changes for device connection/disconnection events.
/// Used during firmware upload to track USB re-enumeration when devices reboot.
/// </summary>
public class PortMonitor : IDisposable
{
    private readonly ILogger? _logger;
    private bool _disposed;
    private readonly HashSet<string> _knownPorts;

    public event EventHandler<PortChangedEventArgs>? PortArrived;
    public event EventHandler<PortChangedEventArgs>? PortRemoved;

    public PortMonitor(ILogger? logger = null)
    {
        _logger = logger;
        _knownPorts = new HashSet<string>(GetAvailablePorts());
    }

    /// <summary>
    /// Get all currently available serial ports.
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets the set of ports that existed when the monitor was created.
    /// </summary>
    public HashSet<string> GetInitialPorts()
    {
        return new HashSet<string>(_knownPorts);
    }

    /// <summary>
    /// Wait for a new port to appear that wasn't in the initial set.
    /// </summary>
    /// <param name="existingPorts">Ports that existed before the device change</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="filter">Optional filter function to validate the new port</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The new port name, or null if timeout</returns>
    public static async Task<string?> WaitForNewPortAsync(
        HashSet<string> existingPorts,
        TimeSpan timeout,
        Func<string, bool>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentPorts = GetAvailablePorts();

            foreach (var port in currentPorts)
            {
                if (!existingPorts.Contains(port))
                {
                    if (filter == null || filter(port))
                    {
                        return port;
                    }
                }
            }

            await Task.Delay(200, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Wait for a specific port to disappear (device disconnected).
    /// </summary>
    /// <param name="portName">Port to watch</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if port disconnected within timeout</returns>
    public static async Task<bool> WaitForPortDisconnectAsync(
        string portName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentPorts = GetAvailablePorts();

            if (!currentPorts.Contains(portName))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Wait for a specific port to appear (or reappear after disconnect).
    /// </summary>
    /// <param name="portName">Port to wait for</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if port appeared within timeout</returns>
    public static async Task<bool> WaitForPortConnectAsync(
        string portName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentPorts = GetAvailablePorts();

            if (currentPorts.Contains(portName))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Wait for any port changes (additions or removals) and return the new port set.
    /// </summary>
    /// <param name="previousPorts">Previous set of ports</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (new ports added, ports removed)</returns>
    public static async Task<(List<string> Added, List<string> Removed)> WaitForPortChangesAsync(
        HashSet<string> previousPorts,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentPorts = new HashSet<string>(GetAvailablePorts());

            var added = currentPorts.Except(previousPorts).ToList();
            var removed = previousPorts.Except(currentPorts).ToList();

            if (added.Count > 0 || removed.Count > 0)
            {
                return (added, removed);
            }

            await Task.Delay(200, cancellationToken);
        }

        return (new List<string>(), new List<string>());
    }

    /// <summary>
    /// Continuously monitor port changes and raise events.
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        var previousPorts = new HashSet<string>(GetAvailablePorts());

        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                var currentPorts = new HashSet<string>(GetAvailablePorts());

                // Check for new ports
                foreach (var port in currentPorts.Except(previousPorts))
                {
                    _logger?.LogInformation("Port arrived: {Port}", port);
                    PortArrived?.Invoke(this, new PortChangedEventArgs(port));
                }

                // Check for removed ports
                foreach (var port in previousPorts.Except(currentPorts))
                {
                    _logger?.LogInformation("Port removed: {Port}", port);
                    PortRemoved?.Invoke(this, new PortChangedEventArgs(port));
                }

                previousPorts = currentPorts;
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error monitoring ports");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Event args for port change events.
/// </summary>
public class PortChangedEventArgs : EventArgs
{
    public string PortName { get; }

    public PortChangedEventArgs(string portName)
    {
        PortName = portName;
    }
}
