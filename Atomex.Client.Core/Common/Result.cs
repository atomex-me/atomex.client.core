using Atomex.Core;

namespace Atomex.Common
{
    public class Result<T>
    {
        public Error Error { get; }
        public T Value { get; }
        public bool HasError => Error != null;

        public Result(T value) => Value = value;
        public Result(Error error) => Error = error;
    }
}