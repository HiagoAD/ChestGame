using System;
using System.Linq;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <c>ISaveCodec.ToJson</c> (property 7): every codec's own bytes, as a JSON document.
    /// Each codec is asserted against what its own <c>Encode</c> actually produced.
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

        /// <remarks>
        /// See docs/saving.md, "The codecs".
        /// </remarks>
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

            Exception fromDecode = Assert.Catch<Exception>(() => codec.Decode<TestState>(notGzip));
            Exception fromToJson = Assert.Catch<Exception>(() => codec.ToJson(notGzip));

            Assert.AreEqual(fromDecode.GetType(), fromToJson.GetType(),
                "ToJson and Decode<T> share the same Decompress step, so genuinely non-gzip bytes have to fail with the same exception type through either entry point");
        }
    }
}
