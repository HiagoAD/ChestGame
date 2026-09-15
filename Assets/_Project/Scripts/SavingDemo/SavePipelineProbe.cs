using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// Runs one storage/codec/protection combination end to end, composing the components directly
    /// rather than going through <see cref="SaveServiceFactory"/>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Why the probe builds from SaveComponentFactory rather than SaveServiceFactory".
    /// </remarks>
    public static class SavePipelineProbe
    {
        /// <summary>
        /// Strict UTF-8 decoding used to detect a byte sequence <see cref="Render"/> cannot render as text.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The bytes are always renderable, and that is structural".
        /// </remarks>
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        /// <remarks>
        /// See docs/saving.md, "Why the probe builds from SaveComponentFactory rather than SaveServiceFactory".
        /// </remarks>
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

            byte[] rawBytes = await store.ReadAsync(key, ct);
            (string renderedText, bool isHexDump) = Render(rawBytes);

            return new SaveProbeResult(storage, codec, protection, codecInstance.Id, protector.Id,
                rawBytes, renderedText, isHexDump, writeStopwatch.Elapsed.TotalMilliseconds,
                readStopwatch.Elapsed.TotalMilliseconds, loaded);
        }

        /// <summary>
        /// The plaintext baseline every combination's size is measured against.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The bytes are always renderable, and that is structural".
        /// </remarks>
        public static UniTask<SaveProbeResult> RunBaselineAsync(SaveStorage storage, SaveFactoryInputs inputs,
            string baselineKey, SaveInspectorDocument document, CancellationToken ct) =>
            RunAsync(storage, SaveCodec.Json, SaveProtection.None, inputs, baselineKey, document, ct);

        /// <summary>
        /// Renders <paramref name="bytes"/> as text. <c>isHexDump</c> is true only when
        /// <paramref name="bytes"/> is not valid UTF-8.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The bytes are always renderable, and that is structural".
        /// </remarks>
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
