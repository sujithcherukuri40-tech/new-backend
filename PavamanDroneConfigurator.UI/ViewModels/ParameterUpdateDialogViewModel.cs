using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// Represents a pending parameter change for display in the confirmation dialog.
/// </summary>
public partial class PendingParameterChange : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private float _originalValue;

    [ObservableProperty]
    private float _newValue;

    public string OriginalValueDisplay => OriginalValue.ToString("G6");
    public string NewValueDisplay => NewValue.ToString("G6");
}

/// <summary>
/// ViewModel for the parameter update confirmation dialog.
/// </summary>
public partial class ParameterUpdateDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<PendingParameterChange> _pendingChanges = new();

    [ObservableProperty]
    private bool _isConfirmed;

    [ObservableProperty]
    private string _dialogTitle = "Confirm Parameter Updates";

    [ObservableProperty]
    private string _dialogMessage = "The following parameters will be updated on the vehicle:";

    public int ChangeCount => PendingChanges.Count;

    public ParameterUpdateDialogViewModel()
    {
    }

    public ParameterUpdateDialogViewModel(IEnumerable<PendingParameterChange> changes)
    {
        foreach (var change in changes)
        {
            PendingChanges.Add(change);
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        IsConfirmed = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
    }
}
