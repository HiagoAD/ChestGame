using System;
using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // The corrected claim (docs/saving.md, "Value-exactness, and where the formatting stops"):
    // every VALUE in a text-safe body survives GetBody(Wrap(x)) exactly - the date-shaped string,
    // the fractional seconds and the trailing-zero decimal all unchanged - but the BYTES do not,
    // because JRaw.Create(reader) re-serialises the captured body through a fresh, default-formatted
    // writer on read. That normalisation is invisible for JsonCodec, whose own output is already
    // compact, and is pinned explicitly for PrettyJsonCodec below rather than left untested.
    // Enumerated with Enum.GetValues so a fourth codec is picked up here automatically; CodecFor's
    // default arm throws rather than silently skipping it, the same reasoning SaveServiceFactory's
    // own switches use for why a missing arm has to be visible rather than quietly wrong.
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

        // Every codec: the value itself survives, regardless of what happens to formatting.
        // Then, per codec, the one further thing that is actually true and worth pinning:
        //  - Json: already compact, so Parse has nothing to normalise - the bytes come back
        //    unchanged, the same guarantee SaveEnvelopeTests pins with a fake codec.
        //  - JsonPretty: Parse normalises its indentation to compact on read. Asserting the
        //    resulting bytes equal what JsonCodec would have written for the same value pins that
        //    normalisation explicitly, so it cannot quietly regress into "no normalisation" or into
        //    "reformats the value too" without a test noticing either way.
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
