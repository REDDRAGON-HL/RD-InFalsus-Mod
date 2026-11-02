using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static class FontMerge
    {
        internal static byte[] Append(byte[] baseFont, byte[] patch)
        {
            try
            {
                return AppendInner(baseFont, patch);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private sealed class Table
        {
            internal uint Tag;
            internal int Offset;
            internal int Length;
        }

        private static Dictionary<uint, Table> ReadTables(byte[] font)
        {
            uint version = U32(font, 0);
            if (version != 0x00010000 && version != 0x74727565) return null;
            int count = U16(font, 4);
            var tables = new Dictionary<uint, Table>(count);

            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (record + 16 > font.Length) return null;

                var table = new Table
                {
                    Tag = U32(font, record),
                    Offset = (int)U32(font, record + 8),
                    Length = (int)U32(font, record + 12),
                };

                if (table.Offset < 0 || table.Length < 0 ||
                    (long)table.Offset + table.Length > font.Length) return null;
                tables[table.Tag] = table;
            }

            return tables;
        }

        private static byte[] AppendInner(byte[] baseFont, byte[] patch)
        {
            var baseTables = ReadTables(baseFont);
            var patchTables = ReadTables(patch);
            if (baseTables == null || patchTables == null) return null;
            byte[] head = Slice(baseFont, baseTables, Tag("head"));
            byte[] maxp = Slice(baseFont, baseTables, Tag("maxp"));
            byte[] hhea = Slice(baseFont, baseTables, Tag("hhea"));
            byte[] glyf = Slice(baseFont, baseTables, Tag("glyf"));
            byte[] loca = Slice(baseFont, baseTables, Tag("loca"));
            byte[] hmtx = Slice(baseFont, baseTables, Tag("hmtx"));
            byte[] cmap = Slice(baseFont, baseTables, Tag("cmap"));
            if (head == null || maxp == null || hhea == null || glyf == null ||
                loca == null || hmtx == null || cmap == null) return null;
            int baseGlyphs = (int)U16(maxp, 4);
            bool baseLongLoca = U16(head, 50) != 0;
            int baseMetrics = U16(hhea, 34);
            if (baseGlyphs <= 0 || baseMetrics <= 0 || baseMetrics > baseGlyphs) return null;
            var offsets = new int[baseGlyphs + 1];
            for (int i = 0; i <= baseGlyphs; i++)
                offsets[i] = baseLongLoca ? (int)U32(loca, i * 4) : U16(loca, i * 2) * 2;
            var chars = new SortedDictionary<int, int>();
            foreach (var pair in ReadCmap(cmap)) chars[pair.Key] = pair.Value;
            var metrics = new List<(int advance, int lsb)>(baseGlyphs);
            int lastAdvance = 0;
            for (int gid = 0; gid < baseGlyphs; gid++)
            {
                if (gid < baseMetrics)
                {
                    lastAdvance = U16(hmtx, gid * 4);
                    metrics.Add((lastAdvance, (short)U16(hmtx, gid * 4 + 2)));
                }
                else
                {
                    int lsb = U16(hmtx, baseMetrics * 4 + (gid - baseMetrics) * 2);
                    metrics.Add((lastAdvance, (short)lsb));
                }
            }

            byte[] patchMaxp = Slice(patch, patchTables, Tag("maxp"));
            byte[] patchHhea = Slice(patch, patchTables, Tag("hhea"));
            byte[] patchHmtx = Slice(patch, patchTables, Tag("hmtx"));
            byte[] patchGlyf = Slice(patch, patchTables, Tag("glyf"));
            byte[] patchLoca = Slice(patch, patchTables, Tag("loca"));
            byte[] patchHead = Slice(patch, patchTables, Tag("head"));
            byte[] patchCmap = Slice(patch, patchTables, Tag("cmap"));
            if (patchMaxp == null || patchHhea == null || patchHmtx == null ||
                patchGlyf == null || patchLoca == null || patchHead == null ||
                patchCmap == null) return null;
            int patchGlyphs = (int)U16(patchMaxp, 4);
            bool patchLongLoca = U16(patchHead, 50) != 0;
            int patchMetrics = U16(patchHhea, 34);
            var patchOffsets = new int[patchGlyphs + 1];
            for (int i = 0; i <= patchGlyphs; i++)
                patchOffsets[i] = patchLongLoca ? (int)U32(patchLoca, i * 4) : U16(patchLoca, i * 2) * 2;
            var data = new List<byte>(glyf);
            var appended = new List<(int offset, int length, int advance, int lsb)>();

            foreach (var pair in ReadCmap(patchCmap))
            {
                int codepoint = pair.Key;
                int gid = pair.Value;
                if (chars.ContainsKey(codepoint) || gid <= 0 || gid >= patchGlyphs) continue;
                int start = patchOffsets[gid], end = patchOffsets[gid + 1];
                if (end <= start) continue;
                while ((data.Count & 3) != 0) data.Add(0);
                int newOffset = data.Count;
                for (int i = start; i < end; i++) data.Add(patchGlyf[i]);
                int advance;
                if (gid < patchMetrics) advance = U16(patchHmtx, gid * 4);
                else if (patchMetrics > 0) advance = U16(patchHmtx, (patchMetrics - 1) * 4);
                else advance = 0;
                int lsb = (short)U16(patchGlyf, start + 2);
                metrics.Add((advance, lsb));
                appended.Add((newOffset, end - start, advance, lsb));
                chars[codepoint] = baseGlyphs + appended.Count - 1;
            }

            if (appended.Count == 0) return baseFont;
            var newOffsets = new int[baseGlyphs + appended.Count + 1];
            Array.Copy(offsets, newOffsets, offsets.Length);
            for (int i = 0; i < appended.Count; i++)
                newOffsets[baseGlyphs + i] = appended[i].offset;
            int lastAppended = appended.Count - 1;
            newOffsets[baseGlyphs + appended.Count] =
                appended[lastAppended].offset + appended[lastAppended].length;
            byte[] newLoca = new byte[newOffsets.Length * 4];
            for (int i = 0; i < newOffsets.Length; i++) WriteU32(newLoca, i * 4, (uint)newOffsets[i]);
            byte[] newHmtx = new byte[metrics.Count * 4];
            for (int i = 0; i < metrics.Count; i++)
            {
                WriteU16(newHmtx, i * 4, (ushort)metrics[i].advance);
                WriteU16(newHmtx, i * 4 + 2, (ushort)(short)metrics[i].lsb);
            }

            byte[] newMaxp = (byte[])maxp.Clone();
            WriteU16(newMaxp, 4, (ushort)metrics.Count);
            byte[] newHhea = (byte[])hhea.Clone();
            WriteU16(newHhea, 34, (ushort)metrics.Count);
            byte[] newHead = (byte[])head.Clone();
            WriteU16(newHead, 50, 1);
            WriteU32(newHead, 8, 0);
            byte[] newCmap = BuildCmap(chars);
            if (newCmap == null) return null;
            var tables = new Dictionary<uint, byte[]>();
            foreach (var pair in baseTables)
            {
                uint tag = pair.Key;
                if (tag == Tag("head") || tag == Tag("loca") || tag == Tag("glyf") ||
                    tag == Tag("hmtx") || tag == Tag("maxp") || tag == Tag("hhea") ||
                    tag == Tag("cmap") || tag == Tag("vhea") || tag == Tag("vmtx") ||
                    tag == Tag("DSIG")) continue;
                tables[tag] = Slice(baseFont, pair.Value.Offset, pair.Value.Length);
            }

            tables[Tag("glyf")] = data.ToArray();
            tables[Tag("loca")] = newLoca;
            tables[Tag("hmtx")] = newHmtx;
            tables[Tag("maxp")] = newMaxp;
            tables[Tag("hhea")] = newHhea;
            tables[Tag("head")] = newHead;
            tables[Tag("cmap")] = newCmap;
            return Assemble(tables);
        }

        private static Dictionary<int, int> ReadCmap(byte[] cmap)
        {
            var map = new Dictionary<int, int>();
            int count = U16(cmap, 2);

            for (int i = 0; i < count; i++)
            {
                int record = 4 + i * 8;
                int platform = U16(cmap, record);
                int encoding = U16(cmap, record + 2);
                int offset = (int)U32(cmap, record + 4);
                if (offset + 2 > cmap.Length) continue;
                int format = U16(cmap, offset);
                if (format == 4 && (platform == 3 && encoding == 1 || platform == 0))
                {
                    ReadFormat4(cmap, offset, map);
                    if (map.Count > 0) return map;
                }
            }

            return map;
        }

        private static void ReadFormat4(byte[] cmap, int table, Dictionary<int, int> map)
        {
            int segments = U16(cmap, table + 6) / 2;
            int ends = table + 14;
            int starts = ends + segments * 2 + 2;
            int deltas = starts + segments * 2;
            int ranges = deltas + segments * 2;
            if (segments <= 0 || ranges + segments * 2 > cmap.Length) return;

            for (int i = 0; i < segments; i++)
            {
                int end = U16(cmap, ends + i * 2);
                int start = U16(cmap, starts + i * 2);
                int delta = (short)U16(cmap, deltas + i * 2);
                int range = U16(cmap, ranges + i * 2);
                if (start > end) continue;

                for (int cp = start; cp <= end && cp != 0x10000; cp++)
                {
                    int gid;
                    if (range == 0)
                    {
                        gid = (cp + delta) & 0xFFFF;
                    }
                    else
                    {
                        int at = ranges + i * 2 + range + (cp - start) * 2;
                        if (at + 2 > cmap.Length) continue;
                        gid = U16(cmap, at);
                        if (gid != 0) gid = (gid + delta) & 0xFFFF;
                    }

                    if (gid != 0 && !map.ContainsKey(cp)) map[cp] = gid;
                }
            }
        }

        private static byte[] BuildCmap(SortedDictionary<int, int> chars)
        {
            var pairs = new List<KeyValuePair<int, int>>();
            foreach (var pair in chars)
                if (pair.Key >= 0 && pair.Key < 0xFFFF) pairs.Add(pair);
            int segments = pairs.Count + 1;
            int endCodes = 14;
            int startCodes = endCodes + segments * 2 + 2;
            int deltas = startCodes + segments * 2;
            int ranges = deltas + segments * 2;
            int length = ranges + segments * 2;
            byte[] table = new byte[length];
            WriteU16(table, 0, 4);
            WriteU16(table, 2, (ushort)length);
            WriteU16(table, 4, 0);
            WriteU16(table, 6, (ushort)(segments * 2));
            int power = 1;
            int selector = 0;
            while (power * 2 <= segments) { power *= 2; selector++; }
            WriteU16(table, 8, (ushort)(power * 2));
            WriteU16(table, 10, (ushort)selector);
            WriteU16(table, 12, (ushort)(segments * 2 - power * 2));

            for (int i = 0; i < pairs.Count; i++)
            {
                int cp = pairs[i].Key;
                int gid = pairs[i].Value;
                WriteU16(table, endCodes + i * 2, (ushort)cp);
                WriteU16(table, startCodes + i * 2, (ushort)cp);
                WriteU16(table, deltas + i * 2, (ushort)((gid - cp) & 0xFFFF));
                WriteU16(table, ranges + i * 2, 0);
            }

            int last = pairs.Count;
            WriteU16(table, endCodes + last * 2, 0xFFFF);
            WriteU16(table, startCodes + last * 2, 0xFFFF);
            WriteU16(table, deltas + last * 2, 1);
            WriteU16(table, ranges + last * 2, 0);
            WriteU16(table, endCodes + last * 2 + 2, 0);
            const int subtableOffset = 4 + 2 * 8;
            byte[] cmap = new byte[subtableOffset + length];
            WriteU16(cmap, 0, 0);
            WriteU16(cmap, 2, 2);
            WriteU16(cmap, 4, 0);
            WriteU16(cmap, 6, 3);
            WriteU32(cmap, 8, subtableOffset);
            WriteU16(cmap, 12, 3);
            WriteU16(cmap, 14, 1);
            WriteU32(cmap, 16, subtableOffset);
            Array.Copy(table, 0, cmap, subtableOffset, length);
            return cmap;
        }

        private static byte[] Assemble(Dictionary<uint, byte[]> tables)
        {
            var tags = new List<uint>(tables.Keys);
            tags.Sort();
            int count = tags.Count;
            int power = 1, selector = 0;
            while (power * 2 <= count) { power *= 2; selector++; }

            int offset = 12 + count * 16;
            var offsets = new int[count];
            var lengths = new int[count];
            for (int i = 0; i < count; i++)
            {
                lengths[i] = tables[tags[i]].Length;
                offsets[i] = offset;
                offset += (lengths[i] + 3) & ~3;
            }

            byte[] font = new byte[offset];
            WriteU32(font, 0, 0x00010000);
            WriteU16(font, 4, (ushort)count);
            WriteU16(font, 6, (ushort)(power * 16));
            WriteU16(font, 8, (ushort)selector);
            WriteU16(font, 10, (ushort)(count * 16 - power * 16));

            for (int i = 0; i < count; i++)
            {
                byte[] data = tables[tags[i]];
                int record = 12 + i * 16;
                WriteU32(font, record, tags[i]);
                WriteU32(font, record + 4, Checksum(data));
                WriteU32(font, record + 8, (uint)offsets[i]);
                WriteU32(font, record + 12, (uint)lengths[i]);
                Array.Copy(data, 0, font, offsets[i], data.Length);
            }

            int head = -1;
            for (int i = 0; i < count; i++) if (tags[i] == Tag("head")) head = offsets[i];
            if (head >= 0) WriteU32(font, head + 8, 0xB1B0AFBA - Checksum(font));
            return font;
        }

        private static uint Checksum(byte[] data)
        {
            uint sum = 0;
            for (int i = 0; i < data.Length; i += 4)
            {
                uint word = 0;
                for (int b = 0; b < 4; b++) word = (word << 8) | (i + b < data.Length ? data[i + b] : 0u);
                sum += word;
            }
            return sum;
        }

        private static uint Tag(string text)
        {
            uint tag = 0;
            for (int i = 0; i < 4; i++) tag = (tag << 8) | text[i];
            return tag;
        }

        private static byte[] Slice(byte[] data, Dictionary<uint, Table> tables, uint tag)
        {
            return tables.TryGetValue(tag, out Table table)
                ? Slice(data, table.Offset, table.Length)
                : null;
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            byte[] slice = new byte[length];
            Array.Copy(data, offset, slice, 0, length);
            return slice;
        }

        private static int U16(byte[] data, int offset) =>
            data[offset] << 8 | data[offset + 1];

        private static uint U32(byte[] data, int offset) =>
            (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

        private static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        private static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
