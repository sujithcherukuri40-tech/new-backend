using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PavamanDroneConfigurator.UI.ViewModels;

namespace PavamanDroneConfigurator.UI;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var fullName = param.GetType().FullName!;

        // Strategy 1: XxxViewModel -> XxxView  (e.g. AdminDashboardViewModel -> AdminDashboardView)
        // Note: this also converts "ViewModels." -> "Views." in the namespace automatically.
        var nameAsView = fullName.Replace("ViewModel", "View", StringComparison.Ordinal);
        var typeAsView = Type.GetType(nameAsView);
        if (typeAsView != null)
            return (Control)Activator.CreateInstance(typeAsView)!;

        // Strategy 2: XxxViewModel -> Xxx  (e.g. CameraConfigPageViewModel -> CameraConfigPage)
        // Fix namespace first so "ViewModels." doesn't collapse to "s."
        var nameAsPage = fullName
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "", StringComparison.Ordinal);
        var typeAsPage = Type.GetType(nameAsPage);
        if (typeAsPage != null)
            return (Control)Activator.CreateInstance(typeAsPage)!;

        return new TextBlock { Text = "Not Found: " + fullName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
