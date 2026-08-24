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
    // Every handle is kept rather than one per reference, because Addressables ref-counts per load:
    // two loads of one asset are two ref-counts and need two releases, so overwriting an entry
    // would leak whatever it replaced and handing the whole list back on the first release would
    // drop a ref-count that a second live caller is still relying on.
    //
    // One take therefore pairs with one Remember. Which of the handles held for a key comes back is
    // deliberately unspecified: they are ref-count tokens for the same runtime key, so releasing
    // any one of them decrements exactly the one count a single load added, and the seam's Release
    // takes a reference rather than a handle so it could not name a particular one anyway. The
    // order is last-in-first-out, which is what makes a load that has to undo its own bookkeeping —
    // one that was cancelled or failed after taking its ref-count — take back the handle it just
    // added rather than someone else's.
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

        // Hands back one handle for that asset and stops tracking that one. A reference nothing is
        // currently held for answers false rather than failing, which is what lets the teardown
        // paths release unconditionally without every one of them repeating the same guard.
        public bool TryTake(AssetReference reference, out AsyncOperationHandle handle)
        {
            handle = default;

            string key = KeyOf(reference);
            if (key == null || !_handles.TryGetValue(key, out List<AsyncOperationHandle> handles)) return false;

            int last = handles.Count - 1;
            handle = handles[last];
            handles.RemoveAt(last);

            // The key goes with its last handle so an asset loaded and released over and over does
            // not leave an empty list behind for the rest of the session.
            if (handles.Count == 0) _handles.Remove(key);

            return true;
        }

        private static string KeyOf(AssetReference reference) => reference?.RuntimeKey?.ToString();
    }
}
