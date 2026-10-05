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

        /// <summary>
        /// Exactly the bytes the last <c>Decode</c> call was handed, copied on the way in; null
        /// before any call or when it was handed null.
        /// </summary>
        public byte[] LastDecodeInput { get; private set; }

        /// <summary>
        /// Exactly the bytes the last <see cref="ToJson"/> call was handed, copied on the way in;
        /// null before any call or when it was handed null.
        /// </summary>
        public byte[] LastToJsonInput { get; private set; }

        /// <summary>The bytes every <c>Encode</c> call returns, whatever value it is given.</summary>
        public byte[] EncodeResult { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Computes what <c>Decode</c> returns from the bytes it was handed. When null,
        /// <c>Decode</c> returns <c>default</c>.
        /// </summary>
        public Func<byte[], object> DecodeResult { get; set; }

        /// <summary>The string every <see cref="ToJson"/> call returns unless <see cref="ToJsonFromInput"/> is set.</summary>
        public string ToJsonResult { get; set; } = "{}";

        /// <summary>
        /// When set, wins over <see cref="ToJsonResult"/>, so the JSON a migration sees can depend
        /// on the bytes that reached this codec.
        /// </summary>
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
