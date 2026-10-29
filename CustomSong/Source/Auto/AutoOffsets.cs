namespace CustomSong.Auto
{
    internal static class AutoOffsets
    {
        internal static class Engine
        {
            internal static int CursorRaw = 0x10;
            internal static int CursorClamped = 0x98;
            internal static int ChartTime = 0xD0;
            internal static int Input = 0x58;
        }

        internal static class SafeArea
        {
            internal static int SafeDuration = 0x18;
            internal static int ForcedMiss = 0x20;
            internal static int GroupMark = 0x40;
            internal static int NoteMark = 0x48;
            internal static int InWindow = 0x50;
            internal static int OnBar = 0x51;
            internal static int StartOk = 0x52;
            internal const double SafeDurationFull = 300.0;
        }

        internal static class InputLane
        {
            internal static int Stride = 24;
            internal static int PressEdge = 0;
            internal static int Held = 1;
            internal static int Consumed = 2;
            internal static int JudgeTime = 8;
            internal static int LastInput = 16;
        }

        internal static class Note
        {
            internal static int Size = 128;
            internal static int Id = 0x00;
            internal static int Side = 0x10;
            internal static int Type = 0x14;
            internal static int StartMs = 0x18;
            internal static int EndMs = 0x1C;
            internal static int StartX = 0x28;
            internal static int EndX = 0x34;
            internal static int StartWidth = 0x40;
            internal static int EndWidth = 0x4C;
            internal static int DeltaMs = 0x50;
            internal static int Flags = 0x74;
            internal static int LaneFirst = 0x78;
            internal static int LaneLast = 0x7C;
        }

        internal static class Plane
        {
            internal const int None = 0;
            internal const int Main = 1;
            internal const int Shift = 2;
            internal const int Space = 3;
            internal const int Sky = 4;
            internal const int LaneCount = 6;
        }

        internal static class NoteType
        {
            internal const int SkyFlick = 4;
            internal const int SkyBar = 5;
        }

        internal static class Track
        {
            internal static int SkyCursor = 0x170;
            internal static int SkyCursorRenderer = 0x178;
            internal static int SkyGhostCursor = 0x1A0;
            internal static int CursorExtras = 0x1A8;
            internal static int Engine = 0x830;
        }

        internal static class NotePlayer
        {
            internal static int ChartObject = 0x40;
        }

        internal static class ChartObject
        {
            internal static int PlaneArrays = 0x10;
        }

        internal static class PlaneHolder
        {
            internal static int NoteArray = 0x10;
            internal static int Count = 0x28;
        }

        internal static class Runtime
        {
            internal const int ArrayDataOffset = 0x20;
            internal const int ListSize = 0x18;
            internal const int DictEntries = 0x18;
            internal const int DictEntrySize = 24;
            internal const int DictEntryValue = 16;
        }

        internal static void Resolve()
        {
            Engine.CursorRaw = F("_VD", "_oEA", Engine.CursorRaw);
            Engine.CursorClamped = F("_VD", "_tEA", Engine.CursorClamped);
            Engine.ChartTime = F("_VD", "_WEA", Engine.ChartTime);
            Engine.Input = F("_VD", "_pEA", Engine.Input);
            SafeArea.SafeDuration = E("_VD", "_OEA", "_Ae", "_OfA", SafeArea.SafeDuration);
            SafeArea.ForcedMiss = E("_VD", "_OEA", "_Ae", "_pfA", SafeArea.ForcedMiss);
            SafeArea.GroupMark = E("_VD", "_OEA", "_Ae", "_rfA", SafeArea.GroupMark);
            SafeArea.NoteMark = E("_VD", "_OEA", "_Ae", "_RfA", SafeArea.NoteMark);
            SafeArea.InWindow = E("_VD", "_OEA", "_Ae", "_sfA", SafeArea.InWindow);
            SafeArea.OnBar = E("_VD", "_OEA", "_Ae", "_SfA", SafeArea.OnBar);
            SafeArea.StartOk = E("_VD", "_OEA", "_Ae", "_tfA", SafeArea.StartOk);
            Note.Id = F("_fA", "_ZD", Note.Id);
            Note.Side = F("_fA", "_Ae", Note.Side);
            Note.Type = F("_fA", "_be", Note.Type);
            Note.StartMs = F("_fA", "_Be", Note.StartMs);
            Note.EndMs = F("_fA", "_ce", Note.EndMs);
            Note.StartX = E("_fA", "_Ce", "_BA", "_LD", Note.StartX);
            Note.EndX = E("_fA", "_de", "_BA", "_LD", Note.EndX);
            Note.StartWidth = E("_fA", "_De", "_BA", "_LD", Note.StartWidth);
            Note.EndWidth = E("_fA", "_ee", "_BA", "_LD", Note.EndWidth);
            Note.DeltaMs = F("_fA", "_Ee", Note.DeltaMs);
            Note.Flags = F("_fA", "_he", Note.Flags);
            Note.LaneFirst = F("_fA", "_He", Note.LaneFirst);
            Note.LaneLast = F("_fA", "_ie", Note.LaneLast);
            Track.SkyCursor = F("Track", "skyCursor", Track.SkyCursor);
            Track.SkyCursorRenderer = F("Track", "skyCursorMeshRenderer", Track.SkyCursorRenderer);
            Track.SkyGhostCursor = F("Track", "skyGhostCursor", Track.SkyGhostCursor);
            Track.CursorExtras = F("Track", "cursorExtrasContainer", Track.CursorExtras);
            Track.Engine = F("Track", "_YnA", Track.Engine);
            NotePlayer.ChartObject = F("LogicalNotePlayer", "_ve", NotePlayer.ChartObject);
            ChartObject.PlaneArrays = F("_T", "_vC", ChartObject.PlaneArrays);
            PlaneHolder.NoteArray = F("_hA", "_ye", PlaneHolder.NoteArray);
            PlaneHolder.Count = F("_hA", "_ze", PlaneHolder.Count);
            InputLane.PressEdge = F("_zD", "_lfA", InputLane.PressEdge);
            InputLane.Held = F("_zD", "_LfA", InputLane.Held);
            InputLane.Consumed = F("_zD", "_mfA", InputLane.Consumed);
            InputLane.JudgeTime = F("_zD", "_MfA", InputLane.JudgeTime);
            InputLane.LastInput = F("_zD", "_nfA", InputLane.LastInput);
        }

        private static int F(string type, string field, int fallback)
        {
            int offset = FieldResolver.Field(type, field);
            return offset < 0 ? fallback : offset;
        }

        private static int E(string type, string structField, string structType, string field, int fallback)
        {
            int outer = FieldResolver.Lookup(type, structField);
            int inner = FieldResolver.Lookup(structType, field);
            if (outer < 0 || inner < 0) return fallback;
            return outer + inner;
        }
    }
}
