using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Saving.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// <see cref="SavePipelineProbe"/> against <see cref="SaveStorage"/>.InMemory.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "SavePipelineProbeTests, and why isolation is per-key not per-store".
    /// </remarks>
    public class SavePipelineProbeTests
    {
        private string _key;
        private SaveFactoryInputs _inputs;

        [SetUp]
        public void SetUp()
        {
            _key = "probe-" + Guid.NewGuid();
            _inputs = SaveFactoryInputs.Defaults();
        }

        private static IEnumerable<object[]> EveryCodecAndProtection()
        {
            foreach (SaveCodec codec in Enum.GetValues(typeof(SaveCodec)).Cast<SaveCodec>())
            foreach (SaveProtection protection in Enum.GetValues(typeof(SaveProtection)).Cast<SaveProtection>())
                yield return new object[] { codec, protection };
        }

        [TestCaseSource(nameof(EveryCodecAndProtection))]
        public void RunAsync_EveryCombination_RoundTripsAndRendersSomething(SaveCodec codec, SaveProtection protection)
        {
            SaveInspectorDocument document = new() { Balance = 1250, Nickname = "Ada", Level = 4 };

            SaveProbeResult result = SynchronousUniTask.Result(
                SavePipelineProbe.RunAsync(SaveStorage.InMemory, codec, protection, _inputs, _key, document, CancellationToken.None));

            Assert.AreEqual(document.Balance, result.Loaded.Balance);
            Assert.AreEqual(document.Nickname, result.Loaded.Nickname);
            Assert.AreEqual(document.Level, result.Loaded.Level);
            Assert.IsNotEmpty(result.RenderedText);
            Assert.Greater(result.ByteCount, 0);
        }

        /// <remarks>
        /// See docs/saving.md, "The envelope".
        /// See docs/saving.md, "The bytes are always renderable, and that is structural".
        /// </remarks>
        [TestCaseSource(nameof(EveryCodecAndProtection))]
        public void RunAsync_EveryCombination_StoresValidUtf8NotAHexDump(SaveCodec codec, SaveProtection protection)
        {
            SaveInspectorDocument document = new() { Balance = 1250, Nickname = "Ada", Level = 4 };

            SaveProbeResult result = SynchronousUniTask.Result(
                SavePipelineProbe.RunAsync(SaveStorage.InMemory, codec, protection, _inputs, _key, document, CancellationToken.None));

            Assert.IsFalse(result.IsHexDump, $"{codec}/{protection} unexpectedly needed the hex fallback");
        }

        // RawBytes is documented as what actually landed, read back through the store rather than
        // re-encoded. The shared InMemory store is the one SaveComponentFactory hands back again
        // here, so reading the probe's own key from it is an independent look at what landed. Aes
        // draws a fresh IV per save, so a probe that re-encoded instead would differ from the store
        // even for the same document.
        [TestCaseSource(nameof(EveryCodecAndProtection))]
        public void RunAsync_EveryCombination_ReportsExactlyTheBytesThatLandedInTheStore(SaveCodec codec, SaveProtection protection)
        {
            SaveInspectorDocument document = new() { Balance = 1250, Nickname = "Ada", Level = 4 };

            SaveProbeResult result = SynchronousUniTask.Result(
                SavePipelineProbe.RunAsync(SaveStorage.InMemory, codec, protection, _inputs, _key, document, CancellationToken.None));

            ISaveStore store = SaveComponentFactory.CreateStore(SaveStorage.InMemory, _inputs);
            byte[] landed = SynchronousUniTask.Result(store.ReadAsync(_key, CancellationToken.None));

            Assert.IsNotNull(landed, "guard: the probe has to have written under its own key in the shared InMemory store");
            CollectionAssert.AreEqual(landed, result.RawBytes,
                $"{codec}/{protection}: RawBytes has to be exactly what the store holds under the key");
            Assert.AreEqual(result.RawBytes.Length, result.ByteCount, "ByteCount has to count the bytes it reports");
            Assert.AreEqual(new UTF8Encoding(false).GetString(landed), result.RenderedText,
                "valid UTF-8 has to render as itself, not a summary or a re-serialisation");

            SaveEnvelope envelope = SaveEnvelope.Parse(new UTF8Encoding(false).GetString(landed));
            Assert.AreEqual(result.CodecId, envelope.CodecId, "the codec the probe reports has to be the one the stored envelope names");
            Assert.AreEqual(result.ProtectorId, envelope.ProtectorId, "the protector the probe reports has to be the one the stored envelope names");
        }

        [Test]
        public void Render_OfGenuinelyInvalidUtf8_FallsBackToAHexDump()
        {
            byte[] invalid = { 0x7B, 0xFF, 0xFE, 0x00, 0x41 };

            (string text, bool isHexDump) = SavePipelineProbe.Render(invalid);

            Assert.IsTrue(isHexDump);
            StringAssert.Contains("FF", text);
        }

        [Test]
        public void Render_OfEmptyBytes_IsEmptyTextNotAHexDump()
        {
            (string text, bool isHexDump) = SavePipelineProbe.Render(Array.Empty<byte>());

            Assert.AreEqual(string.Empty, text);
            Assert.IsFalse(isHexDump);
        }

        [Test]
        public void RunBaselineAsync_UsesJsonAndNone()
        {
            SaveInspectorDocument document = new() { Balance = 1250, Nickname = "Ada", Level = 4 };

            SaveProbeResult baseline = SynchronousUniTask.Result(
                SavePipelineProbe.RunBaselineAsync(SaveStorage.InMemory, _inputs, "baseline-" + Guid.NewGuid(), document, CancellationToken.None));

            Assert.AreEqual("json", baseline.CodecId);
            Assert.AreEqual("none", baseline.ProtectorId);
        }

        /// <remarks>
        /// See docs/saving.md, "SaveBenchmark, and the number the plan got wrong".
        /// </remarks>
        [Test]
        public void RunAsync_WithJsonGzip_IsSmallerThanTheComputedBaselineForALargeDocument()
        {
            SaveInspectorDocument document = new() { Balance = 42, Nickname = new string('A', 2000), Level = 9 };

            SaveProbeResult baseline = SynchronousUniTask.Result(
                SavePipelineProbe.RunBaselineAsync(SaveStorage.InMemory, _inputs, "baseline-" + Guid.NewGuid(), document, CancellationToken.None));
            SaveProbeResult gzip = SynchronousUniTask.Result(
                SavePipelineProbe.RunAsync(SaveStorage.InMemory, SaveCodec.JsonGzip, SaveProtection.None, _inputs,
                    "gzip-" + Guid.NewGuid(), document, CancellationToken.None));

            Assert.Less(gzip.ByteCount, baseline.ByteCount);
        }
    }
}
