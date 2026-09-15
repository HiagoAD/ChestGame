using System;
using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// An <see cref="ISaveMigration"/> test double whose transformation and
    /// <see cref="FromVersion"/> are both supplied by the test, so a chain - or a deliberately
    /// broken one - can be built without a real save model to migrate.
    /// </summary>
    /// <remarks>
    /// <see cref="LastInput"/> lets a test prove ordering: that a later step actually saw an
    /// earlier step's output, not a fresh copy of the original document.
    /// </remarks>
    public class FakeSaveMigration : ISaveMigration
    {
        public int FromVersion { get; }
        public Func<JObject, JObject> ApplyFunc { get; set; }
        public bool ApplyWasCalled { get; private set; }
        public JObject LastInput { get; private set; }

        public FakeSaveMigration(int fromVersion, Func<JObject, JObject> applyFunc = null)
        {
            FromVersion = fromVersion;
            ApplyFunc = applyFunc;
        }

        public JObject Apply(JObject document)
        {
            ApplyWasCalled = true;
            LastInput = document;
            return ApplyFunc != null ? ApplyFunc(document) : document;
        }
    }
}
