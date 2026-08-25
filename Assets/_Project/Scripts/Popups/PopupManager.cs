using System;
using System.Collections.Generic;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Company.ChestGame.Popups
{
    // Spawns popups from prefabs. Where those come from and where they get parented are both
    // supplied, which leaves this class picking a prefab, picking a parent, handing over the data.
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
