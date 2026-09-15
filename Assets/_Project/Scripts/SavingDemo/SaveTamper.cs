using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// Edits a save <see cref="SavePipelineProbe.RunAsync"/> already wrote, in place, then reloads it
    /// through the same combination to see whether the edit is accepted or caught.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The tamper button, and why it edits two different ways".
    /// </remarks>
    public static class SaveTamper
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        /// <remarks>
        /// See docs/saving.md, "The tamper button, and why it edits two different ways".
        /// </remarks>
        public static async UniTask<SaveTamperResult> RunAsync(SaveStorage storage, SaveCodec codec, SaveProtection protection,
            SaveFactoryInputs inputs, string key, long tamperedBalance, CancellationToken ct)
        {
            ISaveStore store = SaveComponentFactory.CreateStore(storage, inputs);
            ISaveCodec codecInstance = SaveComponentFactory.CreateCodec(codec);
            IPayloadProtector protector = SaveComponentFactory.CreateProtector(protection, inputs);

            byte[] storedBytes = await store.ReadAsync(key, ct);
            if (storedBytes == null) throw SaveInspectorException.NothingToTamper(key);

            SaveEnvelope envelope = SaveEnvelope.Parse(Utf8.GetString(storedBytes));

            byte[] tamperedBody = protection switch
            {
                SaveProtection.Hmac => FlipLastByte(envelope.GetBody()),
                SaveProtection.Aes => FlipLastByte(envelope.GetBody()),
                _ => ReEncodeWithBalance(envelope.GetBody(), codecInstance, protector, tamperedBalance)
            };

            bool textSafe = codecInstance.IsTextSafe && protector.IsTextSafe;
            SaveEnvelope tamperedEnvelope = SaveEnvelope.Wrap(
                envelope.Version ?? SaveService.CurrentSchemaVersion, envelope.CodecId, envelope.ProtectorId, textSafe, tamperedBody);
            await store.WriteAsync(key, Utf8.GetBytes(tamperedEnvelope.Serialize()), ct);

            SaveService service = new(codecInstance, protector, store);
            try
            {
                SaveInspectorDocument loaded = await service.LoadAsync<SaveInspectorDocument>(key, ct);
                return new SaveTamperResult(SaveTamperOutcome.Loaded, loaded.Balance, null);
            }
            catch (SaveException error)
            {
                SaveTamperOutcome outcome = error is SaveTamperedException
                    ? SaveTamperOutcome.RejectedAsTampered
                    : SaveTamperOutcome.RejectedAsUnreadable;
                return new SaveTamperResult(outcome, null, error);
            }
        }

        private static byte[] ReEncodeWithBalance(byte[] protectedBody, ISaveCodec codec, IPayloadProtector protector, long balance)
        {
            SaveInspectorDocument document = codec.Decode<SaveInspectorDocument>(protector.Unprotect(protectedBody));
            document.Balance = balance;
            return protector.Protect(codec.Encode(document));
        }

        private static byte[] FlipLastByte(byte[] bytes)
        {
            byte[] copy = (byte[])bytes.Clone();
            copy[^1] ^= 0xFF;
            return copy;
        }
    }
}
