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

        /// <summary>
        /// The smallest valid gzip member: a 10-byte header, an empty deflate stream, and an 8-byte
        /// trailer holding the CRC32 and length of what was decompressed.
        /// </summary>
        private const int MinimumMemberLength = 20;
        private const int TrailerLength = 8;

        /// <summary>
        /// Decompresses <paramref name="bytes"/> and verifies the gzip trailer against the result.
        /// </summary>
        /// <exception cref="InvalidDataException">When the input is too short to be a complete gzip
        /// member, or its trailer does not match the data it holds.</exception>
        /// <remarks>
        /// Reads the trailer from the end of the input, so it expects exactly one member, as
        /// <see cref="Encode"/> writes.
        /// See docs/saving.md, "The codecs".
        /// </remarks>
        private static byte[] Decompress(byte[] bytes)
        {
            if (bytes == null || bytes.Length < MinimumMemberLength)
            {
                throw new InvalidDataException("The data is too short to be a complete gzip stream.");
            }

            using MemoryStream compressed = new(bytes);
            using GZipStream gzip = new(compressed, CompressionMode.Decompress);
            using MemoryStream json = new();
            gzip.CopyTo(json);

            byte[] decompressed = json.ToArray();

            uint expectedCrc = ReadUInt32LittleEndian(bytes, bytes.Length - TrailerLength);
            uint expectedLength = ReadUInt32LittleEndian(bytes, bytes.Length - TrailerLength + 4);
            if (Crc32(decompressed) != expectedCrc || unchecked((uint)decompressed.Length) != expectedLength)
            {
                throw new InvalidDataException("The gzip stream is truncated or corrupt: its trailer does not match the data it holds.");
            }

            return decompressed;
        }

        private static uint ReadUInt32LittleEndian(byte[] bytes, int offset) =>
            bytes[offset] | ((uint)bytes[offset + 1] << 8) | ((uint)bytes[offset + 2] << 16) | ((uint)bytes[offset + 3] << 24);

        /// <summary>
        /// CRC32 lookup table: IEEE polynomial, reflected, which is what gzip's trailer uses.
        /// </summary>
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint entry = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    entry = (entry & 1) != 0 ? (entry >> 1) ^ 0xEDB88320u : entry >> 1;
                }

                table[i] = entry;
            }

            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }
    }
}
