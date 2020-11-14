namespace Atomex.Common
{
    public static class FileSystem
    {
        private static IFileSystem _fileSystem;

        public static void UseFileSystem(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public static IFileSystem Current => _fileSystem ?? new DefaultFileSystem();
    }
}