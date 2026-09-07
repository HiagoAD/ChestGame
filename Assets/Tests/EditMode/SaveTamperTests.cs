using System;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Saving.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // One test per protector, each pinning the finding docs/saving.md states for phase 8a: None,
    // Base64 and Xor are obfuscation the demo can decode, edit and re-encode, so a reload hands back
    // the tampered value; Hmac and Aes catch the edit and refuse to load it at all. Storage is
    // InMemory throughout - see SavePipelineProbeTests for why a unique key per test is what keeps
    // that process-shared store from leaking between cases.
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

        [Test]
        public void RunAsync_WithBase64_LoadsTheTamperedBalance()
        {
            // Base64 is a second illegibility layered on the envelope's own - see docs/saving.md,
            // "Base64Obfuscator" - not protection, so the demo decoding it and re-encoding is exactly
            // what a curious player with a decoder can do too.
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
