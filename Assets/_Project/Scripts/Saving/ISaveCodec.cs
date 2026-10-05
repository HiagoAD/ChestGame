namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Turns a value into bytes and back. Knows nothing about keys, envelopes or protection.
    /// </summary>
    public interface ISaveCodec
    {
        string Id { get; }

        /// <summary>
        /// Answer true only if <see cref="Encode{T}"/>'s output is valid JSON. Valid UTF-8 is not
        /// enough: a bare unquoted string survives a text round trip and still corrupts the document
        /// it is embedded in.
        /// </summary>
        bool IsTextSafe { get; }

        byte[] Encode<T>(T value);

        T Decode<T>(byte[] bytes);

        /// <summary>
        /// The same bytes as a JSON document: decompressed or decoded as far as this codec needs,
        /// and otherwise untouched. Use it to read a stored document whose shape no longer matches
        /// any <c>T</c>. An implementation that is not JSON underneath cannot satisfy this.
        /// </summary>
        string ToJson(byte[] encoded);
    }
}
