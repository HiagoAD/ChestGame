using System.Collections;
using System.Reflection;
using Company.ChestGame.Minigame.Chests.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Company.ChestGame.Tests.PlayMode
{
    // A chest's model belongs to the controller and outlives the view showing it. This pins the
    // ownership rule that makes tearing a minigame down safe: when the element view dies it must
    // let go of the model, or the next state change drives a destroyed MonoBehaviour.
    public class ChestElementViewLifetimeTests
    {
        private GameObject _viewObject;

        [TearDown]
        public void TearDown()
        {
            if (_viewObject != null) Object.Destroy(_viewObject);
        }

        // The view's serialized references are wired by the prefab in the real game; here they are
        // set directly, with the object inactive so Awake does not run before they exist.
        private ChestsMinigameChestElementView BuildView()
        {
            _viewObject = new GameObject("Chest");
            _viewObject.SetActive(false);

            ChestsMinigameChestElementView view = _viewObject.AddComponent<ChestsMinigameChestElementView>();

            // Children, as they are in the real prefab. This matters: they have to die with the
            // view, otherwise a leaked subscription would keep working against live objects and
            // the leak would go unnoticed.
            Set(view, "_chestImage", AddChild<Image>("Image"));
            Set(view, "_timerSlider", AddChild<Slider>("Slider"));
            Set(view, "_button", AddChild<Button>("Button"));

            _viewObject.SetActive(true);
            return view;
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

        [UnityTest]
        public IEnumerator ADestroyedChestView_StopsListeningToItsModel()
        {
            ChestsMinigameChestModel model = new();
            ChestsMinigameChestElementView view = BuildView();
            view.Init(model, _ => { });

            Object.Destroy(_viewObject);
            yield return null;

            // If the view were still subscribed, this would reach into its destroyed Image and
            // Slider and throw MissingReferenceException.
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

            Slider slider = (Slider)view.GetType()
                .GetField("_timerSlider", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(view);

            model.SetOpening(0.25f);

            Assert.AreEqual(0.25f, slider.value, 0.0001f);
            Assert.IsTrue(slider.gameObject.activeSelf, "the timer shows while a chest is opening");
        }
    }
}
