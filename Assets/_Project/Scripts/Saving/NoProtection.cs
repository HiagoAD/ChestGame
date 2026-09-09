namespace Company.ChestGame.Saving
{
    // The baseline every other protector is measured against: no protection at all.
    public class NoProtection : IPayloadProtector
    {
        public string Id => "none";
        public bool IsTextSafe => true;

        public byte[] Protect(byte[] plain) => plain;

        public byte[] Unprotect(byte[] stored) => stored;
    }
}
