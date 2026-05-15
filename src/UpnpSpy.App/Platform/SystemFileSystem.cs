using UpnpSpy.Core.Diagnostics;

namespace UpnpSpy.App.Platform;

public sealed class SystemFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string path) => File.Exists(path);
    public long FileLength(string path) => new FileInfo(path).Length;
    public void Delete(string path) => File.Delete(path);
    public void Move(string source, string destination, bool overwrite) =>
        File.Move(source, destination, overwrite);
    public Stream OpenAppend(string path) =>
        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
}
