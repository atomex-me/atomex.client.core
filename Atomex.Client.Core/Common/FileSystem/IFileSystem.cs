using System.IO;

namespace Atomex.Common
{
    public interface IFileSystem
    {
        string PathToDocuments { get; }
        string BaseDirectory { get; }
        string AssetsDirectory { get; }

        string ToFullPath(string path);
        Stream GetResourceStream(string path);
    }
}