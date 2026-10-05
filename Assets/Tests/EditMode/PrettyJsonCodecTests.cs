using System;
using System.IO;
using System.Linq;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests <see cref="PrettyJsonCodec"/>: <see cref="JsonCodec"/>'s own serialization with
    /// <c>Formatting.Indented</c> instead of <c>Formatting.None</c>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The codecs".
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
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

        [Test]
        public void SaveAsync_ThroughFileStore_WritesTheBodyGenuinelyIndented_AsFirstWrittenToDisk()
        {
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
