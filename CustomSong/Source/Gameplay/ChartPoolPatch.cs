using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe class ChartPoolPatch
    {
        private const int PointsRoom = 0x7E80;
        private const int BackScan = 0x200;
        private const int ForwardScan = 0x1000;
        private const int NoteRoom = 1 << 14;
        private const int EventRoom = 1 << 12;
        private const int TickRoom = 1 << 15;
        private const byte MovEdx = 0xBA;
        private const byte MovEax = 0xB8;
        private const byte MovEsi = 0xBE;
        private const byte MovEbx = 0xBB;

        internal static void Apply()
        {
            try
            {
                if (!Patch()) ModLog.WarnRelease(
                    "the chart size limits could not be found in this build; a chart longer than " +
                    "the shipped ones will stop the run while it is being set up");
            }
            catch (Exception e)
            {
                ModLog.Error("patching the chart size limits failed: " + ModLog.Describe(e));
            }
        }

        private static bool Patch()
        {
            byte* image = (byte*)GameAssembly.Base;
            if (image == null || *(ushort*)image != 0x5A4D) return false;
            byte* pe = image + *(int*)(image + 0x3C);
            if (*(uint*)pe != 0x00004550) return false;
            int sections = *(ushort*)(pe + 6);
            byte* section = pe + 24 + *(ushort*)(pe + 20);

            for (int s = 0; s < sections; s++, section += 40)
            {
                if ((*(uint*)(section + 36) & 0x20000000) == 0) continue;
                byte* start = image + *(uint*)(section + 12);
                uint size = *(uint*)(section + 8);
                if (size == 0 || start == null) continue;
                byte* anchor = Find(start, size, PointsRoom);
                if (anchor != null) return Rewrite(start, anchor);
            }

            return false;
        }

        private static byte* Find(byte* start, uint size, int value)
        {
            for (byte* at = start; at + 5 <= start + size; at++)
            {
                if (!IsSizeMove(at)) continue;
                if (*(int*)(at + 1) != value) continue;
                return at;
            }

            return null;
        }

        private static bool IsSizeMove(byte* at) =>
            *at == MovEdx || *at == MovEax || *at == MovEsi || *at == MovEbx;

        private static bool Rewrite(byte* start, byte* anchor)
        {
            var seen = new List<(IntPtr At, int Value)>();

            for (byte* at = anchor - 1; at >= start && at >= anchor - BackScan; at--)
            {
                if (!IsSizeMove(at)) continue;
                int value = *(int*)(at + 1);
                if (value == 1024 || value == 4096 || value == 8096) seen.Add(((IntPtr)at, value));
            }

            bool expected = seen.Count == 6 &&
                            seen[0].Value == 8096 && seen[1].Value == 8096 && seen[2].Value == 8096 &&
                            seen[3].Value == 4096 && seen[4].Value == 4096 && seen[5].Value == 1024;

            if (!expected)
            {
                ModLog.Warn($"{seen.Count} chart pool size(s) were found above " +
                            $"0x{((IntPtr)anchor).ToInt64():X}, but not the expected six; " +
                            "the limits are left alone");
                return false;
            }

            for (byte* at = anchor + 1; at < anchor + ForwardScan; at++)
            {
                if (!IsSizeMove(at)) continue;
                if (*(int*)(at + 1) != 4096) continue;
                seen.Add(((IntPtr)at, 4096));
                break;
            }

            int patched = 0;
            foreach (var entry in seen)
            {
                int room = entry.Value == 1024 ? EventRoom
                         : entry.Value == 4096 ? NoteRoom
                         : TickRoom;
                if (!Swap(entry.At + 1, room)) continue;
                int now = *(int*)(entry.At + 1);
                if (now != room)
                {
                    ModLog.Error($"chart pool at 0x{entry.At.ToInt64():X} reads back {now}, not {room}");
                    continue;
                }

                patched++;
                ModLog.Load($"chart pool {entry.Value} -> {room}");
            }

            return patched == seen.Count;
        }

        private static bool Swap(IntPtr at, int value)
        {
            if (!VirtualProtect(at, (UIntPtr)4, PageExecuteReadWrite, out uint previous))
                return false;
            *(int*)at = value;
            VirtualProtect(at, (UIntPtr)4, previous, out uint _);
            return true;
        }

        private const uint PageExecuteReadWrite = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect,
                                                  out uint previous);
    }
}
