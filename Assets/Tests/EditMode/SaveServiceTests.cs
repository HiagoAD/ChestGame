using System;
using System.Linq;
using System.Text;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// SaveService's own logic, isolated from JsonCodec's real serialization and from a real file
    /// system with FakeSaveCodec, FakePayloadProtector and FakeSaveStore.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class SaveServiceTests
    {
        private const string Key = "profile";

        private FakeSaveStore _store;
        private FakeSaveCodec _codec;
        private FakePayloadProtector _protector;
        private SaveService _service;

        private class TestState
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            _store = new FakeSaveStore();
            _codec = new FakeSaveCodec { Id = "json" };
            _protector = new FakePayloadProtector { Id = "none" };
            _service = new SaveService(_codec, _protector, _store);
        }

        private static byte[] Bytes(string text) => new UTF8Encoding(false).GetBytes(text);

        /// <summary>
        /// Builds an envelope JSON string, defaulting every field to this fixture's own codec and
        /// protector ids and the current schema version so a caller overriding only one parameter
        /// leaves the rest valid.
        /// </summary>
        /// <param name="version">Text for the version field, or null to omit the field.</param>
        /// <param name="codec">Text for the codec field, already JSON-quoted.</param>
        /// <param name="protector">Text for the protector field, already JSON-quoted.</param>
        /// <param name="body">Text for the body field, or null to omit the field.</param>
        /// <returns>The envelope as a JSON string.</returns>
        private static string EnvelopeJson(string version = "1", string codec = "\"json\"", string protector = "\"none\"", string body = "{}")
        {
            string v = version == null ? "" : $@"""v"":{version},";
            string b = body == null ? "" : $@",""body"":{body}";
            return $@"{{{v}""codec"":{codec},""prot"":{protector},""enc"":""raw""{b}}}";
        }

        [Test]
        public void LoadAsync_WhenNothingIsStored_ReturnsAFreshInstanceWithoutTouchingTheCodec()
        {
            TestState result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.IsNotNull(result);
            Assert.IsFalse(_codec.DecodeWasCalled, "a first run has nothing to decode");
        }

        [Test]
        public void LoadAsync_WhenTheStoredFileIsZeroLength_ThrowsSaveException()
        {
            _store.Seed(Key, Array.Empty<byte>());

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
        }

        [Test]
        public void LoadAsync_WhenTheStoredBytesAreNotJson_ThrowsSaveException()
        {
            _store.Seed(Key, Bytes("this is not json at all {{{"));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("could not be read back", error.Message);
        }

        [Test]
        public void LoadAsync_WhenTheStoredJsonIsAnObjectButNotAnEnvelope_ThrowsSaveException()
        {
            _store.Seed(Key, Bytes(@"{""unexpected"":""shape""}"));

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
        }

        [Test]
        public void LoadAsync_WhenTheEnvelopeHasNoBodyField_ThrowsSaveException()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(body: null)));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("no body", error.Message);
        }

        [Test]
        public void LoadAsync_WhenTheEnvelopeBodyIsExplicitlyNull_ThrowsSaveException()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(body: "null")));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("no body", error.Message);
        }

        [Test]
        public void LoadAsync_WhenTheEnvelopeHasNoVersionFieldAtAll_Throws()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(version: null)));

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)),
                "an envelope with no v field at all has to be refused rather than reaching the codec");
        }

        [Test]
        public void LoadAsync_WhenTheVersionFieldIsExplicitlyJsonNull_IsNotSilentlyTreatedAsVersionZero()
        {
            _store.Seed(Key, Bytes(@"{""v"":null,""codec"":""json"",""prot"":""none"",""enc"":""raw"",""body"":{}}"));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.DoesNotContain("schema version 0", error.Message,
                "an explicit null must not silently read as a real, if low, schema version");
        }

        [Test]
        public void LoadAsync_WhenVersionIsNewerThanCurrent_ThrowsNamingBothVersions()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(version: (SaveService.CurrentSchemaVersion + 1).ToString())));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("newer than", error.Message);
            Assert.IsFalse(_codec.DecodeWasCalled, "a newer save is refused outright rather than partially read");
        }

        [Test]
        public void LoadAsync_WhenVersionIsOlderThanCurrent_ThrowsNamingBothVersions()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(version: (SaveService.CurrentSchemaVersion - 1).ToString())));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("no migration chain", error.Message);
            Assert.IsFalse(_codec.DecodeWasCalled);
        }

        [Test]
        public void LoadAsync_WhenTheEnvelopesCodecIdDiffersFromWhatIsConfigured_ThrowsRatherThanDecoding()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(codec: "\"a-different-codec\"")));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("codec", error.Message);
            Assert.IsFalse(_codec.DecodeWasCalled, "a mismatched codec must be refused before decoding, not decoded as garbage");
        }

        [Test]
        public void LoadAsync_WhenTheEnvelopesProtectorIdDiffersFromWhatIsConfigured_ThrowsRatherThanDecoding()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(protector: "\"a-different-protector\"")));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("protector", error.Message);
            Assert.IsFalse(_codec.DecodeWasCalled);
        }

        [Test]
        public void LoadAsync_WhenVersionIsNewerAndComponentsAlsoDiffer_ReportsTheVersionRatherThanTheComponent()
        {
            _store.Seed(Key, Bytes(EnvelopeJson(
                version: (SaveService.CurrentSchemaVersion + 1).ToString(),
                codec: "\"a-codec-from-the-future\"")));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("newer than", error.Message);
        }

        [Test]
        public void SaveAsync_ThenLoadAsync_RoundTripsThroughTheConfiguredCodecAndProtector()
        {
            // The decoded value is read out of whatever bytes reach Decode rather than handed back
            // canned, so a LoadAsync that passed the codec anything other than what Encode produced
            // - the whole envelope, a re-serialised body, nothing - changes the answer.
            TestState state = new() { Value = 42 };
            byte[] encoded = Bytes(@"{""Value"":42}");
            _codec.EncodeResult = encoded;
            _codec.DecodeResult = bytes => JsonConvert.DeserializeObject<TestState>(new UTF8Encoding(false).GetString(bytes));

            SynchronousUniTask.Complete(_service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            CollectionAssert.AreEqual(encoded, _codec.LastDecodeInput,
                "Decode has to receive exactly the bytes Encode produced, unwrapped from the envelope and unprotected");
            Assert.AreEqual(42, loaded.Value);
        }

        /// <remarks>
        /// See docs/testing.md, "The save fixtures".
        /// </remarks>
        [Test]
        public void SaveAsync_WhenTheProtectorIsNotTextSafe_StoresABase64BodyThatLoadAsyncCanStillRead()
        {
            _protector.IsTextSafe = false;
            byte[] binaryPlain = { 0, 1, 2, 254, 255 };
            _codec.EncodeResult = binaryPlain;
            // Derived from the content, not just the length: five wrong bytes must not read back
            // the same as the right five.
            _codec.DecodeResult = bytes => new TestState { Value = bytes.Sum(b => (int)b) };

            SynchronousUniTask.Complete(_service.SaveAsync(Key, new TestState(), CancellationToken.None));
            byte[] stored = SynchronousUniTask.Result(_store.ReadAsync(Key, CancellationToken.None));
            SaveEnvelope writtenEnvelope = SaveEnvelope.Parse(new UTF8Encoding(false).GetString(stored));
            Assert.AreEqual(SaveEnvelope.Base64Encoding, writtenEnvelope.BodyEncoding,
                "a non-text-safe protector has to push the envelope onto the base64 branch");

            TestState loaded = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            CollectionAssert.AreEqual(binaryPlain, _codec.LastDecodeInput,
                "the base64 branch has to hand Decode back exactly the bytes Encode produced, not merely as many of them");
            Assert.AreEqual(binaryPlain.Sum(b => (int)b), loaded.Value, "and LoadAsync has to be able to read what SaveAsync wrote");
        }

        [Test]
        public void SaveAsync_WithAnAlreadyCancelledToken_ThrowsBeforeEncodingTheValue()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                SynchronousUniTask.Complete(_service.SaveAsync(Key, new TestState(), cancellation.Token)));

            Assert.IsFalse(_codec.EncodeWasCalled,
                "the store checks cancellation too, but by then the whole value would already be serialised");
        }

        /// <remarks>
        /// See docs/saving.md, "Exceptions".
        /// </remarks>
        [Test]
        public void SaveException_IsUnderChestGameException()
        {
            Assert.IsInstanceOf<ChestGameException>(SaveException.NoKey());
        }
    }
}
