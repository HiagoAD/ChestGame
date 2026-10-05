namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// How a lane's pool is prepared before <see cref="PoolRace{T}"/>'s timed fill begins.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "The demo's fill modes and what they measure".
    /// </remarks>
    public enum FillMode
    {
        Cold,
        Prewarmed,
        Reuse
    }
}
