using Random = UnityEngine.Random;

namespace Company.ChestGame.Common
{
    // The production implementation, a thin forward to UnityEngine.Random.
    public class UnityRandomProvider : IRandomProvider
    {
        public float Value => Random.value;

        public int Range(int minInclusive, int maxExclusive) => Random.Range(minInclusive, maxExclusive);
    }
}
