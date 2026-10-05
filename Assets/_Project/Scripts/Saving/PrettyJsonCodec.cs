using System.Text;
using Newtonsoft.Json;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// <see cref="JsonCodec"/> with indentation: the same data, formatted for a person to read
    /// rather than for size.
    /// </summary>
    public class PrettyJsonCodec : ISaveCodec
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public string Id => "json-pretty";
        public bool IsTextSafe => true;

        public byte[] Encode<T>(T value) => Utf8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented));

        /// <summary>
        /// Lets <see cref="Newtonsoft.Json.JsonException"/> propagate: this type has no key to
        /// report a failure against.
        /// </summary>
        public T Decode<T>(byte[] bytes) => JsonConvert.DeserializeObject<T>(Utf8.GetString(bytes));

        /// <summary>Already JSON text; nothing to undo before handing it to a migration.</summary>
        public string ToJson(byte[] encoded) => Utf8.GetString(encoded);
    }
}
