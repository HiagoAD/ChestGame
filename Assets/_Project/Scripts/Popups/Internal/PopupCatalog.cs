using System;
using System.Collections.Generic;
using Company.ChestGame.Common;

namespace Company.ChestGame.Popups.Internal
{
    // The popups the game can spawn, indexed by popup type.
    public class PopupCatalog : IPopupCatalog
    {
        public IReadOnlyDictionary<Type, PopupBase> Popups { get; }

        public PopupCatalog(IReadOnlyList<PopupBase> entries) =>
            Popups = CatalogBuilder.Build(entries, entry => entry.GetType(), "Popup list");
    }
}
