using System;
using System.IO;

namespace CustomSong
{
    internal static class AudioCodec
    {
        private const ulong Gold = 0x9E3779B97F4A7C15;
        private const ulong Mul1 = 0xD6E8FEB86659FD93;
        private const ulong Mul2 = 0xA24BAED4963EE407;

        internal sealed class Prepared
        {
            internal byte[] Bytes;
            internal int Length => Bytes.Length;
        }

        internal static Prepared Load(string path, out string reason)
        {
            reason = null;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                reason = $"{Path.GetFileName(path)} could not be read: {ModLog.Describe(e)}";
                return null;
            }

            if (bytes.Length == 0)
            {
                reason = $"{Path.GetFileName(path)} is empty";
                return null;
            }

            bytes = SkipTags(bytes);
            if (bytes.Length == 0)
            {
                reason = $"{Path.GetFileName(path)} holds only tag data";
                return null;
            }

            ulong seed = unchecked(Gold * (uint)bytes.Length);
            int i = 0;
            while (i < bytes.Length)
            {
                long position = i;
                ulong word = Word(position >> 3, seed);
                int groupEnd = (int)Math.Min(((position >> 3) + 1) << 3, bytes.Length);
                for (; i < groupEnd; i++)
                    bytes[i] ^= (byte)(word >> (int)(8 * ((long)i & 7)));
            }

            return new Prepared { Bytes = bytes };
        }

        private static byte[] SkipTags(byte[] bytes)
        {
            int start = 0;
            int end = bytes.Length;

            if (end >= 10 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3')
            {
                int version = bytes[3];
                int size = version == 2
                    ? ((bytes[6] & 0x7F) << 14) | ((bytes[7] & 0x7F) << 7) | (bytes[8] & 0x7F)
                    : ((bytes[6] & 0x7F) << 21) | ((bytes[7] & 0x7F) << 14) |
                      ((bytes[8] & 0x7F) << 7) | (bytes[9] & 0x7F);
                int total = 10 + size;
                if (version >= 4 && (bytes[5] & 0x10) != 0) total += 10;
                if (total > 0 && total <= end) start = total;
            }

            if (end - start >= 128 &&
                bytes[end - 128] == 'T' && bytes[end - 127] == 'A' && bytes[end - 126] == 'G')
                end -= 128;
            if (start == 0 && end == bytes.Length) return bytes;
            var audio = new byte[end - start];
            Buffer.BlockCopy(bytes, start, audio, 0, audio.Length);
            return audio;
        }

        internal static int Measure(string path, out string reason)
        {
            reason = null;

            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = file.Length;
                if (length == 0)
                {
                    reason = $"{Path.GetFileName(path)} is empty";
                    return -1;
                }

                long start = 0;
                long end = length;

                if (length >= 10)
                {
                    var head = new byte[10];
                    if (file.Read(head, 0, 10) == 10 &&
                        head[0] == 'I' && head[1] == 'D' && head[2] == '3')
                    {
                        int version = head[3];
                        int size = version == 2
                            ? ((head[6] & 0x7F) << 14) | ((head[7] & 0x7F) << 7) | (head[8] & 0x7F)
                            : ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14) |
                              ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);
                        long total = 10 + (long)size;
                        if (version >= 4 && (head[5] & 0x10) != 0) total += 10;
                        if (total > 0 && total <= length) start = total;
                    }
                }

                if (length - start >= 128)
                {
                    var tail = new byte[128];
                    file.Seek(length - 128, SeekOrigin.Begin);
                    if (file.Read(tail, 0, 128) == 128 &&
                        tail[0] == 'T' && tail[1] == 'A' && tail[2] == 'G')
                        end -= 128;
                }

                long audio = end - start;
                if (audio <= 0)
                {
                    reason = $"{Path.GetFileName(path)} holds only tag data";
                    return -1;
                }

                return (int)audio;
            }
            catch (Exception e)
            {
                reason = $"{Path.GetFileName(path)} could not be read: {ModLog.Describe(e)}";
                return -1;
            }
        }

        private static ulong Word(long n, ulong seed)
        {
            ulong t = unchecked(Mul1 * (ulong)n + seed);
            ulong u = t ^ Ror(t, 47);
            ulong v = unchecked(Mul2 * u);
            return v ^ Ror(v, 23);
        }

        private static ulong Ror(ulong x, int r) => (x >> r) | (x << (64 - r));
    }
}
