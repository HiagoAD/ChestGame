using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Popups
{
    // A popup was requested that the catalog does not list.
    public class PopupNotFoundException : ChestGameException
    {
        public Type PopupType { get; }

        public PopupNotFoundException(Type popupType)
            : base($"No popup prefab is registered for type {popupType.Name}")
        {
            PopupType = popupType;
        }
    }
}
