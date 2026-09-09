using System;
using Company.ChestGame.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    // ISaveFlushable with every branch a registry test needs to drive by hand: CanFlushBlocking
    // toggles the wiring guard, OnFlush runs inside FlushBlocking itself so a test can mutate the
    // registry at exactly the point it is iterating, and FlushThrows drives the fault isolation
    // FlushAll owes every other flushable.
    public class FakeSaveFlushable : ISaveFlushable
    {
        public bool CanFlushBlocking { get; set; } = true;
        public string SaveKey { get; set; }
        public bool FlushThrows { get; set; }
        public Action OnFlush { get; set; }

        public int FlushCallCount { get; private set; }

        public FakeSaveFlushable(string saveKey = "fake") => SaveKey = saveKey;

        public void FlushBlocking()
        {
            FlushCallCount++;
            OnFlush?.Invoke();
            if (FlushThrows) throw new InvalidOperationException($"FakeSaveFlushable '{SaveKey}' was configured to fail");
        }
    }
}
