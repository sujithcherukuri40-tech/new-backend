using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Enhanced event model for display in the Events tab DataGrid.
/// Extends the base LogEvent with additional display properties.
/// </summary>
public class EventDisplayItem : INotifyPropertyChanged
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double TimestampSeconds { get; set; }
    public string TimestampDisplay => Timestamp.ToString("MMM dd, yyyy HH:mm:ss");
    public string Severity { get; set; } = "Info";
    public string SeverityDisplay => Severity;
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string UserHost { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Time range options for filtering events.
/// </summary>
public enum EventTimeRange
{
    Last1Hour,
    Last24Hours,
    Last7Days,
    Custom,
    All
}

/// <summary>
/// Sorting options for the events grid.
/// </summary>
public enum EventSortColumn
{
    Timestamp,
    Severity,
    EventType,
    Source,
    UserHost
}

/// <summary>
/// Sort direction for the events grid.
/// </summary>
public enum EventSortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Enhanced event summary with additional statistics.
/// </summary>
public class EventDisplaySummary
{
    public int TotalEvents { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int CriticalCount { get; set; }
    
    public string TotalEventsDisplay => TotalEvents.ToString("N0");
    public string ErrorCountDisplay => ErrorCount.ToString("N0");
    public string WarningCountDisplay => WarningCount.ToString("N0");
    public string InfoCountDisplay => InfoCount.ToString("N0");
    public string CriticalCountDisplay => CriticalCount.ToString("N0");
}

/// <summary>
/// Pagination state for the events grid.
/// </summary>
public class EventPaginationState
{
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalItems { get; set; }
    public int TotalPages => TotalItems > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 1;
    
    public int StartIndex => (CurrentPage - 1) * PageSize;
    public int EndIndex => Math.Min(StartIndex + PageSize, TotalItems);
    
    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;
    
    public string DisplayText => TotalItems > 0 
        ? $"Showing {StartIndex + 1} to {EndIndex} of {TotalItems:N0} events"
        : "No events to display";
    
    public string CurrentPageDisplay => $"Page {CurrentPage} of {TotalPages}";
}

/// <summary>
/// Auto-refresh interval options.
/// </summary>
public enum AutoRefreshInterval
{
    Off = 0,
    FiveSeconds = 5,
    TenSeconds = 10,
    ThirtySeconds = 30
}
