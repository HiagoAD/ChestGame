using System;
using Company.ChestGame.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// An <see cref="ISaveCodec"/> test double every field of which a test can point somewhere it
    /// controls.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class FakeSaveCodec : ISaveCodec
    {
        public string Id { get; set; } = "fake-codec";
        public bool IsTextSafe { get; set; } = true;

        /// <summary>
        /// Whether <see cref="Encode{T}"/> has been called.
        /// </summary>
        /// <remarks>
        /// Asserting this false after a cancelled call proves the cancellation was honoured before
        /// the value was ever turned into bytes, not merely that the store was never reached.
        /// </remarks>
        public bool EncodeWasCalled { get; private set; }

        /// <summary>
        /// Whether <see cref="Decode{T}"/> has been called.
        /// </summary>
        /// <remarks>
        /// Proving this false is how "returned a fresh <c>T</c>" on a first run is told apart from
        /// "happened to decode into something that looks fresh".
        /// </remarks>
        public bool DecodeWasCalled { get; private set; }

        /// <summary>
        /// Whether <see cref="ToJson"/> has been called.
        /// </summary>
        /// <remarks>
        /// The same proof-of-non-use its migration-only callers need as <see cref="DecodeWasCalled"/>
        /// above.
        /// </remarks>
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
