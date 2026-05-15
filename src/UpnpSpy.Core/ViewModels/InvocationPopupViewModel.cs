using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpnpSpy.Core.Control;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Drives the popup window opened when the user double-clicks an action node.
/// Holds editable per-input strings, an InvokeCommand, and the most recent
/// <see cref="InvocationResult"/>. Reacts to the device disappearing from the
/// registry by transitioning into a closeable "device no longer reachable"
/// state (FR-037).
/// </summary>
public partial class InvocationPopupViewModel : ObservableObject, IDisposable
{
    private readonly IControlClient _controlClient;
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly CancellationToken _shutdownToken;
    private CancellationTokenSource? _invocationCts;
    private bool _disposed;

    public Service Service { get; }
    public ActionDefinition Action { get; }
    public string Title { get; }

    public ObservableCollection<InvocationInputViewModel> Inputs { get; } = new();
    public ObservableCollection<InvocationOutputViewModel> Outputs { get; } = new();

    [ObservableProperty]
    private bool _isInvoking;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isFault;

    [ObservableProperty]
    private int _faultHttpStatus;

    [ObservableProperty]
    private int _faultErrorCode;

    [ObservableProperty]
    private string _faultErrorDescription = string.Empty;

    [ObservableProperty]
    private string _faultRawXml = string.Empty;

    [ObservableProperty]
    private bool _isTransportError;

    [ObservableProperty]
    private string _transportErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isDeviceUnreachable;

    public IAsyncRelayCommand InvokeCommand { get; }

    public InvocationPopupViewModel(
        Service service,
        ActionDefinition action,
        IControlClient controlClient,
        DeviceRegistry registry,
        IDispatcher dispatcher,
        IClock clock,
        CancellationToken shutdownToken)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        _controlClient = controlClient ?? throw new ArgumentNullException(nameof(controlClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _shutdownToken = shutdownToken;

        Title = $"{service.Label} · {action.Name}";

        foreach (var arg in action.Inputs)
            Inputs.Add(new InvocationInputViewModel(arg));

        InvokeCommand = new AsyncRelayCommand(InvokeAsync, () => !IsInvoking && !IsDeviceUnreachable);

        _registry.DeviceRemoved += OnDeviceRemoved;
    }

    private async Task InvokeAsync()
    {
        if (IsDeviceUnreachable) return;

        ClearResult();
        IsInvoking = true;
        InvokeCommand.NotifyCanExecuteChanged();

        _invocationCts?.Dispose();
        _invocationCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);

        var inputs = BuildInputsDictionary();

        try
        {
            var result = await _controlClient
                .InvokeAsync(Service, Action, inputs, _invocationCts.Token)
                .ConfigureAwait(false);
            _dispatcher.Post(() => ApplyResult(result));
        }
        catch (OperationCanceledException)
        {
            // Device removal or shutdown — leave the popup in its current state;
            // OnDeviceRemoved already transitions to "unreachable" when applicable.
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                IsInvoking = false;
                InvokeCommand.NotifyCanExecuteChanged();
            });
        }
    }

    public IReadOnlyDictionary<string, string> SnapshotInputs() => BuildInputsDictionary();

    public InvocationRequest BuildRequest()
    {
        return new InvocationRequest(
            Service,
            Action,
            BuildInputsDictionary(),
            _clock.UtcNow,
            _invocationCts?.Token ?? _shutdownToken);
    }

    private Dictionary<string, string> BuildInputsDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in Inputs)
            dict[input.Argument.Name] = input.Value ?? string.Empty;
        return dict;
    }

    private void ApplyResult(InvocationResult result)
    {
        HasResult = true;
        switch (result)
        {
            case InvocationResult.Success success:
                IsSuccess = true;
                Outputs.Clear();
                foreach (var (name, value) in success.Outputs)
                    Outputs.Add(new InvocationOutputViewModel(name, value));
                SuccessMessage = Action.Outputs.Count == 0 || success.Outputs.Count == 0
                    ? "Succeeded (no output values)"
                    : "Succeeded";
                break;

            case InvocationResult.UpnpFault fault:
                IsFault = true;
                FaultHttpStatus = fault.HttpStatusCode;
                FaultErrorCode = fault.UpnpErrorCode;
                FaultErrorDescription = fault.UpnpErrorDescription;
                FaultRawXml = fault.RawFaultXml;
                break;

            case InvocationResult.TransportError transport:
                IsTransportError = true;
                TransportErrorMessage = transport.Message;
                break;
        }
    }

    private void ClearResult()
    {
        HasResult = false;
        IsSuccess = false;
        SuccessMessage = string.Empty;
        Outputs.Clear();
        IsFault = false;
        FaultHttpStatus = 0;
        FaultErrorCode = 0;
        FaultErrorDescription = string.Empty;
        FaultRawXml = string.Empty;
        IsTransportError = false;
        TransportErrorMessage = string.Empty;
    }

    private void OnDeviceRemoved(DeviceRemovedEvent evt)
    {
        if (!string.Equals(evt.Uuid, Service.OwningDeviceUuid, StringComparison.Ordinal))
            return;

        _dispatcher.Post(() =>
        {
            IsDeviceUnreachable = true;
            try { _invocationCts?.Cancel(); }
            catch (ObjectDisposedException) { }
            InvokeCommand.NotifyCanExecuteChanged();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registry.DeviceRemoved -= OnDeviceRemoved;
        try { _invocationCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _invocationCts?.Dispose();
    }
}

public partial class InvocationInputViewModel : ObservableObject
{
    public ArgumentDefinition Argument { get; }
    public string Name => Argument.Name;
    public string? DataType => Argument.DataType;

    [ObservableProperty]
    private string _value = string.Empty;

    public InvocationInputViewModel(ArgumentDefinition argument)
    {
        Argument = argument ?? throw new ArgumentNullException(nameof(argument));
    }
}

public sealed record InvocationOutputViewModel(string Name, string Value);
