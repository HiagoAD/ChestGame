using Company.ChestGame.Mvc;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Company.ChestGame.Gameplay
{
    /// <summary>
    /// The game shell's view: the start button and the parent transform a minigame's view is
    /// instantiated under.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/architecture.md, "Entry point and game flow".
    /// </remarks>
    public class GameShellView : ViewBase<GameShellController>
    {
        [SerializeField] private Transform _minigamesParent;
        [SerializeField] private Button _startButton;

        /// <summary>
        /// Authored in the scene; the initializer is only a default for a freshly added component.
        /// </summary>
        [SerializeField] private string _minigameId = "chests";

        [Inject]
        private void Inject(GameShellController controller) => Bind(controller);

        private void Awake()
        {
            _startButton.onClick.AddListener(StartConfiguredMinigame);
        }

        protected override void OnBind()
        {
            Controller.OnBusyChanged += RenderBusy;
            RenderBusy(false);
        }

        protected override void OnUnbind()
        {
            Controller.OnBusyChanged -= RenderBusy;
        }

        protected override void OnDestroy()
        {
            _startButton.onClick.RemoveListener(StartConfiguredMinigame);
            base.OnDestroy();
        }

        private void StartConfiguredMinigame()
        {
            if (!IsBound) return;

            Controller.StartAsync(_minigameId, _minigamesParent, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void RenderBusy(bool busy)
        {
            if (_startButton == null) return;

            _startButton.interactable = !busy;
        }
    }
}
