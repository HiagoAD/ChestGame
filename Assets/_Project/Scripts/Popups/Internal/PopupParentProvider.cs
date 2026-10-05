using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    /// <summary>
    /// Creates the shared popup canvas on first use rather than at construction.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Popups".
    /// </remarks>
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
