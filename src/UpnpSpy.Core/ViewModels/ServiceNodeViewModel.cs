using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Tree node for a single service under a device. <see cref="ExpandAsync"/> is
/// invoked exactly once (on first user expansion) and lazily fetches the SCPD
/// to populate <see cref="Children"/> with <see cref="ActionNodeViewModel"/>
/// instances. Failures surface as a single inline placeholder string per FR-013.
/// </summary>
public partial class ServiceNodeViewModel : ObservableObject
{
    /// <summary>Placeholder child inserted at construction so the chevron is visible immediately.</summary>
    public const string LoadingPlaceholder = "Loading…";

    private readonly IScpdFetcher _scpdFetcher;
    private readonly IDispatcher _dispatcher;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly CancellationToken _shutdownToken;
    private readonly object _gate = new();
    private Task? _expandTask;

    public Service Service { get; }

    [ObservableProperty]
    private string _label;

    public ObservableCollection<object> Children { get; } = new();

    public ServiceNodeViewModel(
        Service service,
        IScpdFetcher scpdFetcher,
        IDispatcher dispatcher,
        IBrowserLauncher browserLauncher,
        CancellationToken shutdownToken)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        _scpdFetcher = scpdFetcher ?? throw new ArgumentNullException(nameof(scpdFetcher));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _browserLauncher = browserLauncher ?? throw new ArgumentNullException(nameof(browserLauncher));
        _shutdownToken = shutdownToken;
        _label = service.Label;
        FetchScpdCommand = new AsyncRelayCommand(FetchScpdAsync);
        // FR-044: chevron affordance — see DeviceNodeViewModel for the rationale.
        Children.Add(LoadingPlaceholder);
    }

    public IAsyncRelayCommand FetchScpdCommand { get; }

    private Task FetchScpdAsync() => _browserLauncher.OpenAsync(Service.ScpdUrl, _shutdownToken);

    public Task ExpandAsync()
    {
        lock (_gate)
        {
            if (_expandTask is not null) return _expandTask;
            _expandTask = LoadAsync();
            return _expandTask;
        }
    }

    private async Task LoadAsync()
    {
        Service.ScpdFetchState = FetchState.Fetching;
        var result = await _scpdFetcher.FetchAsync(Service.ScpdUrl, _shutdownToken).ConfigureAwait(false);

        _dispatcher.Post(() => ApplyResult(result));
    }

    private void ApplyResult(ScpdFetchResult result)
    {
        Children.Clear();
        switch (result)
        {
            case ScpdFetchResult.Success success:
                Service.Actions = success.Document.Actions;
                Service.StateVariables = success.Document.StateVariables;
                Service.ScpdFetchState = FetchState.Loaded;
                Service.ScpdFetchError = null;
                foreach (var action in success.Document.Actions)
                    Children.Add(new ActionNodeViewModel(Service, action));
                break;

            case ScpdFetchResult.HttpError http:
                FailWith($"⚠ Actions unavailable: HTTP {http.StatusCode} {http.ReasonPhrase}");
                break;

            case ScpdFetchResult.TransportError transport:
                FailWith($"⚠ Actions unavailable: {transport.Message}");
                break;

            case ScpdFetchResult.ParseError parse:
                FailWith($"⚠ Actions unavailable: {parse.Message}");
                break;
        }
    }

    private void FailWith(string placeholder)
    {
        Service.ScpdFetchState = FetchState.Failed;
        Service.ScpdFetchError = placeholder;
        Children.Add(placeholder);
    }
}
