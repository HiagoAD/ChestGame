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
    // A chest's model belongs to the controller and outlives the view showing it, so when the
    // element view stops showing that model it has to let go of it. There are two ways that happens
    // and they are not the same: the view is destroyed, or the view is released back to a pool and
    // goes on existing. The second is the one that looks like nothing is wrong.
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

        // Wired by the prefab in the real game; set directly here, with the object inactive so
        // Awake does not run before they exist.
        private ChestsMinigameChestElementView BuildView()
        {
            _viewObject = new GameObject("Chest");
            _viewObject.SetActive(false);

            ChestsMinigameChestElementView view = _viewObject.AddComponent<ChestsMinigameChestElementView>();

            // Three distinct sprites rather than the prefab's art. What matters is only that they
            // tell each other apart: with all three left null, every chest state paints the same
            // nothing and a view showing the wrong one would look right.
            _closedSprite = NewSprite();
            _openedEmptySprite = NewSprite();
            _openedFullSprite = NewSprite();
            Set(view, "_closedSprite", _closedSprite);
            Set(view, "_openedEmptySprite", _openedEmptySprite);
            Set(view, "_openedFullSprite", _openedFullSprite);

            // Children, as in the real prefab: they have to die with the view, or a leaked
            // subscription would keep working against live objects and go unnoticed.
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

        [UnityTest]
        public IEnumerator ADestroyedChestView_StopsListeningToItsModel()
        {
            ChestsMinigameChestModel model = new();
            ChestsMinigameChestElementView view = BuildView();
            view.Init(model, _ => { });

            Object.Destroy(_viewObject);
            yield return null;

            // A still-subscribed view would reach into its destroyed Image and Slider and throw.
            Assert.DoesNotThrow(() => model.SetOpening(0.5f));
            Assert.DoesNotThrow(() => model.SetOpen(true));
        }

        [UnityTest]
        public IEnumerator ALiveChestView_StillFollowsItsModel()
        {
            // The counterpart: unsubscribing on destroy must not mean unsubscribing early.
            ChestsMinigameChestModel model = new();
            ChestsMinigameChestElementView view = BuildView();
            view.Init(model, _ => { });

            yield return null;

            Slider slider = Read<Slider>(view, "_timerSlider");

            model.SetOpening(0.25f);

            Assert.AreEqual(0.25f, slider.value, 0.0001f);
            Assert.IsTrue(slider.gameObject.activeSelf, "the timer shows while a chest is opening");
        }

        [UnityTest]
        public IEnumerator AReleasedChestView_StopsListeningToItsModel()
        {
            // The mirror of the destroyed case, and the one pooling introduces. A released view is
            // still alive and still wired to everything it was wired to, so a subscription left on
            // it throws nothing and shows nothing: it simply follows a chest it is no longer for.
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

            // The other half of what a release drops. ParkedPool leaves a released instance active,
            // so a button that still carried the callback would send the old model to the controller
            // from a chest that is not on the board.
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

            // The old model must not reach it any more, and the new one must.
            previous.SetOpen(hasPrize: true);
            Assert.AreSame(_closedSprite, image.sprite,
                "the chest it used to show opened, and this view is not the one that should have reacted");

            fresh.SetOpening(0.9f);
            Assert.IsTrue(slider.gameObject.activeSelf, "while the chest it does show has to drive it");
            Assert.AreEqual(0.9f, slider.value, 0.0001f);
        }
    }
}
