using System;
using System.IO;
using System.Text;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // Property 3: tamper detection is a distinct, typed failure from a merely corrupt or
    // unreadable payload - proven against a real FileStore, since that is the fixture the property
    // asks for, with the contrast against an unprotected save proving the distinction is real.
    // Property 4: a save read back by a protector holding a different key fails exactly the same
    // way a genuinely tampered save does, because a MAC has no way to tell "tampered" from "signed
    // under a different key" apart. See docs/saving.md, "Tamper detection is a different failure
    // from a corrupt payload".
    public class SaveServiceTamperDetectionTests
    {
        private const string Key = "profile";
        private static readonly UTF8Encoding Utf8 = new(false);

        private string _root;

        private class TestState
        {
            public int Value;
            public string Label;
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static byte[] KeyBytes(string seed) => Encoding.UTF8.GetBytes(seed);

        private static IPayloadProtector MakeProtector(string kind, string keySeed) =>
            kind switch
            {
                "hmac" => new HmacSignedProtector(KeyBytes(keySeed)),
                "aes" => new AesProtector(KeyBytes(keySeed)),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

        private static byte[] FlipLastByte(byte[] bytes)
        {
            byte[] copy = (byte[])bytes.Clone();
            copy[copy.Length - 1] ^= 0xFF;
            return copy;
        }

        // Reads the envelope a real save produced, flips one byte of the protected payload it
        // carries, and writes the tampered envelope straight back through the same real store -
        // simulating a byte flipped on disk between a write and the next load.
        private static void TamperStoredPayload(ISaveStore store, string codecId, string protectorId, bool protectorTextSafe)
        {
            byte[] envelopeBytes = SynchronousUniTask.Result(store.ReadAsync(Key, CancellationToken.None));
            SaveEnvelope envelope = SaveEnvelope.Parse(Utf8.GetString(envelopeBytes));
            byte[] tamperedPayload = FlipLastByte(envelope.GetBody());
            SaveEnvelope tampered = SaveEnvelope.Wrap(SaveService.CurrentSchemaVersion, codecId, protectorId, protectorTextSafe, tamperedPayload);
            SynchronousUniTask.Complete(store.WriteAsync(Key, Utf8.GetBytes(tampered.Serialize()), CancellationToken.None));
        }

        [TestCase("hmac")]
        [TestCase("aes")]
        public void LoadAsync_ThroughARealStore_AfterFlippingAByteInTheProtectedPayload_ReportsTamperingNotUnreadable(string kind)
        {
            FileStore store = new(_root);
            IPayloadProtector protector = MakeProtector(kind, "the-real-key");
            SaveService service = new(new JsonCodec(), protector, store);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 99, Label = "chest" }, CancellationToken.None));
            TamperStoredPayload(store, "json", protector.Id, protector.IsTextSafe);

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None)));

            StringAssert.Contains("integrity check", error.Message);
            StringAssert.DoesNotContain("could not be read back", error.Message,
                "a proven tamper has to read as PayloadTampered, never as the generic PayloadUnreadable");
        }

        [Test]
        public void LoadAsync_ThroughARealStore_AfterFlippingAByteInAnUnprotectedSave_ReportsUnreadableNotTampering()
        {
            // The contrast that proves the distinction above is real: the exact same kind of flip,
            // with no protector at all to detect it, has to land on the generic failure instead.
            FileStore store = new(_root);
            SaveService service = new(new JsonCodec(), new NoProtection(), store);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1, Label = "chest" }, CancellationToken.None));
            TamperStoredPayload(store, "json", "none", protectorTextSafe: true);

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None)));

            StringAssert.Contains("could not be read back", error.Message);
            StringAssert.DoesNotContain("integrity check", error.Message,
                "an unprotected save has no MAC to fail; a corrupted body has to read as PayloadUnreadable, never as PayloadTampered");
        }

        // --- Property 4: a different key reads as tampering, for both protectors -----------------

        [TestCase("hmac")]
        [TestCase("aes")]
        public void LoadAsync_WithAProtectorHoldingADifferentKey_ReportsTamperingNotGarbageOrACryptographicFailure(string kind)
        {
            FakeSaveStore store = new();
            IPayloadProtector writerProtector = MakeProtector(kind, "key-one");
            IPayloadProtector readerProtector = MakeProtector(kind, "key-two-entirely-different");

            SaveService writer = new(new JsonCodec(), writerProtector, store);
            SaveService reader = new(new JsonCodec(), readerProtector, store);

            SynchronousUniTask.Complete(writer.SaveAsync(Key, new TestState { Value = 42, Label = "chest" }, CancellationToken.None));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(reader.LoadAsync<TestState>(Key, CancellationToken.None)));

            StringAssert.Contains("integrity check", error.Message);
        }
    }
}
