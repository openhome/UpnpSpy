using FluentAssertions;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Models;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Eventing;

public sealed class SubscriptionRenewalSchedulerTests
{
    private static readonly TimeSpan Granted = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Requested = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ExpectedLead = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task First_renewal_fires_30s_before_granted_timeout()
    {
        var clock = new FakeClock();
        var client = new FakeSubscriptionClient();
        var state = MakeState(clock, Granted);
        client.EnqueueRenew(new RenewResult.Success(Granted));

        await using var scheduler = new SubscriptionRenewalScheduler(
            client, clock, state, Requested, CancellationToken.None);

        scheduler.Start();
        await WaitForPendingDelay(clock);

        clock.PendingDelays.Should().ContainSingle()
            .Which.Should().Be(Granted - ExpectedLead);
    }

    [Fact]
    public async Task Successful_renewal_reschedules_off_new_granted_timeout()
    {
        var clock = new FakeClock();
        var client = new FakeSubscriptionClient();
        var state = MakeState(clock, Granted);
        var newGranted = TimeSpan.FromMinutes(10);
        client.EnqueueRenew(new RenewResult.Success(newGranted));

        await using var scheduler = new SubscriptionRenewalScheduler(
            client, clock, state, Requested, CancellationToken.None);

        scheduler.Start();
        await WaitForPendingDelay(clock);
        clock.Advance(Granted - ExpectedLead);
        clock.CompleteAllDelays();

        await WaitFor(() => client.RenewCalls.Count == 1);
        await WaitForPendingDelay(clock);

        state.GrantedTimeout.Should().Be(newGranted);
        clock.PendingDelays.Should().ContainSingle()
            .Which.Should().Be(newGranted - ExpectedLead);
    }

    [Fact]
    public async Task Http_error_transitions_to_Lapsed_and_stops()
    {
        var clock = new FakeClock();
        var client = new FakeSubscriptionClient();
        var state = MakeState(clock, Granted);
        client.EnqueueRenew(new RenewResult.HttpError(412, "Precondition Failed"));

        await using var scheduler = new SubscriptionRenewalScheduler(
            client, clock, state, Requested, CancellationToken.None);

        scheduler.Start();
        await WaitForPendingDelay(clock);
        clock.CompleteAllDelays();
        // Wait on both fields: the scheduler sets Status before FailureReason,
        // so polling on Status alone can read FailureReason==null on another thread.
        await WaitFor(() => state.Status == SubscriptionStatus.Lapsed && state.FailureReason is not null);

        state.Status.Should().Be(SubscriptionStatus.Lapsed);
        state.FailureReason.Should().Contain("412");

        // No further delays are queued after lapse.
        await Task.Yield();
        clock.PendingDelays.Should().BeEmpty();
        client.RenewCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Transport_error_transitions_to_Lapsed_and_stops()
    {
        var clock = new FakeClock();
        var client = new FakeSubscriptionClient();
        var state = MakeState(clock, Granted);
        client.EnqueueRenew(new RenewResult.TransportError("connection reset", null));

        await using var scheduler = new SubscriptionRenewalScheduler(
            client, clock, state, Requested, CancellationToken.None);

        scheduler.Start();
        await WaitForPendingDelay(clock);
        clock.CompleteAllDelays();
        await WaitFor(() => state.Status == SubscriptionStatus.Lapsed && state.FailureReason is not null);

        state.Status.Should().Be(SubscriptionStatus.Lapsed);
        state.FailureReason.Should().Contain("connection reset");
        client.RenewCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Dispose_cancels_pending_delay_cleanly()
    {
        var clock = new FakeClock();
        var client = new FakeSubscriptionClient();
        var state = MakeState(clock, Granted);

        var scheduler = new SubscriptionRenewalScheduler(
            client, clock, state, Requested, CancellationToken.None);

        scheduler.Start();
        await WaitForPendingDelay(clock);

        await scheduler.DisposeAsync();

        client.RenewCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Onlapsed_callback_invoked_with_renewal_result()
    {
        var clock = new FakeClock();
        var client = new FakeSubscriptionClient();
        var state = MakeState(clock, Granted);
        client.EnqueueRenew(new RenewResult.HttpError(500, "Server Error"));

        RenewResult? observed = null;
        await using var scheduler = new SubscriptionRenewalScheduler(
            client, clock, state, Requested, CancellationToken.None,
            onLapsed: r => observed = r);

        scheduler.Start();
        await WaitForPendingDelay(clock);
        clock.CompleteAllDelays();
        await WaitFor(() => observed is not null);

        observed.Should().BeOfType<RenewResult.HttpError>()
            .Which.StatusCode.Should().Be(500);
    }

    private static SubscriptionState MakeState(FakeClock clock, TimeSpan granted)
    {
        var service = new Service
        {
            OwningDeviceUuid = "uuid-1",
            ContainingDeviceUdn = "uuid:uuid-1",
            ServiceId = "urn:upnp-org:serviceId:AVT",
            ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
            ScpdUrl = new Uri("http://1.2.3.4/scpd"),
            ControlUrl = new Uri("http://1.2.3.4/ctrl"),
            EventSubUrl = new Uri("http://1.2.3.4/evt"),
        };

        return new SubscriptionState
        {
            Service = service,
            CallbackUrl = new Uri("http://127.0.0.1:9000/upnpspy/abc/"),
            CreatedUtc = clock.UtcNow,
            Sid = "uuid:sub-1",
            GrantedTimeout = granted,
            RenewalDueUtc = clock.UtcNow + granted - TimeSpan.FromSeconds(30),
            Status = SubscriptionStatus.Active,
        };
    }

    private static async Task WaitForPendingDelay(FakeClock clock)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (clock.PendingDelays.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        clock.PendingDelays.Should().NotBeEmpty("scheduler should have parked on a delay");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        condition().Should().BeTrue();
    }
}
