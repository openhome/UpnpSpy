using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

/// <summary>
/// Picks between <see cref="TextTemplate"/> and <see cref="LinkTemplate"/> based
/// on the concrete <see cref="PropertyRow"/> subtype. Used by the device
/// Properties window so each row renders with the right control (TextBlock vs
/// HyperlinkButton) without needing a Visibility converter inside a DataTemplate
/// — which the WinUI 3 codegen does not support under a Window XAML root.
/// </summary>
public sealed class PropertyRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate TextTemplate { get; set; } = default!;
    public DataTemplate LinkTemplate { get; set; } = default!;

    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        PropertyLinkRow => LinkTemplate,
        _ => TextTemplate,
    };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
