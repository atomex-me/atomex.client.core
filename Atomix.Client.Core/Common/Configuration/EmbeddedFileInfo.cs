using System;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace Atomix.Common.Configuration
{
    public class EmbeddedFileInfo : IFileInfo
    {
        private readonly Stream _fileStream;

        public EmbeddedFileInfo(string name, Stream fileStream)
        {
            _fileStream = fileStream ?? throw new ArgumentNullException(nameof(fileStream));

            Exists = true;
            IsDirectory = false;
            Length = fileStream.Length;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PhysicalPath = name;
            LastModified = DateTimeOffset.Now;
        }

        public Stream CreateReadStream()
        {
            return _fileStream;
        }

        public bool Exists { get; }
        public bool IsDirectory { get; }
        public long Length { get; }
        public string Name { get; }
        public string PhysicalPath { get; }
        public DateTimeOffset LastModified { get; }
    }
}
