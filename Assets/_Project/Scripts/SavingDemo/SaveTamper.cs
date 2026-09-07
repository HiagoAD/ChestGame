using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving.Demo
{
    // Edits a save SavePipelineProbe.RunAsync already wrote, in place, then reloads it through the
    // same combination to see whether the edit is accepted or caught. Which edit applies is the
    // whole demonstration: None, Base64 and Xor are decoded, edited and re-encoded exactly as a
    // curious player with a decoder would; Hmac and Aes cannot be reached that way, so a byte in the
    // protected body is flipped instead. See docs/saving.md, "The protectors, and what a key
    // shipping inside the binary buys".
    public static class SaveTamper
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public static async UniTask<SaveTamperResult> RunAsync(SaveStorage storage, SaveCodec codec, SaveProtection protection,
            SaveFactoryInputs inputs, string key, long tamperedBalance, CancellationToken ct)
        {
            ISaveStore store = SaveComponentFactory.CreateStore(storage, inputs);
            ISaveCodec codecInstance = SaveComponentFactory.CreateCodec(codec);
            IPayloadProtector protector = SaveComponentFactory.CreateProtector(protection, inputs);

            byte[] storedBytes = await store.ReadAsync(key, ct);
            if (storedBytes == null) throw SaveInspectorException.NothingToTamper(key);

            SaveEnvelope envelope = SaveEnvelope.Parse(Utf8.GetString(storedBytes));

            // Hmac and Aes: flip a byte of the protected body itself - the demo has no key-free way
            // to reach the value underneath. Everything else: decode, edit the balance, re-encode -
            // exactly what a curious player with a decoder does.
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
