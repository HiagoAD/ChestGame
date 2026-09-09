using Company.ChestGame.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    // The baseline protector with every field a test can override - in particular IsTextSafe,
    // which the two real implementations (NoProtection and JsonCodec) never set to false between
    // them, so the base64 body branch is otherwise unreachable through anything real.
    public class FakePayloadProtector : IPayloadProtector
    {
        public string Id { get; set; } = "fake-protector";
        public bool IsTextSafe { get; set; } = true;

        public byte[] Protect(byte[] plain) => plain;

        public byte[] Unprotect(byte[] stored) => stored;
    }
}
