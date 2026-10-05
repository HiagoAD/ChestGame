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
    /// <summary>
    /// Pins that every value in a text-safe body survives <c>GetBody(Wrap(x))</c> exactly, for every
    /// <see cref="SaveCodec"/>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Value-exactness, and where the formatting stops".
    /// </remarks>
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

        /// <summary>
        /// Round-trips <paramref name="state"/> through the codec and the envelope, asserts the
        /// selected value is unchanged, then asserts the one further guarantee that codec makes:
        /// <c>Json</c> bytes come back unchanged, <c>JsonPretty</c> comes back as the same JSON
        /// document, and <c>JsonGzip</c> has nothing further to check.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Value-exactness, and where the formatting stops".
        /// </remarks>
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

        /// <summary>
        /// Parses JSON bytes without reinterpreting dates or decimals, so two documents compare equal
        /// only when their text says the same thing.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Value-exactness, and where the formatting stops".
        /// </remarks>
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
