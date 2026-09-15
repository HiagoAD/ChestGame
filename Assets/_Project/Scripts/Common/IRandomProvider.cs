namespace Company.ChestGame.Common
{
    /// <summary>
    /// Seam over <see cref="UnityEngine.Random"/> so gameplay code that draws random numbers stays
    /// testable.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Assembly layout".
    /// See docs/design-decisions.md, "3. Engine seams over the clock and the random source".
    /// </remarks>
    public interface IRandomProvider
    {
        /// <summary>
        /// Uniform value in the [0, 1] range, inclusive on both ends, matching
        /// <see cref="UnityEngine.Random.value"/>.
        /// </summary>
        float Value { get; }

        int Range(int minInclusive, int maxExclusive);
    }
}
