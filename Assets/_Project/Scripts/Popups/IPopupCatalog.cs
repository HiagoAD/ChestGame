using System;
using System.Collections.Generic;

namespace Company.ChestGame.Popups
{
    // The popups the game knows how to spawn, keyed by popup type.
    public interface IPopupCatalog
    {
        IReadOnlyDictionary<Type, PopupBase> Popups { get; }
    }
}
