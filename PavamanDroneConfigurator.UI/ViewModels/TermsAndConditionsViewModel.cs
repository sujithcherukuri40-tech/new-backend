using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Terms and Conditions dialog.
/// </summary>
public partial class TermsAndConditionsViewModel : ViewModelBase
{
    /// <summary>
    /// The title of the Terms and Conditions.
    /// </summary>
    public string Title => TermsAndConditions.Title;

    /// <summary>
    /// The full Terms and Conditions text.
    /// </summary>
    public string TermsContent => TermsAndConditions.FullText;

    /// <summary>
    /// The copyright notice.
    /// </summary>
    public string Copyright => TermsAndConditions.Copyright;

    /// <summary>
    /// Last updated date.
    /// </summary>
    public string LastUpdated => TermsAndConditions.LastUpdated;

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
