using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Minigame.Core
{
    public class MinigameContainer : IInitializable
    {
        protected bool _running;

        public virtual bool Running => _running;

        public MinigameViewBase ViewInstance { get; private set; }


        public MinigameViewBase ViewRef { get; private set; }
        public MinigameControllerBase ControllerInstance { get; private set; }

        [Inject]
        private IObjectResolver _resolver;


        public virtual void Set(MinigameControllerBase controller, MinigameViewBase view)
        {
            ControllerInstance = controller;
            ViewRef = view;
        }

        public virtual void Initialize()
        {

        }

        public virtual void Begin(Transform parent)
        {
            ViewInstance = _resolver.Instantiate(ViewRef, parent);
            ViewInstance.SetController(ControllerInstance);
            _running = true;
        }

        // Safe to call on a minigame that was never begun, or begun and already ended, so callers
        // can tear down unconditionally.
        public virtual void End()
        {
            if (!_running) return;

            _running = false;
            ControllerInstance.Dispose();

            if (ViewInstance != null)
            {
                Object.Destroy(ViewInstance.gameObject);
            }
            ViewInstance = null;
        }
    }

}