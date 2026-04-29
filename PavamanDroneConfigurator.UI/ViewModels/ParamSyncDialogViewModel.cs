using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// Represents a single parameter whose drone value differs from the admin-locked value.
/// </summary>
public class ParamSyncMismatch
{
    public string Name { get; init; } = string.Empty;
    public float LockedValue { get; init; }
    public float DroneValue { get; init; }
    public string LockedDisplay => LockedValue.ToString("G");
    public string DroneDisplay => DroneValue.ToString("G");
}

/// <summary>
/// ViewModel for the parameter sync dialog shown when drone parameter values
/// differ from the admin-locked values stored in the cloud.
/// </summary>
public partial class ParamSyncDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<ParamSyncMismatch> _mismatches = new();

    public ParamSyncDialogViewModel() { }

    public ParamSyncDialogViewModel(IEnumerable<ParamSyncMismatch> mismatches)
    {
        foreach (var m in mismatches)
            Mismatches.Add(m);
    }

    public int MismatchCount => Mismatches.Count;

    public string SubTitle => Mismatches.Count == 1
        ? "1 locked parameter has a different value on this drone."
        : $"{Mismatches.Count} locked parameters have different values on this drone.";
}
