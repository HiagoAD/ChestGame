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

        // --- GzipJsonCodec.ToJson on bad input fails the same way Decode<T> does -------------------
        //
        // "The same way" is read from what Decode<T> actually does on this runtime, not from a
        // hardcoded .NET exception type - GzipJsonCodecTests already shows Decode<T> on 5 truncated
        // bytes degrades quietly (Decompress produces zero bytes, JsonConvert.DeserializeObject of
        // an empty string returns null, no exception at all - exactly the case SaveService's own
        // LoadAsync comment calls out and null-guards against), while genuinely non-gzip bytes do
        // throw. ToJson shares Decode<T>'s Decompress step for both, so it has to match Decode<T>'s
        // own behaviour on each input - not "throws InvalidDataException" as a fixed assumption.

        [Test]
        public void GzipJsonCodec_ToJson_OnTruncatedBytes_DegradesQuietly_TheSameWayDecodeDoes()
        {
            GzipJsonCodec codec = new();
            byte[] valid = codec.Encode(new TestState { Value = 1 });
            byte[] truncated = valid.Take(5).ToArray();

            TestState decoded = null;
            Assert.DoesNotThrow(() => decoded = codec.Decode<TestState>(truncated),
                "pins Decode<T>'s own behaviour on this input before comparing ToJson against it");
            Assert.IsNull(decoded, "this truncation decompresses to zero bytes, which DeserializeObject reads as null rather than failing");

            string json = null;
            Assert.DoesNotThrow(() => json = codec.ToJson(truncated),
                "ToJson shares the same Decompress step, so it must degrade the same way Decode<T> just did, not throw where Decode<T> did not");
            Assert.AreEqual(string.Empty, json);
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
