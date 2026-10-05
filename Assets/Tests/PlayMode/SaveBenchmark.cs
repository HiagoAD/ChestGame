using System;
using System.Collections;
using System.Text;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Saving.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// A measurement, not a behaviour test: every duration below is logged, never asserted on.
    /// Exercises every codec/protector pair <see cref="SaveComponentFactory"/> can build, all against
    /// <see cref="SaveStorage"/>.InMemory.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "SaveBenchmark, and the number the plan got wrong".
    /// See docs/saving.md, "Sealing the boot path a test cannot pass arguments through".
    /// </remarks>
    public class SaveBenchmark
    {
        /// <remarks>
        /// See docs/saving.md, "SaveBenchmark, and the number the plan got wrong".
        /// </remarks>
        private const int Iterations = 200;
        private const SaveStorage Storage = SaveStorage.InMemory;

        private SaveFactoryInputs _inputs;

        [SetUp]
        public void SetUp() => _inputs = SaveFactoryInputs.Defaults();

        /// <remarks>
        /// See docs/saving.md, "SaveBenchmark, and the number the plan got wrong".
        /// </remarks>
        [UnityTest]
        public IEnumerator Measure_EveryCodecAndProtectorPair_AgainstThePlaintextBaseline()
        {
            StringBuilder report = new();
            report.AppendLine($"save benchmark: {Iterations} write/read pairs per combination, Unity {Application.unityVersion}");

            yield return MeasureDocument(
                "small - what this game actually saves",
                new SaveInspectorDocument { Balance = 1250, Nickname = "Benchmark", Level = 5 },
                report);

            yield return MeasureDocument(
                $"large - a {LargeNicknameLength}-character repetitive field, the shape compression is for",
                new SaveInspectorDocument { Balance = 1250, Nickname = LargeNickname(), Level = 5 },
                report);

            Debug.Log(report.ToString());
        }

        private const int LargeNicknameLength = 4000;

        /// <remarks>
        /// See docs/saving.md, "SaveBenchmark, and the number the plan got wrong".
        /// </remarks>
        private static string LargeNickname() =>
            new StringBuilder().Insert(0, "chest-run-segment-", LargeNicknameLength / 18).ToString();

        private IEnumerator MeasureDocument(string label, SaveInspectorDocument document, StringBuilder report)
        {
            SaveProbeResult baseline = SynchronousUniTask.Result(
                SavePipelineProbe.RunBaselineAsync(Storage, _inputs, $"bench-baseline-{label.GetHashCode()}", document, CancellationToken.None));
            Assert.Greater(baseline.ByteCount, 0, "guard: the plaintext baseline produced no bytes to measure against");

            report.AppendLine();
            report.AppendLine($"  {label} - baseline {baseline.ByteCount} bytes");
            report.AppendLine("  codec        protector   bytes   % of baseline   write (ms)   read (ms)");

            foreach (SaveCodec codec in (SaveCodec[])Enum.GetValues(typeof(SaveCodec)))
            foreach (SaveProtection protection in (SaveProtection[])Enum.GetValues(typeof(SaveProtection)))
            {
                yield return MeasureOne(codec, protection, document, baseline, report);
            }
        }

        /// <remarks>
        /// See docs/saving.md, "SaveBenchmark, and the number the plan got wrong".
        /// </remarks>
        private IEnumerator MeasureOne(SaveCodec codec, SaveProtection protection, SaveInspectorDocument document,
            SaveProbeResult baseline, StringBuilder report)
        {
            string key = $"bench-{codec}-{protection}-{document.Nickname.Length}";
            double totalWrite = 0d;
            double totalRead = 0d;
            int byteCount = 0;

            for (int i = 0; i < Iterations; i++)
            {
                SaveProbeResult result = SynchronousUniTask.Result(
                    SavePipelineProbe.RunAsync(Storage, codec, protection, _inputs, key, document, CancellationToken.None));

                Assert.AreEqual(document.Balance, result.Loaded.Balance, $"{codec}/{protection} did not round-trip Balance");
                Assert.Greater(result.ByteCount, 0, $"{codec}/{protection} produced no bytes");

                totalWrite += result.WriteMilliseconds;
                totalRead += result.ReadMilliseconds;
                byteCount = result.ByteCount;
            }

            double percentOfBaseline = 100d * byteCount / baseline.ByteCount;
            report.AppendLine(
                $"  {codec,-12} {protection,-11} {byteCount,6}   {percentOfBaseline,11:F0}%   {totalWrite / Iterations,10:F3}   {totalRead / Iterations,9:F3}");

            yield return null;
        }
    }
}
