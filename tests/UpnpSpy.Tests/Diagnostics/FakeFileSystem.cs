using UpnpSpy.Core.Diagnostics;

namespace UpnpSpy.Tests.Diagnostics;

/// <summary>In-memory IFileSystem fake. Backs each file with a MemoryStream-equivalent byte list.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, List<byte>> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    public bool ThrowOnOpenAppend { get; set; }
    public bool ThrowOnCreateDirectory { get; set; }

    public IReadOnlyDictionary<string, byte[]> Files =>
        _files.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => _directories.Contains(NormalizeDir(path));

    public void CreateDirectory(string path)
    {
        if (ThrowOnCreateDirectory) throw new UnauthorizedAccessException("fake");
        _directories.Add(NormalizeDir(path));
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public long FileLength(string path) =>
        _files.TryGetValue(path, out var bytes) ? bytes.Count : throw new FileNotFoundException(path);

    public void Delete(string path) => _files.Remove(path);

    public void Move(string source, string destination, bool overwrite)
    {
        if (!_files.TryGetValue(source, out var bytes))
            throw new FileNotFoundException(source);
        if (_files.ContainsKey(destination) && !overwrite)
            throw new IOException($"Destination exists: {destination}");
        _files[destination] = bytes;
        _files.Remove(source);
    }

    public Stream OpenAppend(string path)
    {
        if (ThrowOnOpenAppend) throw new IOException("fake disk error");
        if (!_files.TryGetValue(path, out var list))
        {
            list = new List<byte>();
            _files[path] = list;
        }
        return new AppendingStream(list);
    }

    private static string NormalizeDir(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class AppendingStream : Stream
    {
        private readonly List<byte> _target;

        public AppendingStream(List<byte> target) { _target = target; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _target.Count;
        public override long Position { get => _target.Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
                _target.Add(buffer[offset + i]);
        }
    }
}
