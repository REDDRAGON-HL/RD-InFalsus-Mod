namespace CustomSong.Auto
{
    internal readonly struct ChartNote
    {
        internal readonly int StartMs;
        internal readonly int EndMs;
        internal readonly int LaneFirst;
        internal readonly int LaneLast;
        internal readonly int Type;
        internal readonly int Side;
        internal readonly float StartX;
        internal readonly float EndX;
        internal readonly float StartWidth;
        internal readonly float EndWidth;
        internal readonly int Flags;
        internal readonly long NoteId;

        internal ChartNote(int startMs, int endMs, int laneFirst, int laneLast, int type, int side,
                           float startX, float endX, float startWidth, float endWidth, int flags,
                           long noteId)
        {
            StartMs = startMs;
            EndMs = endMs;
            LaneFirst = laneFirst;
            LaneLast = laneLast;
            Type = type;
            Side = side;
            StartX = startX;
            EndX = endX;
            StartWidth = startWidth;
            EndWidth = endWidth;
            Flags = flags;
            NoteId = noteId;
        }

        internal bool IsSky => Side == AutoOffsets.Plane.Sky;
        internal bool IsHold => EndMs - StartMs > 40;
    }
}
