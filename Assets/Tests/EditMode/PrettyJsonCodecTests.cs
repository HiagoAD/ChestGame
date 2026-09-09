using System;
using System.IO;
using System.Linq;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // PrettyJsonCodec: JsonCodec's own serialization with Formatting.Indented instead of
    // Formatting.None. See docs/saving.md, "The codecs". The value-survives-but-whitespace-
    // normalises-on-read claim for this codec is covered by SaveCodecEnvelopeValueExactnessTests;
    // what stays here is the other half of that same doc section - that the file on disk, before
    // anything ever re-Parses it, still carries the codec's own indentation verbatim.
    public class PrettyJsonCodecTests
    {
        private class TestState { public int Value; }

        [Test]
        public void Encode_ThenDecode_RoundTrips()
        {
            PrettyJsonCodec codec = new();
            TestState state = new() { Value = 42 };

            byte[] encoded = codec.Encode(state);
            TestState decoded = codec.Decode<TestState>(encoded);

            Assert.AreEqual(42, decoded.Value);
        }

        [Test]
        public void Encode_ProducesLargerOutputThanPlainJsonCodec_BecauseItIsIndented()
        {
            PrettyJsonCodec pretty = new();
            JsonCodec plain = new();
            TestState state = new() { Value = 42 };

            byte[] prettyBytes = pretty.Encode(state);
            byte[] plainBytes = plain.Encode(state);

            Assert.Greater(prettyBytes.Length, plainBytes.Length,
                "Formatting.Indented spends bytes on whitespace the plain codec never writes");
        }

        [Test]
        public void IsTextSafe_IsTrue_TheSameAsPlainJson()
        {
            Assert.IsTrue(new PrettyJsonCodec().IsTextSafe,
                "the output is still JSON, just indented, so it still embeds raw in the envelope");
        }

        [Test]
        public void Id_IsDistinctFromPlainJson()
        {
            Assert.AreNotEqual(new JsonCodec().Id, new PrettyJsonCodec().Id);
        }

        // --- Coverage gap: the file on disk is genuinely indented, not just the codec's own output ---

        [Test]
        public void SaveAsync_ThroughFileStore_WritesTheBodyGenuinelyIndented_AsFirstWrittenToDisk()
        {
            // SaveEnvelope.Wrap builds the body JRaw straight from the codec's own output string,
            // and Serialize writes that JRaw verbatim through WriteRawValue - only a later Parse
            // normalises the whitespace away, and SaveService never re-wraps and re-writes what it
            // loaded. So the bytes this test reads straight back off disk, without ever routing
            // through Parse, have to still be PrettyJsonCodec's own indented text - the whole reason
            // this codec exists (docs/saving.md, "Value-exactness, and where the formatting stops").
            string root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            try
            {
                FileStore store = new(root);
                SaveService service = new(new PrettyJsonCodec(), new NoProtection(), store);

                SynchronousUniTask.Complete(service.SaveAsync("save", new TestState { Value = 42 }, CancellationToken.None));

                string savedFile = Directory.GetFiles(root).Single(f => !f.EndsWith(".bak") && !f.EndsWith(".tmp"));
                string fileText = File.ReadAllText(savedFile).Replace("\r\n", "\n");

                StringAssert.Contains("\n  \"Value\": 42", fileText,
                    "the body on disk has to still carry PrettyJsonCodec's own indentation, not the compact form only Parse would normalise it to");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
