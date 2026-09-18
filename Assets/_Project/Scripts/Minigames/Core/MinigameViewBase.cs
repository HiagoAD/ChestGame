using System;
using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameViewBase : MonoBehaviour
    {
        public abstract void SetController(MinigameControllerBase controller);
    }

    /// <summary>
    /// A <see cref="MinigameViewBase"/> bound to a specific <typeparamref name="TController"/>.
    /// </summary>
    /// <typeparam name="TController">The controller type this view renders.</typeparam>
    /// <remarks>
    /// The non-generic <see cref="MinigameViewBase"/> remains because <c>MinigameContainer</c> holds
    /// a view without naming its controller type.
    /// </remarks>
    public abstract class MinigameViewBase<TController> : MinigameViewBase
        where TController : MinigameControllerBase
    {
        /// <summary>The bound controller, or <c>null</c> before <see cref="SetController"/> runs.</summary>
        protected TController Controller { get; private set; }

        /// <summary>
        /// Casts <paramref name="controller"/> to <typeparamref name="TController"/>, assigns it to
        /// <see cref="Controller"/> and runs <see cref="OnControllerSet"/>.
        /// </summary>
        /// <param name="controller">The controller to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="controller"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="controller"/> is not a <typeparamref name="TController"/>.
        /// </exception>
        public sealed override void SetController(MinigameControllerBase controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (controller is not TController typedController)
            {
                throw new ArgumentException(
                    $"Expected a {typeof(TController).Name} controller, got {controller.GetType().Name} instead",
                    nameof(controller));
            }

            Controller = typedController;
            OnControllerSet();
        }

        /// <summary>
        /// Runs once <see cref="Controller"/> is set. Subscribes to the controller and renders its
        /// current state.
        /// </summary>
        protected abstract void OnControllerSet();
    }
}