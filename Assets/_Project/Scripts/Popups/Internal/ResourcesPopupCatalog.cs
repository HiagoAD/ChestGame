using System.Collections.Generic;
using Company.ChestGame.Common;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Loads the authored popup list from a Resources folder. The only place that knows the path.
    public class ResourcesPopupCatalog : PopupCatalog
    {
        private const string LIST_FILE_NAME = "Popups/PopupList";

        public ResourcesPopupCatalog() : base(LoadEntries()) { }

        private static IReadOnlyList<PopupBase> LoadEntries()
        {
            PopupListSO popupListSO = Resources.Load<PopupListSO>(LIST_FILE_NAME);
            if (popupListSO == null)
            {
                throw new MissingAssetException(LIST_FILE_NAME, "Popup list");
            }

            return popupListSO.Entries;
        }
    }
}
