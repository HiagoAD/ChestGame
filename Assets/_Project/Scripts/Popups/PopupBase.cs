using System;
using Unity.Android.Gradle.Manifest;
using UnityEngine;

namespace Company.ChestGame.Popups
{
    public abstract class PopupDataBase
    {

    }
    public abstract class PopupBase : MonoBehaviour
    {

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
