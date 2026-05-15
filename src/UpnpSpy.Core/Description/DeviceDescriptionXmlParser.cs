using System.Xml;
using System.Xml.Linq;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Description;

/// <summary>
/// Parses a UPnP device description document (UDA 1.0 §2.1). Walks
/// <c>&lt;deviceList&gt;</c> recursively and flattens every <c>&lt;service&gt;</c>
/// at any depth into <see cref="DeviceDescription.Services"/>, annotating each
/// with its immediate containing device's UDN/friendly name (research §20).
/// </summary>
public sealed class DeviceDescriptionXmlParser
{
    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;

    public DeviceDescriptionXmlParser(IDiagnosticSink diagnostics, IClock clock)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Returns <c>null</c> when the document is unparseable or missing the
    /// mandatory root <c>&lt;device&gt;</c>/<c>&lt;UDN&gt;</c>; caller maps that
    /// to <see cref="DeviceDescriptionFetchResult.ParseError"/>.
    /// </summary>
    public DeviceDescription? Parse(Stream xml, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(baseUri);

        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CloseInput = false,
            };
            using var reader = XmlReader.Create(xml, settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException) { return null; }
        catch (IOException) { return null; }

        var rootDevice = ChildElement(doc.Root, "device");
        if (rootDevice is null) return null;

        var udn = ChildText(rootDevice, "UDN");
        if (string.IsNullOrWhiteSpace(udn)) return null;
        var uuid = StripUuidPrefix(udn);
        if (uuid.Length == 0) return null;

        var rootFriendlyName = NormalizeOptional(ChildText(rootDevice, "friendlyName"));
        var deviceType = NormalizeOptional(ChildText(rootDevice, "deviceType")) ?? string.Empty;

        var manufacturer = NormalizeOptional(ChildText(rootDevice, "manufacturer"));
        var manufacturerUrl = ResolveOptional(baseUri, ChildText(rootDevice, "manufacturerURL"));
        var modelName = NormalizeOptional(ChildText(rootDevice, "modelName"));
        var modelDescription = NormalizeOptional(ChildText(rootDevice, "modelDescription"));
        var modelNumber = NormalizeOptional(ChildText(rootDevice, "modelNumber"));
        var modelUrl = ResolveOptional(baseUri, ChildText(rootDevice, "modelURL"));
        var serialNumber = NormalizeOptional(ChildText(rootDevice, "serialNumber"));
        var upc = NormalizeOptional(ChildText(rootDevice, "UPC"));
        var presentationUrl = ResolveOptional(baseUri, ChildText(rootDevice, "presentationURL"));

        var services = new List<ServiceDescriptor>();
        var seen = new HashSet<(string Udn, string ServiceId)>();
        CollectServices(rootDevice, baseUri, services, seen);

        var embeddedDevices = CollectEmbeddedDevices(rootDevice);

        return new DeviceDescription(
            uuid,
            rootFriendlyName,
            deviceType,
            manufacturer,
            manufacturerUrl,
            modelName,
            modelDescription,
            modelNumber,
            modelUrl,
            serialNumber,
            upc,
            presentationUrl,
            services,
            embeddedDevices);
    }

    private static IReadOnlyList<EmbeddedDeviceSummary> CollectEmbeddedDevices(XElement deviceElement)
    {
        var deviceList = ChildElement(deviceElement, "deviceList");
        if (deviceList is null) return Array.Empty<EmbeddedDeviceSummary>();

        var summaries = new List<EmbeddedDeviceSummary>();
        foreach (var child in ChildElements(deviceList, "device"))
        {
            var udn = ChildText(child, "UDN")?.Trim() ?? string.Empty;
            var type = NormalizeOptional(ChildText(child, "deviceType")) ?? string.Empty;
            var friendly = NormalizeOptional(ChildText(child, "friendlyName"));
            summaries.Add(new EmbeddedDeviceSummary(udn, type, friendly, CollectEmbeddedDevices(child)));
        }
        return summaries;
    }

    private void CollectServices(
        XElement deviceElement,
        Uri baseUri,
        List<ServiceDescriptor> services,
        HashSet<(string Udn, string ServiceId)> seen)
    {
        var udn = ChildText(deviceElement, "UDN")?.Trim() ?? string.Empty;
        var friendlyName = NormalizeOptional(ChildText(deviceElement, "friendlyName"));

        var serviceList = ChildElement(deviceElement, "serviceList");
        if (serviceList is not null)
        {
            foreach (var svc in ChildElements(serviceList, "service"))
            {
                var serviceId = ChildText(svc, "serviceId")?.Trim() ?? string.Empty;
                var serviceType = ChildText(svc, "serviceType")?.Trim() ?? string.Empty;
                var scpdUrl = ChildText(svc, "SCPDURL")?.Trim();
                var controlUrl = ChildText(svc, "controlURL")?.Trim();
                var eventSubUrl = ChildText(svc, "eventSubURL")?.Trim();

                if (serviceId.Length == 0 || serviceType.Length == 0
                    || string.IsNullOrEmpty(scpdUrl)
                    || string.IsNullOrEmpty(controlUrl)
                    || string.IsNullOrEmpty(eventSubUrl))
                {
                    continue;
                }

                var key = (udn, serviceId);
                if (!seen.Add(key))
                {
                    EmitDuplicateWarning(udn, serviceId);
                    continue;
                }

                if (!TryResolve(baseUri, scpdUrl, out var scpd)
                    || !TryResolve(baseUri, controlUrl, out var control)
                    || !TryResolve(baseUri, eventSubUrl, out var eventSub))
                {
                    continue;
                }

                services.Add(new ServiceDescriptor(
                    ContainingDeviceUdn: udn,
                    ContainingDeviceFriendlyName: friendlyName,
                    ServiceId: serviceId,
                    ServiceType: serviceType,
                    ScpdUrl: scpd,
                    ControlUrl: control,
                    EventSubUrl: eventSub));
            }
        }

        var deviceList = ChildElement(deviceElement, "deviceList");
        if (deviceList is not null)
        {
            foreach (var child in ChildElements(deviceList, "device"))
                CollectServices(child, baseUri, services, seen);
        }
    }

    private void EmitDuplicateWarning(string udn, string serviceId)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["device.udn"] = udn,
            ["service.id"] = serviceId,
        };
        _diagnostics.Record(new DiagnosticEntry(
            Timestamp: _clock.UtcNow,
            Severity: DiagnosticSeverity.Warning,
            Category: "Description.Parse",
            Message: "Duplicate <service> within the same <device>; second occurrence dropped.",
            Context: context,
            Exception: null));
    }

    private static string StripUuidPrefix(string udn) =>
        udn.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)
            ? udn[5..].Trim()
            : udn.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Uri? ResolveOptional(Uri baseUri, string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs)) return abs;
        if (Uri.TryCreate(baseUri, trimmed, out var rel)) return rel;
        return null;
    }

    private static bool TryResolve(Uri baseUri, string url, out Uri resolved)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            resolved = abs;
            return true;
        }
        if (Uri.TryCreate(baseUri, url, out var rel))
        {
            resolved = rel;
            return true;
        }
        resolved = baseUri;
        return false;
    }

    private static XElement? ChildElement(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    private static IEnumerable<XElement> ChildElements(XElement parent, string localName) =>
        parent.Elements().Where(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    private static string? ChildText(XElement parent, string localName)
    {
        var elem = ChildElement(parent, localName);
        return elem?.Value;
    }
}
