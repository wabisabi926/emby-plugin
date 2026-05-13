namespace TheIntroDB.Models
{
    public sealed class StoredMediaSegment
    {
        public long ItemInternalId { get; set; }
        public MediaSegmentType Type { get; set; }
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }
    }
}

