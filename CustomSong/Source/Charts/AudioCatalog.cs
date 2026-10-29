using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe class AudioCatalog
    {
        private const string Extension = ".wav";

        private sealed class Entry
        {
            internal int Index;
            internal string Path;
            internal int Length;

            internal byte[] Bytes
            {
                get
                {
                    if (_audio != null) return _audio.Bytes;
                    _audio = AudioCodec.Load(Path, out string reason);
                    if (_audio != null) return _audio.Bytes;
                    ModLog.Error($"audio '{Path}' could not be prepared: {reason}");
                    return Array.Empty<byte>();
                }
            }

            private AudioCodec.Prepared _audio;
        }

        private static readonly Dictionary<string, Entry> ByBase =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Entry> ByDifficulty =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Entry> ByIndex = new Dictionary<int, Entry>();
        internal static long Served, ServedBytes, Resolved;

        internal static void Reset()
        {
            ByBase.Clear();
            ByDifficulty.Clear();
            ByIndex.Clear();
        }

        internal static int IndexOf(string baseName) =>
            ByBase.TryGetValue(baseName, out Entry entry) ? entry.Index : -1;

        internal static int BytesOf(string baseName) =>
            ByBase.TryGetValue(baseName, out Entry entry) ? entry.Length : -1;

        internal static bool Add(string baseName, string path, int difficulty, out string reason)
        {
            reason = null;
            int length = AudioCodec.Measure(path, out reason);
            if (length <= 0) return false;
            int index = StreamingAssets.Add(length);
            if (index < 0)
            {
                reason = "the game's asset manager would not take another file";
                return false;
            }

            var entry = new Entry { Index = index, Path = path, Length = length };
            if (difficulty < 0) ByBase[baseName] = entry;
            else ByDifficulty[baseName + "\n" + difficulty] = entry;
            ByIndex[index] = entry;
            return true;
        }

        internal static bool TryResolve(IntPtr namePtr, IntPtr indexPtr)
        {
            if (!CouldBeAudio(namePtr)) return false;
            string name = Memory.Text(namePtr, 128);
            if (name == null) return false;
            if (!name.EndsWith(Extension, StringComparison.Ordinal)) return false;
            string baseName = name.Substring(0, name.Length - Extension.Length);
            if (baseName.Length == 0) return false;
            int difficulty = CurrentSong.AppliedDifficulty;
            if (difficulty < 0 ||
                !ByDifficulty.TryGetValue(baseName + "\n" + difficulty, out Entry entry))
            {
                if (!ByBase.TryGetValue(baseName, out entry)) return false;
            }

            Memory.WriteI32(indexPtr, entry.Index);
            Resolved++;
            return true;
        }

        private static unsafe bool CouldBeAudio(IntPtr s)
        {
            if (!Memory.LooksLikeObject(s)) return false;

            try
            {
                int length = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_length(s);
                if (length < Extension.Length + 1 || length > 128) return false;
                char* chars = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_chars(s);
                if (chars == null) return false;
                return chars[length - 1] == 'v';
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryServe(IntPtr record)
        {
            if (Hooks.CompleteRead == null) return false;
            long handle = Memory.I64(record + Offsets.Runtime.ReadRecord.Handle);
            int index = (int)(handle & Offsets.Runtime.ReadRecord.IndexMask);
            if (!ByIndex.TryGetValue(index, out Entry entry)) return false;
            IntPtr info = Memory.Ptr(record + Offsets.Runtime.ReadRecord.Info);
            if (info == IntPtr.Zero) return false;
            int offset = Memory.I32(info + Offsets.Runtime.ReadRecord.Offset);
            int size = Memory.I32(info + Offsets.Runtime.ReadRecord.Length);
            IntPtr buffer = Memory.Ptr(info + Offsets.Runtime.ReadRecord.Buffer);
            if (buffer == IntPtr.Zero || size <= 0) return false;
            byte[] bytes = entry.Bytes;
            int available = offset >= 0 && offset < bytes.Length ? bytes.Length - offset : 0;
            int count = Math.Min(size, available);
            if (count > 0) Marshal.Copy(bytes, offset, buffer, count);
            Served++;
            ServedBytes += count;
            Memory.WriteI32(info + Offsets.Runtime.ReadRecord.BytesRead, count);
            ReadBookkeeping.Finished(record);
            byte* copy = stackalloc byte[Offsets.Runtime.ReadRecord.Size];
            Buffer.MemoryCopy((void*)record, copy, Offsets.Runtime.ReadRecord.Size,
                              Offsets.Runtime.ReadRecord.Size);
            Hooks.CompleteRead((IntPtr)copy, 0);
            return true;
        }
    }
}
