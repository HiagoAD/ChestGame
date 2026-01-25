using UnityEngine;

namespace Company.ChestGame.Popups
{
    public interface IPopupManager
    {
        public TPopup Spawn<TPopup, TData>(TData data = null, Transform parent = null) where TPopup : PopupBase<TPopup, TData> where TData : PopupDataBase;
    }
}
