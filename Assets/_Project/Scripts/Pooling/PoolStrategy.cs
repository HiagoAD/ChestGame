namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// Which <see cref="IPrefabPool{T}"/> implementation a call site wants, in a form the
    /// inspector can serialize. The four differ in what they cost, not in what they promise.
    /// </summary>
    /// <remarks>
    /// Append new members only, after <see cref="PoolStrategy.DirectSpawner"/>: values are
    /// serialized by index.
    /// See docs/design-decisions.md, "What adding a pool strategy actually takes".
    /// </remarks>
    public enum PoolStrategy
    {
        ActivationPool,
        ParkedPool,
        UnityPool,
        DirectSpawner
    }
}
