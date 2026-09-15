using UnityEngine;

namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// The only place that turns a <see cref="PoolStrategy"/> into an <see cref="IPrefabPool{T}"/>,
    /// and the only place that knows what a holder has to be.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "One place that constructs a pool".
    /// </remarks>
    public static class PoolFactory
    {
        /// <summary>
        /// Builds the pool for the given strategy, including its holder if that strategy needs one.
        /// </summary>
        /// <param name="holderParent">
        /// Where a holder is built for strategies that need one. Must not carry a layout group.
        /// </param>
        /// <remarks>
        /// See docs/pooling.md, "PoolFactory, and what CreateHolder decides for the caller".
        /// </remarks>
        public static IPrefabPool<T> Create<T>(PoolStrategy strategy, T prefab, Transform holderParent, int maxSize, string holderName)
            where T : Component
        {
            if (strategy == PoolStrategy.DirectSpawner) return new DirectSpawner<T>(prefab);

            Transform holder = CreateHolder(holderParent, holderName);

            return strategy switch
            {
                PoolStrategy.ParkedPool => new ParkedPool<T>(prefab, holder, maxSize),
                PoolStrategy.UnityPool => new UnityPool<T>(prefab, holder, maxSize),
                _ => new ActivationPool<T>(prefab, holder, maxSize)
            };
        }

        /// <summary>
        /// Builds the disabled-<see cref="Canvas"/> holder that parked or deactivated instances
        /// live under.
        /// </summary>
        /// <param name="parent">
        /// Must not carry a layout group: parking under one dirties a rebuild on every
        /// <c>Get</c> and <c>Release</c>.
        /// </param>
        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// See docs/pooling.md, "PoolFactory, and what CreateHolder decides for the caller".
        /// </remarks>
        public static Transform CreateHolder(Transform parent, string name)
        {
            GameObject holder = new(name, typeof(RectTransform), typeof(Canvas));

            holder.transform.SetParent(parent, false);
            holder.GetComponent<Canvas>().enabled = false;

            return holder.transform;
        }
    }
}
