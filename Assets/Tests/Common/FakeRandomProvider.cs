using System.Collections.Generic;
using Company.ChestGame.Common;

namespace Company.ChestGame.Tests.Common
{
    // Deterministic IRandomProvider. Either pin a single value/result, or queue a sequence that is
    // consumed one draw at a time; a queue that runs dry falls back to the pinned value.
    public class FakeRandomProvider : IRandomProvider
    {
        public float NextValue { get; set; }
        public int NextRangeResult { get; set; }

        public readonly Queue<float> ValueSequence = new();
        public readonly Queue<int> RangeSequence = new();

        public readonly List<(int minInclusive, int maxExclusive)> RangeCalls = new();
        public int ValueCallCount { get; private set; }

        public FakeRandomProvider() { }

        public FakeRandomProvider(params float[] values)
        {
            foreach (float value in values)
            {
                ValueSequence.Enqueue(value);
            }
        }

        public float Value
        {
            get
            {
                ValueCallCount++;
                return ValueSequence.Count > 0 ? ValueSequence.Dequeue() : NextValue;
            }
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            RangeCalls.Add((minInclusive, maxExclusive));
            return RangeSequence.Count > 0 ? RangeSequence.Dequeue() : NextRangeResult;
        }
    }
}
