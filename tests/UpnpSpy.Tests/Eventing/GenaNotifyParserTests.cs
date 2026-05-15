using FluentAssertions;
using UpnpSpy.Core.Eventing;
using Xunit;

namespace UpnpSpy.Tests.Eventing;

public sealed class GenaNotifyParserTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Single_property_notification_is_parsed()
    {
        const string body = """
            <?xml version="1.0"?>
            <e:propertyset xmlns:e="urn:schemas-upnp-org:event-1-0">
              <e:property><Volume>42</Volume></e:property>
            </e:propertyset>
            """;

        var ev = GenaNotifyParser.Parse(body, 0, At);

        ev.SequenceNumber.Should().Be(0);
        ev.Properties.Should().BeEquivalentTo(new Dictionary<string, string> { ["Volume"] = "42" });
        ev.RawXml.Should().BeNull();
    }

    [Fact]
    public void Multi_property_notification_preserves_all_values()
    {
        const string body = """
            <?xml version="1.0"?>
            <e:propertyset xmlns:e="urn:schemas-upnp-org:event-1-0">
              <e:property><Volume>10</Volume></e:property>
              <e:property><Mute>0</Mute></e:property>
              <e:property><Channel>Master</Channel></e:property>
            </e:propertyset>
            """;

        var ev = GenaNotifyParser.Parse(body, 7, At);

        ev.SequenceNumber.Should().Be(7u);
        ev.Properties.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Volume"] = "10",
            ["Mute"] = "0",
            ["Channel"] = "Master",
        });
    }

    [Fact]
    public void Bare_property_set_without_namespace_prefix_is_parsed()
    {
        const string body = """
            <?xml version="1.0"?>
            <propertyset xmlns="urn:schemas-upnp-org:event-1-0">
              <property><LastChange>0</LastChange></property>
            </propertyset>
            """;

        var ev = GenaNotifyParser.Parse(body, 1, At);

        ev.Properties.Should().ContainKey("LastChange").WhoseValue.Should().Be("0");
    }

    [Fact]
    public void Malformed_xml_returns_raw_only_without_throwing()
    {
        const string body = "<e:propertyset><not closed";
        var ev = GenaNotifyParser.Parse(body, 2, At);

        ev.Properties.Should().BeEmpty();
        ev.RawXml.Should().Be(body);
    }

    [Fact]
    public void Empty_body_returns_empty_properties_and_null_raw()
    {
        var ev = GenaNotifyParser.Parse(string.Empty, 0, At);

        ev.Properties.Should().BeEmpty();
        ev.RawXml.Should().BeNull();
    }

    [Fact]
    public void Wrong_root_element_returns_raw_only()
    {
        const string body = "<something xmlns=\"urn:schemas-upnp-org:event-1-0\"/>";
        var ev = GenaNotifyParser.Parse(body, 0, At);

        ev.Properties.Should().BeEmpty();
        ev.RawXml.Should().Be(body);
    }

    [Fact]
    public void Property_with_empty_inner_is_recorded_as_empty_string()
    {
        const string body = """
            <?xml version="1.0"?>
            <e:propertyset xmlns:e="urn:schemas-upnp-org:event-1-0">
              <e:property><Status></Status></e:property>
            </e:propertyset>
            """;

        var ev = GenaNotifyParser.Parse(body, 0, At);

        ev.Properties.Should().ContainKey("Status").WhoseValue.Should().BeEmpty();
    }
}
