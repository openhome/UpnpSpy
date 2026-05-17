using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

/// <summary>
/// Selects a XAML template per node kind. Strings (e.g. the "⚠ Services
/// unavailable: …" failure placeholder) fall through to
/// <see cref="PlaceholderTemplate"/> so failure messages render inline in the tree.
/// </summary>
public sealed partial class DeviceTreeNodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DeviceTemplate { get; set; }
    public DataTemplate? ServiceTemplate { get; set; }
    public DataTemplate? ActionTemplate { get; set; }
    public DataTemplate? PlaceholderTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        DeviceNodeViewModel => DeviceTemplate,
        ServiceNodeViewModel => ServiceTemplate,
        ActionNodeViewModel => ActionTemplate,
        _ => PlaceholderTemplate,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
