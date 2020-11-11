using System;
using System.IO;

namespace Atomex.Common
{
    public class DefaultFileSystem : IFileSystem
    {
        public string PathToDocuments => BaseDirectory;
        public string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
        public string AssetsDirectory => BaseDirectory;

        public Stream GetResourceStream(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read);

        public string ToFullPath(string path) =>
            Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(BaseDirectory + path);
    }
}