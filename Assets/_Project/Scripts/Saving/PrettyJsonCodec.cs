using System.Text;
using Newtonsoft.Json;

namespace Company.ChestGame.Saving
{
    // JsonCodec with indentation. Same data, formatted for a developer reading the file rather than
    // for size — development and the phase 8 demo.
    public class PrettyJsonCodec : ISaveCodec
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public string Id => "json-pretty";
        public bool IsTextSafe => true;

        public byte[] Encode<T>(T value) => Utf8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented));

        // Lets JsonException propagate: this type has no key to report a failure against.
        public T Decode<T>(byte[] bytes) => JsonConvert.DeserializeObject<T>(Utf8.GetString(bytes));
    }
}
