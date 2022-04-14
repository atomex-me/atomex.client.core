namespace Atomex.Services.Abstract
{
    public interface ITransactionsTracker
    {
        void Start();
        void Stop();
    }
}