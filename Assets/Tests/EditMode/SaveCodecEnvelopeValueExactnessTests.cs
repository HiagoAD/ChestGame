using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Company.ChestGame.Saving;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // The corrected claim (docs/saving.md, "Value-exactness, and where the formatting stops"):
    // every VALUE in a text-safe body survives GetBody(Wrap(x)) exactly - the date-shaped string,
    // the fractional seconds and the trailing-zero decimal all unchanged - but the BYTES are only
    // promised for a body that was compact to begin with. Whitespace inside an indented body is
    // formatting, not data, so for PrettyJsonCodec the body is held to being the same JSON document
    // its codec wrote, token for token, and nothing either way about its indentation: the envelope
    // may normalise it or keep it. Enumerated with Enum.GetValues so a fourth codec is picked up
    // here automatically; CodecFor's default arm throws rather than silently skipping it, the same
    // reasoning SaveServiceFactory's own switches use for why a missing arm has to be visible
    // rather than quietly wrong.
    public class SaveCodecEnvelopeValueExactnessTests
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        private class DatedState { public string LastPlayed; }
        private class TimestampState { public string At; }
        private class MultiplierState { public decimal Multiplier; }

        private static IEnumerable<SaveCodec> EveryCodec() => Enum.GetValues(typeof(SaveCodec)).Cast<SaveCodec>();

        private static ISaveCodec CodecFor(SaveCodec codec) =>
            codec switch
            {
                SaveCodec.Json => new JsonCodec(),
                SaveCodec.JsonPretty => new PrettyJsonCodec(),
                SaveCodec.JsonGzip => new GzipJsonCodec(),
                _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "a new SaveCodec member needs a mapping here too")
            };

        // Every codec: the value itself survives, regardless of what happens to formatting.
        // Then, per codec, the one further thing that is actually promised:
        //  - Json: already compact, so there is nothing to normalise - the bytes come back
        //    unchanged, the same guarantee SaveEnvelopeTests pins with a fake codec.
        //  - JsonPretty: the body read back is the same JSON document the codec encoded - every
        //    token, every value, in the same shape - compared as parsed documents, so stripping the
        //    indentation and keeping it both pass, and dropping a field or changing a value both
        //    fail. DeepEquals compares numbers by value, so 1.50 and 1.5 would agree; each document
        //    is also re-written compact by the same writer and compared as text, which keeps that
        //    trailing zero honest without caring how the original was indented.
        //  - JsonGzip: not text-safe, so its body only ever travels as base64, already proven exact
        //    on its own in SaveEnvelopeTests. Nothing further to pin about formatting here.
        private static void AssertSurvives<T>(SaveCodec codecKind, T state, Func<T, object> select, object expected)
        {
            ISaveCodec codec = CodecFor(codecKind);
            byte[] encoded = codec.Encode(state);
            SaveEnvelope envelope = SaveEnvelope.Wrap(1, codec.Id, "none", codec.IsTextSafe, encoded);
            SaveEnvelope parsed = SaveEnvelope.Parse(envelope.Serialize());
            byte[] body = parsed.GetBody();

            T decoded = codec.Decode<T>(body);
            Assert.AreEqual(expected, select(decoded), $"{codec.Id}'s value must survive the round trip unchanged");

            if (codecKind == SaveCodec.Json)
            {
                CollectionAssert.AreEqual(encoded, body,
                    "JsonCodec's own compact bytes have nothing for Parse to normalise, so they must come back byte for byte");
            }
            else if (codecKind == SaveCodec.JsonPretty)
            {
                JToken written = ParseExactly(encoded);
                JToken readBack = ParseExactly(body);
                Assert.IsTrue(JToken.DeepEquals(written, readBack),
                    $"PrettyJsonCodec's body has to come back as the same JSON document it encoded, whitespace aside; wrote {written.ToString(Formatting.None)}, read back {readBack.ToString(Formatting.None)}");
                Assert.AreEqual(written.ToString(Formatting.None), readBack.ToString(Formatting.None),
                    "and every value has to be written exactly as it was encoded, not merely to something numerically equal");
            }
        }

        // DateParseHandling.None and FloatParseHandling.Decimal for the same reason SaveEnvelope.Parse
        // uses them: the default reader would turn "2026-09-01" into a DateTime and 1.50 into a
        // double on the way in, and two documents could then compare equal while their text did not
        // say the same thing.
        private static JToken ParseExactly(byte[] json)
        {
            using StringReader text = new(Utf8.GetString(json));
            using JsonTextReader reader = new(text)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal
            };

            return JToken.ReadFrom(reader);
        }

        [TestCaseSource(nameof(EveryCodec))]
        public void ADateShapedString_SurvivesThroughTheEnvelope(SaveCodec codecKind)
        {
            DatedState state = new() { LastPlayed = "2026-09-01" };

            AssertSurvives(codecKind, state, s => s.LastPlayed, "2026-09-01");
        }

        [TestCaseSource(nameof(EveryCodec))]
        public void AFractionalSecondsTimestamp_SurvivesThroughTheEnvelope(SaveCodec codecKind)
        {
            TimestampState state = new() { At = "2026-09-01T12:34:56.789" };

            AssertSurvives(codecKind, state, s => s.At, "2026-09-01T12:34:56.789");
        }

        [TestCaseSource(nameof(EveryCodec))]
        public void ADecimalWithATrailingZero_SurvivesThroughTheEnvelope(SaveCodec codecKind)
        {
            MultiplierState state = new() { Multiplier = 1.50m };

            AssertSurvives(codecKind, state, s => s.Multiplier, 1.50m);
        }
    }
}
