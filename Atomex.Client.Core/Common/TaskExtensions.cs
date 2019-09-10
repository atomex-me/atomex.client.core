using System;
using System.Threading.Tasks;
using Serilog;

namespace Atomex.Common
{
    public static class TaskExtensions
    {
        public static async void FireAndForget(this Task task)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Log.Error(e, "Task fire and forget error");
            }
        }
    }
}