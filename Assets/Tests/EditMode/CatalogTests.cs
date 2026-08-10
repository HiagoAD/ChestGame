using System.Collections.Generic;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    // Authored lists are hand-maintained, so the states worth covering are the authoring mistakes:
    // an empty inspector slot, and the same type listed twice. Both used to reach LINQ and surface
    // as a NullReferenceException or a raw ArgumentException from inside a constructor.
    //
    // Splitting the catalogs from their Resources loaders is what makes these reachable at all:
    // the entries go in as a plain list, with no asset involved.
    public class CatalogTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        private FakeMinigameSO NewMinigame()
        {
            FakeMinigameSO minigame = FakeMinigameSO.Create();
            _created.Add(minigame);
            return minigame;
        }

        private TPopup NewPopup<TPopup>() where TPopup : PopupBase
        {
            GameObject host = new(typeof(TPopup).Name);
            _created.Add(host);
            return host.AddComponent<TPopup>();
        }

        // --- Minigame catalog --------------------------------------------------------------

        [Test]
        public void MinigameCatalog_IndexesEntriesByContainerType()
        {
            FakeMinigameSO minigame = NewMinigame();

            MinigameCatalog catalog = new(new List<MinigameBaseSO> { minigame });

            Assert.AreEqual(1, catalog.Minigames.Count);
            Assert.AreSame(minigame, catalog.Minigames[typeof(FakeMinigameContainer)]);
        }

        [Test]
        public void MinigameCatalog_SkipsEmptySlotsInsteadOfCrashing()
        {
            // An empty inspector slot is the most common authoring mistake, and OnValidate leaves
            // one behind whenever it clears a duplicate. The game stays playable.
            LogAssert.Expect(LogType.Warning, "Minigame list has an empty entry at index 0, skipping it");
            FakeMinigameSO minigame = NewMinigame();

            MinigameCatalog catalog = new(new List<MinigameBaseSO> { null, minigame });

            Assert.AreEqual(1, catalog.Minigames.Count);
            Assert.AreSame(minigame, catalog.Minigames[typeof(FakeMinigameContainer)]);
        }

        [Test]
        public void MinigameCatalog_OfNothingButEmptySlots_IsEmptyRatherThanBroken()
        {
            LogAssert.Expect(LogType.Warning, "Minigame list has an empty entry at index 0, skipping it");

            MinigameCatalog catalog = new(new List<MinigameBaseSO> { null });

            CollectionAssert.IsEmpty(catalog.Minigames);
        }

        [Test]
        public void MinigameCatalog_WithTheSameTypeTwice_ThrowsInvalidCatalog()
        {
            // OnValidate only guards edits made through the inspector; a merge or a hand-edited
            // YAML can still produce this.
            List<MinigameBaseSO> entries = new() { NewMinigame(), NewMinigame() };

            InvalidCatalogException error = Assert.Throws<InvalidCatalogException>(() => new MinigameCatalog(entries));

            Assert.AreEqual(typeof(FakeMinigameContainer), error.OffendingType);
        }

        // --- Popup catalog -----------------------------------------------------------------

        [Test]
        public void PopupCatalog_IndexesEntriesByPopupType()
        {
            CatalogTestPopup popup = NewPopup<CatalogTestPopup>();

            PopupCatalog catalog = new(new List<PopupBase> { popup });

            Assert.AreEqual(1, catalog.Popups.Count);
            Assert.AreSame(popup, catalog.Popups[typeof(CatalogTestPopup)]);
        }

        [Test]
        public void PopupCatalog_SkipsEmptySlotsInsteadOfCrashing()
        {
            LogAssert.Expect(LogType.Warning, "Popup list has an empty entry at index 0, skipping it");
            CatalogTestPopup popup = NewPopup<CatalogTestPopup>();

            PopupCatalog catalog = new(new List<PopupBase> { null, popup });

            Assert.AreEqual(1, catalog.Popups.Count);
            Assert.AreSame(popup, catalog.Popups[typeof(CatalogTestPopup)]);
        }

        [Test]
        public void PopupCatalog_WithTheSameTypeTwice_ThrowsInvalidCatalog()
        {
            List<PopupBase> entries = new() { NewPopup<CatalogTestPopup>(), NewPopup<CatalogTestPopup>() };

            InvalidCatalogException error = Assert.Throws<InvalidCatalogException>(() => new PopupCatalog(entries));

            Assert.AreEqual(typeof(CatalogTestPopup), error.OffendingType);
        }

        [Test]
        public void PopupCatalog_KeepsDistinctTypesApart()
        {
            CatalogTestPopup first = NewPopup<CatalogTestPopup>();
            OtherCatalogTestPopup second = NewPopup<OtherCatalogTestPopup>();

            PopupCatalog catalog = new(new List<PopupBase> { first, second });

            Assert.AreEqual(2, catalog.Popups.Count);
            Assert.AreSame(first, catalog.Popups[typeof(CatalogTestPopup)]);
            Assert.AreSame(second, catalog.Popups[typeof(OtherCatalogTestPopup)]);
        }

        public class CatalogTestPopupData : PopupDataBase { }

        public class CatalogTestPopup : PopupBase<CatalogTestPopup, CatalogTestPopupData> { }

        public class OtherCatalogTestPopup : PopupBase<OtherCatalogTestPopup, CatalogTestPopupData> { }
    }
}
