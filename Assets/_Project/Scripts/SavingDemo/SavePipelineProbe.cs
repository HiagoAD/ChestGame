using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving.Demo
{
    // Runs one storage/codec/protection combination end to end, composing the components directly
    // rather than going through SaveServiceFactory. See docs/saving.md, "Why the probe builds from
    // SaveComponentFactory rather than SaveServiceFactory".
    public static class SavePipelineProbe
    {
        // throwOnInvalidBytes: true - the default UTF8Encoding silently replaces bad sequences with
        // U+FFFD instead of reporting them, which would hide exactly the case Render exists to catch.
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public static async UniTask<SaveProbeResult> RunAsync(SaveStorage storage, SaveCodec codec, SaveProtection protection,
            SaveFactoryInputs inputs, string key, SaveInspectorDocument document, CancellationToken ct)
        {
            ISaveStore store = SaveComponentFactory.CreateStore(storage, inputs);
            ISaveCodec codecInstance = SaveComponentFactory.CreateCodec(codec);
            IPayloadProtector protector = SaveComponentFactory.CreateProtector(protection, inputs);
            SaveService service = new(codecInstance, protector, store);

            Stopwatch writeStopwatch = Stopwatch.StartNew();
            await service.SaveAsync(key, document, ct);
            writeStopwatch.Stop();

            Stopwatch readStopwatch = Stopwatch.StartNew();
            SaveInspectorDocument loaded = await service.LoadAsync<SaveInspectorDocument>(key, ct);
            readStopwatch.Stop();

            // Straight back through the store, not a re-encode: this is what actually landed.
            byte[] rawBytes = await store.ReadAsync(key, ct);
            (string renderedText, bool isHexDump) = Render(rawBytes);

            return new SaveProbeResult(storage, codec, protection, codecInstance.Id, protector.Id,
                rawBytes, renderedText, isHexDump, writeStopwatch.Elapsed.TotalMilliseconds,
                readStopwatch.Elapsed.TotalMilliseconds, loaded);
        }

        // The plaintext baseline every combination's size is measured against. Runs the probe
        // again under a different key rather than hard-coding a number, so a change to
        // SaveInspectorDocument or SaveEnvelope's shape is reflected here too.
        public static UniTask<SaveProbeResult> RunBaselineAsync(SaveStorage storage, SaveFactoryInputs inputs,
            string baselineKey, SaveInspectorDocument document, CancellationToken ct) =>
            RunAsync(storage, SaveCodec.Json, SaveProtection.None, inputs, baselineKey, document, ct);

        // isHexDump is true only when bytes is not valid UTF-8. Nothing this factory can build
        // today produces that - every combination stores valid UTF-8 end to end - so the hex path
        // exists for a byte sequence nothing ships today.
        public static (string text, bool isHexDump) Render(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return (string.Empty, false);

            try
            {
                return (StrictUtf8.GetString(bytes), false);
            }
            catch (DecoderFallbackException)
            {
                return (ToHexDump(bytes), true);
            }
        }

        private static string ToHexDump(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ');
    }
}
