namespace Company.ChestGame.Saving
{
    // Turns a value into bytes and back. Knows nothing about keys, envelopes or protection.
    public interface ISaveCodec
    {
        string Id { get; }

        // Valid JSON, not merely valid UTF-8 text: SaveEnvelope embeds a text-safe body raw, so a
        // codec emitting a bare unquoted string would corrupt the envelope while still round
        // tripping through UTF8Encoding.
        bool IsTextSafe { get; }

        byte[] Encode<T>(T value);

        T Decode<T>(byte[] bytes);
    }
}
