using System.IO;
using System.Threading.Tasks;

namespace Atomix.Updates.Abstract
{
    public interface IBinariesProvider
    {
        Task<Stream> GetLatestBinariesAsync();
    }
}
