using UnityEngine;

namespace Company.ChestGame.Popups
{
    // Supplies the canvas popups land under when the caller does not name one. Kept separate from
    // the catalog because creating that canvas is a side effect, and one worth deferring until a
    // popup is actually shown.
    public interface IPopupParentProvider
    {
        Transform Default { get; }
    }
}
