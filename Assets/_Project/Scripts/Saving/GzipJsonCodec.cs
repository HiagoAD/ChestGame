using System.IO;
using System.IO.Compression;

namespace Company.ChestGame.Saving
{
    // JsonCodec's own bytes, gzipped. IsTextSafe is false even though the wrapped codec is
    // text-safe: gzip's magic bytes are not JSON, and embedding them raw instead of base64 would
    // corrupt the envelope.
    public class GzipJsonCodec : ISaveCodec
    {
        private readonly JsonCodec _json = new();

        public string Id => "json-gzip";
        public bool IsTextSafe => false;

        public byte[] Encode<T>(T value)
        {
            byte[] json = _json.Encode(value);

            using MemoryStream compressed = new();
            // leaveOpen: the GZipStream's own Dispose flushes the compressed trailer before
            // compressed.ToArray() runs; disposing compressed too would just be redundant.
            using (GZipStream gzip = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(json, 0, json.Length);
            }

            return compressed.ToArray();
        }

        // Lets JsonException and InvalidDataException propagate: this type has no key to report a
        // failure against.
        public T Decode<T>(byte[] bytes) => _json.Decode<T>(Decompress(bytes));

        // Decompresses first, then defers to JsonCodec.ToJson.
        public string ToJson(byte[] encoded) => _json.ToJson(Decompress(encoded));

        private static byte[] Decompress(byte[] bytes)
        {
            using MemoryStream compressed = new(bytes);
            using GZipStream gzip = new(compressed, CompressionMode.Decompress);
            using MemoryStream json = new();
            gzip.CopyTo(json);

            return json.ToArray();
        }
    }
}
