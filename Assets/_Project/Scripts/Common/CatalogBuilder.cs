using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // Shared construction for the game's catalogs. Authoring lists are hand-maintained, so they
    // arrive with empty slots and, occasionally, repeats. An empty slot is skipped with a warning
    // because the rest of the game is still playable; a repeat is fatal because there is no right
    // answer for which entry wins.
    //
    // Each catalog keeps its own interface and property name; only this policy is shared, with the
    // key selector left to the caller as the one thing that genuinely differs between them. The key
    // type is generic too, so one entry list can be indexed more than once — the minigame catalog
    // builds both a type-keyed and an id-keyed lookup over the same entries.
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

        // The id-keyed variant, which needs one rule the generic build cannot express: an id that
        // was never authored is blank, and blank is not a key. Skipping it with a warning follows
        // the same reasoning as an empty slot — the rest of the game still runs, and the entry is
        // still reachable through its type — and it stops two unauthored entries from colliding as
        // a false duplicate. A duplicate of a real id still throws, for the usual reason.
        //
        // An empty slot is passed over silently here because the type-keyed build over the same
        // entries has already warned about it; warning twice for one authoring mistake is noise.
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
