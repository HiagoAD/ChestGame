using System;
using Company.ChestGame.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    // A codec every field of which a test can point somewhere it controls, so SaveService's own
    // logic - the version and component checks, and exactly when SaveAsync touches the codec - can
    // be proven without JsonCodec's real serialization standing in the way.
    public class FakeSaveCodec : ISaveCodec
    {
        public string Id { get; set; } = "fake-codec";
        public bool IsTextSafe { get; set; } = true;

        // Whether Encode<T> has run at all, which is what proves a cancellation was honoured before
        // the value was ever turned into bytes rather than after.
        public bool EncodeWasCalled { get; private set; }

        // A first run has nothing to decode: proving Decode never ran is how "returned a fresh T"
        // is told apart from "happened to decode into something that looks fresh".
        public bool DecodeWasCalled { get; private set; }

        // Phase 4: whether ToJson has run, the same proof-of-non-use ToJson's migration-only callers
        // need as Decode's own flag above.
        public bool ToJsonWasCalled { get; private set; }

        // Exactly what each call was handed, copied on the way in. A canned DecodeResult or
        // ToJsonResult answers the same whatever arrives, so without these a round trip through
        // this fake proves nothing about which bytes SaveService actually passed along.
        public byte[] LastDecodeInput { get; private set; }
        public byte[] LastToJsonInput { get; private set; }

        public byte[] EncodeResult { get; set; } = Array.Empty<byte>();
        public Func<byte[], object> DecodeResult { get; set; }
        public string ToJsonResult { get; set; } = "{}";

        // When set, wins over ToJsonResult, so a test can make the JSON a migration sees depend on
        // the bytes that reached this codec rather than on a fixed string.
        public Func<byte[], string> ToJsonFromInput { get; set; }

        public byte[] Encode<T>(T value)
        {
            EncodeWasCalled = true;
            return EncodeResult;
        }

        public T Decode<T>(byte[] bytes)
        {
            DecodeWasCalled = true;
            LastDecodeInput = (byte[])bytes?.Clone();
            return DecodeResult != null ? (T)DecodeResult(bytes) : default;
        }

        public string ToJson(byte[] encoded)
        {
            ToJsonWasCalled = true;
            LastToJsonInput = (byte[])encoded?.Clone();
            return ToJsonFromInput != null ? ToJsonFromInput(encoded) : ToJsonResult;
        }
    }
}
