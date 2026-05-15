using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Discovery;

public abstract record DeviceRegistryEvent;

public sealed record DeviceAddedEvent(Device Device) : DeviceRegistryEvent;

public sealed record DeviceUpdatedEvent(Device Device) : DeviceRegistryEvent;

public sealed record DeviceRemovedEvent(string Uuid) : DeviceRegistryEvent;
