using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// Shared policy for building the game's catalogs from a list of authored entries.
    /// </summary>
    /// <remarks>
    /// An empty slot is skipped with a warning. A duplicate key throws
    /// <see cref="InvalidCatalogException"/>. The <c>TEntry</c> constraint makes the null check use
    /// Unity's overloaded equality, which also catches destroyed objects.
    /// See docs/architecture.md, "Catalogs".
    /// </remarks>
    public static class CatalogBuilder
    {
        public static IReadOnlyDictionary<TKey, TEntry> Build<TKey, TEntry>(
            IReadOnlyList<TEntry> entries, Func<TEntry, TKey> keyOf, string catalogName)
            where TEntry : UnityEngine.Object
        {
            Dictionary<TKey, TEntry> byKey = new();

            for (int i = 0; i < entries.Count; i++)
            {
                TEntry entry = entries[i];
                if (entry == null)
                {
                    Debug.LogWarning($"{catalogName} has an empty entry at index {i}, skipping it");
                    continue;
                }

                if (!byKey.TryAdd(keyOf(entry), entry))
                {
                    throw new InvalidCatalogException(catalogName, keyOf(entry));
                }
            }

            return byKey;
        }

        /// <summary>
        /// Builds an id-keyed lookup from <paramref name="entries"/>. An entry with no authored id is
        /// skipped from the lookup with a warning; a null entry is skipped silently.
        /// </summary>
        /// <exception cref="InvalidCatalogException">Two entries produce the same id.</exception>
        /// <remarks>
        /// See docs/architecture.md, "Catalogs".
        /// </remarks>
        public static IReadOnlyDictionary<string, TEntry> BuildById<TEntry>(
            IReadOnlyList<TEntry> entries, Func<TEntry, string> idOf, string catalogName)
            where TEntry : UnityEngine.Object
        {
            Dictionary<string, TEntry> byId = new();

            for (int i = 0; i < entries.Count; i++)
            {
                TEntry entry = entries[i];
                if (entry == null) continue;

                string id = idOf(entry);
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning($"{catalogName} has an entry with no id at index {i}, skipping it from the id lookup");
                    continue;
                }

                if (!byId.TryAdd(id, entry))
                {
                    throw new InvalidCatalogException(catalogName, id);
                }
            }

            return byId;
        }
    }
}
