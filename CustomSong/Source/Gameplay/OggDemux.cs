using System;
using System.IO;

namespace CustomSong
{
    internal static class OggDemux
    {
        private const int PageHeader = 27;
        private const int InspectBytes = 65536;

        internal static bool LooksMuxed(string path)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                int size = (int)Math.Min(file.Length, InspectBytes);
                var head = new byte[size];
                int read = file.Read(head, 0, size);
                if (read < size) Array.Resize(ref head, read);
                return LooksMuxed(head);
            }
            catch (Exception e)
            {
                ModLog.Warn($"{Path.GetFileName(path)} could not be inspected for a muxed Ogg: " +
                            ModLog.Describe(e));
                return false;
            }
        }

        internal static byte[] VorbisOnly(byte[] bytes, out string reason)
        {
            reason = null;
            if (!IsOgg(bytes)) return bytes;

            if (!FindStreams(bytes, out uint vorbis, out bool theora))
                return bytes;
            if (!theora || vorbis == 0) return bytes;
            using var output = new MemoryStream(bytes.Length);
            int at = 0;
            while (PageAt(bytes, at, out int length, out uint serial))
            {
                if (serial == vorbis) output.Write(bytes, at, length);
                at += length;
            }

            if (output.Length == 0)
            {
                reason = $"{nameof(OggDemux)} found no Vorbis pages to keep";
                return null;
            }

            return output.ToArray();
        }

        private static bool LooksMuxed(byte[] bytes)
        {
            if (!IsOgg(bytes)) return false;
            return FindStreams(bytes, out uint vorbis, out bool theora) && vorbis != 0 && theora;
        }

        private static bool FindStreams(byte[] bytes, out uint vorbisSerial, out bool theora)
        {
            vorbisSerial = 0;
            theora = false;
            int at = 0;
            while (PageAt(bytes, at, out int length, out uint serial))
            {
                int payload = PayloadAt(bytes, at);
                int size = length - (payload - at);

                if (size >= 7)
                {
                    if (bytes[payload] == 0x01 && Match(bytes, payload + 1, "vorbis") &&
                        vorbisSerial == 0)
                        vorbisSerial = serial;
                    else if (bytes[payload] == 0x80 && Match(bytes, payload + 1, "theora"))
                        theora = true;
                }

                at += length;
            }

            return vorbisSerial != 0;
        }

        private static bool Match(byte[] bytes, int at, string text)
        {
            if (at + text.Length > bytes.Length) return false;
            for (int i = 0; i < text.Length; i++)
                if (bytes[at + i] != text[i]) return false;
            return true;
        }

        private static bool IsOgg(byte[] bytes) =>
            bytes != null && bytes.Length >= PageHeader &&
            bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S';

        private static bool PageAt(byte[] bytes, int at, out int length, out uint serial)
        {
            length = 0;
            serial = 0;
            if (bytes == null || at < 0 || at + PageHeader > bytes.Length) return false;
            if (bytes[at] != 'O' || bytes[at + 1] != 'g' || bytes[at + 2] != 'g' ||
                bytes[at + 3] != 'S') return false;

            serial = (uint)(bytes[at + 14] | (bytes[at + 15] << 8) |
                            (bytes[at + 16] << 16) | (bytes[at + 17] << 24));
            int segments = bytes[at + 26];
            if (at + PageHeader + segments > bytes.Length) return false;
            int payload = 0;
            for (int i = 0; i < segments; i++) payload += bytes[at + PageHeader + i];
            length = PageHeader + segments + payload;
            return at + length <= bytes.Length;
        }

        private static int PayloadAt(byte[] bytes, int at)
        {
            int segments = bytes[at + 26];
            return at + PageHeader + segments;
        }
    }
}
