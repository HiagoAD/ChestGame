using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using UnityEditor;
using UnityEngine;

namespace Company.ChestGame.Editor
{
    // Produces a golden corpus file under Assets/Tests/EditMode/SaveCorpus/ by running a fixture
    // through the real pipeline (SaveService over JsonCodec and NoProtection) and freezing whatever
    // bytes come out - never bytes typed by hand to look plausible.
    //
    // NEVER re-run this against a version already in the corpus. Regenerating an existing corpus
    // file defeats the entire reason it exists: the file's value is that it is what an *old* build
    // actually wrote, and a file this tool writes today only proves today's build agrees with
    // itself - a normal round-trip test already does that far more directly. This tool exists to add
    // the *next* version's file, once CurrentSchemaVersion has moved past it and a real migration
    // needs bytes that predate it to prove itself against - see docs/saving.md, "The golden corpus"
    // and "What adding a schema version takes". It refuses outright if the target file exists.
    //
    // v1.json was regenerated once, during phase 4 review, before CurrentSchemaVersion ever moved
    // past it - see docs/saving.md, "The golden corpus", for why that one regeneration was legitimate
    // and the rule above is not already broken.
    public static class SaveCorpusGenerator
    {
        private const string CorpusRelativeDirectory = "Tests/EditMode/SaveCorpus";
        private const string Key = "corpus";

        // Stand-in for a save model that does not exist yet - phase 7 defines the real one. Nothing
        // here names ResourceBank, CurrencyType or any other game type.
        //
        // Every field past Note/Coins is here because it was verified, by an A/B comparison against
        // SaveEnvelope.Parse with DateParseHandling.None/FloatParseHandling.Decimal stripped out, to
        // actually come back different - not merely because it looks fragile. Do not "tidy" any of
        // these back to their more obvious-looking form; that is exactly the edit each one exists to
        // catch:
        //   - Multiplier keeps its trailing zero (1.50). Without FloatParseHandling.Decimal this
        //     comes back 1.5.
        //   - HighPrecisionTimestamp carries 9 fractional-second digits - past the 7 (100ns ticks) a
        //     .NET DateTime can hold. Anything that round-trips it through DateTime truncates it.
        //   - ZonedTimestamp carries a UTC offset. A naive round trip (verified against
        //     JsonConvert.DeserializeObject<SaveEnvelope>, the implementation SaveEnvelope's own
        //     header warns against reintroducing) converts it to the parsing machine's local offset
        //     instead of preserving it - the kind of failure that would only show up on whichever
        //     machine's clock happens to disagree with UTC.
        //   - LargeId sits one past 2^53, the largest integer a double can represent exactly, in
        //     case anything ever routes it through one.
        // A bare date with no time component ("2026-09-01") was tried and rejected for this fixture:
        // on the Newtonsoft version this project ships, it round-trips correctly either way, so it
        // would not actually have caught anything - see docs/saving.md, "The golden corpus".
        private class FixtureV1
        {
            public string Note = "golden corpus fixture, not a real save model";
            public long Coins = 1250;
            public decimal Multiplier = 1.50m;
            public string HighPrecisionTimestamp = "2026-09-01T10:00:00.123456789";
            public string ZonedTimestamp = "2026-09-01T10:00:00+05:00";
            public long LargeId = 9007199254740993;
        }

        [MenuItem("Tools/Saving/Generate Golden Corpus File (v1)")]
        public static void GenerateV1()
        {
            string directory = Path.Combine(Application.dataPath, CorpusRelativeDirectory);
            string path = Path.Combine(directory, "v1.json");

            if (File.Exists(path))
            {
                Debug.LogError($"{path} already exists. Regenerating an existing corpus file defeats its entire purpose - see SaveCorpusGenerator's header. Nothing was written.");
                return;
            }

            // InMemoryStore rather than a real file: what is being proven is the envelope, the codec
            // and the protector, not where the resulting bytes happen to land.
            InMemoryStore scratch = new();
            SaveService service = new(new JsonCodec(), new NoProtection(), scratch);

            // Every component in this pipeline (JsonCodec, NoProtection, InMemoryStore) completes
            // synchronously, so blocking on the awaiter here is safe rather than a deadlock risk -
            // the same reasoning Tests/Common/SynchronousUniTask relies on, just without a test
            // assembly this editor-only tool has no business depending on.
            service.SaveAsync(Key, new FixtureV1(), CancellationToken.None).GetAwaiter().GetResult();
            byte[] bytes = scratch.ReadAsync(Key, CancellationToken.None).GetAwaiter().GetResult();

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            Debug.Log($"Wrote golden corpus file: {path}");
        }
    }
}
