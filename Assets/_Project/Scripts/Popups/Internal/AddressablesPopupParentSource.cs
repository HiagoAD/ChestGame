using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Fetches the shared popup canvas prefab through the asset provider. The only place that knows
    // the key. It loads the prefab and stops there; nothing is instantiated here.
    //
    // The prefab is asked for as a GameObject and the component read off it, rather than asking for
    // PopupParent directly. Whether a loader can hand back a component off a prefab depends on the
    // loader, and this way the answer does not have to be the same in every play mode script.
    public class AddressablesPopupParentSource : IPopupParentSource
    {
        private const string PARENT_KEY = "Popups/PopupParent";

        private readonly IAssetProvider _assets;

        public AddressablesPopupParentSource(IAssetProvider assets) => _assets = assets;

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
