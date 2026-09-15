namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// What one storage/codec/protection combination did, end to end: the raw bytes that actually
    /// landed in the store (read back through the store, not re-encoded), how they render, and how
    /// long each direction took.
    /// </summary>
    /// <remarks>
    /// Produced by <see cref="SavePipelineProbe"/>.
    /// </remarks>
    public readonly struct SaveProbeResult
    {
        public SaveStorage Storage { get; }
        public SaveCodec Codec { get; }
        public SaveProtection Protection { get; }
        public string CodecId { get; }
        public string ProtectorId { get; }
        public byte[] RawBytes { get; }
        public string RenderedText { get; }
        public bool IsHexDump { get; }
        public int ByteCount { get; }
        public double WriteMilliseconds { get; }
        public double ReadMilliseconds { get; }
        public SaveInspectorDocument Loaded { get; }

        public SaveProbeResult(SaveStorage storage, SaveCodec codec, SaveProtection protection, string codecId, string protectorId,
            byte[] rawBytes, string renderedText, bool isHexDump, double writeMilliseconds, double readMilliseconds,
            SaveInspectorDocument loaded)
        {
            Storage = storage;
            Codec = codec;
            Protection = protection;
            CodecId = codecId;
            ProtectorId = protectorId;
            RawBytes = rawBytes;
            RenderedText = renderedText;
            IsHexDump = isHexDump;
            ByteCount = rawBytes?.Length ?? 0;
            WriteMilliseconds = writeMilliseconds;
            ReadMilliseconds = readMilliseconds;
            Loaded = loaded;
        }
    }
}
