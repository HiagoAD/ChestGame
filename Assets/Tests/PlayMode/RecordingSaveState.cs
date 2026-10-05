namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// A minimal save model for <c>SaveScheduler&lt;T&gt;</c>'s play-mode fixtures. <c>JsonCodec</c>
    /// needs something concrete to serialize; nothing about these tests cares what shape it is beyond
    /// one field that proves which <c>MarkDirty</c> call's state actually landed.
    /// </summary>
    public class RecordingSaveState
    {
        public int Value;
    }
}
