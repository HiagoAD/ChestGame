using System;
using System.Collections.Generic;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Company.ChestGame.Popups
{
    /// <summary>
    /// Spawns popups from prefabs.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Popups".
    /// </remarks>
    public class PopupManager : IPopupManager
    {
        readonly private IPopupCatalog _catalog;
        readonly private IPopupParentProvider _parentProvider;

        public PopupManager(IPopupCatalog catalog, IPopupParentProvider parentProvider)
        {
            _catalog = catalog;
            _parentProvider = parentProvider;
        }

        public TPopup Spawn<TPopup, TData>(TData data = null, Transform parent = null)
            where TPopup : PopupBase<TPopup, TData>
            where TData : PopupDataBase
        {
            IReadOnlyDictionary<Type, PopupBase> prefabs = _catalog.Popups;
            if (!prefabs.TryGetValue(typeof(TPopup), out PopupBase popupPrefab))
            {
                throw new PopupNotFoundException(typeof(TPopup));
            }

            parent ??= _parentProvider.Default;

            TPopup popup = Object.Instantiate(popupPrefab, parent) as TPopup;
            popup.Initialize(data);

            return popup;
        }
    }
}
