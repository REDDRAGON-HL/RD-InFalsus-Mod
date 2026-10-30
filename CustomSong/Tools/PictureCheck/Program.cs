using System;
using System.IO;
using CustomSong;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: PictureCheck <picture> <out.raw>");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(args[0]);

        Png.Image image = Jpeg.Looks(bytes)
            ? Jpeg.Decode(bytes, out string reason)
            : Png.Decode(bytes, out reason);

        if (image == null)
        {
            Console.Error.WriteLine("decode failed: " + (reason ?? "(no reason)"));
            return 1;
        }

        File.WriteAllBytes(args[1], image.Rgba);
        Console.WriteLine($"{image.Width} {image.Height}");
        return 0;
    }
}
