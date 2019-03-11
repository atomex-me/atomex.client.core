namespace Atomix.Common.Abstract
{
    public interface IBackgroundTaskPerformer
    {
        void Start();
        void Stop();
        void EnqueueTask(BackgroundTask task);
    }
}