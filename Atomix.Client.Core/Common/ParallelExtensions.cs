using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Atomix.Common
{
    public class InfinitePartitioner : Partitioner<bool>
    {
        public override IList<IEnumerator<bool>> GetPartitions(int partitionCount)
        {
            if (partitionCount < 1)
                throw new ArgumentOutOfRangeException(nameof(partitionCount));
            return (from i in Enumerable.Range(0, partitionCount)
                select InfiniteEnumerator()).ToArray();
        }

        public override bool SupportsDynamicPartitions => true;

        public override IEnumerable<bool> GetDynamicPartitions()
        {
            return new InfiniteEnumerators();
        }

        private static IEnumerator<bool> InfiniteEnumerator()
        {
            while (true)
                yield return true;
        }

        private class InfiniteEnumerators : IEnumerable<bool>
        {
            public IEnumerator<bool> GetEnumerator()
            {
                return InfiniteEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
    }

    public class ParallelExtensions
    {
        public static void While(
            ParallelOptions parallelOptions,
            Func<bool> condition,
            Action<ParallelLoopState> body)
        {
            Parallel.ForEach(
                source: new InfinitePartitioner(),
                parallelOptions: parallelOptions,
                body:(ignored, state) =>
                {
                    if (condition())
                        body(state);
                    else
                        state.Stop();
                });
        }
    }
}