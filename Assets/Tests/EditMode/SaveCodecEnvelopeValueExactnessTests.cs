using System;
using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests the corrected claim: every value in a text-safe body survives
    /// <c>GetBody(Wrap(x))</c> exactly, even though the bytes do not.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Value-exactness, and where the formatting stops".
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class SaveCodecEnvelopeValueExactnessTests
    {
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
                byte[] compactEquivalent = new JsonCodec().Encode(state);
                CollectionAssert.AreEqual(compactEquivalent, body,
                    "PrettyJsonCodec's indentation has to be normalised to compact on read, matching byte for byte what JsonCodec would have written for the same value");
            }
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
