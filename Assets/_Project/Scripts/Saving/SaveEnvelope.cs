using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // The always-plaintext header in front of a body that might not be. GetBody(Wrap(x)) reproduces
    // every value in x exactly, though not the whitespace inside a text-safe body; see
    // docs/saving.md, "Value-exactness, and where the formatting stops".
    public class SaveEnvelope
    {
        public const string RawEncoding = "raw";
        public const string Base64Encoding = "b64";

        private static readonly UTF8Encoding Utf8 = new(false);

        [JsonProperty("v")] public int? Version { get; }
        [JsonProperty("codec")] public string CodecId { get; }
        [JsonProperty("prot")] public string ProtectorId { get; }
        [JsonProperty("enc")] public string BodyEncoding { get; }
        [JsonProperty("body")] public JToken Body { get; }

        // Nullable: a file with no "v" has to read as absent, not as version 0.
        public SaveEnvelope(int? version, string codecId, string protectorId, string bodyEncoding, JToken body)
        {
            Version = version;
            CodecId = codecId;
            ProtectorId = protectorId;
            BodyEncoding = bodyEncoding;
            Body = body;
        }

        public static SaveEnvelope Wrap(int version, string codecId, string protectorId, bool textSafe, byte[] payload)
        {
            JToken body = textSafe
                ? new JRaw(Utf8.GetString(payload))
                : new JValue(Convert.ToBase64String(payload));

            return new SaveEnvelope(version, codecId, protectorId, textSafe ? RawEncoding : Base64Encoding, body);
        }

        public string Serialize() => JsonConvert.SerializeObject(this, Formatting.Indented);

        // Hand-rolled rather than JsonConvert.DeserializeObject<SaveEnvelope>, which would rebuild
        // Body from Newtonsoft's object model and lose the text it came from.
        public static SaveEnvelope Parse(string json)
        {
            using StringReader stringReader = new(json);
            using JsonTextReader reader = new(stringReader)
            {
                // Without both, a date-shaped string or a trailing zero is reformatted on the way
                // past, which is the round trip this type exists to avoid.
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal
            };

            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
            {
                throw new JsonReaderException("A save envelope has to be a JSON object");
            }

            int? version = null;
            string codecId = null;
            string protectorId = null;
            string bodyEncoding = null;
            JToken body = null;

            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName)
                {
                    throw new JsonReaderException("Expected a property name in a save envelope");
                }

                string name = (string)reader.Value;
                reader.Read();

                switch (name)
                {
                    case "v":
                        // Guarded like the body case below: Convert.ToInt32(null) is 0, which would
                        // read an explicit "v": null as a real version rather than as absent.
                        version = reader.TokenType == JsonToken.Null ? null : Convert.ToInt32(reader.Value);
                        break;
                    case "codec":
                        codecId = (string)reader.Value;
                        break;
                    case "prot":
                        protectorId = (string)reader.Value;
                        break;
                    case "enc":
                        bodyEncoding = (string)reader.Value;
                        break;
                    case "body":
                        body = reader.TokenType == JsonToken.Null ? null : JRaw.Create(reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            // After the loop, not in the switch: field order is not guaranteed, so enc may arrive
            // after body. JRaw keeps the quotes a base64 body was written with.
            if (bodyEncoding == Base64Encoding && body is JRaw) body = JToken.Parse(body.ToString());

            return new SaveEnvelope(version, codecId, protectorId, bodyEncoding, body);
        }

        public byte[] GetBody()
        {
            if (Body == null) return null;

            return BodyEncoding switch
            {
                RawEncoding => Utf8.GetBytes(Body.ToString(Formatting.None)),
                Base64Encoding => Convert.FromBase64String(Body.ToString()),
                _ => throw new FormatException($"Unknown envelope encoding '{BodyEncoding}'")
            };
        }
    }
}
