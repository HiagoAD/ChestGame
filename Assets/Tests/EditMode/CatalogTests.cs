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
    /// <summary>
    /// Covers the authoring mistakes <c>MinigameCatalog</c> and <c>PopupCatalog</c> have to survive:
    /// an empty slot, and the same type listed twice.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Catalogs".
    /// </remarks>
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

        private TDefinition NewMinigame<TDefinition>(string id) where TDefinition : MinigameBaseSO
        {
            TDefinition definition = ScriptableObject.CreateInstance<TDefinition>().WithId(id);
            _created.Add(definition);
            return definition;
        }

        private TPopup NewPopup<TPopup>() where TPopup : PopupBase
        {
            GameObject host = new(typeof(TPopup).Name);
            _created.Add(host);
            return host.AddComponent<TPopup>();
        }

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
            List<MinigameBaseSO> entries = new() { NewMinigame(), NewMinigame() };

            InvalidCatalogException error = Assert.Throws<InvalidCatalogException>(() => new MinigameCatalog(entries));

            Assert.AreEqual(typeof(FakeMinigameContainer), error.OffendingType);
        }

        [Test]
        public void MinigameCatalog_IndexesEntriesByAuthoredId()
        {
            FakeMinigameSO minigame = NewMinigame<FakeMinigameSO>("chests");

            MinigameCatalog catalog = new(new List<MinigameBaseSO> { minigame });

            Assert.AreEqual(1, catalog.MinigamesById.Count);
            Assert.AreSame(minigame, catalog.MinigamesById["chests"]);
        }

        [Test]
        public void MinigameCatalog_WithTheSameIdTwice_ThrowsInvalidCatalog()
        {
            List<MinigameBaseSO> entries = new()
            {
                NewMinigame<FirstIdOnlyMinigameSO>("chests"),
                NewMinigame<SecondIdOnlyMinigameSO>("chests")
            };

            InvalidCatalogException error = Assert.Throws<InvalidCatalogException>(() => new MinigameCatalog(entries));

            Assert.AreEqual("chests", error.OffendingKey);
        }

        [Test]
        public void MinigameCatalog_WithABlankId_SkipsItFromTheIdLookupWithoutThrowing()
        {
            LogAssert.Expect(LogType.Warning,
                "Minigame list has an entry with no id at index 0, skipping it from the id lookup");

            FakeMinigameSO minigame = NewMinigame<FakeMinigameSO>("   ");

            MinigameCatalog catalog = new(new List<MinigameBaseSO> { minigame });

            CollectionAssert.IsEmpty(catalog.MinigamesById);
            Assert.AreSame(minigame, catalog.Minigames[typeof(FakeMinigameContainer)],
                "the type-keyed lookup still works, which is why a blank id is survivable");
        }

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

        private class FirstIdOnlyMinigameSO : MinigameBaseSO
        {
            public override System.Type ContainerType => typeof(FirstIdOnlyContainer);
            public override MinigameContainer GetMinigameContainer() => new FirstIdOnlyContainer();
        }

        private class SecondIdOnlyMinigameSO : MinigameBaseSO
        {
            public override System.Type ContainerType => typeof(SecondIdOnlyContainer);
            public override MinigameContainer GetMinigameContainer() => new SecondIdOnlyContainer();
        }

        private class FirstIdOnlyContainer : MinigameContainer { }

        private class SecondIdOnlyContainer : MinigameContainer { }

        public class CatalogTestPopupData : PopupDataBase { }

        public class CatalogTestPopup : PopupBase<CatalogTestPopup, CatalogTestPopupData> { }

        public class OtherCatalogTestPopup : PopupBase<OtherCatalogTestPopup, CatalogTestPopupData> { }
    }
}
