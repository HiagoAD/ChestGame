using System;
using System.Collections.Generic;
using Company.ChestGame.Popups;
using UnityEngine;

namespace Company.ChestGame.Tests.Common
{
    // Records what would have been spawned. Returns default(TPopup) because TPopup derives from
    // MonoBehaviour and cannot be constructed outside a GameObject; every current caller ignores
    // the return value.
    public class FakePopupManager : IPopupManager
    {
        public readonly List<(Type popupType, PopupDataBase data, Transform parent)> SpawnCalls = new();

        public TPopup Spawn<TPopup, TData>(TData data = null, Transform parent = null)
            where TPopup : PopupBase<TPopup, TData>
            where TData : PopupDataBase
        {
            SpawnCalls.Add((typeof(TPopup), data, parent));
            return default;
        }
    }
}
