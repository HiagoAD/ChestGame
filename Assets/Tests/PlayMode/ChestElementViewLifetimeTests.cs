using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Company.ChestGame.Minigame.Chests.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Covers what a chest element view must let go of when it stops showing a model, across both
    /// ways that happens: the view is destroyed, or the view is released back to a pool and goes on
    /// existing.
    /// </summary>
    /// <remarks>
    /// See docs/minigames.md, "The views".
    /// See docs/minigames.md, "A chest has two lifetimes now".
    /// </remarks>
    public class ChestElementViewLifetimeTests
    {
        private GameObject _viewObject;
        private readonly List<Object> _spriteAssets = new();

        private Sprite _closedSprite;
        private Sprite _openedEmptySprite;
        private Sprite _openedFullSprite;

        [TearDown]
        public void TearDown()
        {
            if (_viewObject != null) Object.Destroy(_viewObject);

            foreach (Object asset in _spriteAssets)
            {
                if (asset != null) Object.Destroy(asset);
            }
            _spriteAssets.Clear();
        }

        /// <summary>
        /// Builds a chest element view the way the real prefab wires it: fields set directly, with
        /// the object left inactive while they are assigned so <c>Awake</c> does not run before they
        /// exist.
        /// </summary>
        /// <remarks>
        /// See docs/testing.md, "ChestElementViewLifetimeTests, and its fixture choices".
        /// </remarks>
        private ChestsMinigameChestElementView BuildView()
        {
            _viewObject = new GameObject("Chest");
            _viewObject.SetActive(false);

            ChestsMinigameChestElementView view = _viewObject.AddComponent<ChestsMinigameChestElementView>();

            _closedSprite = NewSprite();
            _openedEmptySprite = NewSprite();
            _openedFullSprite = NewSprite();
            Set(view, "_closedSprite", _closedSprite);
            Set(view, "_openedEmptySprite", _openedEmptySprite);
            Set(view, "_openedFullSprite", _openedFullSprite);

            Set(view, "_chestImage", AddChild<Image>("Image"));
            Set(view, "_timerSlider", AddChild<Slider>("Slider"));
            Set(view, "_button", AddChild<Button>("Button"));

            _viewObject.SetActive(true);
            return view;
        }

        private Sprite NewSprite()
        {
            Texture2D texture = new(1, 1);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);

            _spriteAssets.Add(texture);
            _spriteAssets.Add(sprite);
            return sprite;
        }

        private TComponent AddChild<TComponent>(string name) where TComponent : Component
        {
            GameObject child = new(name, typeof(TComponent));
            child.transform.SetParent(_viewObject.transform, false);
            return child.GetComponent<TComponent>();
        }

        private static void Set(object target, string fieldName, object value) =>
            target.GetType()
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);

        private static TField Read<TField>(object target, string fieldName) =>
            (TField)target.GetType()
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(target);

        /// <remarks>
        /// See docs/minigames.md, "A chest has two lifetimes now".
        /// </remarks>
        [UnityTest]
        public IEnumerator ADestroyedChestView_StopsListeningToItsModel()
        {
            ChestsMinigameChestModel model = new();
            ChestsMinigameChestElementView view = BuildView();
            view.Init(model, _ => { });

            Object.Destroy(_viewObject);
            yield return null;

            Assert.DoesNotThrow(() => model.SetOpening(0.5f));
            Assert.DoesNotThrow(() => model.SetOpen(true));
        }

        [UnityTest]
        public IEnumerator ALiveChestView_StillFollowsItsModel()
        {
            ChestsMinigameChestModel model = new();
            ChestsMinigameChestElementView view = BuildView();
            view.Init(model, _ => { });

            yield return null;

            Slider slider = Read<Slider>(view, "_timerSlider");

            model.SetOpening(0.25f);

            Assert.AreEqual(0.25f, slider.value, 0.0001f);
            Assert.IsTrue(slider.gameObject.activeSelf, "the timer shows while a chest is opening");
        }

        /// <remarks>
        /// See docs/minigames.md, "A chest has two lifetimes now".
        /// </remarks>
        [UnityTest]
        public IEnumerator AReleasedChestView_StopsListeningToItsModel()
        {
            ChestsMinigameChestModel model = new();
            ChestsMinigameChestElementView view = BuildView();

            bool clickReachedTheController = false;
            view.Init(model, _ => clickReachedTheController = true);

            view.Release();
            yield return null;

            model.SetOpening(0.75f);

            Slider slider = Read<Slider>(view, "_timerSlider");
            Assert.IsFalse(slider.gameObject.activeSelf,
                "a released chest that still shows its old chest's timer is still subscribed to it");
            Assert.AreEqual(0f, slider.value, 0.0001f);

            Read<Button>(view, "_button").onClick.Invoke();

            Assert.IsFalse(clickReachedTheController,
                "a released chest must not be able to open anything");
        }

        [UnityTest]
        public IEnumerator AReacquiredChestView_FollowsItsNewModelAndShowsNothingOfTheOldOne()
        {
            ChestsMinigameChestModel previous = new();
            previous.SetOpening(0.4f);

            ChestsMinigameChestElementView view = BuildView();
            view.Init(previous, _ => { });
            yield return null;

            Image image = Read<Image>(view, "_chestImage");
            Slider slider = Read<Slider>(view, "_timerSlider");
            Assert.IsTrue(slider.gameObject.activeSelf, "guard: it is showing the previous chest opening");

            view.Release();
            ChestsMinigameChestModel fresh = new();
            view.Init(fresh, _ => { });

            Assert.IsFalse(slider.gameObject.activeSelf,
                "a reused chest still showing the last one's timer is the pooling bug a player sees");
            Assert.AreSame(_closedSprite, image.sprite, "and the new model is closed, so it has to look closed");

            previous.SetOpen(hasPrize: true);
            Assert.AreSame(_closedSprite, image.sprite,
                "the chest it used to show opened, and this view is not the one that should have reacted");

            fresh.SetOpening(0.9f);
            Assert.IsTrue(slider.gameObject.activeSelf, "while the chest it does show has to drive it");
            Assert.AreEqual(0.9f, slider.value, 0.0001f);
        }
    }
}
