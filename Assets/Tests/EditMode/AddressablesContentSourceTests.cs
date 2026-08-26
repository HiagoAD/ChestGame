using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // Each of the four sources has one job: know its own key, and hand back what came out of the
    // provider. Both halves are asserted against FakeAssetProvider, so this runs with no catalog,
    // no bundle and no player loop. That the shipped keys really resolve is GameBootstrapperTests'
    // business.
    public class AddressablesContentSourceTests
    {
        private const string CONFIG_KEY = "GameConfig";
        private const string MINIGAME_LIST_KEY = "Minigames/MinigameList";
        private const string POPUP_LIST_KEY = "Popups/PopupList";
        private const string POPUP_PARENT_KEY = "Popups/PopupParent";

        private readonly List<Object> _created = new();

        private FakeAssetProvider _assets;

        [SetUp]
        public void SetUp() => _assets = new FakeAssetProvider();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        // --- Game config ------------------------------------------------------------------

        [Test]
        public void TheGameConfigSource_AsksForItsOwnKeyAndHandsBackTheDocumentText()
        {
            TextAsset document = Track(new TextAsset(@"{ ""GemsReward"": 1, ""CoinsReward"": 2 }"));
            _assets.With(CONFIG_KEY, document);

            string read = SynchronousUniTask.Result(
                new AddressablesGameConfigSource(_assets).ReadAsync(CancellationToken.None));

            CollectionAssert.AreEqual(new[] { CONFIG_KEY }, _assets.RequestedKeys);
            Assert.AreEqual(document.text, read);
        }

        [Test]
        public void TheGameConfigSource_SurfacesAMissingAssetAsTheTypedException()
        {
            _assets.FailWith = new MissingAssetException(CONFIG_KEY, "Game config");

            Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesGameConfigSource(_assets).ReadAsync(CancellationToken.None)));
        }

        [Test]
        public void TheGameConfigSource_WithNothingAtItsKey_HandsBackNoDocument()
        {
            // Deliberately not a throw: "the slot is empty" is the parser's failure to describe,
            // and LocalJsonGameConfig turns a null document into a GameConfigException naming it.
            string read = SynchronousUniTask.Result(
                new AddressablesGameConfigSource(_assets).ReadAsync(CancellationToken.None));

            Assert.IsNull(read);
        }

        [Test]
        public void TheGameConfigSource_PassesItsCancellationTokenToTheProvider()
        {
            using CancellationTokenSource cancellation = new();
            _assets.With(CONFIG_KEY, Track(new TextAsset("{}")));

            SynchronousUniTask.Result(new AddressablesGameConfigSource(_assets).ReadAsync(cancellation.Token));

            Assert.AreEqual(cancellation.Token, _assets.LastToken);
        }

        // --- Minigame list ----------------------------------------------------------------

        [Test]
        public void TheMinigameListSource_AsksForItsOwnKeyAndHandsBackTheAuthoredEntries()
        {
            List<MinigameBaseSO> authored = new() { Track(FakeMinigameSO.Create()) };
            _assets.With(MINIGAME_LIST_KEY, ListAssetWith<MinigameListSO, MinigameBaseSO>("minigames", authored));

            IReadOnlyList<MinigameBaseSO> read = SynchronousUniTask.Result(
                new AddressablesMinigameListSource(_assets).ReadAsync(CancellationToken.None));

            CollectionAssert.AreEqual(new[] { MINIGAME_LIST_KEY }, _assets.RequestedKeys);
            CollectionAssert.AreEqual(authored, read);
        }

        [Test]
        public void TheMinigameListSource_SurfacesAMissingAssetAsTheTypedException()
        {
            _assets.FailWith = new MissingAssetException(MINIGAME_LIST_KEY, "Minigame list");

            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesMinigameListSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(MINIGAME_LIST_KEY, error.AssetPath);
        }

        [Test]
        public void TheMinigameListSource_WithNothingAtItsKey_FailsWithATypedException()
        {
            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesMinigameListSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(MINIGAME_LIST_KEY, error.AssetPath);
        }

        // --- Popup list -------------------------------------------------------------------

        [Test]
        public void ThePopupListSource_AsksForItsOwnKeyAndHandsBackTheAuthoredEntries()
        {
            List<PopupBase> authored = new() { NewPopup() };
            _assets.With(POPUP_LIST_KEY, ListAssetWith<PopupListSO, PopupBase>("popups", authored));

            IReadOnlyList<PopupBase> read = SynchronousUniTask.Result(
                new AddressablesPopupListSource(_assets).ReadAsync(CancellationToken.None));

            CollectionAssert.AreEqual(new[] { POPUP_LIST_KEY }, _assets.RequestedKeys);
            CollectionAssert.AreEqual(authored, read);
        }

        [Test]
        public void ThePopupListSource_SurfacesAMissingAssetAsTheTypedException()
        {
            _assets.FailWith = new MissingAssetException(POPUP_LIST_KEY, "Popup list");

            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesPopupListSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(POPUP_LIST_KEY, error.AssetPath);
        }

        [Test]
        public void ThePopupListSource_WithNothingAtItsKey_FailsWithATypedException()
        {
            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesPopupListSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(POPUP_LIST_KEY, error.AssetPath);
        }

        // --- Popup parent -----------------------------------------------------------------

        [Test]
        public void ThePopupParentSource_AsksForItsOwnKeyAndHandsBackTheComponentOffThePrefab()
        {
            PopupParent parent = NewPopupParent();
            _assets.With(POPUP_PARENT_KEY, parent.gameObject);

            PopupParent read = SynchronousUniTask.Result(
                new AddressablesPopupParentSource(_assets).ReadAsync(CancellationToken.None));

            CollectionAssert.AreEqual(new[] { POPUP_PARENT_KEY }, _assets.RequestedKeys);
            Assert.AreSame(parent, read);
        }

        [Test]
        public void ThePopupParentSource_SurfacesAMissingAssetAsTheTypedException()
        {
            _assets.FailWith = new MissingAssetException(POPUP_PARENT_KEY, "Popup parent prefab");

            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesPopupParentSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(POPUP_PARENT_KEY, error.AssetPath);
        }

        [Test]
        public void ThePopupParentSource_GivenAPrefabWithNoPopupParentOnIt_FailsWithATypedException()
        {
            // The key resolving to the wrong prefab is an authoring mistake, so it has to arrive as
            // this game's exception rather than a NullReferenceException from whoever dereferences
            // the result.
            GameObject wrongPrefab = Track(new GameObject("NotAPopupParent"));
            _assets.With(POPUP_PARENT_KEY, wrongPrefab);

            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesPopupParentSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(POPUP_PARENT_KEY, error.AssetPath);
        }

        [Test]
        public void ThePopupParentSource_WithNothingAtItsKey_FailsWithATypedException()
        {
            MissingAssetException error = Assert.Throws<MissingAssetException>(() => SynchronousUniTask.Result(
                new AddressablesPopupParentSource(_assets).ReadAsync(CancellationToken.None)));

            Assert.AreEqual(POPUP_PARENT_KEY, error.AssetPath);
        }

        // --- helpers ----------------------------------------------------------------------

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private PopupParent NewPopupParent() =>
            Track(new GameObject("PopupParentPrefab")).AddComponent<PopupParent>();

        private SourceTestPopup NewPopup() =>
            Track(new GameObject(nameof(SourceTestPopup))).AddComponent<SourceTestPopup>();

        // The authoring lists keep their entries in a private serialized field, so filling it in
        // uses the same reflect-the-field-in pattern MinigameDefinitionAuthoring does. That is what
        // lets the assertion be identity rather than "something came back".
        private TListAsset ListAssetWith<TListAsset, TEntry>(string fieldName, List<TEntry> entries)
            where TListAsset : ScriptableObject
        {
            TListAsset listAsset = Track(ScriptableObject.CreateInstance<TListAsset>());

            FieldInfo field = typeof(TListAsset).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{typeof(TListAsset).Name} no longer has a '{fieldName}' field");
            field.SetValue(listAsset, entries);

            return listAsset;
        }

        public class SourceTestPopupData : PopupDataBase { }

        public class SourceTestPopup : PopupBase<SourceTestPopup, SourceTestPopupData> { }
    }
}
