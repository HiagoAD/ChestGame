using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    /// <summary>
    /// Fetches the shared popup canvas prefab through the asset provider.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Popups".
    /// </remarks>
    public class AddressablesPopupParentSource : IPopupParentSource
    {
        private const string PARENT_KEY = "Popups/PopupParent";

        private readonly IAssetProvider _assets;

        public AddressablesPopupParentSource(IAssetProvider assets) => _assets = assets;

        /// <exception cref="AssetLoadException">The key resolved but the load itself failed.</exception>
        /// <exception cref="MissingAssetException">
        /// The key is not in the shipped catalog, or the loaded prefab carries no
        /// <see cref="PopupParent"/>.
        /// </exception>
        public async UniTask<PopupParent> ReadAsync(CancellationToken ct)
        {
            GameObject prefab = await _assets.LoadAsync<GameObject>(PARENT_KEY, ct);

            PopupParent parent = prefab == null ? null : prefab.GetComponent<PopupParent>();
            if (parent == null)
            {
                throw new MissingAssetException(PARENT_KEY, "Popup parent prefab");
            }

            return parent;
        }
    }
}
