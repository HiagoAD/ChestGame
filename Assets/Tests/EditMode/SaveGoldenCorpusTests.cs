using System.Globalization;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // Assets/Tests/EditMode/SaveCorpus/v1.json is frozen, real bytes an old build actually wrote -
    // never regenerated here, never reached through SaveCorpusGenerator. Read straight off disk via
    // Application.dataPath and pushed through the real pipeline (JsonCodec, NoProtection) the same
    // way a real load would, over a FakeSaveStore that only ever stands in for where the bytes came
    // from. See docs/saving.md, "The golden corpus" and "The fixture values are chosen, not
    // incidental" - every field below is one of the five values that were verified to actually come
    // back different if SaveEnvelope.Parse's DateParseHandling.None or FloatParseHandling.Decimal
    // were ever removed, or if JRaw.Create were simplified back to a plain JToken property. Asserting
    // "it parsed" would not catch any of that; only the exact values below do.
    public class SaveGoldenCorpusTests
    {
        private const string Key = "corpus";

        // Mirrors SaveCorpusGenerator.FixtureV1's shape - a stand-in for a save model that does not
        // exist yet, not this game's real one.
        private class FixtureV1
        {
            public string Note;
            public long Coins;
            public decimal Multiplier;
            public string HighPrecisionTimestamp;
            public string ZonedTimestamp;
            public long LargeId;
        }

        private static string CorpusPath =>
            Path.Combine(Application.dataPath, "Tests/EditMode/SaveCorpus/v1.json");

        [Test]
        public void V1Json_LoadedThroughTheRealPipeline_EveryValueSurvivesExactly()
        {
            byte[] bytes = File.ReadAllBytes(CorpusPath);
            FakeSaveStore store = new();
            store.Seed(Key, bytes);
            SaveService service = new(new JsonCodec(), new NoProtection(), store);

            FixtureV1 loaded = SynchronousUniTask.Result(service.LoadAsync<FixtureV1>(Key, CancellationToken.None));

            Assert.AreEqual("golden corpus fixture, not a real save model", loaded.Note);
            Assert.AreEqual(1250, loaded.Coins);

            // A plain AreEqual(1.50m, loaded.Multiplier) would pass even if the trailing zero were
            // lost - decimal equality is value-based and 1.50m == 1.5m. The scale only shows up in
            // ToString(), which is what FloatParseHandling.Decimal is what actually protects.
            Assert.AreEqual("1.50", loaded.Multiplier.ToString(CultureInfo.InvariantCulture),
                "the trailing zero is lost the moment FloatParseHandling.Decimal is removed from SaveEnvelope.Parse");

            // Nine fractional-second digits - one more than a .NET DateTime's seven (100ns ticks).
            // Anything that round trips this through DateTime along the way truncates it silently;
            // asserting the exact string is what catches that, an assertion on a parsed DateTime
            // would not.
            Assert.AreEqual("2026-09-01T10:00:00.123456789", loaded.HighPrecisionTimestamp);

            // Carries a UTC offset. A naive round trip re-expresses it in the parsing machine's own
            // local offset instead of preserving the literal text - a failure that would only show
            // up on a machine whose clock disagrees with UTC, which is exactly why the frozen
            // expected value here has to be the literal string, not a parsed, offset-normalised one.
            Assert.AreEqual("2026-09-01T10:00:00+05:00", loaded.ZonedTimestamp);

            // One past 2^53, the largest integer a double can represent exactly.
            Assert.AreEqual(9007199254740993L, loaded.LargeId);
        }

        [Test]
        public void V1Json_OnDisk_StillLiteralyContainsTheTrailingZero_NotShortenedTo1Point5()
        {
            // Independent of the read half above: this pins that the file itself, as committed,
            // still carries "1.50" verbatim - the write-time half of the same guarantee. A round
            // trip that happened to fix up the read path but wrote a shortened "1.5" on some other
            // occasion would not be caught by the test above alone.
            string raw = File.ReadAllText(CorpusPath);

            StringAssert.Contains("\"Multiplier\":1.50,", raw);
            Assert.IsFalse(raw.Contains("\"Multiplier\":1.5,"),
                "the corpus file must carry the trailing-zero decimal verbatim, not a shortened 1.5");
        }
    }
}
