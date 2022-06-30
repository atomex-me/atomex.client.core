namespace Atomex.Common
{
    public class Result<T>
    {
        public Error Error { get; }
        public T Value { get; }
        public bool HasError => Error != null;

        public Result(T value) => Value = value;
        public Result(Error error) => Error = error;

        public static implicit operator Result<T>(T value) => new(value);
        public static implicit operator Result<T>(Error error) => new(error);

        public bool IsConnectionError =>
            Error != null && Error.Code == Errors.RequestError;
    }
}