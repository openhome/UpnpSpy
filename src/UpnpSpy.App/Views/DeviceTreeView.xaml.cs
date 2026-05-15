using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class DeviceTreeView : UserControl
{
    private readonly InvocationPopupFactory _invocationFactory;
    private readonly SubscriptionPopupFactory _subscriptionFactory;
    private readonly DevicePropertiesPopupFactory _propertiesFactory;
    private readonly MainWindowHandleProvider _handleProvider;

    public DeviceTreeViewModel ViewModel { get; }

    public DeviceTreeView(
        DeviceTreeViewModel viewModel,
        InvocationPopupFactory invocationFactory,
        SubscriptionPopupFactory subscriptionFactory,
        DevicePropertiesPopupFactory propertiesFactory,
        MainWindowHandleProvider handleProvider)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _invocationFactory = invocationFactory ?? throw new ArgumentNullException(nameof(invocationFactory));
        _subscriptionFactory = subscriptionFactory ?? throw new ArgumentNullException(nameof(subscriptionFactory));
        _propertiesFactory = propertiesFactory ?? throw new ArgumentNullException(nameof(propertiesFactory));
        _handleProvider = handleProvider ?? throw new ArgumentNullException(nameof(handleProvider));
        InitializeComponent();
    }

    private void OnTreeViewExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        switch (args.Item)
        {
            case DeviceNodeViewModel device:
                _ = device.ExpandAsync();
                break;
            case ServiceNodeViewModel service:
                _ = service.ExpandAsync();
                break;
        }
    }

    private void OnActionDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not ActionNodeViewModel action) return;

        var vm = _invocationFactory.Create(action.Service, action.Action);
        var popup = new InvocationPopup(vm);
        OwnedWindowHelper.SetOwner(popup, _handleProvider.Handle);
        popup.Activate();

        e.Handled = true;
    }

    private void OnSubscribeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.Tag is not Service service) return;

        var vm = _subscriptionFactory.Create(service);
        var popup = new SubscriptionPopup(vm);
        OwnedWindowHelper.SetOwner(popup, _handleProvider.Handle);
        popup.Activate();
        _ = popup.StartAsync();
    }

    private void OnPropertiesClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.Tag is not Device device) return;

        var vm = _propertiesFactory.Create(device);
        var window = new DevicePropertiesWindow(vm);
        OwnedWindowHelper.SetOwner(window, _handleProvider.Handle);
        window.Activate();
    }
}
