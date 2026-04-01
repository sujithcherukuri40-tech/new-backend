using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Infrastructure.Services;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the MAVLink Logs window.
/// Subscribes to <see cref="IMavLinkMessageLogger"/> and surfaces a
/// live, filterable, scrollable list of MAVLink message entries.
/// </summary>
public partial class MavlinkLogsViewModel : ViewModelBase
{
    private readonly IMavLinkMessageLogger _logger;

    // -----------------------------------------------------------------------
    // Observable Properties
    // -----------------------------------------------------------------------

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoggingEnabled = true;

    [ObservableProperty]
    private string _statusText = "Listening for MAVLink messages…";

    [ObservableProperty]
    private int _totalMessageCount;

    // Filtered view of log entries (always on the UI thread)
    public ObservableCollection<MavLinkLogEntry> FilteredEntries { get; } = new();

    // Full backing store (UI thread only after initial load)
    private readonly List<MavLinkLogEntry> _allEntries = new();

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public MavlinkLogsViewModel(IMavLinkMessageLogger mavLinkMessageLogger)
    {
        _logger = mavLinkMessageLogger;
        _logger.MessageLogged += OnMessageLogged;

        // Populate with any messages already captured before this window opened.
        var existing = _logger.GetRecentMessages(500);
        foreach (var entry in existing.Reverse())
        {
            _allEntries.Add(entry);
        }
        ApplyFilter();
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void ClearLog()
    {
        _logger.ClearLog();
        _allEntries.Clear();
        FilteredEntries.Clear();
        TotalMessageCount = 0;
        StatusText = "Log cleared.";
    }

    [RelayCommand]
    private void ToggleLogging()
    {
        IsLoggingEnabled = !IsLoggingEnabled;
        _logger.IsLoggingEnabled = IsLoggingEnabled;
        StatusText = IsLoggingEnabled ? "Logging enabled." : "Logging paused.";
    }

    // -----------------------------------------------------------------------
    // Filter support
    // -----------------------------------------------------------------------

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var filter = FilterText.Trim();

        var items = string.IsNullOrEmpty(filter)
            ? _allEntries
            : _allEntries.Where(e =>
                e.MessageType.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.Details.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredEntries.Add(item);
        }
    }

    // -----------------------------------------------------------------------
    // Event handler
    // -----------------------------------------------------------------------

    private void OnMessageLogged(object? sender, MavLinkLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _allEntries.Add(entry);
            TotalMessageCount = _allEntries.Count;
            StatusText = $"{TotalMessageCount} messages captured.";

            // Keep backing list bounded to avoid unbounded memory growth.
            while (_allEntries.Count > 2000)
            {
                _allEntries.RemoveAt(0);
            }

            // Only add to the visible list if it passes the current filter.
            var filter = FilterText.Trim();
            if (string.IsNullOrEmpty(filter) ||
                entry.MessageType.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                entry.Details.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredEntries.Add(entry);

                // Mirror the cap on the filtered list.
                while (FilteredEntries.Count > 2000)
                {
                    FilteredEntries.RemoveAt(0);
                }
            }
        });
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.MessageLogged -= OnMessageLogged;
        }
        base.Dispose(disposing);
    }
}
