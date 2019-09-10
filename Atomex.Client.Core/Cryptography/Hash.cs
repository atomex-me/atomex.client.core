using System.Text;

namespace Atomex.Cryptography
{
    public abstract class Hash
    {
        public abstract byte[] ComputeHash(byte[] input, int offset, int count);

        public byte[] ComputeHash(byte[] input)
        {
            return ComputeHash(input, 0, input.Length);
        }
        
        public byte[] ComputeHash(string input, Encoding encoding)
        {
            return ComputeHash(encoding.GetBytes(input));
        }
    }
}