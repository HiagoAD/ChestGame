using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Loads the authored popup list from a Resources folder. The only place that knows the path.
    public class ResourcesPopupListSource : IPopupListSource
    {
        private const string LIST_FILE_NAME = "Popups/PopupList";

        public UniTask<IReadOnlyList<PopupBase>> ReadAsync(CancellationToken ct)
        {
            PopupListSO popupListSO = Resources.Load<PopupListSO>(LIST_FILE_NAME);
            if (popupListSO == null)
            {
                throw new MissingAssetException(LIST_FILE_NAME, "Popup list");
            }

            return UniTask.FromResult(popupListSO.Entries);
        }
    }
}
