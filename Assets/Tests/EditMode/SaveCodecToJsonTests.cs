using System;
using System.Linq;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // ISaveCodec.ToJson (property 7, docs/saving.md "ToJson, and why a migration cannot go through
    // Decode<T>"): every codec's own bytes, as a JSON document. Each codec is asserted against what
    // its own Encode actually produced, never against a value merely equal after a fresh
    // deserialize - that would not catch ToJson quietly reformatting or routing through the wrong
    // underlying codec.
    public class SaveCodecToJsonTests
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        private class TestState
        {
            public int Value;
        }

        [Test]
        public void JsonCodec_ToJson_ReturnsExactlyWhatItsOwnEncodeProduced()
        {
            JsonCodec codec = new();
            byte[] encoded = codec.Encode(new TestState { Value = 42 });

            string json = codec.ToJson(encoded);

            Assert.AreEqual(Utf8.GetString(encoded), json);
        }

        [Test]
        public void PrettyJsonCodec_ToJson_ReturnsExactlyWhatItsOwnEncodeProduced()
        {
            PrettyJsonCodec codec = new();
            byte[] encoded = codec.Encode(new TestState { Value = 42 });

            string json = codec.ToJson(encoded);

            Assert.AreEqual(Utf8.GetString(encoded), json);
        }

        [Test]
        public void GzipJsonCodec_ToJson_ReturnsTheUnderlyingJsonCodecsOwnJson()
        {
            // GzipJsonCodec composes JsonCodec rather than duplicating its serialization (see
            // GzipJsonCodec's own header): its own contribution is the gzip layer, so ToJson has to
            // hand back exactly what a plain JsonCodec would have produced for the same value, not
            // merely something that deserializes to an equal object.
            GzipJsonCodec gzip = new();
            JsonCodec plain = new();
            TestState state = new() { Value = 42 };
            byte[] gzipEncoded = gzip.Encode(state);
            byte[] plainEncoded = plain.Encode(state);

            string json = gzip.ToJson(gzipEncoded);

            Assert.AreEqual(Utf8.GetString(plainEncoded), json);
        }

        // --- GzipJsonCodec on bad input: both entry points throw -----------------------------
        //
        // ISaveCodec's contract is two outcomes: the value, or a throw. A truncated gzip stream can
        // decompress to zero bytes without the stream itself complaining, and then Decode<T> hands
        // back null and ToJson hands back "" - a third outcome, silent corruption, that every caller
        // would have to know to check for. SaveService happens to null-guard Decode<T>, but a
        // migration fed "" fails somewhere else entirely, and nothing else that holds a codec is
        // protected at all. So truncation has to be refused where it is detected, by the codec,
        // through either entry point. The exception type is deliberately not pinned: what matters
        // is that something is thrown, not which layer noticed first.

        [Test]
        public void GzipJsonCodec_ToJson_OnTruncatedBytes_Throws_AndSoDoesDecode()
        {
            GzipJsonCodec codec = new();
            byte[] valid = codec.Encode(new TestState { Value = 1 });
            byte[] truncated = valid.Take(5).ToArray();

            Assert.Catch<Exception>(() => codec.Decode<TestState>(truncated),
                "Decode<T> on a truncated stream has to throw, never quietly hand back null");

            Assert.Catch<Exception>(() => codec.ToJson(truncated),
                "ToJson shares Decode<T>'s Decompress step and the same contract: a truncated stream throws rather than reading as an empty document");
        }

        [Test]
        public void GzipJsonCodec_ToJson_OnBytesThatAreNotGzipAtAll_ThrowsTheSameExceptionTypeAsDecode()
        {
            GzipJsonCodec codec = new();
            byte[] notGzip = Utf8.GetBytes("this is not gzip data at all");

            // Assert.Catch, not Assert.Throws: the latter requires the exact type given, and the
            // whole point here is to accept whatever type this runtime's GZipStream actually throws
            // and then hold ToJson to that same type - not to assume it in advance.
            Exception fromDecode = Assert.Catch<Exception>(() => codec.Decode<TestState>(notGzip));
            Exception fromToJson = Assert.Catch<Exception>(() => codec.ToJson(notGzip));

            Assert.AreEqual(fromDecode.GetType(), fromToJson.GetType(),
                "ToJson and Decode<T> share the same Decompress step, so genuinely non-gzip bytes have to fail with the same exception type through either entry point");
        }
    }
}
