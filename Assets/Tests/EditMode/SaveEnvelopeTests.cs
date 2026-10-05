using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="SaveEnvelope"/> through a full Serialize -&gt; Parse round trip. Every case
    /// here asserts on the raw bytes <c>GetBody</c> hands back, never on a value decoded from them: a
    /// decoded object can compare equal while the bytes underneath differ, which is exactly the
    /// failure this file exists to catch.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Value-exactness, and where the formatting stops".
    /// </remarks>
    public class SaveEnvelopeTests
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        private static bool CombinedTextSafe(bool codecTextSafe, bool protectorTextSafe) =>
            new FakeSaveCodec { IsTextSafe = codecTextSafe }.IsTextSafe &&
            new FakePayloadProtector { IsTextSafe = protectorTextSafe }.IsTextSafe;

        private static byte[] RoundTrip(byte[] payload, bool textSafe)
        {
            SaveEnvelope written = SaveEnvelope.Wrap(1, "json", "none", textSafe, payload);
            string json = written.Serialize();
            SaveEnvelope read = SaveEnvelope.Parse(json);
            return read.GetBody();
        }

        [Test]
        public void ARawBody_ContainingADateShapedString_SurvivesByteForByte()
        {
            byte[] payload = Utf8.GetBytes(@"{""lastPlayed"":""2026-09-01""}");

            byte[] roundTripped = RoundTrip(payload, textSafe: true);

            CollectionAssert.AreEqual(payload, roundTripped);
        }

        [Test]
        public void ARawBody_ContainingFractionalSeconds_SurvivesByteForByte()
        {
            byte[] payload = Utf8.GetBytes(@"{""at"":""2026-09-01T12:34:56.789""}");

            byte[] roundTripped = RoundTrip(payload, textSafe: true);

            CollectionAssert.AreEqual(payload, roundTripped);
        }

        [Test]
        public void ARawBody_ContainingADecimalWithATrailingZero_SurvivesByteForByte()
        {
            byte[] payload = Utf8.GetBytes(@"{""multiplier"":1.50}");

            byte[] roundTripped = RoundTrip(payload, textSafe: true);

            CollectionAssert.AreEqual(payload, roundTripped);
        }

        /// <summary>
        /// See docs/saving.md, "The envelope".
        /// </summary>
        [Test]
        public void ARawBody_IsEmbeddedLiterallyRatherThanAsAQuotedString()
        {
            byte[] payload = Utf8.GetBytes(@"{""x"":1}");

            string json = SaveEnvelope.Wrap(1, "json", "none", textSafe: true, payload).Serialize();

            StringAssert.Contains(SaveEnvelope.RawEncoding, json);
            StringAssert.DoesNotContain(SaveEnvelope.Base64Encoding, json);
            StringAssert.Contains(@"""body"": {""x"":1}", json);
        }

        [Test]
        public void ANonTextSafeBody_SurvivesByteForByte_CarriedAsBase64()
        {
            bool textSafe = CombinedTextSafe(codecTextSafe: true, protectorTextSafe: false);
            Assert.IsFalse(textSafe, "guard: the combination this test exists for has to actually be non-text-safe");

            byte[] payload = { 0x00, 0x01, 0x02, 0x7B, 0x22, 0x5C, 0xFF, 0xFE, 0x0A, 0x0D };

            byte[] roundTripped = RoundTrip(payload, textSafe);

            CollectionAssert.AreEqual(payload, roundTripped);
        }

        [Test]
        public void ANonTextSafeBody_IsRecordedAsBase64InTheEnvelope()
        {
            byte[] payload = { 1, 2, 3 };

            SaveEnvelope written = SaveEnvelope.Wrap(1, "json", "none", textSafe: false, payload);

            Assert.AreEqual(SaveEnvelope.Base64Encoding, written.BodyEncoding);
        }

        /// <summary>
        /// See docs/saving.md, "Value-exactness, and where the formatting stops".
        /// </summary>
        [Test]
        public void ABase64Body_ParsesBackEvenWhenBodyArrivesBeforeEnc()
        {
            byte[] payload = { 9, 8, 7 };
            string base64 = System.Convert.ToBase64String(payload);
            string json = $@"{{""v"":1,""body"":""{base64}"",""codec"":""json"",""prot"":""none"",""enc"":""b64""}}";

            SaveEnvelope parsed = SaveEnvelope.Parse(json);
            byte[] body = parsed.GetBody();

            CollectionAssert.AreEqual(payload, body);
        }
    }
}
