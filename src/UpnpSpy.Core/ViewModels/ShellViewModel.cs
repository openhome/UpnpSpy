using System.Collections.Specialized;
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
    private readonly IDispatcher _dispatcher;

    // Serializes InitializeAsync and SwitchAdapterAsync so a quick adapter pick
    // during startup can't race the still-running initial discovery (both touch
    // the callback host and SSDP transport).
    private readonly SemaphoreSlim _bindGate = new(1, 1);

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

    /// <summary>True while any background work is in progress (init / rescan / adapter switch).</summary>
    public bool IsBusy => IsInitializing || IsRescanInProgress || IsAdapterSwitchInProgress;

    /// <summary>Headline string for the busy InfoBar.</summary>
    public string BusyTitle
    {
        get
        {
            if (IsAdapterSwitchInProgress) return "Switching network adapter";
            if (IsRescanInProgress) return "Rescanning the network";
            if (IsInitializing) return "Discovering UPnP devices";
            return string.Empty;
        }
    }

    /// <summary>Sub-headline for the busy InfoBar.</summary>
    public string BusyMessage
    {
        get
        {
            if (IsAdapterSwitchInProgress) return "Rebinding sockets and re-running discovery.";
            if (IsRescanInProgress) return "Sending M-SEARCH and pruning non-responders.";
            if (IsInitializing) return "Listening for SSDP advertisements on the local network.";
            return string.Empty;
        }
    }

    /// <summary>Display label for the currently-selected adapter (e.g. "Wi-Fi · 192.168.1.42").</summary>
    public string AdapterDisplay => SelectedAdapter is { } a
        ? $"{a.Name} · {a.Ipv4Address}"
        : "(no adapter)";

    /// <summary>Live count of devices visible in the tree.</summary>
    public int DeviceCount => DeviceTree.Devices.Count;

    /// <summary>Live count of SSDP advertisements in the right-pane log.</summary>
    public int SsdpLogCount => SsdpLog.Entries.Count;

    /// <summary>Pre-formatted "N devices" for status-bar display.</summary>
    public string DeviceCountText => $"{DeviceCount} device{(DeviceCount == 1 ? "" : "s")}";

    /// <summary>Pre-formatted "N SSDP messages" for status-bar display.</summary>
    public string SsdpLogCountText => $"{SsdpLogCount} SSDP message{(SsdpLogCount == 1 ? "" : "s")}";

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
        DeviceRegistry registry,
        IDispatcher dispatcher)
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
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsRescanInProgress && !IsAdapterSwitchInProgress);
        SelectAdapterCommand = new AsyncRelayCommand<EligibleInterface?>(SelectAdapterAsync,
            adapter => adapter is not null && !IsAdapterSwitchInProgress);
        _adapterSelector.Changed += OnAdapterChanged;

        // Re-emit the computed busy properties when any of the three underlying
        // flags flip, so the View can bind once and react to all three.
        PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(IsInitializing):
                case nameof(IsRescanInProgress):
                case nameof(IsAdapterSwitchInProgress):
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(BusyTitle));
                    OnPropertyChanged(nameof(BusyMessage));
                    break;
                case nameof(SelectedAdapter):
                    OnPropertyChanged(nameof(AdapterDisplay));
                    break;
            }
        };

        DeviceTree.Devices.CollectionChanged += OnDevicesChanged;
        SsdpLog.Entries.CollectionChanged += OnSsdpEntriesChanged;
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(DeviceCountText));
    }

    private void OnSsdpEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SsdpLogCount));
        OnPropertyChanged(nameof(SsdpLogCountText));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SetBusyFlagAsync(setInit: true).ConfigureAwait(false);
        await _bindGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            _bindGate.Release();
            await SetBusyFlagAsync(setInit: false).ConfigureAwait(false);
        }
    }

    private async Task RescanAsync()
    {
        await SetBusyFlagAsync(setRescan: true).ConfigureAwait(false);
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
            await SetBusyFlagAsync(setRescan: false).ConfigureAwait(false);
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
        await SetBusyFlagAsync(setSwitch: true).ConfigureAwait(false);
        _dispatcher.Post(() => OnPropertyChanged(nameof(SelectedAdapter)));

        // Serialize with InitializeAsync so a quick adapter pick during startup
        // can't tangle with the initial discovery's still-running bind sequence.
        await _bindGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
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
            _bindGate.Release();
            await SetBusyFlagAsync(setSwitch: false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Mutates one of the three busy flags on the UI thread so the bound
    /// busy-InfoBar / IsBusy / BusyTitle PropertyChanged events fire from a
    /// thread WinUI's binding system will service. WinUI x:Bind silently drops
    /// PropertyChanged that arrive from a pool thread — without this marshal,
    /// the busy bar would stick on its first value forever once a flag flipped
    /// after the first <c>await</c>.
    /// </summary>
    private Task SetBusyFlagAsync(bool? setInit = null, bool? setRescan = null, bool? setSwitch = null) =>
        _dispatcher.RunOnUiAsync(() =>
        {
            if (setInit is { } i) IsInitializing = i;
            if (setRescan is { } r)
            {
                IsRescanInProgress = r;
                RescanCommand.NotifyCanExecuteChanged();
            }
            if (setSwitch is { } s)
            {
                IsAdapterSwitchInProgress = s;
                RescanCommand.NotifyCanExecuteChanged();
                SelectAdapterCommand.NotifyCanExecuteChanged();
            }
        });
}
