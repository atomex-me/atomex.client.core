namespace Atomex.Common
{
    public static class FileSystem
    {
        public static void UseFileSystem(IFileSystem fileSystem)
        {
            Current = fileSystem;
        }

        public static IFileSystem Current { get; private set; }
    }
}