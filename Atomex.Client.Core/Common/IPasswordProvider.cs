using System.Security;

namespace Atomex.Common
{
    public interface IPasswordProvider
    {
        SecureString Password { get; }
    }
}