using System.IO;
using System.Threading.Tasks;

using Atomix.Updates.Abstract;

namespace Atomix.Updates
{
    public class LocalBinariesProvider : IBinariesProvider
    {
        readonly string FilePath;

        public LocalBinariesProvider(string filePath)
        {
            FilePath = filePath;
        }

        public Task<Stream> GetLatestBinariesAsync()
        {
            return Task.FromResult((Stream)File.OpenRead(FilePath));
        }
    }

    public static class LocalBinariesProviderExt
    {        
        public static Updater UseLocalBinariesProvider(this Updater updater, string filePath)
        {
            return updater.UseBinariesProvider(new LocalBinariesProvider(filePath));
        }
    }
}
