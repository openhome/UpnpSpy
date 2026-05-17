using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// FR-052: read-only view-model behind the device Properties window. Surfaces
/// every metadata field the application has captured for one
/// <see cref="Device"/>, with <see cref="Placeholder"/> in place of any field
/// the device did not declare so the rendered grid is unambiguous. Watches
/// <see cref="DeviceRegistry"/> so the window can show a "device no longer
/// reachable" banner per FR-037 if the user keeps it open after byebye.
///
/// Exposes two interfaces: (a) flat <c>FriendlyName</c>/<c>DeviceType</c>/etc.
/// properties for test access; (b) a <c>Sections</c> collection consumed by the
/// XAML's ItemsControl-of-ItemsControl rendering, so the view stays declarative.
/// </summary>
public partial class DevicePropertiesViewModel : ObservableObject, IDisposable
{
    /// <summary>Rendered in place of any field the device did not declare.</summary>
    public const string Placeholder = "—";

    private readonly DeviceRegistry _registry;
    private bool _disposed;

    public Device Device { get; }
    public string Title { get; }

    public string FriendlyName { get; }
    public string DeviceType { get; }
    public string Uuid { get; }
    public string PresentationUrlText { get; }
    public Uri? PresentationUrl { get; }

    public string Manufacturer { get; }
    public string ManufacturerUrlText { get; }
    public Uri? ManufacturerUrl { get; }
    public string ModelName { get; }
    public string ModelNumber { get; }
    public string ModelDescription { get; }
    public string ModelUrlText { get; }
    public Uri? ModelUrl { get; }
    public string SerialNumber { get; }
    public string Upc { get; }

    public string LocationUrlText { get; }
    public Uri LocationUrl { get; }
    public string Endpoint { get; }
    public string ServerHeader { get; }
    public string CacheControlMaxAge { get; }

    public string FirstSeenUtc { get; }
    public string LastSeenUtc { get; }
    public string AliveCount { get; }
    public string BootId { get; }
    public string ConfigId { get; }

    public IReadOnlyList<EmbeddedDeviceSummary> EmbeddedDevices { get; }

    /// <summary>
    /// Declarative section/row tree consumed by the view. Built once at
    /// construction from the flat properties above.
    /// </summary>
    public IReadOnlyList<PropertySection> Sections { get; }

    [ObservableProperty]
    private bool _isDeviceUnreachable;

    public DevicePropertiesViewModel(Device device, DeviceRegistry registry)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        Title = $"Properties · {device.Label}";

        FriendlyName = device.Label;
        DeviceType = Or(device.DeviceType);
        Uuid = device.Uuid;
        PresentationUrl = device.PresentationUrl;
        PresentationUrlText = device.PresentationUrl?.ToString() ?? Placeholder;

        Manufacturer = Or(device.Manufacturer);
        ManufacturerUrl = device.ManufacturerUrl;
        ManufacturerUrlText = device.ManufacturerUrl?.ToString() ?? Placeholder;
        ModelName = Or(device.ModelName);
        ModelNumber = Or(device.ModelNumber);
        ModelDescription = Or(device.ModelDescription);
        ModelUrl = device.ModelUrl;
        ModelUrlText = device.ModelUrl?.ToString() ?? Placeholder;
        SerialNumber = Or(device.SerialNumber);
        Upc = Or(device.Upc);

        LocationUrl = device.LocationUrl;
        LocationUrlText = device.LocationUrl.ToString();
        Endpoint = $"{device.LocationUrl.Host}:{device.LocationUrl.Port}";
        ServerHeader = Or(device.ServerHeader);
        CacheControlMaxAge = device.CacheControlMaxAge is int max
            ? $"{max.ToString(CultureInfo.InvariantCulture)} s"
            : Placeholder;

        FirstSeenUtc = device.FirstSeenUtc == default
            ? Placeholder
            : device.FirstSeenUtc.ToLocalTime().ToString("u", CultureInfo.InvariantCulture);
        LastSeenUtc = device.LastSeenUtc == default
            ? Placeholder
            : device.LastSeenUtc.ToLocalTime().ToString("u", CultureInfo.InvariantCulture);
        AliveCount = device.AliveCount.ToString(CultureInfo.InvariantCulture);
        BootId = device.BootId?.ToString(CultureInfo.InvariantCulture) ?? Placeholder;
        ConfigId = device.ConfigId?.ToString(CultureInfo.InvariantCulture) ?? Placeholder;

        EmbeddedDevices = device.EmbeddedDevices;

