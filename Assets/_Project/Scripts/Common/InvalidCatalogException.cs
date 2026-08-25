using System;

namespace Company.ChestGame.Common
{
    // A catalog asset was found and its contents are wrong, such as the same key listed twice. The
    // key is carried as object because the catalogs index by different things: a container type for
    // the type-keyed lookups, an authored string id for the id-keyed one.
    public class InvalidCatalogException : ChestGameException
    {
        public object OffendingKey { get; }

        // Kept for callers that only ever key by type; null when the catalog keys by something else.
        public Type OffendingType => OffendingKey as Type;

        public InvalidCatalogException(string catalogName, object offendingKey)
            : base(MessageFor(catalogName, offendingKey))
        {
            OffendingKey = offendingKey;
        }

        // Anything that is not a type is quoted, because a blank-looking id would be invisible.
        private static string MessageFor(string catalogName, object offendingKey) =>
            offendingKey is Type type
                ? $"{catalogName} lists {type.Name} more than once"
                : $"{catalogName} lists '{offendingKey}' more than once";
    }
}
