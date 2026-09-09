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
    // A measurement, not a behaviour test - see PoolBenchmark's own header for why: every duration
    // below is logged, never asserted on, so a slow CI machine cannot make this suite flaky.
    //
    // Every codec/protector pair SaveComponentFactory can build (SavePipelineProbeTests's own
    // combination space), all against SaveStorage.InMemory: the encoded bytes are identical
    // whichever store carries them, and InMemory is the one backend that cannot touch
    // Application.persistentDataPath or a real PlayerPrefs table - this project destroyed a
    // developer's real save once already, and a benchmark that runs on every test pass is not the
    // place to risk it again.
    public class SaveBenchmark
    {
        // Averaged per combination so one write/read pair's own noise does not read as the
        // combination's cost - repeating InMemory's sub-millisecond operations is what makes the
        // average mean something.
        private const int Iterations = 200;
        private const SaveStorage Storage = SaveStorage.InMemory;

        private SaveFactoryInputs _inputs;

        [SetUp]
        public void SetUp() => _inputs = SaveFactoryInputs.Defaults();

        // Two sizes, not one, because a single size answers the wrong question. A save this game
        // actually writes is tens of bytes, and at that size gzip costs more than it saves - its
        // header and trailer, plus the base64 the envelope owes any non-text-safe body, outweigh
        // everything compression can find. Measuring only that would read as "gzip is useless";
        // measuring only a large one would read as "always compress". The crossover is the finding.
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

            // The whole point of the class: a human reads this out of the run log.
            Debug.Log(report.ToString());
        }

        private const int LargeNicknameLength = 4000;

        // Repetitive on purpose and labelled as such in the report: this is compression's best case,
        // not a typical payload, and the number it produces is the ceiling rather than an estimate.
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

                // The deterministic half, and the only thing asserted: the round trip actually
                // carried the value, and produced something to size at all.
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
