using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // Shared construction for the game's type-keyed catalogs. Authoring lists are hand-maintained,
    // so they arrive with empty slots and, occasionally, repeats. An empty slot is skipped with a
    // warning because the rest of the game is still playable; a repeat is fatal because there is no
    // right answer for which entry wins.
    //
    // Each catalog keeps its own interface and property name; only this policy is shared, with the
    // key selector left to the caller as the one thing that genuinely differs between them.
    public static class CatalogBuilder
    {
        public static IReadOnlyDictionary<Type, TEntry> Build<TEntry>(
            IReadOnlyList<TEntry> entries, Func<TEntry, Type> keyOf, string catalogName)
            where TEntry : UnityEngine.Object
        {
            Dictionary<Type, TEntry> byKey = new();

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
    }
}
