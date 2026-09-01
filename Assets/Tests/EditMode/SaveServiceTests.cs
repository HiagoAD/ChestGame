using System;
using System.Text;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // SaveService's own logic, isolated from JsonCodec's real serialization and from a real file
    // system with FakeSaveCodec, FakePayloadProtector and FakeSaveStore. FileStoreTests covers what
    // only a real file system can prove; SaveEnvelopeTests covers the byte-exact round trip.
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

        // A ready-made envelope naming this fixture's own codec and protector ids and the current
        // schema version, so a test that is only exercising one field can leave the rest correct.
        private static string EnvelopeJson(string version = "1", string codec = "\"json\"", string protector = "\"none\"", string body = "{}")
        {
            string v = version == null ? "" : $@"""v"":{version},";
            string b = body == null ? "" : $@",""body"":{body}";
            return $@"{{{v}""codec"":{codec},""prot"":{protector},""enc"":""raw""{b}}}";
        }

        // --- First run versus corrupt (property 2) ----------------------------------------------

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
            // Valid JSON, but nothing that looks like v, codec, prot or body - the shape an
            // unrelated document, not a save, would take.
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

        // --- Version handling (property 3) -------------------------------------------------------

        [Test]
        public void LoadAsync_WhenTheEnvelopeHasNoVersionFieldAtAll_Throws()
        {
            // The case a nullable Version exists to make representable: compared with > or < a null
            // answers false both ways, so only the explicit HasValue check ahead of those
            // comparisons keeps this from reaching the codec silently.
            _store.Seed(Key, Bytes(EnvelopeJson(version: null)));

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)),
                "an envelope with no v field at all has to be refused rather than reaching the codec");
        }

        [Test]
        public void LoadAsync_WhenTheVersionFieldIsExplicitlyJsonNull_IsNotSilentlyTreatedAsVersionZero()
        {
            // A field present but holding JSON null carries exactly as much version information as
            // the field being absent altogether - none. Version is nullable so that case reads as
            // absent rather than as version 0; a Convert.ToInt32(null) of 0 along the way would let
            // this one slip past the HasValue guard the test above just proved works for a truly
            // missing field.
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

        // --- Codec and protector id mismatch (property 4) ----------------------------------------

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
            // The version check comes first: a save from a newer build may legitimately name a
            // codec this one has never heard of, and "written by a newer build" is the more useful
            // thing to report than "unknown codec".
            _store.Seed(Key, Bytes(EnvelopeJson(
                version: (SaveService.CurrentSchemaVersion + 1).ToString(),
                codec: "\"a-codec-from-the-future\"")));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));
            StringAssert.Contains("newer than", error.Message);
        }

        // --- The happy path, wiring the fakes together to confirm they agree with SaveService ----

        [Test]
        public void SaveAsync_ThenLoadAsync_RoundTripsThroughTheConfiguredCodecAndProtector()
        {
            TestState state = new() { Value = 42 };
            _codec.EncodeResult = Bytes(@"{""Value"":42}");
            _codec.DecodeResult = _ => new TestState { Value = 42 };

            SynchronousUniTask.Complete(_service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(42, loaded.Value);
        }

        [Test]
        public void SaveAsync_WhenTheProtectorIsNotTextSafe_StoresABase64BodyThatLoadAsyncCanStillRead()
        {
            // Both real components (JsonCodec, NoProtection) are text-safe, so nothing real ever
            // drives SaveService into computing IsTextSafe as false. This proves the composition -
            // _codec.IsTextSafe && _protector.IsTextSafe - actually reaches SaveEnvelope.Wrap and
            // round-trips through LoadAsync, not just that SaveEnvelope itself can do it in isolation.
            _protector.IsTextSafe = false;
            byte[] binaryPlain = { 0, 1, 2, 254, 255 };
            _codec.EncodeResult = binaryPlain;
            _codec.DecodeResult = bytes => new TestState { Value = bytes.Length };

            SynchronousUniTask.Complete(_service.SaveAsync(Key, new TestState(), CancellationToken.None));
            byte[] stored = SynchronousUniTask.Result(_store.ReadAsync(Key, CancellationToken.None));
            SaveEnvelope writtenEnvelope = SaveEnvelope.Parse(new UTF8Encoding(false).GetString(stored));
            Assert.AreEqual(SaveEnvelope.Base64Encoding, writtenEnvelope.BodyEncoding,
                "a non-text-safe protector has to push the envelope onto the base64 branch");

            TestState loaded = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(binaryPlain.Length, loaded.Value, "and LoadAsync has to be able to read what SaveAsync wrote");
        }

        // --- Cancellation (property 7): SaveAsync stops before the value is ever encoded ---------

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

        // --- What a save failure must be, unlike a pooling failure -------------------------------

        [Test]
        public void SaveException_IsUnderChestGameException()
        {
            // Unlike PoolException: every failure saving reports can happen to a player who wired
            // the game correctly (a full disk, a save a newer build wrote), where every pool
            // failure is a wiring mistake only a developer can cause. See docs/saving.md.
            Assert.IsInstanceOf<ChestGameException>(SaveException.NoKey());
        }
    }
}
