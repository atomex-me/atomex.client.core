using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomix.Updates
{
    static class Wait
    {
        public static void While(Func<bool> expression, int timeout = 0)
        {
            timeout = timeout <= 0 ? int.MaxValue : timeout;

            while (expression())
            {
                if ((timeout -= 100) <= 0)
                    throw new TimeoutException();

                Thread.Sleep(100);
            }
        }

        public static async Task WhileAsync(Func<bool> expression, int timeout = 0)
        {
            timeout = timeout <= 0 ? int.MaxValue : timeout;

            while (expression())
            {
                if ((timeout -= 100) <= 0)
                    throw new TimeoutException();

                await Task.Delay(100);
            }
        }
    }
}
