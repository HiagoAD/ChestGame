namespace Company.ChestGame.Tests.PlayMode
{
    // A minimal save model for SaveScheduler<T>'s play-mode fixtures. JsonCodec needs something
    // concrete to serialize; nothing about these tests cares what shape it is beyond "one field that
    // proves which MarkDirty call's state actually landed".
    public class RecordingSaveState
    {
        public int Value;
    }
}
