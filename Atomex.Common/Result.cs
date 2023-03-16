#nullable enable

namespace Atomex.Common
{
    public readonly struct Result<T>
    {
        public T? Value { get; init; }
        public Error? Error { get; init; }

        public static implicit operator Result<T>(T value) => new() { Value = value };
        public static implicit operator Result<T>(Error error) => new() { Error = error };
        public static implicit operator Result<T>(Error? error) => new() { Error = error };

        public void Deconstruct(out T? value, out Error? error)
        {
            value = Value;
            error = Error;
        }
    }
}