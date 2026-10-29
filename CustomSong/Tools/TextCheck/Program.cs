using System;
using System.IO;
using System.Text;
using CustomSong;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: TextCheck <chart.spc> <key> <out.spc>");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(args[0]);
        byte[] encoded = TextChart.Encoded(bytes, args[1], out string reason);
        if (encoded == null)
        {
            Console.Error.WriteLine("failed: " + reason);
            return 1;
        }

        File.WriteAllBytes(args[2], encoded);
        Console.WriteLine($"wrote {encoded.Length} bytes to {args[2]}");

        if (!TextChart.TryParse(Encoding.UTF8.GetString(bytes), out TextChart chart, out reason))
        {
            Console.Error.WriteLine("parse for json failed: " + reason);
            return 1;
        }

        File.WriteAllText(Path.ChangeExtension(args[2], ".json"), Json(chart), new UTF8Encoding(false));
        return 0;
    }

    private static string Json(TextChart chart)
    {
        var text = new StringBuilder();
        text.Append("{\"format\":\"ICP1\",\"version\":1");
        text.Append(",\"bpm\":").Append(chart.Bpm.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.Append(",\"meter\":").Append(chart.Meter.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.Append(",\"noteCount\":").Append(chart.Notes.Count);
        text.Append(",\"eventCount\":").Append(chart.Events.Count);
        text.Append(",\"notes\":[");

        for (int i = 0; i < chart.Notes.Count; i++)
        {
            TextChart.Note note = chart.Notes[i];
            if (i > 0) text.Append(',');
            text.Append("{\"index\":").Append(i)
                .Append(",\"line\":").Append(note.Line)
                .Append(",\"type\":").Append(note.Type)
                .Append(",\"track\":").Append(note.Track)
                .Append(",\"timeMs\":").Append(note.Start)
                .Append(",\"endMs\":").Append(note.End)
                .Append(",\"fractions\":[")
                .Append('[').Append(note.Plains[0]).Append(',').Append(note.Plains[1]).Append("],")
                .Append('[').Append(note.Plains[2]).Append(',').Append(note.Plains[3]).Append("],")
                .Append('[').Append(note.Plains[4]).Append(',').Append(note.Plains[5]).Append("],")
                .Append('[').Append(note.Plains[6]).Append(',').Append(note.Plains[7]).Append("]]");

            if (note.Flags != 0)
                text.Append(",\"flags\":").Append(note.Flags);
            if (note.Type == 4)
                text.Append(",\"flick\":").Append(note.Flick);
            if (note.Type == 5)
                text.Append(",\"leftEase\":").Append(note.LeftEase == 0x04 ? 0 : note.LeftEase == 0x08 ? 1 : 2)
                    .Append(",\"rightEase\":").Append(note.RightEase == 0x20 ? 0 : note.RightEase == 0x40 ? 1 : 2);
            text.Append('}');
        }

        text.Append("],\"events\":[");
        for (int i = 0; i < chart.Events.Count; i++)
        {
            TextChart.Event e = chart.Events[i];
            if (i > 0) text.Append(',');
            text.Append("{\"id\":").Append(i)
                .Append(",\"type\":").Append(e.Type)
                .Append(",\"timeMs\":").Append(e.TimeMs);
            if (e.Type == 3) text.Append(",\"flags\":").Append(e.Flags);
            else if (e.Type == 1 || e.Type == 2)
                text.Append(",\"bpm\":").Append(e.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            else
                text.Append(",\"value\":").Append(e.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

            if (e.HasSpeed)
                text.Append(",\"speed\":").Append(e.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            text.Append('}');
        }

        text.Append("]}");
        return text.ToString();
    }
}

namespace CustomSong
{
    internal static class ModLog
    {
        internal static void Load(string message) => Console.WriteLine("[log] " + message);
    }
}
