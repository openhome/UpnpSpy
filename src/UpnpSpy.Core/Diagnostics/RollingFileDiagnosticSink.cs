using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Diagnostics;

/// <summary>
/// Writes JSON-lines to a rolling log file under a configured directory.
/// Non-blocking: entries are queued onto an unbounded channel and drained by a
/// single background task. Fail-open: any I/O exception disables further writes
/// silently (FR-042); the in-memory ring continues to work.
/// </summary>
public sealed class RollingFileDiagnosticSink : IDiagnosticSink, IAsyncDisposable
{
    public const long DefaultMaxBytesPerFile = 2L * 1024 * 1024;
    public const int DefaultMaxFiles = 8;
    private const string BaseFileName = "upnpspy.log";

    private readonly IFileSystem _fs;
    private readonly string _directory;
    private readonly long _maxBytesPerFile;
    private readonly int _maxFiles;
    private readonly Channel<DiagnosticEntry> _channel;
    private readonly Task _drainTask;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disabled;

    public RollingFileDiagnosticSink(
        IFileSystem fileSystem,
        string directory,
        long maxBytesPerFile = DefaultMaxBytesPerFile,
        int maxFiles = DefaultMaxFiles)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory must be non-empty.", nameof(directory));
        if (maxBytesPerFile <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerFile));
        if (maxFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFiles));

        _fs = fileSystem;
        _directory = directory;
        _maxBytesPerFile = maxBytesPerFile;
        _maxFiles = maxFiles;

        _channel = Channel.CreateUnbounded<DiagnosticEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        _drainTask = Task.Run(DrainAsync);
    }

    public string CurrentPath => Path.Combine(_directory, BaseFileName);

    public void Record(DiagnosticEntry entry)
    {
        if (_disabled) return;
        _channel.Writer.TryWrite(entry);
    }

    /// <summary>
    /// Test seam: signals the channel as complete and awaits the drain task.
    /// After this returns the sink will no longer accept entries.
    /// </summary>
    public async Task FlushAndStopAsync()
    {
        _channel.Writer.TryComplete();
        try { await _drainTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task DrainAsync()
    {
        try { _fs.CreateDirectory(_directory); }
        catch { _disabled = true; return; }

        await foreach (var entry in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
        {
            try
            {
                var bytes = SerializeJsonLine(entry);
                using (var stream = _fs.OpenAppend(CurrentPath))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
                if (_fs.FileLength(CurrentPath) > _maxBytesPerFile)
                    Rotate();
            }
            catch
            {
                _disabled = true;
                return;
            }
        }
    }

    private void Rotate()
    {
        var oldest = NumberedPath(_maxFiles - 1);
        if (_fs.FileExists(oldest))
            _fs.Delete(oldest);

        for (var i = _maxFiles - 2; i >= 1; i--)
        {
            var src = NumberedPath(i);
            if (!_fs.FileExists(src)) continue;
            _fs.Move(src, NumberedPath(i + 1), overwrite: true);
        }

        _fs.Move(CurrentPath, NumberedPath(1), overwrite: true);
    }

    private string NumberedPath(int index) =>
        Path.Combine(_directory, $"upnpspy.{index}.log");

    private static byte[] SerializeJsonLine(DiagnosticEntry entry)
    {
        var payload = new
        {
            ts = entry.Timestamp,
            sev = entry.Severity.ToString(),
            cat = entry.Category,
            msg = entry.Message,
            ctx = entry.Context,
            ex = entry.Exception,
        };
        var json = JsonSerializer.Serialize(payload);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try { await _drainTask.ConfigureAwait(false); }
        catch { /* swallow on shutdown */ }
        _cts.Dispose();
    }
}
