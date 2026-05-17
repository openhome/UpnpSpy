namespace UpnpSpy.Core.Diagnostics;

/// <summary>
/// Narrow file-system abstraction used by the rolling diagnostic file sink so its
/// rotation logic can be tested against an in-memory fake.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool FileExists(string path);
    long FileLength(string path);
    void Delete(string path);
    void Move(string source, string destination, bool overwrite);
    Stream OpenAppend(string path);
}
