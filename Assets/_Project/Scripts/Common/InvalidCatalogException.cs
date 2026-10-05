using System;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// A catalog asset was found and its contents are wrong, such as the same key listed twice.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class InvalidCatalogException : ChestGameException
    {
        public object OffendingKey { get; }

        /// <summary>
        /// Kept for callers that only ever key by type; null when the catalog keys by something else.
        /// </summary>
        public Type OffendingType => OffendingKey as Type;

        public InvalidCatalogException(string catalogName, object offendingKey)
            : base(MessageFor(catalogName, offendingKey))
        {
            OffendingKey = offendingKey;
        }

        private static string MessageFor(string catalogName, object offendingKey) =>
            offendingKey is Type type
                ? $"{catalogName} lists {type.Name} more than once"
                : $"{catalogName} lists '{offendingKey}' more than once";
    }
}
