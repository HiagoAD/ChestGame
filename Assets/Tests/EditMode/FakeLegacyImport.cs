using System;
using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// An <see cref="ILegacyImport"/> test double with every branch a test needs to drive by hand:
    /// <see cref="Present"/> toggles <see cref="IsPresent"/>, <see cref="ImportFunc"/> supplies the
    /// reshaped document, and <see cref="OnClear"/> runs inside <see cref="Clear"/> itself so a test
    /// can inspect, or mutate, the world exactly at the point <c>SaveService</c> considers the import
    /// finished.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "FakeLegacyImport, and how OnClear proves write-before-clear directly".
    /// </remarks>
    public class FakeLegacyImport : ILegacyImport
    {
        /// <summary>
        /// The save key this import's data belongs under. Defaults to <c>"profile"</c>; settable for
        /// a test that needs a different key.
        /// </summary>
        public string TargetKey { get; set; }

        public bool Present { get; set; }
        public Func<JObject> ImportFunc { get; set; }
        public Action OnClear { get; set; }
        public bool ClearThrows { get; set; }

        public int IsPresentCallCount { get; private set; }
        public int ImportCallCount { get; private set; }
        public int ClearCallCount { get; private set; }

        public FakeLegacyImport(string targetKey = "profile")
        {
            TargetKey = targetKey;
        }

        public bool IsPresent()
        {
            IsPresentCallCount++;
            return Present;
        }

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
