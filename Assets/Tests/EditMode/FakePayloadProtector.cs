using Company.ChestGame.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// A baseline <see cref="IPayloadProtector"/> test double whose <see cref="Id"/> and
    /// <see cref="IsTextSafe"/> a test can set directly.
    /// </summary>
    public class FakePayloadProtector : IPayloadProtector
    {
        public string Id { get; set; } = "fake-protector";
        public bool IsTextSafe { get; set; } = true;

        public byte[] Protect(byte[] plain) => plain;

        public byte[] Unprotect(byte[] stored) => stored;
    }
}
