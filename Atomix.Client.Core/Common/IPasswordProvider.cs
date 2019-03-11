using System.Security;

namespace Atomix.Common
{
    public interface IPasswordProvider
    {
        SecureString Password { get; }
    }
}