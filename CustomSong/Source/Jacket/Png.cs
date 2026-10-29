using System;
using System.IO;
using System.IO.Compression;

namespace CustomSong
{
    internal static class Png
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        internal sealed class Image
        {
            internal int Width;
            internal int Height;
            internal byte[] Rgba;
            internal byte[] AsColor32 => Rgba;
        }

        internal static Image Decode(byte[] bytes, out string reason)
        {
            reason = null;

            if (bytes == null || bytes.Length < 8)
            {
#if DEBUG
                reason = "the file is too short to be a PNG";
#endif
                return null;
            }

            for (int i = 0; i < Signature.Length; i++)
                if (bytes[i] != Signature[i])
                {
#if DEBUG
                    reason = "the file is not a PNG";
#endif
                    return null;
                }

            int width = 0, height = 0, bitDepth = 0, colourType = 0, interlace = 0;
            bool haveHeader = false;
            byte[] palette = null;
            using var compressed = new MemoryStream();
            int at = 8;

            while (at + 8 <= bytes.Length)
            {
                int length = ReadInt32(bytes, at);
                if (length < 0 || at + 12 + (long)length > bytes.Length)
                {
#if DEBUG
                    reason = "a PNG chunk runs past the end of the file";
#endif
                    return null;
                }

                string type = "" + (char)bytes[at + 4] + (char)bytes[at + 5] + (char)bytes[at + 6] + (char)bytes[at + 7];
                int body = at + 8;

                if (type == "IHDR")
                {
                    width = ReadInt32(bytes, body);
                    height = ReadInt32(bytes, body + 4);
                    bitDepth = bytes[body + 8];
                    colourType = bytes[body + 9];
                    interlace = bytes[body + 12];
                    haveHeader = true;

                    if (width <= 0 || height <= 0)
                    {
#if DEBUG
                        reason = "the PNG has no pixels";
#endif
                        return null;
                    }
                    if (interlace != 0)
                    {
#if DEBUG
                        reason = "interlaced PNGs are not supported";
#endif
                        return null;
                    }
                    if (bitDepth != 8)
                    {
#if DEBUG
                        reason = $"only 8-bit PNGs are supported, this one is {bitDepth}-bit";
#endif
                        return null;
                    }
                    if (colourType != 0 && colourType != 2 && colourType != 3 && colourType != 4 && colourType != 6)
                    {
#if DEBUG
                        reason = $"PNG colour type {colourType} is not supported";
#endif
                        return null;
                    }
                }
                else if (type == "PLTE")
                {
                    palette = new byte[length];
                    Array.Copy(bytes, body, palette, 0, length);
                }
                else if (type == "IDAT")
                {
                    compressed.Write(bytes, body, length);
                }
                else if (type == "IEND")
                {
                    break;
                }

                at = body + length + 4;
            }

            if (!haveHeader)
            {
#if DEBUG
                reason = "the PNG has no header chunk";
#endif
                return null;
            }
            if (colourType == 3 && palette == null)
            {
#if DEBUG
                reason = "the PNG is paletted but has no palette";
#endif
                return null;
            }

            byte[] raw;
            try
            {
                raw = Inflate(compressed.ToArray());
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"the PNG data could not be decompressed: {ModLog.Describe(e)}";
#else
                _ = e;
#endif
                return null;
            }

            int channels = Channels(colourType);
            int stride = width * channels;
            if (raw.Length < (long)(stride + 1) * height)
            {
#if DEBUG
                reason = "the PNG data is shorter than its header says";
#endif
                return null;
            }

            byte[] pixels;
            try
            {
                pixels = Unfilter(raw, width, height, channels, stride);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"the PNG rows could not be reconstructed: {ModLog.Describe(e)}";
#else
                _ = e;
#endif
                return null;
            }

            byte[] rgba = ToRgba(pixels, width, height, colourType, channels, palette, out reason);
            if (rgba == null) return null;
            return new Image { Width = width, Height = height, Rgba = rgba };
        }

        private static int Channels(int colourType)
        {
            switch (colourType)
            {
                case 0: return 1;
                case 2: return 3;
                case 3: return 1;
                case 4: return 2;
                default: return 4;
            }
        }

        private static byte[] Inflate(byte[] zlib)
        {
            if (zlib.Length < 2) throw new InvalidDataException("no zlib data");
            using var input = new MemoryStream(zlib, 2, zlib.Length - 2);
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] Unfilter(byte[] raw, int width, int height, int channels, int stride)
        {
            var pixels = new byte[stride * height];
            int from = 0;

            for (int y = 0; y < height; y++)
            {
                int filter = raw[from++];
                int to = y * stride;
                int up = to - stride;

                for (int x = 0; x < stride; x++)
                {
                    int value = raw[from + x];
                    int left = x >= channels ? pixels[to + x - channels] : 0;
                    int above = y > 0 ? pixels[up + x] : 0;
                    int aboveLeft = y > 0 && x >= channels ? pixels[up + x - channels] : 0;

                    switch (filter)
                    {
                        case 0: break;
                        case 1: value += left; break;
                        case 2: value += above; break;
                        case 3: value += (left + above) >> 1; break;
                        case 4: value += Paeth(left, above, aboveLeft); break;
                        default: throw new InvalidDataException($"row {y} has filter {filter}");
                    }

                    pixels[to + x] = (byte)value;
                }

                from += stride;
            }

            return pixels;
        }

        private static int Paeth(int left, int above, int aboveLeft)
        {
            int estimate = left + above - aboveLeft;
            int dl = Math.Abs(estimate - left);
            int da = Math.Abs(estimate - above);
            int dal = Math.Abs(estimate - aboveLeft);
            if (dl <= da && dl <= dal) return left;
            return da <= dal ? above : aboveLeft;
        }

        private static byte[] ToRgba(byte[] pixels, int width, int height, int colourType,
                                     int channels, byte[] palette, out string reason)
        {
            reason = null;
            var rgba = new byte[width * height * 4];
            int count = width * height;

            for (int i = 0; i < count; i++)
            {
                int src = i * channels;
                int dst = i * 4;

                switch (colourType)
                {
                    case 0:
                        rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = pixels[src];
                        rgba[dst + 3] = 255;
                        break;

                    case 2:
                        rgba[dst] = pixels[src];
                        rgba[dst + 1] = pixels[src + 1];
                        rgba[dst + 2] = pixels[src + 2];
                        rgba[dst + 3] = 255;
                        break;

                    case 3:
                        int entry = pixels[src] * 3;
                        if (entry + 2 >= palette.Length)
                        {
#if DEBUG
                            reason = "the PNG uses a palette entry that is not in its palette";
#endif
                            return null;
                        }
                        rgba[dst] = palette[entry];
                        rgba[dst + 1] = palette[entry + 1];
                        rgba[dst + 2] = palette[entry + 2];
                        rgba[dst + 3] = 255;
                        break;

                    case 4:
                        rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = pixels[src];
                        rgba[dst + 3] = pixels[src + 1];
                        break;

                    default:
                        rgba[dst] = pixels[src];
                        rgba[dst + 1] = pixels[src + 1];
                        rgba[dst + 2] = pixels[src + 2];
                        rgba[dst + 3] = pixels[src + 3];
                        break;
                }
            }

            return rgba;
        }

        private static int ReadInt32(byte[] bytes, int at) =>
            (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
    }
}
