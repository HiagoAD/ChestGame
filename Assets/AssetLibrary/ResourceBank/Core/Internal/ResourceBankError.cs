namespace TapNation.Modules.ResourceBank
{
    public sealed class ResourceBankError
    {
        public static readonly ResourceBankError None = new(true, nameof(None));
        public static readonly ResourceBankError NegativeAmount = new(false, nameof(NegativeAmount));
        public static readonly ResourceBankError ZeroAmount = new(false, nameof(ZeroAmount));
        public static readonly ResourceBankError InsufficientAmount = new(false, nameof(InsufficientAmount));

        private readonly bool _value;
        private readonly string _name;

        private ResourceBankError(bool value, string name)
        {
            _value = value;
            _name = name;
        }

        public static implicit operator bool(ResourceBankError status)
        {
            return status?._value ?? false;
        }

        public static bool operator ==(ResourceBankError errorA, ResourceBankError errorB)
        {
            if (ReferenceEquals(errorA, errorB)) return true;
            if (errorA is null || errorB is null) return false;
            return errorA._name == errorB._name;
        }

        public static bool operator !=(ResourceBankError errorA, ResourceBankError errorB)
        {
            return !(errorA == errorB);
        }


        public override string ToString() => _name;

        public override bool Equals(object obj)
        {
            if (obj is ResourceBankError other)
                return this == other;
            return false;
        }


        public override int GetHashCode()
        {
            return _name.GetHashCode();
        }
    }
}