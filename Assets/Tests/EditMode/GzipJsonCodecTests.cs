using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <c>GzipJsonCodec</c>, which composes <c>JsonCodec</c> and gzips its output.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The codecs".
    /// </remarks>
    public class GzipJsonCodecTests
    {
        private class EmptyState { }

        private class RepetitiveState
        {
            public List<string> Items;
        }

        [Test]
        public void Encode_ThenDecode_RoundTripsAnEmptyObject()
        {
            GzipJsonCodec codec = new();

            byte[] encoded = codec.Encode(new EmptyState());
            EmptyState decoded = codec.Decode<EmptyState>(encoded);

            Assert.IsNotNull(decoded);
        }

        [Test]
        public void Encode_ThenDecode_RoundTripsALargeRepetitivePayload()
        {
            GzipJsonCodec codec = new();
            RepetitiveState state = new() { Items = Enumerable.Repeat("chest", 5000).ToList() };

            byte[] encoded = codec.Encode(state);
            RepetitiveState decoded = codec.Decode<RepetitiveState>(encoded);

            CollectionAssert.AreEqual(state.Items, decoded.Items);
        }

        [Test]
        public void Encode_OfACompressiblePayload_ProducesSmallerOutputThanJsonCodec()
        {
            GzipJsonCodec gzip = new();
            JsonCodec json = new();
            RepetitiveState state = new() { Items = Enumerable.Repeat("chest", 5000).ToList() };

            byte[] gzipBytes = gzip.Encode(state);
            byte[] jsonBytes = json.Encode(state);

            Assert.Less(gzipBytes.Length, jsonBytes.Length,
                "5000 repeats of the same short string is exactly the shape gzip exists to shrink");
        }

        private static byte[] TruncatedTo(int keep)
        {
            byte[] valid = new GzipJsonCodec().Encode(new RepetitiveState { Items = Enumerable.Repeat("chest", 50).ToList() });
            Assert.Less(keep, valid.Length, "guard: the truncation has to actually drop something");

            return valid.Take(keep).ToArray();
        }

        [TestCase(5, TestName = "Decode_OnBytesTruncatedInsideTheGzipHeader_Throws")]
        [TestCase(20, TestName = "Decode_OnBytesTruncatedInsideTheCompressedData_Throws")]
        public void Decode_OnTruncatedBytes_Throws_RatherThanReturningNull(int keep)
        {
            GzipJsonCodec codec = new();
            byte[] truncated = TruncatedTo(keep);

            Assert.Catch<Exception>(() => codec.Decode<RepetitiveState>(truncated),
                "a truncated stream has to be refused, never handed back as null or as whatever survived");
        }

        [TestCase(5, TestName = "ToJson_OnBytesTruncatedInsideTheGzipHeader_Throws")]
        [TestCase(20, TestName = "ToJson_OnBytesTruncatedInsideTheCompressedData_Throws")]
        public void ToJson_OnTruncatedBytes_Throws_RatherThanReturningAnEmptyDocument(int keep)
        {
            GzipJsonCodec codec = new();
            byte[] truncated = TruncatedTo(keep);

            Assert.Catch<Exception>(() => codec.ToJson(truncated),
                "a truncated stream has to be refused, not handed to a migration as an empty or partial document");
        }

        [Test]
        public void LoadAsync_WithTruncatedGzipBytes_ThrowsPayloadUnreadable_NotARawInvalidDataException()
        {
            GzipJsonCodec codec = new();
            byte[] validGzip = codec.Encode(new RepetitiveState { Items = new List<string> { "a", "b", "c" } });
            byte[] truncated = validGzip.Take(5).ToArray();

            SaveException error = LoadThroughSeededEnvelope(codec, truncated);

            StringAssert.Contains("could not be read back", error.Message);
        }

        [Test]
        public void LoadAsync_WithBytesThatAreNotGzipAtAll_ThrowsPayloadUnreadable_NotARawInvalidDataException()
        {
            GzipJsonCodec codec = new();
            byte[] notGzip = Encoding.UTF8.GetBytes("this is not gzip data at all");

            SaveException error = LoadThroughSeededEnvelope(codec, notGzip);

            StringAssert.Contains("could not be read back", error.Message);
        }

        private static SaveException LoadThroughSeededEnvelope(GzipJsonCodec codec, byte[] corruptBody)
        {
            FakeSaveStore store = new();
            SaveService service = new(codec, new NoProtection(), store);
            SaveEnvelope envelope = SaveEnvelope.Wrap(SaveService.CurrentSchemaVersion, codec.Id, "none", codec.IsTextSafe, corruptBody);
            store.Seed("key", new UTF8Encoding(false).GetBytes(envelope.Serialize()));

            return Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(service.LoadAsync<RepetitiveState>("key", CancellationToken.None)));
        }
    }
}
