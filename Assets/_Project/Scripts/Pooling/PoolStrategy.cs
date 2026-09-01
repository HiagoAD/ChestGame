namespace Company.ChestGame.Pooling
{
    // Which IPrefabPool implementation a call site wants, in a form an inspector can serialize. The
    // four differ in what they cost rather than in what they promise.
    //
    // Append only. These are serialized by index - ChestsMinigame.prefab stores _poolStrategy: 1
    // for ParkedPool - so inserting a member in the middle silently repoints every authored value
    // at a different strategy. The baseline sits last because first place is what a newly
    // serialized field lands on, but that is a preference and appending is the rule: a fifth
    // strategy goes after DirectSpawner, not before it. See docs/design-decisions.md.
    public enum PoolStrategy
    {
        ActivationPool,
        ParkedPool,
        UnityPool,
        DirectSpawner
    }
}
