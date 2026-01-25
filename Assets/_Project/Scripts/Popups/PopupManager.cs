using System;
using System.Collections.Generic;
using Company.ChestGame.Popups.Internal;
using UnityEditor.U2D.Aseprite;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Company.ChestGame.Popups
{
    public class PopupManager : IPopupManager
    {
        private const string PARENT_FILE_NAME = "Popups/PopupParent";
        private const string LIST_FILE_NAME = "Popups/PopupList";

        readonly private Transform _defaultPopupParent;
        readonly private Dictionary<Type, PopupBase> _popupPrefabs;


        public PopupManager()
        {
            PopupParent parentRef = Resources.Load<PopupParent>(PARENT_FILE_NAME);
            if (parentRef == null)
            {
                throw new Exception($"File {PARENT_FILE_NAME} not found, make sure that it exists on a Resources folder");
            }

            PopupListSO popupListSO = Resources.Load<PopupListSO>(LIST_FILE_NAME);
            if(popupListSO == null)
            {
                throw new Exception($"File {LIST_FILE_NAME} not found, make sure that it exists on a Resources folder");
            }

            _popupPrefabs = popupListSO.Popups;

            PopupParent parentInstance = Object.Instantiate(parentRef);
            Object.DontDestroyOnLoad(parentInstance);
            _defaultPopupParent = parentInstance.Target;
        }

        public TPopup Spawn<TPopup, TData>(TData data = null, Transform parent = null) where TPopup : PopupBase<TPopup, TData> where TData : PopupDataBase
        {
            if(!_popupPrefabs.TryGetValue(typeof(TPopup), out PopupBase popupPrefab))
            {
                throw new Exception("Popup prefab not found");
            }

            if (parent == null)
                parent = _defaultPopupParent;

            
            TPopup popup = Object.Instantiate(popupPrefab, parent) as TPopup;
            popup.Initialize(data);

            return popup;
        }
    }
}
