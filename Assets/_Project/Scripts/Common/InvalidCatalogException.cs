using System;

namespace Company.ChestGame.Common
{
    // A catalog asset was found but describes something the game cannot use, such as the same type
    // listed twice. Distinct from a missing asset: the file is there, its contents are wrong.
    public class InvalidCatalogException : ChestGameException
    {
        public Type OffendingType { get; }

        public InvalidCatalogException(string catalogName, Type offendingType)
            : base($"{catalogName} lists {offendingType.Name} more than once")
        {
            OffendingType = offendingType;
        }
    }
}
