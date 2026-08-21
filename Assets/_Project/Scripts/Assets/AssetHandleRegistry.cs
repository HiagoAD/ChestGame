using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Company.ChestGame.Assets
{
    // What the provider is holding on behalf of each authored reference, kept apart from the
    // provider itself because it is the half that has a rule of its own rather than a translation.
    //
    // The rule is the key. AssetReference overrides neither Equals nor GetHashCode, so a dictionary
    // keyed on the reference has reference identity: it works only for as long as every caller
    // hands back the very same serialized instance it loaded with, and any caller that rebuilds a
    // reference from the same GUID gets a silent no-op release and a leaked handle. Keying on the
    // runtime key instead gives the lookup the value semantics the type does not have. It is the
    // reference's GUID, plus the sub-object name when there is one, so two references naming the
    // same thing are one entry and a reference naming a sub-asset is not confused with its parent.
    //
    // Every handle is kept rather than one per reference, because Addressables ref-counts per load
    // and overwriting an entry would leak whatever it replaced.
    public class AssetHandleRegistry
    {
        private readonly Dictionary<string, List<AsyncOperationHandle>> _handles = new();

        public void Remember(AssetReference reference, AsyncOperationHandle handle)
        {
            string key = KeyOf(reference);
            if (key == null) return;

            if (!_handles.TryGetValue(key, out List<AsyncOperationHandle> handles))
            {
                handles = new List<AsyncOperationHandle>();
                _handles[key] = handles;
            }

            handles.Add(handle);
        }

        // Hands back everything held for that asset and stops tracking it. A reference nothing was
        // ever loaded for yields nothing rather than failing, which is what lets the teardown paths
        // release unconditionally without every one of them repeating the same guard.
        public IReadOnlyList<AsyncOperationHandle> Take(AssetReference reference)
        {
            string key = KeyOf(reference);
            if (key == null || !_handles.TryGetValue(key, out List<AsyncOperationHandle> handles))
            {
                return Array.Empty<AsyncOperationHandle>();
            }

            _handles.Remove(key);
            return handles;
        }

        private static string KeyOf(AssetReference reference) => reference?.RuntimeKey?.ToString();
    }
}
