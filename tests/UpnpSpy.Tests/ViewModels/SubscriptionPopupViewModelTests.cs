using System.Net;
using FluentAssertions;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.Eventing;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class SubscriptionPopupViewModelTests
{
    private static readonly TimeSpan Requested = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Granted = TimeSpan.FromMinutes(30);

    [Fact]
    public async Task Successful_subscribe_populates_sid_and_marks_active()
    {
        var ctx = await ArrangeAsync(client =>
            client.EnqueueSubscribe(new SubscribeResult.Success("uuid:sub-1", Granted)));

        ctx.Vm.Status.Should().Be(SubscriptionStatus.Active);
        ctx.Vm.State!.Sid.Should().Be("uuid:sub-1");
        ctx.Vm.State!.GrantedTimeout.Should().Be(Granted);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Subscribe_http_failure_marks_failed_and_close_does_not_unsubscribe()
    {
        var ctx = await ArrangeAsync(client =>
            client.EnqueueSubscribe(new SubscribeResult.HttpError(412, "Precondition Failed")));

        ctx.Vm.Status.Should().Be(SubscriptionStatus.Failed);
        ctx.Vm.FailureReason.Should().Contain("412");

        await ctx.Vm.CloseAsync();
        ctx.Client.UnsubscribeCalls.Should().BeEmpty();

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Events_flow_into_collection_newest_first_per_FR_033()
    {
        var ctx = await ArrangeAsync(client =>
            client.EnqueueSubscribe(new SubscribeResult.Success("uuid:sub-1", Granted)));

        var registration = ctx.Host.Registrations.Single();
        ctx.Host.Push(registration, new EventNotification(
            DateTimeOffset.UtcNow, 0,
            new Dictionary<string, string> { ["Volume"] = "10" }, null));
        ctx.Host.Push(registration, new EventNotification(
            DateTimeOffset.UtcNow, 1,
            new Dictionary<string, string> { ["Volume"] = "20" }, null));

        await WaitFor(() => ctx.Vm.Events.Count == 2);

        // FR-033: newest event at index 0, older events scroll off the bottom.
        ctx.Vm.Events.Select(e => e.SequenceNumber).Should().ContainInOrder(1u, 0u);
        ctx.Vm.Events[0].SequenceNumber.Should().Be(1u, "the most-recent event must be the first row");
        ctx.Vm.LatestProperties.Should().ContainSingle()
            .Which.Value.Should().Be("20");

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Close_while_active_sends_unsubscribe_with_sid()
    {
        var ctx = await ArrangeAsync(client =>
        {
            client.EnqueueSubscribe(new SubscribeResult.Success("uuid:sub-1", Granted));
            client.EnqueueUnsubscribe(new UnsubscribeResult.Success());
        });

        await ctx.Vm.CloseAsync();

        ctx.Client.UnsubscribeCalls.Should().ContainSingle()
            .Which.Sid.Should().Be("uuid:sub-1");
        ctx.Vm.Status.Should().Be(SubscriptionStatus.Closed);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renewal_failure_transitions_to_lapsed_and_close_does_not_unsubscribe()
    {
        var ctx = await ArrangeAsync(client =>
        {
            client.EnqueueSubscribe(new SubscribeResult.Success("uuid:sub-1", Granted));
            client.EnqueueRenew(new RenewResult.HttpError(412, "Precondition Failed"));
        });

        await WaitFor(() => ctx.Clock.PendingDelays.Count > 0);
        ctx.Clock.CompleteAllDelays();

        await WaitFor(() => ctx.Vm.Status == SubscriptionStatus.Lapsed);
        await ctx.Vm.CloseAsync();

        ctx.Client.UnsubscribeCalls.Should().BeEmpty();

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Device_byebye_marks_unreachable_and_skips_unsubscribe()
    {
        var ctx = await ArrangeAsync(client =>
            client.EnqueueSubscribe(new SubscribeResult.Success("uuid:sub-1", Granted)));

        ctx.Registry.Remove(ctx.Vm.Service.OwningDeviceUuid);

        await WaitFor(() => ctx.Vm.IsDeviceUnreachable);
        await ctx.Vm.CloseAsync();

        ctx.Client.UnsubscribeCalls.Should().BeEmpty();
        ctx.Vm.Status.Should().Be(SubscriptionStatus.Closed);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Event_overflow_drops_oldest_without_crashing()
    {
        var ctx = await ArrangeAsync(client =>
            client.EnqueueSubscribe(new SubscribeResult.Success("uuid:sub-1", Granted)));

        // BoundedObservableCollection capacity is 5_000 — pushing past it must
        // not throw, and Count must stay bounded.
        var registration = ctx.Host.Registrations.Single();
        for (var i = 0; i < 5_050; i++)
        {
            ctx.Host.Push(registration, new EventNotification(
                DateTimeOffset.UtcNow, (uint)i,
                new Dictionary<string, string> { ["Counter"] = i.ToString() },
                null));
        }

        await WaitFor(() => ctx.Vm.Events.Count >= 5_000);
        ctx.Vm.Events.Count.Should().BeLessThanOrEqualTo(5_000);
        // FR-033: newest-first, so on overflow the most-recent SEQ stays at index 0
        // and the oldest is dropped from the tail.
        ctx.Vm.Events[0].SequenceNumber.Should().Be(5_049u);

        await ctx.DisposeAsync();
    }

    private static async Task<Context> ArrangeAsync(Action<FakeSubscriptionClient> configure)
    {
        var service = new Service
        {
            OwningDeviceUuid = "uuid-1",
            ContainingDeviceUdn = "uuid:uuid-1",
            ServiceId = "urn:upnp-org:serviceId:AVT",
            ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
            ScpdUrl = new Uri("http://192.0.2.10/scpd"),
            ControlUrl = new Uri("http://192.0.2.10/ctrl"),
            EventSubUrl = new Uri("http://192.0.2.10/evt"),
        };

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(new Device
        {
            Uuid = service.OwningDeviceUuid,
            LocationUrl = new Uri("http://192.0.2.10/desc.xml"),
        });

        var client = new FakeSubscriptionClient();
        configure(client);
        var host = new FakeEventCallbackHost();
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);

        var clock = new FakeClock();
        var vm = new SubscriptionPopupViewModel(
            service,
            client, host, registry,
            new SynchronousDispatcher(), clock,
            Requested, CancellationToken.None);

        await vm.StartAsync();

        return new Context(vm, client, host, registry, clock);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        condition().Should().BeTrue();
    }

    private sealed record Context(
        SubscriptionPopupViewModel Vm,
        FakeSubscriptionClient Client,
        FakeEventCallbackHost Host,
        DeviceRegistry Registry,
        FakeClock Clock)
    {
        public async Task DisposeAsync()
        {
            await Vm.DisposeAsync();
            await Host.DisposeAsync();
        }
    }
}
