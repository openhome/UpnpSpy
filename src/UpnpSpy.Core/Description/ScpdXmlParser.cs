using System.Xml;
using System.Xml.Linq;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Description;

/// <summary>
/// Parses a UPnP SCPD document (UDA 1.0 §2.2). Preserves SCPD-declared order
/// of input and output arguments — the SOAP envelope built in US7 must emit
/// arguments in that exact order (UDA 1.0 §3.1.1).
/// </summary>
public sealed class ScpdXmlParser
{
    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;

    public ScpdXmlParser(IDiagnosticSink diagnostics, IClock clock)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Returns <c>null</c> on malformed SCPD or invalid argument direction;
    /// caller maps that to <see cref="ScpdFetchResult.ParseError"/>.
    /// </summary>
    public ScpdDocument? Parse(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

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
        catch (XmlException ex)
        {
            EmitWarning("Malformed SCPD XML.", ex);
            return null;
        }
        catch (IOException ex)
        {
            EmitWarning("Failed to read SCPD XML.", ex);
            return null;
        }

        if (doc.Root is null) return null;

        var stateVariables = ParseStateVariables(doc.Root);
        var stateVariableLookup = stateVariables.ToDictionary(
            sv => sv.Name,
            sv => sv.DataType,
            StringComparer.Ordinal);

        var actions = ParseActions(doc.Root, stateVariableLookup);
        if (actions is null) return null;

        return new ScpdDocument(actions, stateVariables);
    }

    private IReadOnlyList<StateVariableDefinition> ParseStateVariables(XElement root)
    {
        var table = ChildElement(root, "serviceStateTable");
        if (table is null) return Array.Empty<StateVariableDefinition>();

        var result = new List<StateVariableDefinition>();
        foreach (var sv in ChildElements(table, "stateVariable"))
        {
            var name = ChildText(sv, "name")?.Trim();
            var dataType = ChildText(sv, "dataType")?.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dataType)) continue;

            var sendEventsAttr = sv.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, "sendEvents", StringComparison.Ordinal))?.Value;
            var sendsEvents = !string.Equals(sendEventsAttr, "no", StringComparison.OrdinalIgnoreCase);

            IReadOnlyList<string>? allowed = null;
            var allowedList = ChildElement(sv, "allowedValueList");
            if (allowedList is not null)
            {
                allowed = ChildElements(allowedList, "allowedValue")
                    .Select(e => e.Value)
                    .ToArray();
            }

            result.Add(new StateVariableDefinition(name, dataType, sendsEvents, allowed));
        }
        return result;
    }

    private IReadOnlyList<ActionDefinition>? ParseActions(
        XElement root,
        IReadOnlyDictionary<string, string> stateVariableLookup)
    {
        var actionList = ChildElement(root, "actionList");
        if (actionList is null) return Array.Empty<ActionDefinition>();

        var result = new List<ActionDefinition>();
        foreach (var action in ChildElements(actionList, "action"))
        {
            var name = ChildText(action, "name")?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var inputs = new List<ArgumentDefinition>();
            var outputs = new List<ArgumentDefinition>();

            var argumentList = ChildElement(action, "argumentList");
            if (argumentList is not null)
            {
                foreach (var arg in ChildElements(argumentList, "argument"))
                {
                    var argName = ChildText(arg, "name")?.Trim();
                    var directionText = ChildText(arg, "direction")?.Trim();
                    var relatedSv = ChildText(arg, "relatedStateVariable")?.Trim() ?? string.Empty;

                    if (string.IsNullOrEmpty(argName) || string.IsNullOrEmpty(directionText))
                        continue;

                    ArgumentDirection direction;
                    if (string.Equals(directionText, "in", StringComparison.OrdinalIgnoreCase))
                    {
                        direction = ArgumentDirection.In;
                    }
                    else if (string.Equals(directionText, "out", StringComparison.OrdinalIgnoreCase))
                    {
                        direction = ArgumentDirection.Out;
                    }
                    else
                    {
                        EmitWarning(
                            $"SCPD action '{name}' argument '{argName}' has invalid direction '{directionText}'.",
                            null);
                        return null;
                    }

                    stateVariableLookup.TryGetValue(relatedSv, out var dataType);
                    var argument = new ArgumentDefinition(argName, direction, relatedSv, dataType);
                    (direction == ArgumentDirection.In ? inputs : outputs).Add(argument);
                }
            }

            result.Add(new ActionDefinition(name, inputs, outputs));
        }
        return result;
    }

    private void EmitWarning(string message, Exception? exception)
    {
        _diagnostics.Record(new DiagnosticEntry(
            Timestamp: _clock.UtcNow,
            Severity: DiagnosticSeverity.Warning,
            Category: "Scpd.Parse",
            Message: message,
            Context: new Dictionary<string, string>(StringComparer.Ordinal),
            Exception: exception?.ToString()));
    }

    private static XElement? ChildElement(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    private static IEnumerable<XElement> ChildElements(XElement parent, string localName) =>
        parent.Elements().Where(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    private static string? ChildText(XElement parent, string localName) =>
        ChildElement(parent, localName)?.Value;
}
