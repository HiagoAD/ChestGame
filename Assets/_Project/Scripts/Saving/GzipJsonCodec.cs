using System.IO;
using System.IO.Compression;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// <see cref="JsonCodec"/>'s own bytes, gzipped.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The codecs".
    /// </remarks>
    public class GzipJsonCodec : ISaveCodec
    {
        private readonly JsonCodec _json = new();

        public string Id => "json-gzip";
        public bool IsTextSafe => false;

        /// <remarks>
        /// See docs/saving.md, "The codecs".
        /// </remarks>
        public byte[] Encode<T>(T value)
        {
            byte[] json = _json.Encode(value);

            using MemoryStream compressed = new();
            using (GZipStream gzip = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(json, 0, json.Length);
            }

            return compressed.ToArray();
        }

        /// <summary>
        /// Lets <see cref="Newtonsoft.Json.JsonException"/> and <see cref="InvalidDataException"/>
        /// propagate: this type has no key to report a failure against.
        /// </summary>
        public T Decode<T>(byte[] bytes) => _json.Decode<T>(Decompress(bytes));

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
