using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using UnityEditor;
using UnityEngine;

namespace Company.ChestGame.Editor
{
    // Produces a golden corpus file under Assets/Tests/EditMode/SaveCorpus/ by running a fixture
    // through the real pipeline and freezing whatever bytes come out - never bytes typed by hand to
    // look plausible.
    //
    // NEVER re-run this against a version already in the corpus - it refuses outright if the
    // target file exists. Use it to add the *next* version's file, once CurrentSchemaVersion has
    // moved past it. See docs/saving.md, "The golden corpus" and "What adding a schema version
    // takes".
    public static class SaveCorpusGenerator
    {
        private const string CorpusRelativeDirectory = "Tests/EditMode/SaveCorpus";
        private const string Key = "corpus";

        // A stand-in save model that names no game type. Do not "tidy" any field past Note/Coins
        // back to its more obvious-looking form; that edit is exactly what each one exists to
        // catch:
        //   - Multiplier keeps its trailing zero (1.50), which comes back 1.5 without
        //     FloatParseHandling.Decimal.
        //   - HighPrecisionTimestamp carries 9 fractional-second digits, past the 7 a .NET DateTime
        //     can hold, so anything round-tripping it through DateTime truncates it.
        //   - ZonedTimestamp carries a UTC offset, which a naive round trip converts to the parsing
        //     machine's local offset - a failure only visible on a machine that disagrees with UTC.
        //   - LargeId sits one past 2^53, the largest integer a double holds exactly.
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

            InMemoryStore scratch = new();
            SaveService service = new(new JsonCodec(), new NoProtection(), scratch);

            // Every component in this pipeline completes synchronously, so blocking on the awaiter
            // is safe here rather than a deadlock risk.
            service.SaveAsync(Key, new FixtureV1(), CancellationToken.None).GetAwaiter().GetResult();
            byte[] bytes = scratch.ReadAsync(Key, CancellationToken.None).GetAwaiter().GetResult();

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            Debug.Log($"Wrote golden corpus file: {path}");
        }
    }
}
