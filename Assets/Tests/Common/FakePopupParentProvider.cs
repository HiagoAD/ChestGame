using Company.ChestGame.Popups;
using UnityEngine;

namespace Company.ChestGame.Tests.Common
{
    // Hands out a parent the test owns and can assert against, with none of the real provider's
    // side effects. There is no fake catalog to go with it: the real PopupCatalog takes a plain
    // list, so tests use that one directly.
    public class FakePopupParentProvider : IPopupParentProvider
    {
        public Transform Parent { get; set; }

        public int DefaultAccessCount { get; private set; }

        public Transform Default
        {
            get
            {
                DefaultAccessCount++;
                return Parent;
            }
        }
    }
}
