using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Creates the shared popup canvas on first use rather than at construction, so resolving
    // IPopupManager stays free of side effects until something actually needs a popup shown. That
    // matters beyond tidiness: the instance is DontDestroyOnLoad, so building it during resolution
    // would leak a scene object into every consumer of the container, tests included.
    //
    // The prefab arrives already loaded, which is why this class has nothing to say about where it
    // came from.
    public class PopupParentProvider : IPopupParentProvider
    {
        private readonly PopupParent _prefab;

        private Transform _default;

        public PopupParentProvider(PopupParent prefab) => _prefab = prefab;

        public Transform Default
        {
            get
            {
                if (_default != null) return _default;

                PopupParent parentInstance = Object.Instantiate(_prefab);
                Object.DontDestroyOnLoad(parentInstance);
                _default = parentInstance.Target;

                return _default;
            }
        }
    }
}
