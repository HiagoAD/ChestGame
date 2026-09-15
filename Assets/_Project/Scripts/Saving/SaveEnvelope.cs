using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// The always-plaintext header in front of a body that might not be.
    /// </summary>
    /// <remarks>
    /// <see cref="GetBody"/> applied to the result of <see cref="Wrap"/> reproduces every value
    /// exactly, though not the whitespace inside a text-safe body.
    /// See docs/saving.md, "Value-exactness, and where the formatting stops".
    /// </remarks>
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

        /// <param name="version">
        /// Nullable: a file with no "v" has to read as absent, not as version 0.
        /// </param>
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

        /// <remarks>
        /// See docs/saving.md, "Value-exactness, and where the formatting stops".
        /// See docs/saving.md, "Loading, and the three ways a version goes wrong".
        /// </remarks>
        public static SaveEnvelope Parse(string json)
        {
            using StringReader stringReader = new(json);
            using JsonTextReader reader = new(stringReader)
            {
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
