using System.Text;
using Newtonsoft.Json;

namespace Company.ChestGame.Saving
{
    public class JsonCodec : ISaveCodec
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public string Id => "json";
        public bool IsTextSafe => true;

        public byte[] Encode<T>(T value) => Utf8.GetBytes(JsonConvert.SerializeObject(value, Formatting.None));

        // Lets JsonException propagate: this type has no key to report a failure against.
        public T Decode<T>(byte[] bytes) => JsonConvert.DeserializeObject<T>(Utf8.GetString(bytes));
    }
}
