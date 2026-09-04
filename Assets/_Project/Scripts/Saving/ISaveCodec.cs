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

        // The codec's own bytes as a JSON document - decompressed first if this codec compresses,
        // but otherwise untouched. This is what makes migration possible: a migration rewrites a
        // document into a shape today's T no longer matches, which is the one thing Decode<T> is
        // built never to do. Every codec this assembly ships is JSON underneath - compact, indented,
        // or gzipped - so this method reaches that JSON directly instead of every migration needing
        // to know how each codec gets there. That is a real constraint this method hard-codes rather
        // than hides: a codec that were not JSON-shaped under here could not participate in
        // migration at all.
        string ToJson(byte[] encoded);
    }
}
