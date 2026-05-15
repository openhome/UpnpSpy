using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using Xunit;

namespace UpnpSpy.Tests.Diagnostics;

public sealed class RollingFileDiagnosticSinkTests
{
    private const string LogDir = @"C:\fake\logs";
    private static string CurrentLog => Path.Combine(LogDir, "upnpspy.log");
    private static string Numbered(int i) => Path.Combine(LogDir, $"upnpspy.{i}.log");

    [Fact]
    public async Task Writes_one_json_line_per_entry()
    {
        var fs = new FakeFileSystem();
        var sut = new RollingFileDiagnosticSink(fs, LogDir, maxBytesPerFile: 1024 * 1024);

        sut.Record(Entry("first"));
        sut.Record(Entry("second"));
        await sut.FlushAndStopAsync();

        var contents = ReadText(fs, CurrentLog);
        contents.Should().NotBeNullOrEmpty();
        var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("\"msg\":\"first\"");
        lines[1].Should().Contain("\"msg\":\"second\"");
    }

    [Fact]
    public async Task Rotates_when_current_file_exceeds_threshold()
    {
        var fs = new FakeFileSystem();
        // Small threshold so a single record forces rotation.
        var sut = new RollingFileDiagnosticSink(fs, LogDir, maxBytesPerFile: 10, maxFiles: 3);

        sut.Record(Entry("first-message-which-exceeds-ten-bytes"));
        sut.Record(Entry("second-message-which-also-exceeds-ten-bytes"));
        await sut.FlushAndStopAsync();

        fs.FileExists(Numbered(1)).Should().BeTrue("first record's file was rotated");
        // After the second write, the originally-rotated file moves to .2 and the second write is in the rotated .1.
        fs.FileExists(Numbered(2)).Should().BeTrue("second record forced another rotation");
    }

    [Fact]
    public async Task Caps_the_number_of_files_at_maxFiles()
    {
        var fs = new FakeFileSystem();
        var sut = new RollingFileDiagnosticSink(fs, LogDir, maxBytesPerFile: 5, maxFiles: 3);

        for (var i = 0; i < 6; i++)
            sut.Record(Entry($"entry-{i}-padding-to-force-rotation"));
        await sut.FlushAndStopAsync();

        // With maxFiles=3 we keep current + .1 + .2, never .3
        fs.FileExists(Numbered(3)).Should().BeFalse();
    }

    [Fact]
    public async Task Fail_open_when_directory_creation_throws()
    {
        var fs = new FakeFileSystem { ThrowOnCreateDirectory = true };
        var sut = new RollingFileDiagnosticSink(fs, LogDir);

        var act = async () =>
        {
            sut.Record(Entry("anything"));
            await sut.FlushAndStopAsync();
        };

        await act.Should().NotThrowAsync();
        fs.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Fail_open_when_open_append_throws()
    {
        var fs = new FakeFileSystem { ThrowOnOpenAppend = true };
        var sut = new RollingFileDiagnosticSink(fs, LogDir);

        var act = async () =>
        {
            sut.Record(Entry("anything"));
            await sut.FlushAndStopAsync();
        };

        await act.Should().NotThrowAsync();
    }

    private static DiagnosticEntry Entry(string message) => new(
        new DateTimeOffset(2026, 5, 12, 0, 0, 0, TimeSpan.Zero),
        DiagnosticSeverity.Warning, "Test", message,
        new Dictionary<string, string>(), null);

    private static string ReadText(FakeFileSystem fs, string path)
    {
        return fs.Files.TryGetValue(path, out var bytes) ? Encoding.UTF8.GetString(bytes) : "";
    }
}
