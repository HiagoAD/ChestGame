namespace Company.ChestGame.Common
{
    // Seam over UnityEngine.Random so gameplay code that draws random numbers stays testable. In a
    // leaf assembly on purpose: Core already depends on Rewards, so hosting it there would cycle.
    public interface IRandomProvider
    {
        // Uniform value in the [0, 1] range, inclusive on both ends, matching UnityEngine.Random.value
        float Value { get; }

        int Range(int minInclusive, int maxExclusive);
    }
}
