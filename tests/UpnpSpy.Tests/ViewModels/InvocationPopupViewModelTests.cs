using FluentAssertions;
using UpnpSpy.Core.Control;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.Control;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class InvocationPopupViewModelTests
{
    private static readonly Uri ControlUrl = new("http://192.168.1.10:8080/avt/ctrl");

    [Fact]
    public async Task Invoke_builds_request_from_input_values()
    {
        var service = MakeService();
        var action = new ActionDefinition(
            "Seek",
            new[]
            {
                new ArgumentDefinition("InstanceID", ArgumentDirection.In, "A_ARG_TYPE_InstanceID", "ui4"),
                new ArgumentDefinition("Unit", ArgumentDirection.In, "A_ARG_TYPE_SeekMode", "string"),
            },
            Array.Empty<ArgumentDefinition>());

        var control = new FakeControlClient();
        control.SetResult("Seek", new InvocationResult.Success(
            new Dictionary<string, string>(),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = MakeViewModel(service, action, control);
        sut.Inputs[0].Value = "0";
        sut.Inputs[1].Value = "REL_TIME";

        await sut.InvokeCommand.ExecuteAsync(null);

        control.Calls.Should().ContainSingle();
        var call = control.Calls[0];
        call.Service.Should().BeSameAs(service);
        call.Action.Should().BeSameAs(action);
        call.Inputs.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["InstanceID"] = "0",
            ["Unit"] = "REL_TIME",
        });
    }

    [Fact]
    public async Task Success_populates_outputs_and_marks_success()
    {
        var service = MakeService();
        var action = new ActionDefinition(
            "GetVolume",
            new[] { new ArgumentDefinition("InstanceID", ArgumentDirection.In, "A_ARG_TYPE_InstanceID", "ui4") },
            new[] { new ArgumentDefinition("CurrentVolume", ArgumentDirection.Out, "Volume", "ui2") });
        var control = new FakeControlClient();
        control.SetResult("GetVolume", new InvocationResult.Success(
            new Dictionary<string, string> { ["CurrentVolume"] = "42" },
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = MakeViewModel(service, action, control);
        sut.Inputs[0].Value = "0";

        await sut.InvokeCommand.ExecuteAsync(null);

        sut.HasResult.Should().BeTrue();
        sut.IsSuccess.Should().BeTrue();
        sut.IsFault.Should().BeFalse();
        sut.IsTransportError.Should().BeFalse();
        sut.Outputs.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new InvocationOutputViewModel("CurrentVolume", "42"));
        sut.SuccessMessage.Should().Be("Succeeded");
    }

    [Fact]
    public async Task Soap_fault_populates_http_and_error_fields()
    {
        var service = MakeService();
        var action = new ActionDefinition("Seek", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>());
        var control = new FakeControlClient();
        control.SetResult("Seek", new InvocationResult.UpnpFault(
            HttpStatusCode: 500,
            UpnpErrorCode: 711,
            UpnpErrorDescription: "Illegal seek target",
            RawFaultXml: "<UPnPError>raw</UPnPError>",
            CompletedUtc: new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = MakeViewModel(service, action, control);

        await sut.InvokeCommand.ExecuteAsync(null);

        sut.HasResult.Should().BeTrue();
        sut.IsFault.Should().BeTrue();
        sut.IsSuccess.Should().BeFalse();
        sut.FaultHttpStatus.Should().Be(500);
        sut.FaultErrorCode.Should().Be(711);
        sut.FaultErrorDescription.Should().Be("Illegal seek target");
        sut.FaultRawXml.Should().Contain("UPnPError");
    }

    [Fact]
    public async Task Transport_error_populates_message()
    {
        var service = MakeService();
        var action = new ActionDefinition("Stop", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>());
        var control = new FakeControlClient();
        control.SetResult("Stop", new InvocationResult.TransportError(
            "Connection refused",
            new IOException("refused"),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = MakeViewModel(service, action, control);

        await sut.InvokeCommand.ExecuteAsync(null);

        sut.HasResult.Should().BeTrue();
        sut.IsTransportError.Should().BeTrue();
        sut.TransportErrorMessage.Should().Be("Connection refused");
    }

    [Fact]
    public async Task Zero_input_action_invocable()
    {
        var service = MakeService();
        var action = new ActionDefinition("Stop", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>());
        var control = new FakeControlClient();
        control.SetResult("Stop", new InvocationResult.Success(
            new Dictionary<string, string>(),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = MakeViewModel(service, action, control);

        sut.Inputs.Should().BeEmpty();
        await sut.InvokeCommand.ExecuteAsync(null);

        control.Calls.Should().ContainSingle().Which.Inputs.Should().BeEmpty();
        sut.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Zero_output_success_shows_no_output_values_message()
    {
        var service = MakeService();
        var action = new ActionDefinition("Pause", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>());
        var control = new FakeControlClient();
        control.SetResult("Pause", new InvocationResult.Success(
            new Dictionary<string, string>(),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = MakeViewModel(service, action, control);

        await sut.InvokeCommand.ExecuteAsync(null);

        sut.SuccessMessage.Should().Be("Succeeded (no output values)");
        sut.Outputs.Should().BeEmpty();
    }

    [Fact]
    public async Task Device_byebye_during_invocation_flips_to_unreachable()
    {
        var service = MakeService();
        var action = new ActionDefinition("Play", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>());

        var registry = new DeviceRegistry();
        var device = new Device
        {
            Uuid = service.OwningDeviceUuid,
            LocationUrl = new Uri("http://192.168.1.10:8080/desc.xml"),
        };
        registry.TryAddOrUpdate(device);

        var control = new FakeControlClient
        {
            CompletionGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        control.SetResult("Play", new InvocationResult.Success(
            new Dictionary<string, string>(),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)));

        using var sut = new InvocationPopupViewModel(
            service, action, control, registry,
            new SynchronousDispatcher(), new FakeClock(), CancellationToken.None);

        var invocation = sut.InvokeCommand.ExecuteAsync(null);

        registry.Remove(service.OwningDeviceUuid);

        // Release the gate so the awaiting task completes (it should have observed cancellation).
        control.CompletionGate.SetResult(true);
        await invocation;

        sut.IsDeviceUnreachable.Should().BeTrue();
        sut.InvokeCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Subsequent_invocation_clears_previous_result_state()
    {
        var service = MakeService();
        var action = new ActionDefinition(
            "GetVolume",
            Array.Empty<ArgumentDefinition>(),
            new[] { new ArgumentDefinition("CurrentVolume", ArgumentDirection.Out, "Volume", "ui2") });
        var control = new FakeControlClient();

        using var sut = MakeViewModel(service, action, control);

        control.Default = new InvocationResult.UpnpFault(500, 401, "Invalid Action", "<raw/>",
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        await sut.InvokeCommand.ExecuteAsync(null);
        sut.IsFault.Should().BeTrue();

        control.Default = new InvocationResult.Success(
            new Dictionary<string, string> { ["CurrentVolume"] = "10" },
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        await sut.InvokeCommand.ExecuteAsync(null);

        sut.IsFault.Should().BeFalse();
        sut.IsSuccess.Should().BeTrue();
        sut.Outputs.Should().ContainSingle();
    }

    private static Service MakeService() => new()
    {
        OwningDeviceUuid = "root-uuid",
        ContainingDeviceUdn = "uuid:root-uuid",
        ContainingDeviceFriendlyName = "Root",
        ServiceId = "urn:upnp-org:serviceId:AVT",
        ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
        ScpdUrl = new Uri("http://192.168.1.10:8080/avt/scpd.xml"),
        ControlUrl = ControlUrl,
        EventSubUrl = new Uri("http://192.168.1.10:8080/avt/evt"),
    };

    private static InvocationPopupViewModel MakeViewModel(Service service, ActionDefinition action, IControlClient control) =>
        new(service, action, control, new DeviceRegistry(),
            new SynchronousDispatcher(), new FakeClock(), CancellationToken.None);
}
