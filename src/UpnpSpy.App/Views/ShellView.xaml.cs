using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class ShellView : UserControl
{
    private readonly Func<DiagnosticsViewerViewModel> _diagnosticsVmFactory;
    private readonly MainWindowHandleProvider _handleProvider;

    public ShellViewModel ViewModel { get; }

    public ShellView(
        ShellViewModel viewModel,
        InvocationPopupFactory invocationFactory,
        SubscriptionPopupFactory subscriptionFactory,
        DevicePropertiesPopupFactory propertiesFactory,
        Func<DiagnosticsViewerViewModel> diagnosticsVmFactory,
        MainWindowHandleProvider handleProvider)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(invocationFactory);
        ArgumentNullException.ThrowIfNull(subscriptionFactory);
        ArgumentNullException.ThrowIfNull(propertiesFactory);
        _diagnosticsVmFactory = diagnosticsVmFactory ?? throw new ArgumentNullException(nameof(diagnosticsVmFactory));
        _handleProvider = handleProvider ?? throw new ArgumentNullException(nameof(handleProvider));
        InitializeComponent();
        DeviceTreeHost.Content = new DeviceTreeView(viewModel.DeviceTree, invocationFactory, subscriptionFactory, propertiesFactory, handleProvider);
        SsdpLogHost.Content = new SsdpLogView(viewModel.SsdpLog);

        PopulateNetworkAdapterMenu();
    }

    private void PopulateNetworkAdapterMenu()
    {
        NetworkAdapterSubMenu.Items.Clear();

        if (ViewModel.AvailableAdapters.Count == 0)
        {
            NetworkAdapterSubMenu.Items.Add(new MenuFlyoutItem
            {
                Text = "(no eligible adapters)",
                IsEnabled = false,
            });
            return;
        }

        foreach (var adapter in ViewModel.AvailableAdapters)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = $"{adapter.Name} ({adapter.Ipv4Address})",
                Tag = adapter,
                IsChecked = AdapterEquals(adapter, ViewModel.SelectedAdapter),
            };
            item.Click += OnAdapterMenuItemClicked;
            NetworkAdapterSubMenu.Items.Add(item);
        }

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.SelectedAdapter))
                DispatcherQueue.TryEnqueue(RefreshAdapterChecks);
        };
    }

    private void RefreshAdapterChecks()
    {
        foreach (var raw in NetworkAdapterSubMenu.Items)
        {
            if (raw is ToggleMenuFlyoutItem toggle && toggle.Tag is EligibleInterface adapter)
                toggle.IsChecked = AdapterEquals(adapter, ViewModel.SelectedAdapter);
        }
    }

    private static bool AdapterEquals(EligibleInterface a, EligibleInterface? b) =>
        b is not null
        && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && a.Ipv4Address.Equals(b.Ipv4Address);

    private void OnAdapterMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item) return;
        if (item.Tag is not EligibleInterface adapter) return;

        // Block double-selection toggling off the current item.
        if (AdapterEquals(adapter, ViewModel.SelectedAdapter))
        {
            item.IsChecked = true;
            return;
        }

        if (ViewModel.SelectAdapterCommand.CanExecute(adapter))
            ViewModel.SelectAdapterCommand.Execute(adapter);
    }

    private void OnDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        var vm = _diagnosticsVmFactory();
        var window = new DiagnosticsWindow(vm);
        OwnedWindowHelper.SetOwner(window, _handleProvider.Handle);
        window.Activate();
    }
}
