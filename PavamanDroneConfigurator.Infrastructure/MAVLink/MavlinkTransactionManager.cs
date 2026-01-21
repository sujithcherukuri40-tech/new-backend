using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.MAVLink
{
    /// <summary>
    /// Command result from MAVLink COMMAND_ACK
    /// </summary>
    public enum CommandResult : byte
    {
        Accepted = 0,
        TemporarilyRejected = 1,
        Denied = 2,
        Unsupported = 3,
        Failed = 4,
        InProgress = 5
    }

    /// <summary>
    /// Transaction tracking for MAVLink commands with retry logic
    /// </summary>
    internal class MavlinkCommandTransaction
    {
        public ushort CommandId { get; set; }
        public TaskCompletionSource<CommandResult> CompletionSource { get; set; } = new();
        public DateTime SentAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
        public CancellationTokenSource? TimeoutCts { get; set; }

        public bool IsExpired => DateTime.UtcNow - SentAt > Timeout;
        public bool CanRetry => RetryCount < MaxRetries;
    }

    /// <summary>
    /// Parameter transaction tracking
    /// </summary>
    internal class ParameterTransaction
    {
        public string ParameterName { get; set; } = string.Empty;
        public ushort ParameterIndex { get; set; }
        public TaskCompletionSource<(string Name, float Value)> CompletionSource { get; set; } = new();
        public DateTime SentAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);

        public bool IsExpired => DateTime.UtcNow - SentAt > Timeout;
        public bool CanRetry => RetryCount < MaxRetries;
    }

    /// <summary>
    /// Manages MAVLink command and parameter transactions with retry logic
    /// Production-grade reliability layer
    /// </summary>
    internal class MavlinkTransactionManager
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<ushort, MavlinkCommandTransaction> _commandTransactions = new();
        private readonly ConcurrentDictionary<string, ParameterTransaction> _parameterTransactions = new();
        private readonly Timer _cleanupTimer;

        public MavlinkTransactionManager(ILogger logger)
        {
            _logger = logger;
            
            // Cleanup expired transactions every 1 second
            _cleanupTimer = new Timer(CleanupExpiredTransactions, null, 
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Register a command transaction and wait for ACK
        /// </summary>
        public Task<CommandResult> RegisterCommandAsync(ushort commandId, TimeSpan? timeout = null, int maxRetries = 3)
        {
            var transaction = new MavlinkCommandTransaction
            {
                CommandId = commandId,
                SentAt = DateTime.UtcNow,
                RetryCount = 0,
                MaxRetries = maxRetries,
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            };

            if (!_commandTransactions.TryAdd(commandId, transaction))
            {
                // Command already pending, return existing task
                if (_commandTransactions.TryGetValue(commandId, out var existing))
                {
                    _logger.LogWarning("Command {CommandId} already has pending transaction", commandId);
                    return existing.CompletionSource.Task;
                }
            }

            _logger.LogDebug("Registered command transaction: cmd={CommandId} timeout={Timeout}s", 
                commandId, transaction.Timeout.TotalSeconds);

            return transaction.CompletionSource.Task;
        }

        /// <summary>
        /// Handle COMMAND_ACK response
        /// </summary>
        public void HandleCommandAck(ushort commandId, byte resultCode)
        {
            if (_commandTransactions.TryRemove(commandId, out var transaction))
            {
                var result = (CommandResult)resultCode;
                var elapsed = DateTime.UtcNow - transaction.SentAt;

                _logger.LogDebug("Command ACK received: cmd={CommandId} result={Result} elapsed={Elapsed}ms",
                    commandId, result, elapsed.TotalMilliseconds);

                transaction.TimeoutCts?.Cancel();
                transaction.CompletionSource.TrySetResult(result);
            }
            else
            {
                _logger.LogTrace("Received ACK for command {CommandId} with no pending transaction", commandId);
            }
        }

        /// <summary>
        /// Register parameter read transaction
        /// </summary>
        public Task<(string Name, float Value)> RegisterParameterReadAsync(string parameterName, TimeSpan? timeout = null)
        {
            var transaction = new ParameterTransaction
            {
                ParameterName = parameterName,
                SentAt = DateTime.UtcNow,
                RetryCount = 0,
                Timeout = timeout ?? TimeSpan.FromSeconds(2)
            };

            if (!_parameterTransactions.TryAdd(parameterName, transaction))
            {
                if (_parameterTransactions.TryGetValue(parameterName, out var existing))
                {
                    _logger.LogWarning("Parameter {ParamName} already has pending read", parameterName);
                    return existing.CompletionSource.Task;
                }
            }

            _logger.LogDebug("Registered parameter read: param={ParamName}", parameterName);
            return transaction.CompletionSource.Task;
        }

        /// <summary>
        /// Handle PARAM_VALUE response
        /// </summary>
        public void HandleParameterValue(string parameterName, float value)
        {
            if (_parameterTransactions.TryRemove(parameterName, out var transaction))
            {
                var elapsed = DateTime.UtcNow - transaction.SentAt;

                _logger.LogDebug("Parameter value received: param={ParamName} value={Value} elapsed={Elapsed}ms",
                    parameterName, value, elapsed.TotalMilliseconds);

                transaction.CompletionSource.TrySetResult((parameterName, value));
            }
        }

        /// <summary>
        /// Get command transaction for retry logic
        /// </summary>
        public MavlinkCommandTransaction? GetCommandTransaction(ushort commandId)
        {
            _commandTransactions.TryGetValue(commandId, out var transaction);
            return transaction;
        }

        /// <summary>
        /// Get parameter transaction for retry logic
        /// </summary>
        public ParameterTransaction? GetParameterTransaction(string parameterName)
        {
            _parameterTransactions.TryGetValue(parameterName, out var transaction);
            return transaction;
        }

        /// <summary>
        /// Cleanup expired transactions
        /// </summary>
        private void CleanupExpiredTransactions(object? state)
        {
            try
            {
                // Cleanup expired command transactions
                foreach (var kvp in _commandTransactions)
                {
                    if (kvp.Value.IsExpired)
                    {
                        if (_commandTransactions.TryRemove(kvp.Key, out var expired))
                        {
                            _logger.LogWarning("Command {CommandId} transaction expired after {Elapsed}s",
                                kvp.Key, (DateTime.UtcNow - expired.SentAt).TotalSeconds);

                            expired.TimeoutCts?.Cancel();
                            expired.CompletionSource.TrySetException(
                                new TimeoutException($"Command {kvp.Key} timed out after {expired.Timeout.TotalSeconds}s"));
                        }
                    }
                }

                // Cleanup expired parameter transactions
                foreach (var kvp in _parameterTransactions)
                {
                    if (kvp.Value.IsExpired)
                    {
                        if (_parameterTransactions.TryRemove(kvp.Key, out var expired))
                        {
                            _logger.LogWarning("Parameter {ParamName} transaction expired after {Elapsed}s",
                                kvp.Key, (DateTime.UtcNow - expired.SentAt).TotalSeconds);

                            expired.CompletionSource.TrySetException(
                                new TimeoutException($"Parameter read '{kvp.Key}' timed out"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired transactions");
            }
        }

        /// <summary>
        /// Clear all pending transactions (on disconnect)
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in _commandTransactions)
            {
                if (_commandTransactions.TryRemove(kvp.Key, out var transaction))
                {
                    transaction.TimeoutCts?.Cancel();
                    transaction.CompletionSource.TrySetCanceled();
                }
            }

            foreach (var kvp in _parameterTransactions)
            {
                if (_parameterTransactions.TryRemove(kvp.Key, out var transaction))
                {
                    transaction.CompletionSource.TrySetCanceled();
                }
            }

            _logger.LogInformation("Cleared all pending transactions");
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            ClearAll();
        }
    }
}
