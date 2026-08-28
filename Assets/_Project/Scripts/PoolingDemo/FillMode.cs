namespace Company.ChestGame.Pooling.Demo
{
    // How a lane's pool is prepared before the timed fill begins. Cold and Prewarmed both start
    // every lane empty, differing only in whether the instantiate cost lands inside the timed race
    // or ahead of it. Reuse starts every lane exactly where the previous race left it: nothing is
    // trimmed, so a pooled lane's Get calls are hits against real stock and a repeat run instantiates
    // nothing. That is what Phase 2's own NewGame already does against the single real board every
    // time it hands the whole board back and takes it again - the one demonstration the other two
    // modes cannot show, because both of them deliberately start from nothing.
    public enum FillMode
    {
        Cold,
        Prewarmed,
        Reuse
    }
}
