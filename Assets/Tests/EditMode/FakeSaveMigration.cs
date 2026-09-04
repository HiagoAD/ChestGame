using System;
using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Tests.EditMode
{
    // A single migration step whose transformation and FromVersion are both supplied by the test,
    // so SaveMigratorTests can build a chain (or a deliberately broken one) without a real save
    // model to migrate. LastInput lets a test prove ordering - that a later step actually saw an
    // earlier step's output, not a fresh copy of the original document.
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
