namespace Company.ChestGame.Saving
{
    // Turns a value into bytes and back. Knows nothing about keys, envelopes or protection.
    public interface ISaveCodec
    {
        string Id { get; }

        // Answer true only if Encode's output is valid JSON. Valid UTF-8 is not enough: a bare
        // unquoted string survives a text round trip and still corrupts the document it is
        // embedded in.
        bool IsTextSafe { get; }

        byte[] Encode<T>(T value);

        T Decode<T>(byte[] bytes);

        // The same bytes as a JSON document: decompressed or decoded as far as this codec needs,
        // and otherwise untouched. Use it to read a stored document whose shape no longer matches
        // any T. An implementation that is not JSON underneath cannot satisfy this.
        string ToJson(byte[] encoded);
    }
}
