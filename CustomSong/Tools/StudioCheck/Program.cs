using System;
using System.IO;
using CustomSong;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: StudioCheck <song folder>");
            return 2;
        }

        string folder = Path.GetFullPath(args[0]);
        StudioProject.EnsureIf(folder);
        string ifPath = Path.Combine(folder, IfReader.InfoFileName);
        if (!File.Exists(ifPath))
        {
            Console.Error.WriteLine("no if was written");
            return 1;
        }

        Console.WriteLine("--- " + IfReader.InfoFileName + " ---");
        Console.WriteLine(File.ReadAllText(ifPath));

        if (!IfReader.TryRead(folder, out ChartInfo info, out string reason))
        {
            Console.Error.WriteLine("the reader refused it: " + (reason ?? "(no reason)"));
            return 1;
        }

        Console.WriteLine("--- read back ---");
        Console.WriteLine($"id={info.BaseName} name='{info.Name}' composer='{info.Composer}'");
        Console.WriteLine($"song={Path.GetFileName(info.SongPath)} preview={info.PreviewStart}..{info.PreviewEnd}");

        for (int d = 0; d < ChartInfo.Difficulties; d++)
        {
            string chart = info.NoChart[d] ? "-" : $"{info.ChartNames[d]} (lv {info.Level[d]}, {info.Charters[d]})";
            Console.WriteLine($"  [{d}] {chart}");
        }

        return 0;
    }
}
