using System;
using UnityEngine;

namespace Company.ChestGame.Mvc
{
    /// <summary>
    /// The base for a view: the <see cref="MonoBehaviour"/> half of a screen, bound to the controller
    /// that holds its rules.
    /// </summary>
    /// <typeparam name="TController">The controller this view renders.</typeparam>
    /// <remarks>
    /// A view holds serialized scene references and cached widgets only. It subscribes in
    /// <see cref="OnBind"/>, releases in <see cref="OnUnbind"/>, and decides nothing. The reasoning
    /// is in docs/mvc.md.
    /// </remarks>
    public abstract class ViewBase<TController> : MonoBehaviour where TController : class, IController
    {
        /// <summary>The bound controller, or <c>null</c> before <see cref="Bind"/> runs.</summary>
        protected TController Controller { get; private set; }

        /// <summary>Whether <see cref="Bind"/> has run and the controller has not been released.</summary>
        public bool IsBound => Controller != null;

        /// <summary>
        /// Hands this view its controller and runs <see cref="OnBind"/>. A view binds once per
        /// instance.
        /// </summary>
        /// <param name="controller">The controller to render.</param>
        /// <exception cref="ArgumentNullException"><paramref name="controller"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">This view is already bound.</exception>
        public void Bind(TController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (Controller != null)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} is already bound; a view binds once per instance");
            }

            Controller = controller;
            OnBind();
        }

        /// <summary>
        /// Subscribes to the controller and renders its current state. Runs once, from
        /// <see cref="Bind"/>, with <see cref="Controller"/> already set.
        /// </summary>
        protected abstract void OnBind();

        /// <summary>
        /// Releases whatever <see cref="OnBind"/> took. Runs once, before the controller reference is
        /// dropped. Does not dispose the controller: a view does not own the controller it renders.
        /// </summary>
        protected virtual void OnUnbind()
        {
        }

        /// <summary>
        /// Unbinds on destruction. An override must call <c>base.OnDestroy()</c> or the view's
        /// subscriptions outlive it.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (Controller == null) return;
            OnUnbind();
            Controller = null;
        }
    }
}
