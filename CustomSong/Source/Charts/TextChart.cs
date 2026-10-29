using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CustomSong
{
    internal sealed class TextChart
    {
        internal float Bpm = 120f;
        internal float Meter = 4f;
        internal readonly List<Note> Notes = new List<Note>();
        internal readonly List<Event> Events = new List<Event>();

        internal struct Note
        {
            internal int Type;
            internal int Line;
            internal int Track;
            internal int Start;
            internal int End;
            internal int Flags;
            internal int Flick;
            internal int LeftEase;
            internal int RightEase;
            internal int[] Plains;
        }

        internal struct Event
        {
            internal int Type;
            internal int TimeMs;
            internal double Number;
            internal bool HasNumber;
            internal int Flags;
            internal float Speed;
            internal bool HasSpeed;
        }

        private const int TypeTap = 1;
        private const int TypeHold = 2;
        private const int TypeFlick = 4;
        private const int TypeSkyArea = 5;
        private static readonly int[] LeftEaseBits = { 0x04, 0x08, 0x10 };
        private static readonly int[] RightEaseBits = { 0x20, 0x40, 0x80 };
        private const int BombFlag = 0x8000;

        internal static bool LooksJson(byte[] bytes)
        {
            if (bytes == null) return false;
            int at = 0;
            while (at < bytes.Length && Space(bytes[at])) at++;
            return at < bytes.Length && bytes[at] == 0x7B;
        }

        private static bool Space(byte b) => b == 0x20 || b == 0x09 || b == 0x0D || b == 0x0A ||
                                           b == 0xEF || b == 0xBB || b == 0xBF;

        internal static bool TryParseJson(string text, out TextChart chart, out string reason)
        {
            chart = new TextChart();
            reason = null;
            object document = Json.Parse(text, out reason);
            if (document == null) { reason = "not JSON: " + reason; return false; }

            Dictionary<string, object> root = Json.Object(document);
            if (root == null) { reason = "the document is not an object"; return false; }

            chart.Bpm = (float)Json.Number(root, "bpm", 120.0);
            chart.Meter = (float)Json.Number(root, "meter", 4.0);
            List<object> notes = Json.Array(Json.Field(root, "notes"));
            if (notes == null) { reason = "the document has no notes array"; return false; }

            foreach (object entry in notes)
            {
                Dictionary<string, object> note = Json.Object(entry);
                if (note == null) continue;
                int[] plains = new int[8];
                List<object> fractions = Json.Array(Json.Field(note, "fractions"));
                if (fractions == null || fractions.Count < 4) { reason = "a note has no fractions"; return false; }

                for (int pair = 0; pair < 4; pair++)
                {
                    List<object> one = Json.Array(fractions[pair]);
                    if (one == null || one.Count < 2) { reason = "a fraction pair is incomplete"; return false; }
                    plains[pair * 2] = (int)Json.Scalar(one[0]);
                    plains[pair * 2 + 1] = (int)Json.Scalar(one[1]);
                }

                int type = (int)Json.Number(note, "type");
                chart.Notes.Add(new Note
                {
                    Type = type,
                    Line = (int)Json.Number(note, "line"),
                    Track = (int)Json.Number(note, "track"),
                    Start = (int)Json.Number(note, "timeMs"),
                    End = (int)Json.Number(note, "endMs"),
                    Flags = Has(note, "flags") ? (int)Json.Number(note, "flags") : 0,
                    Flick = (int)Json.Number(note, "flick"),
                    LeftEase = type == TypeSkyArea ? EaseBits((int)Json.Number(note, "leftEase"), LeftEaseBits) : 0,
                    RightEase = type == TypeSkyArea ? EaseBits((int)Json.Number(note, "rightEase"), RightEaseBits) : 0,
                    Plains = plains,
                });
            }

            List<object> events = Json.Array(Json.Field(root, "events"));
            if (events != null)
            {
                foreach (object entry in events)
                {
                    Dictionary<string, object> item = Json.Object(entry);
                    if (item == null) continue;

                    var e = new Event
                    {
                        Type = (int)Json.Number(item, "type"),
                        TimeMs = (int)Json.Number(item, "timeMs"),
                        Flags = (int)Json.Number(item, "flags"),
                    };

                    if (Has(item, "bpm")) { e.Number = Json.Number(item, "bpm"); e.HasNumber = true; }
                    else if (Has(item, "value")) { e.Number = Json.Number(item, "value"); e.HasNumber = true; }

                    if (Has(item, "speed")) { e.Speed = (float)Json.Number(item, "speed"); e.HasSpeed = true; }

                    chart.Events.Add(e);
                }
            }

            chart.Order();
            chart.RenumberSkyGroups();
            chart.EnsureSpeed();
            return true;
        }

        private static bool Has(Dictionary<string, object> at, string key) => Json.Has(at, key);

        internal static bool LooksText(byte[] bytes)
        {
            if (bytes == null) return false;
            int at = 0;
            while (at < bytes.Length && (bytes[at] == ' ' || bytes[at] == '\t' || bytes[at] == '\r' ||
                                         bytes[at] == '\n' || bytes[at] == 0xEF || bytes[at] == 0xBB ||
                                         bytes[at] == 0xBF))
                at++;
            if (at + 6 > bytes.Length) return false;
            return Encoding.ASCII.GetString(bytes, at, 6) == "chart(";
        }

        internal static byte[] Encoded(byte[] bytes, string seed, out string reason)
        {
            reason = null;
            string text = Encoding.UTF8.GetString(bytes);
            bool json = LooksJson(bytes);
            if (!json && !LooksText(bytes)) return bytes;

            bool parsed = json ? TryParseJson(text, out TextChart chart, out reason)
                               : TryParse(text, out chart, out reason);
            if (!parsed)
            {
                reason = (json ? "json chart: " : "text chart: ") + reason;
                return null;
            }

            byte[] encoded = Icp1Writer.Build(chart, seed);
            ModLog.Load($"text chart converted: '{seed}' -> {chart.Notes.Count} note(s), " +
                        $"{chart.Events.Count} event(s)");
            return encoded;
        }

        internal static bool TryParse(string text, out TextChart chart, out string reason)
        {
            chart = new TextChart();
            reason = null;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool inBlock = false;
            bool blockNoInput = false;
            int skyGroup = 0;
            int lastEnd = int.MinValue;
            bool seenChart = false;
            int skipped = 0;

            foreach (string raw in lines)
            {
                string line = Active(raw);
                if (line.Length == 0) continue;

                if (line == "}" || line.StartsWith("groupend(", StringComparison.Ordinal))
                {
                    inBlock = false;
                    blockNoInput = false;
                    continue;
                }

                int open = line.IndexOf('(');
                int close = line.LastIndexOf(')');
                if (open <= 0 || close <= open) continue;
                string command = line.Substring(0, open).Trim().ToLowerInvariant();

                if (command == "timinggroup")
                {
                    string options = line.Substring(open + 1, close - open - 1);
                    inBlock = true;
                    blockNoInput = options.IndexOf("noinput", StringComparison.OrdinalIgnoreCase) >= 0;
                    continue;
                }

                string[] args = line.Substring(open + 1, close - open - 1).Split(',');
                for (int i = 0; i < args.Length; i++) args[i] = args[i].Trim();

                if (command == "hidegroup" || command == "audiooffset" || command == "beam" ||
                    command == "skyareapack" || command == "packpiece" || command == "showtext" ||
                    command == "setcolor" || command == "scenecontrol" || command == "skynote" ||
                    command == "trackmove")
                    continue;

                if (inBlock && blockNoInput)
                {
                    if (command == "track" && args.Length >= 2 &&
                        Int(args[0], out int at) && Num(args[1], out double speed))
                    {
                        chart.Events.Add(new Event { Type = 0, TimeMs = at, Number = speed, HasNumber = true });
                        skipped++;
                    }
                    else if (command == "bpm" && args.Length >= 2 &&
                             Int(args[0], out int beat) && Num(args[1], out double tempo) && chart.Bpm > 0)
                    {
                        chart.Events.Add(new Event
                        {
                            Type = 0, TimeMs = beat, Number = tempo / chart.Bpm, HasNumber = true,
                        });
                        skipped++;
                    }
                    else
                    {
                        skipped++;
                    }

                    continue;
                }

                switch (command)
                {
                    case "chart":
                        if (args.Length < 2 || !Num(args[0], out double bpm) || !Num(args[1], out double meter))
                            return Fail("chart(bpm, beats)", out reason);
                        chart.Bpm = (float)bpm;
                        chart.Meter = (float)meter;
                        seenChart = true;
                        break;

                    case "tap":
                    case "bomb":
                    {
                        if (args.Length < 3 ||
                            !Int(args[0], out int ms) || !Int(args[1], out int width) || !Int(args[2], out int lane))
                            return Fail($"{command}(ms, width, lane)", out reason);

                        chart.Notes.Add(new Note
                        {
                            Type = TypeTap,
                            Line = chart.Notes.Count,
                            Track = Lane(lane),
                            Start = ms,
                            End = ms,
                            Flags = command == "bomb" ? BombFlag : 0,
                            Plains = Pairs(lane, 4, lane, 4, width, 4, width, 4),
                        });
                        break;
                    }

                    case "hold":
                    {
                        if (args.Length < 4 ||
                            !Int(args[0], out int ms) || !Int(args[1], out int lane) ||
                            !Int(args[2], out int width) || !Int(args[3], out int duration))
                            return Fail("hold(ms, lane, width, duration)", out reason);

                        chart.Notes.Add(new Note
                        {
                            Type = TypeHold,
                            Line = chart.Notes.Count,
                            Track = Lane(lane),
                            Start = ms,
                            End = ms + duration,
                            Plains = Pairs(lane, 4, lane, 4, width, 4, width, 4),
                        });
                        break;
                    }

                    case "flick":
                    {
                        if (args.Length < 5 ||
                            !Int(args[0], out int ms) || !Int(args[1], out int x) ||
                            !Int(args[2], out int split) || !Int(args[3], out int width) ||
                            !Int(args[4], out int kind))
                            return Fail("flick(ms, x, split, width, type)", out reason);
                        if (split == 0) split = 1;

                        chart.Notes.Add(new Note
                        {
                            Type = TypeFlick,
                            Line = chart.Notes.Count,
                            Track = 4,
                            Start = ms,
                            End = ms,
                            Flick = kind,
                            Plains = Pairs(x, split, x, split, width, split, width, split),
                        });
                        break;
                    }

                    case "skyarea":
                    {
                        if (args.Length != 10 && args.Length != 11)
                            return Fail("skyarea(ms, sx, sxSplit, sw, ex, exSplit, ew, easeL, easeR, dur[, group])",
                                        out reason);
                        int[] v = new int[args.Length];
                        for (int i = 0; i < args.Length; i++)
                            if (!Int(args[i], out v[i])) return Fail("skyarea with a non-number", out reason);
                        if (args.Length == 11) skyGroup = v[10];
                        else if (lastEnd == int.MinValue || Math.Abs(v[0] - lastEnd) > 2) skyGroup++;
                        lastEnd = v[0] + v[9];
                        if (v[2] == 0 || v[5] == 0) return Fail("skyarea with a zero split", out reason);

                        chart.Notes.Add(new Note
                        {
                            Type = TypeSkyArea,
                            Line = skyGroup,
                            Track = 4,
                            Start = v[0],
                            End = v[0] + v[9],
                            LeftEase = EaseBits(v[7], LeftEaseBits),
                            RightEase = EaseBits(v[8], RightEaseBits),
                            Plains = Pairs(v[1], v[2], v[4], v[5], v[3], v[2], v[6], v[5]),
                        });
                        break;
                    }

                    case "skynote":
                    {
                        if (args.Length < 4 ||
                            !Int(args[0], out int at) || !Int(args[1], out int centre) ||
                            !Int(args[2], out int split) || !Int(args[3], out int wide))
                            return Fail("skynote(ms, centre, split, width)", out reason);
                        if (split == 0) split = 1;
                        int lane = (int)Math.Round(centre * 4.0 / split);
                        int width = (int)Math.Round(wide * 4.0 / split);
                        lane = Math.Clamp(lane, 1, 4);
                        width = Math.Clamp(width, 1, 4);

                        chart.Notes.Add(new Note
                        {
                            Type = TypeTap,
                            Line = chart.Notes.Count,
                            Track = 1,
                            Start = at,
                            End = at,
                            Plains = Pairs(lane, 4, lane, 4, width, 4, width, 4),
                        });
                        break;
                    }

                    case "track":
                    {
                        if (args.Length < 2 ||
                            !Int(args[0], out int at) || !Num(args[1], out double speed))
                            return Fail("track(ms, speed)", out reason);

                        chart.Events.Add(new Event
                        {
                            Type = 0,
                            TimeMs = at,
                            Number = speed,
                            HasNumber = true,
                        });
                        break;
                    }

                    case "bpm":
                    {
                        if (args.Length < 2 ||
                            !Int(args[0], out int at) || !Num(args[1], out double tempo))
                            return Fail("bpm(ms, bpm[, beats[, mode]])", out reason);
                        double beats = 4.0;
                        if (args.Length > 2 && !Num(args[2], out beats)) beats = 4.0;
                        int mode = 0;
                        if (args.Length > 3 && Num(args[3], out double parsed)) mode = (int)parsed;

                        chart.Events.Add(new Event
                        {
                            Type = mode == 0 ? 1 : 2,
                            TimeMs = at,
                            Number = tempo,
                            HasNumber = true,
                            Speed = (float)beats,
                            HasSpeed = true,
                        });
                        break;
                    }

                    case "lane":
                    {
                        if (args.Length < 3 ||
                            !Int(args[0], out int at) || !Int(args[1], out int lane) ||
                            !Int(args[2], out int on))
                            return Fail("lane(ms, lane, enabled)", out reason);

                        chart.Events.Add(new Event
                        {
                            Type = 3,
                            TimeMs = at,
                            Flags = lane + (on != 0 ? 256 : 0),
                        });
                        break;
                    }

                    default:

                        break;
                }
            }

            if (!seenChart)
            {
                reason = "the file has no chart(bpm, beats) row";
                return false;
            }

            if (skipped > 0)
                ModLog.Load($"text chart: skipped {skipped} decoration row(s) inside a noinput group");
            chart.Order();
            chart.RenumberSkyGroups();
            chart.EnsureSpeed();
            return true;
        }

        private void SeedScrollSpeed()
        {
            foreach (Event e in Events)
                if (e.Type == 0) return;

            Events.Insert(0, new Event { Type = 0, TimeMs = 0, Number = 1.0, HasNumber = true });
        }

        private void Order()
        {
            int count = Notes.Count;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int byTime = Notes[a].Start.CompareTo(Notes[b].Start);
                return byTime != 0 ? byTime : a.CompareTo(b);
            });
            var sorted = new List<Note>(count);
            foreach (int i in order)
            {
                Note note = Notes[i];
                if (note.Type != TypeSkyArea) note.Line = sorted.Count;
                sorted.Add(note);
            }

            Notes.Clear();
            Notes.AddRange(sorted);
        }

        private void RenumberSkyGroups()
        {
            var order = new List<int>();
            for (int i = 0; i < Notes.Count; i++)
                if (Notes[i].Type == TypeSkyArea) order.Add(i);

            order.Sort((a, b) =>
            {
                int byTime = Notes[a].Start.CompareTo(Notes[b].Start);
                return byTime != 0 ? byTime : a.CompareTo(b);
            });
            int next = 0;
            int previous = int.MinValue;

            foreach (int i in order)
            {
                Note note = Notes[i];
                if (note.Line != previous)
                {
                    next++;
                    previous = note.Line;
                }

                note.Line = next;
                Notes[i] = note;
            }
        }

        private void EnsureSpeed()
        {
            foreach (Event e in Events)
                if (e.Type == 0) return;
            Events.Insert(0, new Event { Type = 0, TimeMs = 0, Number = 1.0, HasNumber = true });
        }

        private static int Lane(int lane) => lane >= 1 && lane <= 4 ? 1 : lane == 0 ? 2 : 3;

        private static int EaseBits(int ease, int[] bits) =>
            ease >= 0 && ease < bits.Length ? bits[ease] : 0;

        private static int[] Pairs(int a, int b, int c, int d, int e, int f, int g, int h) =>
            new[] { a, b, c, d, e, f, g, h };

        private static bool Fail(string shape, out string reason)
        {
            reason = "expected " + shape;
            return false;
        }

        private static string Active(string raw)
        {
            string text = raw.Trim();
            if (text.Length == 0 || text[0] == '#') return "";
            int comment = text.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) text = text.Substring(0, comment).Trim();
            if (text.EndsWith(":", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).Trim();
            return text;
        }

        private static bool Int(string text, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                          out double number);
            value = parsed ? (int)number : 0;
            return parsed;
        }

        private static bool Num(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
