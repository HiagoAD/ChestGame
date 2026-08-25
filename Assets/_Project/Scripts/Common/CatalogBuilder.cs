using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // Shared policy for the game's catalogs: an empty slot is skipped with a warning because the
    // rest of the game is still playable, a repeat is fatal because there is no right answer for
    // which entry wins. The TEntry constraint makes the null check use Unity's overloaded equality,
    // which also catches destroyed objects.
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

        // One rule the generic build cannot express: an id that was never authored is blank, and
        // blank is not a key, so two unauthored entries would otherwise collide as a false
        // duplicate. An empty slot passes silently because the type-keyed build already warned.
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
