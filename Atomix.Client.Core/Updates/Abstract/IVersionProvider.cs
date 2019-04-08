using System;
using System.Threading.Tasks;

namespace Atomix.Updates.Abstract
{
    public interface IVersionProvider
    {
        Task<Version> GetLatestVersionAsync();
    }
}
