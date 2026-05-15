using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Leaf node under a service that displays a single SCPD action. Double-click
/// on this node opens the invocation popup in US7 — for US3 it is render-only.
/// </summary>
public partial class ActionNodeViewModel : ObservableObject
{
    public Service Service { get; }
    public ActionDefinition Action { get; }
    public string Label { get; }

    public IReadOnlyList<object> Children { get; } = Array.Empty<object>();

    public ActionNodeViewModel(Service service, ActionDefinition action)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Label = action.Name;
    }
}
