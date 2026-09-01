namespace Company.ChestGame.Pooling.Demo
{
    // How a lane's pool is prepared before the timed fill begins. Cold and Prewarmed both start
    // every lane empty, differing only in whether the instantiate cost lands inside the timed race
    // or ahead of it. Reuse starts every lane where the previous race left it: nothing is trimmed,
    // so a pooled lane's Get calls are hits and a repeat run instantiates nothing - which is what
    // ChestsMinigameView's own NewGame does against the real board, and the one thing the other two
    // modes cannot show.
    public enum FillMode
    {
        Cold,
        Prewarmed,
        Reuse
    }
}
