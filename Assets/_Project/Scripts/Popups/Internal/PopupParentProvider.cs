using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Creates the shared popup canvas on first use rather than at construction. The instance is
    // DontDestroyOnLoad, so building it during resolution would leak a scene object into every
    // consumer of the container, tests included. There is a test pinning that.
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
