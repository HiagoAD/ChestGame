using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Company.ChestGame.Assets
{
    // What the provider is holding on behalf of each authored reference.
    //
    // Keyed on the runtime key, never on the reference: AssetReference overrides neither Equals nor
    // GetHashCode, so a dictionary keyed on it has reference identity and any caller rebuilding a
    // reference from the same GUID would get a silent no-op release and a leaked handle.
    //
    // Every handle is kept, not one per reference, because Addressables ref-counts per load. One
    // TryTake pairs with one Remember, last-in-first-out. See docs/asset-loading.md.
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

        // A reference nothing is currently held for answers false rather than failing, so the
        // teardown paths can release unconditionally.
        public bool TryTake(AssetReference reference, out AsyncOperationHandle handle)
        {
            handle = default;

            string key = KeyOf(reference);
            if (key == null || !_handles.TryGetValue(key, out List<AsyncOperationHandle> handles)) return false;

            int last = handles.Count - 1;
            handle = handles[last];
            handles.RemoveAt(last);

            // The key goes with its last handle, so repeated load-release leaves no empty lists.
            if (handles.Count == 0) _handles.Remove(key);

            return true;
        }

        private static string KeyOf(AssetReference reference) => reference?.RuntimeKey?.ToString();
    }
}
