using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Atomex.Common.Configuration
{
    public class StringProvider : IFileProvider
    {
        private readonly string _data;

        public StringProvider(string data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new EmbeddedFileInfo(subpath, new MemoryStream(Encoding.UTF8.GetBytes(_data)));
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            throw new NotImplementedException();
        }

        public IChangeToken Watch(string filter)
        {
            throw new NotImplementedException();
        }
    }
}