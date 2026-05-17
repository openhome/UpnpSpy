using UpnpSpy.Core.Collections;

namespace UpnpSpy.Core.Models;

public sealed class SubscriptionState
{
    public required Service Service { get; init; }
    public required Uri CallbackUrl { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }

    public string? Sid { get; set; }
    public TimeSpan GrantedTimeout { get; set; }
    public DateTimeOffset RenewalDueUtc { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;
    public string? FailureReason { get; set; }

    public BoundedObservableCollection<EventNotification> Events { get; } = new(capacity: 5_000);
}
