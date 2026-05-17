using FluentAssertions;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.Description;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class ServiceNodeViewModelTests
{
    private static readonly Uri ScpdUrl = new("http://192.168.1.10:8080/avt/scpd.xml");

    [Fact]
    public void Newly_constructed_node_contains_Loading_placeholder_child()
    {
        // FR-044: pre-seeded placeholder so the WinUI TreeView renders the chevron
        // before the user clicks, without waiting for an SCPD fetch.
        var sut = new ServiceNodeViewModel(MakeService(), new FakeScpdFetcher(), new SynchronousDispatcher(), new FakeBrowserLauncher(), CancellationToken.None);

        sut.Children.Should().ContainSingle()
            .Which.Should().Be(ServiceNodeViewModel.LoadingPlaceholder);
    }

    [Fact]
    public async Task First_expansion_fetches_actions_and_populates_children()
    {
        var service = MakeService();
        var fetcher = new FakeScpdFetcher();
        fetcher.SetResult(ScpdUrl, new ScpdFetchResult.Success(new ScpdDocument(
            new[]
            {
                new ActionDefinition("Stop", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>()),
                new ActionDefinition("Play", Array.Empty<ArgumentDefinition>(), Array.Empty<ArgumentDefinition>()),
            },
            Array.Empty<StateVariableDefinition>())));

        var sut = new ServiceNodeViewModel(service, fetcher, new SynchronousDispatcher(), new FakeBrowserLauncher(), CancellationToken.None);

        await sut.ExpandAsync();

        fetcher.CallsFor(ScpdUrl).Should().Be(1);
        sut.Children.Should().HaveCount(2);
        sut.Children.OfType<ActionNodeViewModel>().Select(a => a.Label)
            .Should().BeEquivalentTo(new[] { "Stop", "Play" });
        service.ScpdFetchState.Should().Be(FetchState.Loaded);
    }

    [Fact]
    public async Task Repeated_expansion_does_not_refetch()
    {
        var service = MakeService();
        var fetcher = new FakeScpdFetcher();
        fetcher.SetResult(ScpdUrl, new ScpdFetchResult.Success(new ScpdDocument(
            Array.Empty<ActionDefinition>(), Array.Empty<StateVariableDefinition>())));

        var sut = new ServiceNodeViewModel(service, fetcher, new SynchronousDispatcher(), new FakeBrowserLauncher(), CancellationToken.None);

        await sut.ExpandAsync();
        await sut.ExpandAsync();

        fetcher.CallsFor(ScpdUrl).Should().Be(1);
    }

    [Fact]
    public async Task Http_error_surfaces_inline_placeholder()
    {
        var service = MakeService();
        var fetcher = new FakeScpdFetcher();
        fetcher.SetResult(ScpdUrl, new ScpdFetchResult.HttpError(500, "Internal Server Error"));

        var sut = new ServiceNodeViewModel(service, fetcher, new SynchronousDispatcher(), new FakeBrowserLauncher(), CancellationToken.None);
        await sut.ExpandAsync();

        service.ScpdFetchState.Should().Be(FetchState.Failed);
        sut.Children.Should().HaveCount(1);
        sut.Children[0].Should().BeOfType<string>()
            .Which.Should().Contain("Actions unavailable").And.Contain("500");
    }

    [Fact]
    public async Task Parse_error_surfaces_inline_placeholder()
    {
        var service = MakeService();
        var fetcher = new FakeScpdFetcher();
        fetcher.SetResult(ScpdUrl, new ScpdFetchResult.ParseError("bad scpd"));

        var sut = new ServiceNodeViewModel(service, fetcher, new SynchronousDispatcher(), new FakeBrowserLauncher(), CancellationToken.None);
        await sut.ExpandAsync();

        sut.Children[0].Should().BeOfType<string>().Which.Should().Contain("bad scpd");
    }

    [Fact]
    public async Task FetchScpdCommand_opens_scpd_url_in_browser()
    {
        var service = MakeService();
        var browser = new FakeBrowserLauncher();
        var sut = new ServiceNodeViewModel(service, new FakeScpdFetcher(), new SynchronousDispatcher(), browser, CancellationToken.None);

        await sut.FetchScpdCommand.ExecuteAsync(null);

        browser.Calls.Should().ContainSingle().Which.Should().Be(ScpdUrl);
    }

    [Fact]
    public async Task FetchScpdCommand_does_not_throw_when_launcher_returns_false()
    {
        var service = MakeService();
        var browser = new FakeBrowserLauncher { NextResult = false };
        var sut = new ServiceNodeViewModel(service, new FakeScpdFetcher(), new SynchronousDispatcher(), browser, CancellationToken.None);

        await FluentActions.Awaiting(() => sut.FetchScpdCommand.ExecuteAsync(null))
            .Should().NotThrowAsync();
        browser.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchScpdCommand_works_even_after_scpd_fetch_failed()
    {
        var service = MakeService();
        var fetcher = new FakeScpdFetcher();
        fetcher.SetResult(ScpdUrl, new ScpdFetchResult.HttpError(500, "Internal Server Error"));
        var browser = new FakeBrowserLauncher();
        var sut = new ServiceNodeViewModel(service, fetcher, new SynchronousDispatcher(), browser, CancellationToken.None);

        await sut.ExpandAsync();
        await sut.FetchScpdCommand.ExecuteAsync(null);

        service.ScpdFetchState.Should().Be(FetchState.Failed);
        browser.Calls.Should().ContainSingle().Which.Should().Be(ScpdUrl);
    }

    private static Service MakeService() => new()
    {
        OwningDeviceUuid = "root-uuid",
        ContainingDeviceUdn = "uuid:root-uuid",
        ContainingDeviceFriendlyName = "Root",
        ServiceId = "urn:upnp-org:serviceId:AVT",
        ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
        ScpdUrl = ScpdUrl,
        ControlUrl = new Uri("http://192.168.1.10:8080/avt/ctrl"),
        EventSubUrl = new Uri("http://192.168.1.10:8080/avt/evt"),
    };
}
