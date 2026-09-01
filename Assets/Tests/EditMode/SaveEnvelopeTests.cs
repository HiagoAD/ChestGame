using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // GetBody(Wrap(x)) has to reproduce x byte for byte, through a full Serialize -> Parse round
    // trip: phase 3's HMAC protector signs the exact bytes it is handed, and a body that merely
    // decoded to something equal would fail that signature on every valid save. Every case here
    // therefore asserts on the raw bytes GetBody hands back, never on a value decoded from them - a
    // decoded object can compare equal while the bytes underneath differ, which is exactly the
    // failure this file exists to catch. See docs/saving.md.
    public class SaveEnvelopeTests
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        // Both real components (JsonCodec, NoProtection) report IsTextSafe => true, so nothing real
        // ever drives the base64 branch. A fake protector reporting false stands in for whatever
        // phase 3's HMAC or an obfuscating codec will actually be.
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

        // --- The text-safe branch: body embedded raw --------------------------------------------

        [Test]
        public void ARawBody_ContainingADateShapedString_SurvivesByteForByte()
        {
            // The obvious implementation - JsonConvert.DeserializeObject<SaveEnvelope> - rebuilds
            // Body from Newtonsoft's own object model, which reinterprets a date-shaped string as a
            // DateTime and hands it back as "2026-09-01T00:00:00". This is this game's own save
            // model's shape, not a contrived one.
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
            // The obvious implementation's numeric path returns 1.5 for 1.50: DateParseHandling.None
            // alone does not save this one, FloatParseHandling.Decimal has to as well.
            byte[] payload = Utf8.GetBytes(@"{""multiplier"":1.50}");

            byte[] roundTripped = RoundTrip(payload, textSafe: true);

            CollectionAssert.AreEqual(payload, roundTripped);
        }

        [Test]
        public void ARawBody_IsEmbeddedLiterallyRatherThanAsAQuotedString()
        {
            // The property above would also pass a base64-only implementation that happened to
            // round-trip; this pins the raw branch's own shape, that the body sits in the envelope
            // as JSON rather than as text wrapped in quotes.
            byte[] payload = Utf8.GetBytes(@"{""x"":1}");

            string json = SaveEnvelope.Wrap(1, "json", "none", textSafe: true, payload).Serialize();

            StringAssert.Contains(SaveEnvelope.RawEncoding, json);
            StringAssert.DoesNotContain(SaveEnvelope.Base64Encoding, json);
            // A base64 body would appear as a quoted string; the raw branch has to place the body's
            // own JSON straight into the envelope with no surrounding quotes and no escaping.
            StringAssert.Contains(@"""body"": {""x"":1}", json);
        }

        // --- The non-text-safe branch: body carried as base64 ------------------------------------

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

        [Test]
        public void ABase64Body_ParsesBackEvenWhenBodyArrivesBeforeEnc()
        {
            // JRaw captures literal source text, so a base64 body comes back still wrapped in the
            // quotes it was written with unless Parse unwraps it after the read loop rather than
            // inside the switch - which is the only point at which both fields are known, because
            // JSON field order is not guaranteed. This document puts "body" first to prove that.
            byte[] payload = { 9, 8, 7 };
            string base64 = System.Convert.ToBase64String(payload);
            string json = $@"{{""v"":1,""body"":""{base64}"",""codec"":""json"",""prot"":""none"",""enc"":""b64""}}";

            SaveEnvelope parsed = SaveEnvelope.Parse(json);
            byte[] body = parsed.GetBody();

            CollectionAssert.AreEqual(payload, body);
        }
    }
}
