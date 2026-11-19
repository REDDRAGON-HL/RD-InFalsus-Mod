using System;
using System.Globalization;
using System.IO;
using System.Text;
using CustomSong;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Icp1Dump <chart.spc> <key> [out.txt]");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(args[0]);
        if (!Icp1Reader.TryRead(bytes, args[1], out Icp1Reader chart, out string reason))
        {
            Console.Error.WriteLine("read failed: " + reason);
            return 1;
        }

        var text = new StringBuilder();
        text.Append("bpm ").Append(chart.Bpm.ToString("R", CultureInfo.InvariantCulture))
            .Append(" meter ").Append(chart.Meter.ToString("R", CultureInfo.InvariantCulture))
            .Append(" notes ").Append(chart.Notes.Count)
            .Append(" events ").Append(chart.Events.Count).Append('\n');

        foreach (Icp1Reader.Note note in chart.Notes)
        {
            text.Append("n ").Append(note.Id)
                .Append(' ').Append(note.Line)
                .Append(' ').Append(note.Start)
                .Append(' ').Append((uint)note.End)
                .Append(' ').Append(note.Side)
                .Append(' ').Append(note.Type)
                .Append(' ').Append(note.Flags).Append(' ');
            for (int i = 0; i < 4; i++)
                text.Append(note.Num[i]).Append('/').Append(note.Den[i]).Append(' ');
            text.Append('\n');
        }

        foreach (Icp1Reader.Event item in chart.Events)
            text.Append("e ").Append(item.Id).Append(' ').Append(item.Time)
                .Append(' ').Append(item.Type)
                .Append(' ').Append(item.Value1.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(item.Value2.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        if (args.Length > 2) File.WriteAllText(args[2], text.ToString(), new UTF8Encoding(false));
        else Console.Write(text.ToString());
        return 0;
    }
}

namespace CustomSong
{
    internal static class ModLog
    {
        internal static void Load(string message) => Console.WriteLine("[log] " + message);
        internal static void Info(string message) => Console.WriteLine("[info] " + message);
        internal static void Warn(string message) => Console.WriteLine("[warn] " + message);
        internal static void WarnRelease(string message) => Console.WriteLine("[warn] " + message);
        internal static void Error(string message) => Console.WriteLine("[error] " + message);
        internal static string Describe(Exception e) => e.GetType().Name + ": " + e.Message;
    }
}
