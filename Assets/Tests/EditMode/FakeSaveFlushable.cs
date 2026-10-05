using System;
using Company.ChestGame.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// An <see cref="ISaveFlushable"/> test double with every branch a registry test needs to drive
    /// by hand: <see cref="CanFlushBlocking"/> toggles the wiring guard, <see cref="OnFlush"/> runs
    /// inside <see cref="FlushBlocking"/> itself so a test can mutate the registry at exactly the
    /// point it is iterating, and <see cref="FlushThrows"/> drives the fault isolation
    /// <c>FlushAll</c> owes every other flushable.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Why the flush stopped being one field".
    /// </remarks>
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
