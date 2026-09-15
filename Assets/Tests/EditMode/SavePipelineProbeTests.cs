using System;
using System.Collections.Generic;
using System.Linq;
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

        [TestCaseSource(nameof(EveryCodecAndProtection))]
        public void RunAsync_EveryCombination_RecordsNonNegativeTimings(SaveCodec codec, SaveProtection protection)
        {
            SaveInspectorDocument document = new() { Balance = 1250, Nickname = "Ada", Level = 4 };

            SaveProbeResult result = SynchronousUniTask.Result(
                SavePipelineProbe.RunAsync(SaveStorage.InMemory, codec, protection, _inputs, _key, document, CancellationToken.None));

            Assert.GreaterOrEqual(result.WriteMilliseconds, 0);
            Assert.GreaterOrEqual(result.ReadMilliseconds, 0);
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
