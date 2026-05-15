namespace UpnpSpy.Core.Ssdp;

/// <summary>
/// Common base for the two parsed SSDP message shapes the parser emits.
/// Carries the device's bare UUID (pre-extracted from USN per UDA 1.0 §1.3) and the
/// raw USN string for downstream logging.
/// </summary>
public abstract record ParsedSsdpMessage(string Uuid, string Usn);
