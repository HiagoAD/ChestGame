using System;
using UnityEngine;

namespace Company.ChestGame.Popups
{
    public abstract class PopupDataBase
    {

    }
    public abstract class PopupBase : MonoBehaviour
    {
        /// <summary>
        /// Raised when the popup wants to be closed. The spawner owns destruction; see
        /// <see cref="RequestClose"/>.
        /// </summary>
        public event Action<PopupBase> OnCloseRequested;

        /// <summary>
        /// Signals that this popup should be closed, without destroying it itself.
        /// </summary>
        protected void RequestClose() => OnCloseRequested?.Invoke(this);
    }
    public abstract class PopupBase<TPopup, TData> : PopupBase
    where TPopup : PopupBase<TPopup, TData>
    where TData : PopupDataBase
    {
        protected TData Data { get; private set; }

        public void Initialize(TData data)
        {
            Data = data;
            OnInitialize();
        }

        protected virtual void OnInitialize()
        {
            
        }
    }


}