        Sections = BuildSections();

        _registry.DeviceRemoved += OnDeviceRemoved;
    }

    private IReadOnlyList<PropertySection> BuildSections() => new[]
    {
        new PropertySection("Identity", new PropertyRow[]
        {
            new PropertyTextRow("Friendly name", FriendlyName),
            new PropertyTextRow("Device type", DeviceType, Monospace: true),
            new PropertyTextRow("UUID", Uuid, Monospace: true),
            PresentationUrl is null
                ? new PropertyTextRow("Presentation URL", PresentationUrlText)
                : new PropertyLinkRow("Presentation URL", PresentationUrlText, PresentationUrl),
        }),
        new PropertySection("Manufacturer", new PropertyRow[]
        {
            new PropertyTextRow("Manufacturer", Manufacturer),
            ManufacturerUrl is null
                ? new PropertyTextRow("Manufacturer URL", ManufacturerUrlText)
                : new PropertyLinkRow("Manufacturer URL", ManufacturerUrlText, ManufacturerUrl),
            new PropertyTextRow("Model name", ModelName),
            new PropertyTextRow("Model number", ModelNumber),
            new PropertyTextRow("Model description", ModelDescription),
            ModelUrl is null
                ? new PropertyTextRow("Model URL", ModelUrlText)
                : new PropertyLinkRow("Model URL", ModelUrlText, ModelUrl),
            new PropertyTextRow("Serial number", SerialNumber, Monospace: true),
            new PropertyTextRow("UPC", Upc, Monospace: true),
        }),
        new PropertySection("Network", new PropertyRow[]
        {
            new PropertyLinkRow("Location URL", LocationUrlText, LocationUrl),
            new PropertyTextRow("Endpoint", Endpoint, Monospace: true),
            new PropertyTextRow("SERVER header", ServerHeader, Monospace: true),
            new PropertyTextRow("CACHE-CONTROL max-age", CacheControlMaxAge),
        }),
        new PropertySection("Discovery history", new PropertyRow[]
        {
            new PropertyTextRow("First seen", FirstSeenUtc, Monospace: true),
            new PropertyTextRow("Last seen", LastSeenUtc, Monospace: true),
            new PropertyTextRow("Alive count", AliveCount),
            new PropertyTextRow("BOOTID.UPNP.ORG", BootId),
            new PropertyTextRow("CONFIGID.UPNP.ORG", ConfigId),
        }),
    };

    /// <summary>
    /// Build a single plain-text snapshot of every section/row so the view can
    /// offer a "Copy all" affordance (review item #6 / properties polish).
    /// </summary>
    public string ToClipboardText()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Title).Append('\n').Append('\n');
        foreach (var section in Sections)
        {
            sb.Append(section.Title).Append('\n');
            foreach (var row in section.Rows)
                sb.Append("  ").Append(row.Label).Append(": ").Append(row.Value).Append('\n');
            sb.Append('\n');
        }
        if (EmbeddedDevices.Count > 0)
        {
            sb.Append("Embedded devices\n");
            foreach (var ed in EmbeddedDevices)
                sb.Append("  ").Append(ed.FriendlyName).Append(" — ").Append(ed.DeviceType)
                  .Append(" (").Append(ed.Udn).Append(")\n");
        }
        return sb.ToString();
    }

    private void OnDeviceRemoved(DeviceRemovedEvent evt)
    {
        if (string.Equals(evt.Uuid, Device.Uuid, StringComparison.Ordinal))
            IsDeviceUnreachable = true;
    }

    private static string Or(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Placeholder : value;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registry.DeviceRemoved -= OnDeviceRemoved;
    }
}

/// <summary>One grouped block of rows in the Properties window.</summary>
public sealed record PropertySection(string Title, IReadOnlyList<PropertyRow> Rows);

/// <summary>
/// Base type for one label/value pair in the Properties window. The two
/// subtypes drive a DataTemplateSelector so the view renders text and link
/// rows with distinct templates — no converters required (and converters are
/// unsupported inside DataTemplates whose XAML root is a WinUI Window).
/// </summary>
public abstract record PropertyRow(string Label, string Value);

/// <summary>A plain text value row (read-only).</summary>
public sealed record PropertyTextRow(string Label, string Value, bool Monospace = false)
    : PropertyRow(Label, Value);

/// <summary>A value row rendered as a HyperlinkButton when the link is present.</summary>
public sealed record PropertyLinkRow(string Label, string Value, Uri LinkUri)
    : PropertyRow(Label, Value);
