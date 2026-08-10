using Company.ChestGame.Common;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Creates the shared popup canvas on first use rather than at construction, so resolving
    // IPopupManager stays free of side effects until something actually needs a popup shown.
    public class ResourcesPopupParentProvider : IPopupParentProvider
    {
        private const string PARENT_FILE_NAME = "Popups/PopupParent";

        private Transform _default;

        public Transform Default
        {
            get
            {
                if (_default != null) return _default;

                PopupParent parentRef = Resources.Load<PopupParent>(PARENT_FILE_NAME);
                if (parentRef == null)
                {
                    throw new MissingAssetException(PARENT_FILE_NAME, "Popup parent prefab");
                }

                PopupParent parentInstance = Object.Instantiate(parentRef);
                Object.DontDestroyOnLoad(parentInstance);
                _default = parentInstance.Target;

                return _default;
            }
        }
    }
}
