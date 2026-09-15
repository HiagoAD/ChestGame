using System;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Saving.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// One test per protector, pinning the tamper-then-reload outcome for each
    /// <see cref="SaveProtection"/> value. Storage is InMemory throughout - see
    /// <c>SavePipelineProbeTests</c> for why a unique key per test is what keeps that
    /// process-shared store from leaking between cases.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The tamper button, and why it edits two different ways".
    /// </remarks>
    public class SaveTamperTests
    {
        private const long TamperedBalance = 999999;

        private string _key;
        private SaveFactoryInputs _inputs;

        [SetUp]
        public void SetUp()
        {
            _key = "tamper-" + Guid.NewGuid();
            _inputs = SaveFactoryInputs.Defaults();
        }

        private SaveTamperResult TamperAfterWriting(SaveProtection protection)
        {
            SaveInspectorDocument original = new() { Balance = 100, Nickname = "Original", Level = 1 };
            SynchronousUniTask.Result(SavePipelineProbe.RunAsync(
                SaveStorage.InMemory, SaveCodec.Json, protection, _inputs, _key, original, CancellationToken.None));

            return SynchronousUniTask.Result(SaveTamper.RunAsync(
                SaveStorage.InMemory, SaveCodec.Json, protection, _inputs, _key, TamperedBalance, CancellationToken.None));
        }

        [Test]
        public void RunAsync_WithNone_LoadsTheTamperedBalance()
        {
            SaveTamperResult result = TamperAfterWriting(SaveProtection.None);

            Assert.AreEqual(SaveTamperOutcome.Loaded, result.Outcome);
            Assert.AreEqual(TamperedBalance, result.LoadedBalance);
        }

        /// <remarks>
        /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
        /// </remarks>
        [Test]
        public void RunAsync_WithBase64_LoadsTheTamperedBalance()
        {
            SaveTamperResult result = TamperAfterWriting(SaveProtection.Base64);

            Assert.AreEqual(SaveTamperOutcome.Loaded, result.Outcome);
            Assert.AreEqual(TamperedBalance, result.LoadedBalance);
        }

        [Test]
        public void RunAsync_WithXor_LoadsTheTamperedBalance()
        {
            SaveTamperResult result = TamperAfterWriting(SaveProtection.Xor);

            Assert.AreEqual(SaveTamperOutcome.Loaded, result.Outcome);
            Assert.AreEqual(TamperedBalance, result.LoadedBalance);
        }

        [Test]
        public void RunAsync_WithHmac_RejectsTheEditAsTampered()
        {
            SaveTamperResult result = TamperAfterWriting(SaveProtection.Hmac);

            Assert.AreEqual(SaveTamperOutcome.RejectedAsTampered, result.Outcome);
            Assert.IsNull(result.LoadedBalance);
            Assert.IsInstanceOf<SaveTamperedException>(result.Error);
        }

        [Test]
        public void RunAsync_WithAes_RejectsTheEditAsTampered()
        {
            SaveTamperResult result = TamperAfterWriting(SaveProtection.Aes);

            Assert.AreEqual(SaveTamperOutcome.RejectedAsTampered, result.Outcome);
            Assert.IsNull(result.LoadedBalance);
            Assert.IsInstanceOf<SaveTamperedException>(result.Error);
        }

        [Test]
        public void RunAsync_WithNothingStoredUnderTheKey_ThrowsSaveInspectorException()
        {
            Assert.Throws<SaveInspectorException>(() => SynchronousUniTask.Result(SaveTamper.RunAsync(
                SaveStorage.InMemory, SaveCodec.Json, SaveProtection.None, _inputs, _key, TamperedBalance, CancellationToken.None)));
        }
    }
}
