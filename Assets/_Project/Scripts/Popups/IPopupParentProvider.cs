using UnityEngine;

namespace Company.ChestGame.Popups
{
    // Supplies the canvas popups land under when the caller does not name one. Separate from the
    // catalog because creating that canvas is a side effect worth deferring.
    public interface IPopupParentProvider
    {
        Transform Default { get; }
    }
}
