using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Popups
{
    /// <summary>
    /// A popup was requested that the catalog does not list.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
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
