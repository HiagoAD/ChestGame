using System;
using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Tests.EditMode
{
    // ILegacyImport with every branch a test needs to drive by hand: Present toggles IsPresent(),
    // ImportFunc supplies the reshaped document, and OnClear runs inside Clear() itself so a test
    // can inspect (or mutate) the world exactly at the point SaveService considers the import
    // finished - which is what proves the write-before-clear ordering rather than merely assuming
    // it from the call counts alone.
    public class FakeLegacyImport : ILegacyImport
    {
        public bool Present { get; set; }
        public Func<JObject> ImportFunc { get; set; }
        public Action OnClear { get; set; }
        public bool ClearThrows { get; set; }

        public int ImportCallCount { get; private set; }
        public int ClearCallCount { get; private set; }

        public bool IsPresent() => Present;

        public JObject Import()
        {
            ImportCallCount++;
            return ImportFunc?.Invoke();
        }

        public void Clear()
        {
            ClearCallCount++;
            OnClear?.Invoke();
            if (ClearThrows) throw new InvalidOperationException("FakeLegacyImport.Clear was configured to fail");
        }
    }
}
