using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Company.ChestGame.Assets
{
    /// <summary>
    /// What the provider is holding on behalf of each authored reference.
    /// </summary>
    /// <remarks>
    /// Keyed on the runtime key, never on the reference. Every handle is kept rather than one per
    /// reference, and one <see cref="TryTake"/> pairs with one <see cref="Remember"/>,
    /// last-in-first-out.
    /// See docs/asset-loading.md, "AssetHandleRegistry".
    /// </remarks>
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

        /// <summary>
        /// Takes one handle remembered for <paramref name="reference"/>, if any.
        /// </summary>
        /// <returns>
        /// False when nothing is currently held for the reference, rather than failing, so
        /// teardown paths can call this unconditionally.
        /// </returns>
        /// <remarks>
        /// See docs/asset-loading.md, "AssetHandleRegistry".
        /// </remarks>
        public bool TryTake(AssetReference reference, out AsyncOperationHandle handle)
        {
            handle = default;

            string key = KeyOf(reference);
            if (key == null || !_handles.TryGetValue(key, out List<AsyncOperationHandle> handles)) return false;

            int last = handles.Count - 1;
            handle = handles[last];
            handles.RemoveAt(last);

            if (handles.Count == 0) _handles.Remove(key);

            return true;
        }

        private static string KeyOf(AssetReference reference) => reference?.RuntimeKey?.ToString();
    }
}
