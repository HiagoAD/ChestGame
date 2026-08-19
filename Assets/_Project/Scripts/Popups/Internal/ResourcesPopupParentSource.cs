using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Loads the shared popup canvas prefab from a Resources folder. The only place that knows the
    // path. It loads the prefab and stops there; nothing is instantiated here.
    public class ResourcesPopupParentSource : IPopupParentSource
    {
        private const string PARENT_FILE_NAME = "Popups/PopupParent";

        public UniTask<PopupParent> ReadAsync(CancellationToken ct)
        {
            PopupParent prefab = Resources.Load<PopupParent>(PARENT_FILE_NAME);
            if (prefab == null)
            {
                throw new MissingAssetException(PARENT_FILE_NAME, "Popup parent prefab");
            }

            return UniTask.FromResult(prefab);
        }
    }
}
