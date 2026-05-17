# Contract: `IClock`

**Namespace**: `UpnpSpy.Core.Platform`
**Lifetime**: Singleton
**Spec FR**: FR-038 (subscription timeouts), FR-014/015 (timestamps), supports SC-003, SC-009, SC-010, SC-011

A trivial abstraction over wall-clock time. Exists so every timestamp produced by the app (SSDP log row, diagnostic entry, subscription renewal due time, invocation submitted/completed) flows through a seam that tests can replace.

## C# signature

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

## Behavioural requirements

- The production implementation (`SystemClock`) returns `DateTimeOffset.UtcNow`.
- The test implementation (`FakeClock`) returns a settable value; tests advance time explicitly to verify, e.g., that `SubscriptionRenewalScheduler` schedules its next renewal exactly 30 seconds before the granted timeout.

## Notes

- The clock returns UTC. View-models convert to local time only at render boundaries.
- A timer abstraction (`ITimerFactory`) is **not** included here as a separate file because it is fully derivable from `IClock` plus `Task.Delay`/`Channel` patterns — the renewal scheduler uses `Task.Delay` and rechecks against `IClock.UtcNow` on each wake, which `FakeClock` cooperates with via a test-only fake-delay extension. Keeping the surface minimal here keeps the testable abstraction count low (Principle IV).
