using System;
using System.IO;

namespace Atomex.Common
{
    public static class PathEx
    {
        public static string ToFullPath(string path)
        {
            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + path);
        }

        public static void CreateDirectoryIfNotExists(string pathToFile)
        {
            var pathToDirectory = Path.GetDirectoryName(pathToFile);

            if (pathToDirectory == null)
                throw new ArgumentNullException(nameof(pathToDirectory));

            if (!Directory.Exists(pathToDirectory))
                Directory.CreateDirectory(pathToDirectory);
        }
    }
}