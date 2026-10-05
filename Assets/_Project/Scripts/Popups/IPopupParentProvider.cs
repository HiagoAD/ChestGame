using UnityEngine;

namespace Company.ChestGame.Popups
{
    /// <summary>
    /// Supplies the canvas popups land under when the caller does not name one.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Popups".
    /// </remarks>
    public interface IPopupParentProvider
    {
        Transform Default { get; }
    }
}
