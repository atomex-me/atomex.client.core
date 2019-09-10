using System;
using System.Threading.Tasks;

namespace Atomex.Updates.Abstract
{
    public interface IVersionProvider
    {
        Task<Version> GetLatestVersionAsync();
    }
}
