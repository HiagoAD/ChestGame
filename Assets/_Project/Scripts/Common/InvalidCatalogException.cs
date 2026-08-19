using System;

namespace Company.ChestGame.Common
{
    // A catalog asset was found but describes something the game cannot use, such as the same key
    // listed twice. Distinct from a missing asset: the file is there, its contents are wrong.
    //
    // The key is whatever the catalog indexes by — a container type for the type-keyed lookups, an
    // authored string id for the id-keyed one — so the offending key is carried as object and the
    // message is phrased per key kind.
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

        // Type keys keep the wording they have always had. Anything else reads better quoted,
        // because a blank-looking id is otherwise invisible in the message.
        private static string MessageFor(string catalogName, object offendingKey) =>
            offendingKey is Type type
                ? $"{catalogName} lists {type.Name} more than once"
                : $"{catalogName} lists '{offendingKey}' more than once";
    }
}
