using System;
using System.Collections.Generic;
using Company.ChestGame.Popups;
using UnityEngine;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Records what would have been spawned. Returns <c>default(TPopup)</c>; every current caller
    /// ignores the return value.
    /// </summary>
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
