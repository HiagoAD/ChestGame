namespace Company.ChestGame.Pooling
{
    // Which IPrefabPool implementation a call site wants, in a form an inspector can serialize. The
    // four differ in what they cost rather than in what they promise, so this is a choice a screen
    // makes and changes its mind about, not a type it is written against.
    //
    // The baseline is last because first place is what a newly serialized field lands on, and
    // nothing should ship pooling nothing by default.
    public enum PoolStrategy
    {
        ActivationPool,
        ParkedPool,
        UnityPool,
        DirectSpawner
    }
}
