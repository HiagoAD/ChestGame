using System;
using System.Linq;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests <see cref="ISaveCodec.ToJson"/>: every codec's own bytes, as a JSON document.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "ToJson, and why a migration cannot go through Decode&lt;T&gt;".
    /// </remarks>
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
            GzipJsonCodec gzip = new();
            JsonCodec plain = new();
            TestState state = new() { Value = 42 };
            byte[] gzipEncoded = gzip.Encode(state);
            byte[] plainEncoded = plain.Encode(state);

            string json = gzip.ToJson(gzipEncoded);

            Assert.AreEqual(Utf8.GetString(plainEncoded), json);
        }

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

            Exception fromDecode = Assert.Catch<Exception>(() => codec.Decode<TestState>(notGzip));
            Exception fromToJson = Assert.Catch<Exception>(() => codec.ToJson(notGzip));

            Assert.AreEqual(fromDecode.GetType(), fromToJson.GetType(),
                "ToJson and Decode<T> share the same Decompress step, so genuinely non-gzip bytes have to fail with the same exception type through either entry point");
        }
    }
}
