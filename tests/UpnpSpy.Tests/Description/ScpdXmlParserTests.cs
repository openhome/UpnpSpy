using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Description;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Description;

public sealed class ScpdXmlParserTests
{
    [Fact]
    public void Parses_action_with_zero_inputs_zero_outputs()
    {
        var (sut, _) = CreateSut();
        var doc = sut.Parse(Bytes(@"
<scpd xmlns='urn:schemas-upnp-org:service-1-0'>
  <actionList>
    <action><name>Stop</name></action>
  </actionList>
  <serviceStateTable/>
</scpd>"));

        doc.Should().NotBeNull();
        doc!.Actions.Should().HaveCount(1);
        doc.Actions[0].Name.Should().Be("Stop");
        doc.Actions[0].Inputs.Should().BeEmpty();
        doc.Actions[0].Outputs.Should().BeEmpty();
    }

    [Fact]
    public void Preserves_argument_order_and_separates_in_out()
    {
        var (sut, _) = CreateSut();
        var doc = sut.Parse(Bytes(@"
<scpd>
  <actionList>
    <action>
      <name>SetVolume</name>
      <argumentList>
        <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_InstanceID</relatedStateVariable></argument>
        <argument><name>Channel</name><direction>in</direction><relatedStateVariable>A_ARG_Channel</relatedStateVariable></argument>
        <argument><name>DesiredVolume</name><direction>in</direction><relatedStateVariable>Volume</relatedStateVariable></argument>
      </argumentList>
    </action>
    <action>
      <name>GetVolume</name>
      <argumentList>
        <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_InstanceID</relatedStateVariable></argument>
        <argument><name>Channel</name><direction>in</direction><relatedStateVariable>A_ARG_Channel</relatedStateVariable></argument>
        <argument><name>CurrentVolume</name><direction>out</direction><relatedStateVariable>Volume</relatedStateVariable></argument>
      </argumentList>
    </action>
  </actionList>
  <serviceStateTable>
    <stateVariable sendEvents='no'><name>Volume</name><dataType>ui2</dataType></stateVariable>
    <stateVariable sendEvents='no'><name>A_ARG_InstanceID</name><dataType>ui4</dataType></stateVariable>
    <stateVariable sendEvents='no'><name>A_ARG_Channel</name><dataType>string</dataType></stateVariable>
  </serviceStateTable>
</scpd>"));

        doc.Should().NotBeNull();
        doc!.Actions.Should().HaveCount(2);

        var setVolume = doc.Actions[0];
        setVolume.Inputs.Select(a => a.Name)
            .Should().BeEquivalentTo(new[] { "InstanceID", "Channel", "DesiredVolume" }, opts => opts.WithStrictOrdering());
        setVolume.Outputs.Should().BeEmpty();

        var getVolume = doc.Actions[1];
        getVolume.Inputs.Select(a => a.Name).Should().BeEquivalentTo(new[] { "InstanceID", "Channel" }, o => o.WithStrictOrdering());
        getVolume.Outputs.Should().HaveCount(1);
        getVolume.Outputs[0].Name.Should().Be("CurrentVolume");

        getVolume.Outputs[0].DataType.Should().Be("ui2");
    }

    [Fact]
    public void Missing_action_list_returns_empty_actions()
    {
        var (sut, _) = CreateSut();
        var doc = sut.Parse(Bytes("<scpd><serviceStateTable/></scpd>"));
        doc.Should().NotBeNull();
        doc!.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Empty_action_list_returns_empty_actions()
    {
        var (sut, _) = CreateSut();
        var doc = sut.Parse(Bytes("<scpd><actionList/><serviceStateTable/></scpd>"));
        doc.Should().NotBeNull();
        doc!.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Bad_direction_returns_null_and_emits_warning()
    {
        var (sut, diagnostics) = CreateSut();
        var doc = sut.Parse(Bytes(@"
<scpd>
  <actionList>
    <action>
      <name>Bogus</name>
      <argumentList>
        <argument><name>X</name><direction>sideways</direction><relatedStateVariable>X</relatedStateVariable></argument>
      </argumentList>
    </action>
  </actionList>
  <serviceStateTable/>
</scpd>"));

        doc.Should().BeNull();
        diagnostics.Entries.Should().ContainSingle();
        diagnostics.Entries[0].Category.Should().Be("Scpd.Parse");
    }

    [Fact]
    public void State_variables_populated_with_send_events_and_allowed_values()
    {
        var (sut, _) = CreateSut();
        var doc = sut.Parse(Bytes(@"
<scpd>
  <actionList/>
  <serviceStateTable>
    <stateVariable sendEvents='yes'>
      <name>TransportState</name>
      <dataType>string</dataType>
      <allowedValueList>
        <allowedValue>STOPPED</allowedValue>
        <allowedValue>PLAYING</allowedValue>
      </allowedValueList>
    </stateVariable>
    <stateVariable sendEvents='no'>
      <name>Volume</name>
      <dataType>ui2</dataType>
    </stateVariable>
  </serviceStateTable>
</scpd>"));

        doc.Should().NotBeNull();
        doc!.StateVariables.Should().HaveCount(2);

        var transport = doc.StateVariables[0];
        transport.Name.Should().Be("TransportState");
        transport.DataType.Should().Be("string");
        transport.SendsEvents.Should().BeTrue();
        transport.AllowedValues.Should().BeEquivalentTo(new[] { "STOPPED", "PLAYING" });

        var volume = doc.StateVariables[1];
        volume.SendsEvents.Should().BeFalse();
        volume.AllowedValues.Should().BeNull();
    }

    [Fact]
    public void Malformed_xml_returns_null()
    {
        var (sut, _) = CreateSut();
        var doc = sut.Parse(Bytes("<scpd><actionList"));
        doc.Should().BeNull();
    }

    private static (ScpdXmlParser Sut, RecordingDiagnosticSink Diagnostics) CreateSut()
    {
        var sink = new RecordingDiagnosticSink();
        var clock = new FakeClock();
        return (new ScpdXmlParser(sink, clock), sink);
    }

    private static MemoryStream Bytes(string xml) =>
        new(Encoding.UTF8.GetBytes(xml.Trim()));
}
