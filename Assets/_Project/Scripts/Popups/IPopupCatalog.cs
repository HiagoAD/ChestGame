using System;
using System.Collections.Generic;

namespace Company.ChestGame.Popups
{
    /// <summary>
    /// The popups the game knows how to spawn, keyed by popup type.
    /// </summary>
    public interface IPopupCatalog
    {
        IReadOnlyDictionary<Type, PopupBase> Popups { get; }
    }
}
