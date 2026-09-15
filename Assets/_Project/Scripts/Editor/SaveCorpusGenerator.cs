using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using UnityEditor;
using UnityEngine;

namespace Company.ChestGame.Editor
{
    /// <summary>
    /// Generates a golden corpus file under Assets/Tests/EditMode/SaveCorpus/ by running a fixture
    /// through the real pipeline and writing out whatever bytes come out. Refuses to write when a
    /// file for the requested version already exists; use it to add the next version's file, once
    /// <see cref="SaveService.CurrentSchemaVersion"/> has moved past it.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The golden corpus" and "What adding a schema version takes".
    /// </remarks>
    public static class SaveCorpusGenerator
    {
        private const string CorpusRelativeDirectory = "Tests/EditMode/SaveCorpus";
        private const string Key = "corpus";

        /// <remarks>
        /// See docs/saving.md, "The fixture values are chosen, not incidental".
        /// </remarks>
        private class FixtureV1
        {
            public string Note = "golden corpus fixture, not a real save model";
            public long Coins = 1250;
            public decimal Multiplier = 1.50m;
            public string HighPrecisionTimestamp = "2026-09-01T10:00:00.123456789";
            public string ZonedTimestamp = "2026-09-01T10:00:00+05:00";
            public long LargeId = 9007199254740993;
        }

        /// <remarks>
        /// See docs/saving.md, "The golden corpus".
        /// </remarks>
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

            service.SaveAsync(Key, new FixtureV1(), CancellationToken.None).GetAwaiter().GetResult();
            byte[] bytes = scratch.ReadAsync(Key, CancellationToken.None).GetAwaiter().GetResult();

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            Debug.Log($"Wrote golden corpus file: {path}");
        }
    }
}
