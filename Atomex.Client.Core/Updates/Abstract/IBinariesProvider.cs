using System.IO;
using System.Threading.Tasks;

namespace Atomex.Updates.Abstract
{
    public interface IBinariesProvider
    {
        Task<Stream> GetLatestBinariesAsync();
    }
}
