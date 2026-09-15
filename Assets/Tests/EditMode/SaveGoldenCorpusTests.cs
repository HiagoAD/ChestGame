using System.Globalization;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <c>SaveCorpusGenerator</c>'s frozen output: <c>SaveCorpus/v1.json</c> is real bytes an
    /// old build actually wrote, never regenerated here, read straight off disk and pushed through
    /// the real pipeline (<see cref="JsonCodec"/>, <see cref="NoProtection"/>) the same way a real
    /// load would, over a <see cref="FakeSaveStore"/> that only ever stands in for where the bytes
    /// came from. Every field asserted below is one of the five values verified to actually come back
    /// different if <c>SaveEnvelope.Parse</c>'s date or decimal parse handling were ever removed, or
    /// if its raw-body capture were simplified back to a plain token property.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The golden corpus" and "The fixture values are chosen, not incidental".
    /// </remarks>
    public class SaveGoldenCorpusTests
    {
        private const string Key = "corpus";

        /// <summary>
        /// Mirrors <c>SaveCorpusGenerator.FixtureV1</c>'s shape: a stand-in for a save model that does
        /// not exist yet, not this game's real one.
        /// </summary>
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

            Assert.AreEqual("1.50", loaded.Multiplier.ToString(CultureInfo.InvariantCulture),
                "the trailing zero is lost the moment FloatParseHandling.Decimal is removed from SaveEnvelope.Parse");

            Assert.AreEqual("2026-09-01T10:00:00.123456789", loaded.HighPrecisionTimestamp);

            Assert.AreEqual("2026-09-01T10:00:00+05:00", loaded.ZonedTimestamp);

            Assert.AreEqual(9007199254740993L, loaded.LargeId);
        }

        /// <summary>
        /// See docs/saving.md, "The golden corpus".
        /// </summary>
        [Test]
        public void V1Json_OnDisk_StillLiteralyContainsTheTrailingZero_NotShortenedTo1Point5()
        {
            string raw = File.ReadAllText(CorpusPath);

            StringAssert.Contains("\"Multiplier\":1.50,", raw);
            Assert.IsFalse(raw.Contains("\"Multiplier\":1.5,"),
                "the corpus file must carry the trailing-zero decimal verbatim, not a shortened 1.5");
        }
    }
}
