using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converters for admin panel UI elements.
/// </summary>
public static class AdminPanelConverters
{
    /// <summary>
    /// Converts approval status to background brush for status badge.
    /// </summary>
    public static readonly IValueConverter StatusBackgroundConverter = new FuncValueConverter<bool, IBrush>(
        isApproved => isApproved 
            ? new SolidColorBrush(Color.Parse("#DCFCE7")) // Light green for approved
            : new SolidColorBrush(Color.Parse("#FEF3C7"))  // Light yellow for pending
    );

    /// <summary>
    /// Converts approval status to button background brush.
    /// </summary>
    public static readonly IValueConverter ApprovalButtonColorConverter = new FuncValueConverter<bool, IBrush>(
        isApproved => isApproved 
            ? new SolidColorBrush(Color.Parse("#F44336")) // Red for revoke
            : new SolidColorBrush(Color.Parse("#4CAF50"))  // Green for approve
    );
}
