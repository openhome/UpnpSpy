using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Tree node for a single root UPnP device. Description-derived state
/// (<see cref="Device.FriendlyName"/>, <see cref="Device.Services"/>,
/// <see cref="Device.DescriptionFetchState"/>) is populated eagerly by
/// <see cref="EagerDescriptionDispatcher"/> on registry-add (FR-043), so this
/// view-model never performs an HTTP fetch itself. <see cref="ExpandAsync"/>
/// is a pure UI hydration step that branches on the current fetch state:
/// <see cref="FetchState.Loaded"/> hydrates children from
/// <see cref="Device.Services"/>; <see cref="FetchState.Failed"/> surfaces the
/// FR-013 inline error placeholder; <see cref="FetchState.Fetching"/> shows a
/// transient "Loading…" placeholder and resolves when the registry's
/// <see cref="DeviceRegistry.DeviceUpdated"/> event fires for this device.
/// </summary>
public partial class DeviceNodeViewModel : ObservableObject
{
    /// <summary>Placeholder child inserted while the eager fetch is in flight.</summary>
    public const string LoadingPlaceholder = "Loading…";

    private readonly DeviceRegistry? _registry;
    private readonly ServiceNodeFactory? _serviceFactory;
    private readonly IDispatcher? _dispatcher;
    private readonly IBrowserLauncher? _browserLauncher;
    private readonly CancellationToken _shutdownToken;
    private readonly object _gate = new();
    private Task? _expandTask;

    public Device Device { get; }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _detailLabel;

    public ObservableCollection<object> Children { get; } = new();

    public DeviceNodeViewModel(Device device)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        _label = device.Label;
        _detailLabel = device.DetailLabel;
        // FR-044: seed a placeholder child at construction so the WinUI TreeView
        // renders the expand chevron before the user clicks. Replaced atomically
        // by real children (or the FR-013 error placeholder) inside RenderTerminal.
        Children.Add(LoadingPlaceholder);
    }

    public DeviceNodeViewModel(
        Device device,
        DeviceRegistry registry,
        ServiceNodeFactory serviceFactory,
        IDispatcher dispatcher,
        IBrowserLauncher browserLauncher,
        CancellationToken shutdownToken)
        : this(device)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _browserLauncher = browserLauncher ?? throw new ArgumentNullException(nameof(browserLauncher));
        _shutdownToken = shutdownToken;
        FetchXmlCommand = new AsyncRelayCommand(FetchXmlAsync);
    }

    public IAsyncRelayCommand? FetchXmlCommand { get; }

    private async Task FetchXmlAsync()
    {
        if (_browserLauncher is null) return;
        await _browserLauncher.OpenAsync(Device.LocationUrl, _shutdownToken).ConfigureAwait(false);
    }

    public void RefreshLabel()
    {
        Label = Device.Label;
        DetailLabel = Device.DetailLabel;
    }

    /// <summary>
    /// Idempotent. First call begins the state-machine; subsequent calls return
    /// the same task. The returned task completes once <see cref="Children"/>
    /// reflects the terminal state (<see cref="FetchState.Loaded"/> →
    /// services; <see cref="FetchState.Failed"/> → inline error placeholder).
    /// </summary>
    public Task ExpandAsync()
    {
        if (_registry is null) return Task.CompletedTask; // bare-bones constructor (label-only)

        lock (_gate)
        {
            if (_expandTask is not null) return _expandTask;
            _expandTask = ExpandCoreAsync();
            return _expandTask;
        }
    }

    private async Task ExpandCoreAsync()
    {
        // Subscribe BEFORE re-checking state, so we can't lose the
        // Fetching → Loaded transition to a race.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(DeviceUpdatedEvent e)
        {
            if (!string.Equals(e.Device.Uuid, Device.Uuid, StringComparison.Ordinal)) return;
            if (e.Device.DescriptionFetchState == FetchState.Loaded
                || e.Device.DescriptionFetchState == FetchState.Failed)
            {
                completion.TrySetResult();
            }
        }

        _registry!.DeviceUpdated += Handler;
        using var registration = _shutdownToken.Register(() => completion.TrySetCanceled(_shutdownToken));

        try
        {
            if (IsTerminal(Device.DescriptionFetchState))
            {
                RenderTerminal();
                return;
            }

            // Still Fetching / NotFetched — show placeholder and wait.
            _dispatcher!.Post(() =>
            {
                if (Children.Count == 0)
                    Children.Add(LoadingPlaceholder);
            });

            await completion.Task.ConfigureAwait(false);
            RenderTerminal();
        }
        finally
        {
            _registry.DeviceUpdated -= Handler;
        }
    }

    private static bool IsTerminal(FetchState state) =>
        state == FetchState.Loaded || state == FetchState.Failed;

    private void RenderTerminal()
    {
        _dispatcher!.Post(() =>
        {
            Children.Clear();
            switch (Device.DescriptionFetchState)
            {
                case FetchState.Loaded:
                    foreach (var service in Device.Services)
                        Children.Add(_serviceFactory!.Create(service));
                    break;
                case FetchState.Failed:
                    Children.Add($"⚠ Services unavailable: {Device.DescriptionFetchError ?? "Unknown error"}");
                    break;
            }
        });
    }
}
