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

        public byte[] EncodeResult { get; set; } = Array.Empty<byte>();
        public Func<byte[], object> DecodeResult { get; set; }

        public byte[] Encode<T>(T value)
        {
            EncodeWasCalled = true;
            return EncodeResult;
        }

        public T Decode<T>(byte[] bytes)
        {
            DecodeWasCalled = true;
            return DecodeResult != null ? (T)DecodeResult(bytes) : default;
        }
    }
}
