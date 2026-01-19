using System;
using System.Collections.Generic;
using System.Linq;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Direction of MAVLink message flow
/// </summary>
public enum MavLinkDirection
{
    /// <summary>
    /// Outgoing message (GCS → FC)
    /// </summary>
    Outgoing,
    
    /// <summary>
    /// Incoming message (FC → GCS)
    /// </summary>
    Incoming
}

/// <summary>
/// Represents a single MAVLink message log entry
/// </summary>
public class MavLinkLogEntry
{
    /// <summary>
    /// Timestamp when the message was logged
    /// </summary>
    public DateTime Timestamp { get; init; }
    
    /// <summary>
    /// Direction of the message (TX/RX)
    /// </summary>
    public MavLinkDirection Direction { get; init; }
    
    /// <summary>
    /// Type of MAVLink message (e.g., HEARTBEAT, COMMAND_ACK, STATUSTEXT)
    /// </summary>
    public string MessageType { get; init; } = string.Empty;
    
    /// <summary>
    /// Formatted details of the message (e.g., command ID, parameters, text content)
    /// </summary>
    public string Details { get; init; } = string.Empty;
}

/// <summary>
/// Service for logging and monitoring MAVLink message traffic
/// </summary>
public interface IMavLinkMessageLogger
{
    /// <summary>
    /// Event raised when a new message is logged
    /// </summary>
    event EventHandler<MavLinkLogEntry>? MessageLogged;
    
    /// <summary>
    /// Gets or sets whether logging is enabled
    /// </summary>
    bool IsLoggingEnabled { get; set; }
    
    /// <summary>
    /// Gets the most recent messages from the log
    /// </summary>
    /// <param name="count">Number of messages to retrieve (max 1000)</param>
    /// <returns>List of recent messages, newest first</returns>
    IReadOnlyList<MavLinkLogEntry> GetRecentMessages(int count = 100);
    
    /// <summary>
    /// Clears all logged messages
    /// </summary>
    void ClearLog();
    
    /// <summary>
    /// Logs an outgoing MAVLink message
    /// </summary>
    /// <param name="messageType">Type of message (e.g., COMMAND_LONG, HEARTBEAT)</param>
    /// <param name="details">Formatted details of the message</param>
    void LogOutgoing(string messageType, string details);
    
    /// <summary>
    /// Logs an incoming MAVLink message
    /// </summary>
    /// <param name="messageType">Type of message (e.g., COMMAND_ACK, STATUSTEXT)</param>
    /// <param name="details">Formatted details of the message</param>
    void LogIncoming(string messageType, string details);
}

/// <summary>
/// Thread-safe implementation of MAVLink message logger with circular buffer
/// </summary>
public class MavLinkMessageLogger : IMavLinkMessageLogger
{
    private const int MaxBufferSize = 1000;
    private readonly object _lock = new();
    private readonly List<MavLinkLogEntry> _messages = new();
    private bool _isLoggingEnabled = true;

    /// <inheritdoc/>
    public event EventHandler<MavLinkLogEntry>? MessageLogged;

    /// <inheritdoc/>
    public bool IsLoggingEnabled
    {
        get
        {
            lock (_lock)
            {
                return _isLoggingEnabled;
            }
        }
        set
        {
            lock (_lock)
            {
                _isLoggingEnabled = value;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<MavLinkLogEntry> GetRecentMessages(int count = 100)
    {
        lock (_lock)
        {
            var takeCount = Math.Min(count, _messages.Count);
            return _messages.TakeLast(takeCount).Reverse().ToList();
        }
    }

    /// <inheritdoc/>
    public void ClearLog()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
    }

    /// <inheritdoc/>
    public void LogOutgoing(string messageType, string details)
    {
        if (!IsLoggingEnabled)
            return;

        var entry = new MavLinkLogEntry
        {
            Timestamp = DateTime.Now,
            Direction = MavLinkDirection.Outgoing,
            MessageType = messageType,
            Details = details
        };

        AddEntry(entry);
    }

    /// <inheritdoc/>
    public void LogIncoming(string messageType, string details)
    {
        if (!IsLoggingEnabled)
            return;

        var entry = new MavLinkLogEntry
        {
            Timestamp = DateTime.Now,
            Direction = MavLinkDirection.Incoming,
            MessageType = messageType,
            Details = details
        };

        AddEntry(entry);
    }

    private void AddEntry(MavLinkLogEntry entry)
    {
        lock (_lock)
        {
            _messages.Add(entry);

            // Maintain circular buffer by removing old entries
            while (_messages.Count > MaxBufferSize)
            {
                _messages.RemoveAt(0);
            }
        }

        // Raise event outside of lock to prevent deadlocks
        MessageLogged?.Invoke(this, entry);
    }
}
