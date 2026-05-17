using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.Ssdp;

namespace UpnpSpy.Core.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly DiscoveryService _discovery;
    private readonly EagerDescriptionDispatcher _eagerDispatcher;
    private readonly RescanCoordinator _rescanCoordinator;
    private readonly AppShutdownTokenSource _shutdown;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly INetworkAdapterSelector _adapterSelector;
    private readonly IEventCallbackHost _callbackHost;
    private readonly ISsdpTransport _ssdpTransport;
    private readonly DeviceRegistry _registry;

    public DeviceTreeViewModel DeviceTree { get; }

    public SsdpLogViewModel SsdpLog { get; }

    public IAsyncRelayCommand RescanCommand { get; }

    public IReadOnlyList<EligibleInterface> AvailableAdapters => _adapterSelector.Available;

    public EligibleInterface? SelectedAdapter => _adapterSelector.Selected;

    public IAsyncRelayCommand<EligibleInterface?> SelectAdapterCommand { get; }

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isRescanInProgress;

    [ObservableProperty]
    private bool _isAdapterSwitchInProgress;

    public ShellViewModel(
        DeviceTreeViewModel deviceTree,
        SsdpLogViewModel ssdpLog,
        DiscoveryService discovery,
        EagerDescriptionDispatcher eagerDispatcher,
        RescanCoordinator rescanCoordinator,
        AppShutdownTokenSource shutdown,
        ILogger<ShellViewModel> logger,
        INetworkAdapterSelector adapterSelector,
        IEventCallbackHost callbackHost,
        ISsdpTransport ssdpTransport,
        DeviceRegistry registry)
    {
        DeviceTree = deviceTree ?? throw new ArgumentNullException(nameof(deviceTree));
        SsdpLog = ssdpLog ?? throw new ArgumentNullException(nameof(ssdpLog));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _eagerDispatcher = eagerDispatcher ?? throw new ArgumentNullException(nameof(eagerDispatcher));
        _rescanCoordinator = rescanCoordinator ?? throw new ArgumentNullException(nameof(rescanCoordinator));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapterSelector = adapterSelector ?? throw new ArgumentNullException(nameof(adapterSelector));
        _callbackHost = callbackHost ?? throw new ArgumentNullException(nameof(callbackHost));
        _ssdpTransport = ssdpTransport ?? throw new ArgumentNullException(nameof(ssdpTransport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsRescanInProgress && !IsAdapterSwitchInProgress);
        SelectAdapterCommand = new AsyncRelayCommand<EligibleInterface?>(SelectAdapterAsync,
            adapter => adapter is not null && !IsAdapterSwitchInProgress);
        _adapterSelector.Changed += OnAdapterChanged;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsInitializing = true;
        try
        {
            var selected = _adapterSelector.Selected;
            if (selected is not null)
            {
                try
                {
                    await _callbackHost.StartAsync(selected.Ipv4Address, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Callback host failed to bind; eventing will be unavailable for this session");
                }
            }

            _eagerDispatcher.Start();
            await _discovery.StartAsync(cancellationToken).ConfigureAwait(false);
            await _discovery.RunStartupDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup discovery failed");
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task RescanAsync()
    {
        IsRescanInProgress = true;
        RescanCommand.NotifyCanExecuteChanged();
        try
        {
            await _rescanCoordinator.RescanAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rescan failed");
        }
        finally
        {
            IsRescanInProgress = false;
            RescanCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SelectAdapterAsync(EligibleInterface? adapter)
    {
        if (adapter is null) return;
        if (_adapterSelector.Selected is not null
            && string.Equals(_adapterSelector.Selected.Name, adapter.Name, StringComparison.Ordinal)
            && _adapterSelector.Selected.Ipv4Address.Equals(adapter.Ipv4Address))
        {
            return;
        }

        // Drive the selector; the Changed event fires synchronously and we
        // handle the actual rebind there.
        await _adapterSelector.SelectAsync(adapter, _shutdown.Token).ConfigureAwait(false);
    }

    private void OnAdapterChanged(AdapterSelectionChanged change)
    {
        _ = Task.Run(() => SwitchAdapterAsync(change.Current));
    }

    private async Task SwitchAdapterAsync(EligibleInterface? newAdapter)
    {
        IsAdapterSwitchInProgress = true;
        RescanCommand.NotifyCanExecuteChanged();
        SelectAdapterCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedAdapter));

        try
        {
            // 1. Clear the registry — every device-removed event fans out to
            //    the tree (devices vanish from the left pane) and to any open
            //    popups (FR-037 → "device no longer reachable").
            _registry.Clear();

            // 2. Stop the callback host (drops every active registration so
            //    subscription popups see their event-pump end).
            await _callbackHost.StopAsync(_shutdown.Token).ConfigureAwait(false);

            if (newAdapter is null) return;

            // 3. Rebind the callback host on the new adapter.
            try
            {
                await _callbackHost.StartAsync(newAdapter.Ipv4Address, _shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Callback host failed to rebind on {Adapter}", newAdapter.Name);
            }

            // 4. Rebind the SSDP transport on the new adapter.
            await _ssdpTransport.RestartAsync(_shutdown.Token).ConfigureAwait(false);

            // 5. Fresh M-SEARCH so the tree refills on the new adapter.
            await _discovery.RunStartupDiscoveryAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adapter switch failed");
        }
        finally
        {
            IsAdapterSwitchInProgress = false;
            RescanCommand.NotifyCanExecuteChanged();
            SelectAdapterCommand.NotifyCanExecuteChanged();
        }
    }
}
