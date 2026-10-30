using System;

namespace CustomSong
{
    internal static class Jpeg
    {
        internal static bool Looks(byte[] bytes) =>
            bytes != null && bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;

        internal static Png.Image Decode(byte[] bytes, out string reason) =>
            new Reader(bytes).Run(out reason);

        internal static readonly int[] ZigZag =
        {
             0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        };

        private sealed class Component
        {
            internal int Id, H, V, Tq;
            internal int DcTable, AcTable;
            internal int DcPredictor;
            internal int BlocksX, BlocksY;
            internal int[] Coefficients;
            internal byte[] Samples;
        }

        private sealed class Huffman
        {
            internal readonly int[] Counts = new int[17];
            internal int[] Symbols;
        }

        private sealed class Reader
        {
            private readonly byte[] _data;
            private int _at;

            internal Reader(byte[] data) => _data = data;

            private int _width, _height;
            private int _maxH = 1, _maxV = 1;
            private int _restartInterval;
            private bool _progressive;
            private bool _rgb;

            private Component[] _parts = Array.Empty<Component>();
            private Component _alone;

            private readonly int[][] _quant = new int[4][];
            private readonly Huffman[] _dcTables = new Huffman[4];
            private readonly Huffman[] _acTables = new Huffman[4];

            private bool _done;
            private string _failure;
            private int _blocks;

            private int _bits;
            private uint _buffer;

            internal Png.Image Run(out string reason)
            {
                reason = null;
                _at = 2;

                while (!_done)
                {
                    int marker = NextMarker();
                    if (marker < 0 || marker == 0xD9)
                    {
                        _done = true;
                        break;
                    }

                    switch (marker)
                    {
                        case 0xD8:
                        case 0x01:
                        case 0xD0: case 0xD1: case 0xD2: case 0xD3:
                        case 0xD4: case 0xD5: case 0xD6: case 0xD7:
                            break;

                        case 0xDB: ReadQuantisation(); break;
                        case 0xC4: ReadHuffman(); break;
                        case 0xC0: case 0xC1: ReadFrame(false); break;
                        case 0xC2: ReadFrame(true); break;
                        case 0xDD: _restartInterval = ReadU16(); break;
                        case 0xEE: ReadAdobe(); break;
                        case 0xDA: ReadScan(); break;

                        case 0xC3: case 0xC5: case 0xC6: case 0xC7:
                        case 0xC9: case 0xCA: case 0xCB: case 0xCD:
                        case 0xCE: case 0xCF:
                            reason = "lossless and arithmetic-coded JPEGs are not supported";
                            return null;

                        default: SkipSegment(); break;
                    }

                    if (_failure != null)
                    {
                        reason = _failure;
                        return null;
                    }
                }

                if (_parts.Length == 0 || _width <= 0 || _height <= 0)
                {
                    reason = "the file has no frame header";
                    return null;
                }

                return Compose(out reason);
            }

            private int NextMarker()
            {
                while (_at + 1 < _data.Length)
                {
                    if (_data[_at] != 0xFF) { _at++; continue; }

                    while (_at + 1 < _data.Length && _data[_at + 1] == 0xFF) _at++;

                    int marker = _data[_at + 1];
                    _at += 2;
                    return marker;
                }

                return -1;
            }

            private int ReadU16()
            {
                int value = (_data[_at] << 8) | _data[_at + 1];
                _at += 2;
                return value;
            }

            private void SkipSegment()
            {
                int length = ReadU16();
                if (length < 2) { _failure = "a header segment has no length"; return; }
                _at += length - 2;
            }

            private void ReadQuantisation()
            {
                int length = ReadU16();
                int end = _at + length - 2;

                while (_at < end)
                {
                    int spec = _data[_at++];
                    int precision = spec >> 4;
                    int table = spec & 15;
                    if (table > 3) { _failure = "a quantisation table is numbered past 3"; return; }

                    var values = new int[64];
                    for (int i = 0; i < 64; i++)
                        values[ZigZag[i]] = precision == 0
                            ? _data[_at + i]
                            : (_data[_at + i * 2] << 8) | _data[_at + i * 2 + 1];

                    _at += precision == 0 ? 64 : 128;
                    _quant[table] = values;
                }
            }

            private void ReadHuffman()
            {
                int length = ReadU16();
                int end = _at + length - 2;

                while (_at < end)
                {
                    int spec = _data[_at++];
                    bool ac = (spec >> 4) != 0;
                    int table = spec & 15;
                    if (table > 3) { _failure = "a Huffman table is numbered past 3"; return; }

                    var huffman = new Huffman();
                    int total = 0;

                    for (int i = 1; i <= 16; i++)
                    {
                        huffman.Counts[i] = _data[_at++];
                        total += huffman.Counts[i];
                    }

                    if (total > 256) { _failure = "a Huffman table has more than 256 codes"; return; }

                    huffman.Symbols = new int[total];
                    for (int i = 0; i < total; i++) huffman.Symbols[i] = _data[_at++];

                    if (ac) _acTables[table] = huffman;
                    else _dcTables[table] = huffman;
                }
            }

            private void ReadFrame(bool progressive)
            {
                int length = ReadU16();
                int end = _at + length - 2;
                _progressive = progressive;

                int precision = _data[_at++];
                if (precision != 8)
                {
                    _failure = $"{precision}-bit samples are not supported";
                    return;
                }

                _height = ReadU16();
                _width = ReadU16();
                int count = _data[_at++];

                if (count < 1 || count > 3)
                {
                    _failure = count == 4
                        ? "CMYK JPEGs are not supported"
                        : $"{count} colour planes is not supported";
                    return;
                }

                _parts = new Component[count];
                _maxH = 1;
                _maxV = 1;

                for (int i = 0; i < count; i++)
                {
                    var part = new Component
                    {
                        Id = _data[_at],
                        H = _data[_at + 1] >> 4,
                        V = _data[_at + 1] & 15,
                        Tq = _data[_at + 2],
                    };

                    _at += 3;

                    if (part.H < 1 || part.H > 4 || part.V < 1 || part.V > 4)
                    {
                        _failure = "a colour plane has an impossible sampling factor";
                        return;
                    }

                    _maxH = Math.Max(_maxH, part.H);
                    _maxV = Math.Max(_maxV, part.V);
                    _parts[i] = part;
                }

                foreach (Component part in _parts)
                {
                    part.BlocksX = (_width * part.H + _maxH * 8 - 1) / (_maxH * 8);
                    part.BlocksY = (_height * part.V + _maxV * 8 - 1) / (_maxV * 8);
                    part.Coefficients = new int[part.BlocksX * part.BlocksY * 64];
                    part.Samples = new byte[part.BlocksX * 8 * part.BlocksY * 8];
                }

                _at = end;
            }

            private void ReadAdobe()
            {
                int length = ReadU16();
                int end = _at + length - 2;

                if (length >= 14 && _data[_at] == 'A' && _data[_at + 1] == 'd' &&
                    _data[_at + 2] == 'o' && _data[_at + 3] == 'b' && _data[_at + 4] == 'e')
                {
                    _rgb = _data[_at + 11] == 0;
                }

                _at = end;
            }

            private void ReadScan()
            {
                int length = ReadU16();
                int end = _at + length - 2;
                int count = _data[_at++];

                var scan = new Component[count];

                for (int i = 0; i < count; i++)
                {
                    int id = _data[_at];
                    int tables = _data[_at + 1];
                    _at += 2;

                    Component part = null;
                    foreach (Component candidate in _parts)
                        if (candidate.Id == id) { part = candidate; break; }

                    if (part == null)
                    {
                        _failure = "a scan names a colour plane the frame does not have";
                        return;
                    }

                    part.DcTable = tables >> 4;
                    part.AcTable = tables & 15;
                    if (part.DcTable > 3 || part.AcTable > 3)
                    {
                        _failure = "a scan names a Huffman table past 3";
                        return;
                    }

                    scan[i] = part;
                }

                int startBand = 0, endBand = 63, high = 0, low = 0;

                if (_progressive)
                {
                    startBand = _data[_at];
                    endBand = _data[_at + 1];
                    high = _data[_at + 2] >> 4;
                    low = _data[_at + 2] & 15;
                    _at += 3;
                }

                _at = end;
                _alone = count == 1 ? scan[0] : null;
                _bits = 0;

                foreach (Component part in scan) part.DcPredictor = 0;

                DecodeScan(scan, startBand, endBand, high, low);
            }

            private void DecodeScan(Component[] scan, int startBand, int endBand, int high, int low)
            {
                int eobRun = 0;
                int across, down;

                if (_alone != null)
                {
                    across = _alone.BlocksX;
                    down = _alone.BlocksY;
                }
                else
                {
                    across = (_width + _maxH * 8 - 1) / (_maxH * 8);
                    down = (_height + _maxV * 8 - 1) / (_maxV * 8);
                }

                for (int my = 0; my < down; my++)
                {
                    for (int mx = 0; mx < across; mx++)
                    {
                        if (_alone != null)
                        {
                            DecodeBlock(_alone, mx, my, startBand, endBand, high, low, ref eobRun);
                        }
                        else
                        {
                            foreach (Component part in scan)
                            {
                                for (int v = 0; v < part.V; v++)
                                    for (int h = 0; h < part.H; h++)
                                    {
                                        int bx = mx * part.H + h;
                                        int by = my * part.V + v;
                                        if (bx >= part.BlocksX || by >= part.BlocksY) continue;

                                        DecodeBlock(part, bx, by, startBand, endBand, high, low, ref eobRun);
                                    }
                            }
                        }

                        if (_failure != null) return;

                        if (_restartInterval > 0 && (my * across + mx + 1) % _restartInterval == 0)
                        {
                            eobRun = 0;
                            _bits = 0;

                            int marker = NextMarker();
                            if (marker < 0xD0 || marker > 0xD7)
                            {
                                _failure = "a restart marker is missing";
                                return;
                            }

                            foreach (Component part in _parts) part.DcPredictor = 0;
                        }
                    }
                }
            }

            private void DecodeBlock(Component part, int bx, int by, int startBand, int endBand,
                                     int high, int low, ref int eobRun)
            {
                _blocks++;
                int offset = (by * part.BlocksX + bx) * 64;

                if (!_progressive)
                {
                    DecodeBaseline(part, offset);
                    return;
                }

                if (startBand == 0)
                {
                    if (high == 0)
                    {
                        int category = HuffmanDecode(_dcTables[part.DcTable]);
                        int count = ReadBits(category);
                        part.DcPredictor += category == 0 ? 0 : Extend(count, category);
                        part.Coefficients[offset] = part.DcPredictor << low;
                    }
                    else if (GetBit() != 0)
                    {
                        part.Coefficients[offset] += 1 << low;
                    }

                    return;
                }

                if (high == 0) DecodeAcFirst(part, offset, startBand, endBand, low, ref eobRun);
                else DecodeAcRefine(part, offset, startBand, endBand, low, ref eobRun);
            }

            private void DecodeBaseline(Component part, int offset)
            {
                int category = HuffmanDecode(_dcTables[part.DcTable]);
                int count = ReadBits(category);
                part.DcPredictor += category == 0 ? 0 : Extend(count, category);
                part.Coefficients[offset] = part.DcPredictor;

                for (int k = 1; k < 64; k++)
                {
                    int ac = HuffmanDecode(_acTables[part.AcTable]);
                    int run = ac >> 4;
                    int size = ac & 15;

                    if (size == 0)
                    {
                        if (run != 15) return;
                        k += 15;
                        continue;
                    }

                    k += run;
                    if (k > 63) { _failure = "a block runs past its 64 coefficients"; return; }

                    part.Coefficients[offset + ZigZag[k]] = Extend(ReadBits(size), size);
                }
            }

            private void DecodeAcFirst(Component part, int offset, int startBand, int endBand,
                                       int low, ref int eobRun)
            {
                if (eobRun > 0) { eobRun--; return; }

                for (int k = startBand; k <= endBand; k++)
                {
                    int ac = HuffmanDecode(_acTables[part.AcTable]);
                    int run = ac >> 4;
                    int size = ac & 15;

                    if (size == 0)
                    {
                        if (run < 15)
                        {
                            eobRun = (1 << run) + ReadBits(run) - 1;
                            return;
                        }

                        k += 15;
                        continue;
                    }

                    k += run;
                    if (k > endBand) { _failure = "a block runs past the band it is in"; return; }

                    part.Coefficients[offset + ZigZag[k]] = Extend(ReadBits(size), size) << low;
                }
            }

            private void DecodeAcRefine(Component part, int offset, int startBand, int endBand,
                                        int low, ref int eobRun)
            {
                int[] coefficients = part.Coefficients;
                int step = 1 << low;
                int k = startBand;

                if (eobRun > 0)
                {
                    Refine(coefficients, offset, startBand, endBand, step);
                    eobRun--;
                    return;
                }

                while (k <= endBand)
                {
                    int ac = HuffmanDecode(_acTables[part.AcTable]);
                    int run = ac >> 4;
                    int size = ac & 15;
                    int value = 0;

                    if (size == 0)
                    {
                        if (run < 15)
                        {
                            eobRun = (1 << run) + ReadBits(run);
                            break;
                        }
                    }
                    else
                    {
                        if (size != 1)
                        {
                            _failure = "a refinement scan has a bad coefficient size";
                            return;
                        }

                        value = GetBit() != 0 ? step : -step;
                    }

                    while (k <= endBand)
                    {
                        int natural = ZigZag[k];

                        if (coefficients[offset + natural] != 0)
                        {
                            RefineOne(coefficients, offset + natural, step);
                        }
                        else
                        {
                            if (run == 0) break;
                            run--;
                        }

                        k++;
                    }

                    if (k > endBand) break;

                    coefficients[offset + ZigZag[k]] = value;
                    k++;
                }

                if (eobRun > 0)
                {
                    Refine(coefficients, offset, k, endBand, step);
                    eobRun--;
                }
            }

            private void Refine(int[] coefficients, int offset, int from, int to, int step)
            {
                for (int k = from; k <= to; k++)
                {
                    int natural = ZigZag[k];
                    if (coefficients[offset + natural] != 0) RefineOne(coefficients, offset + natural, step);
                }
            }

            private void RefineOne(int[] coefficients, int at, int step)
            {
                if (GetBit() == 0) return;

                int value = coefficients[at];
                int magnitude = value < 0 ? -value : value;
                if ((magnitude & step) != 0) return;

                coefficients[at] = value > 0 ? value + step : value - step;
            }

            private int GetBit()
            {
                if (_bits == 0)
                {
                    int value = NextByte();
                    _buffer = value < 0 ? 0u : (uint)value;
                    _bits = 8;
                }

                _bits--;
                return (int)((_buffer >> _bits) & 1);
            }

            private int ReadBits(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++) value = (value << 1) | GetBit();
                return value;
            }

            private int NextByte()
            {
                if (_at >= _data.Length) return -1;

                int value = _data[_at];

                if (value == 0xFF)
                {
                    int next = _at + 1 < _data.Length ? _data[_at + 1] : 0;
                    if (next == 0x00) { _at += 2; return 0xFF; }
                    return -1;
                }

                _at++;
                return value;
            }

            private int HuffmanDecode(Huffman table)
            {
                if (table == null)
                {
                    _failure = "a scan uses a Huffman table that was never defined";
                    return 0;
                }

                int code = 0;
                int first = 0;
                int index = 0;

                for (int length = 1; length <= 16; length++)
                {
                    code |= GetBit();
                    int count = table.Counts[length];

                    if (code - first < count) return table.Symbols[index + (code - first)];

                    index += count;
                    first = (first + count) << 1;
                    code <<= 1;
                }

                _failure = $"a Huffman code ran past 16 bits (block {_blocks}, at 0x{_at:X})";
                return 0;
            }

            private static int Extend(int value, int count) =>
                value < (1 << (count - 1)) ? value + (-1 << count) + 1 : value;

            private Png.Image Compose(out string reason)
            {
                reason = null;

                foreach (Component part in _parts) Reconstruct(part);

                int width = _width;
                int height = _height;
                var rgba = new byte[width * height * 4];

                bool three = _parts.Length >= 3;
                Component luma = _parts[0];
                Component blue = three ? _parts[1] : null;
                Component red = three ? _parts[2] : null;

                for (int py = 0; py < height; py++)
                {
                    for (int px = 0; px < width; px++)
                    {
                        int at = (py * width + px) * 4;
                        int y = Sample(luma, px, py);

                        if (!three)
                        {
                            rgba[at] = rgba[at + 1] = rgba[at + 2] = (byte)y;
                            rgba[at + 3] = 255;
                            continue;
                        }

                        int b = Sample(blue, px, py);
                        int r = Sample(red, px, py);

                        if (_rgb)
                        {
                            rgba[at] = (byte)y;
                            rgba[at + 1] = (byte)b;
                            rgba[at + 2] = (byte)r;
                        }
                        else
                        {
                            int cb = b - 128;
                            int cr = r - 128;

                            rgba[at] = Clamp(y + 1.402 * cr);
                            rgba[at + 1] = Clamp(y - 0.344136 * cb - 0.714136 * cr);
                            rgba[at + 2] = Clamp(y + 1.772 * cb);
                        }

                        rgba[at + 3] = 255;
                    }
                }

                return new Png.Image { Width = width, Height = height, Rgba = rgba };
            }

            private static byte Clamp(double value) =>
                (byte)(value < 0 ? 0 : value > 255 ? 255 : (int)(value + 0.5));

            private byte Sample(Component part, int x, int y)
            {
                int stride = part.BlocksX * 8;
                int width = part.BlocksX * 8;
                int height = part.BlocksY * 8;

                if (part.H == _maxH && part.V == _maxV)
                {
                    int sx0 = x < width ? x : width - 1;
                    int sy0 = y < height ? y : height - 1;
                    return part.Samples[sy0 * stride + sx0];
                }

                double fx = (x + 0.5) * part.H / _maxH - 0.5;
                double fy = (y + 0.5) * part.V / _maxV - 0.5;

                int x0 = (int)Math.Floor(fx);
                int y0 = (int)Math.Floor(fy);
                double wx = fx - x0;
                double wy = fy - y0;

                int x1 = ClampIndex(x0 + 1, width);
                int y1 = ClampIndex(y0 + 1, height);
                x0 = ClampIndex(x0, width);
                y0 = ClampIndex(y0, height);

                double top = part.Samples[y0 * stride + x0] * (1 - wx) + part.Samples[y0 * stride + x1] * wx;
                double bottom = part.Samples[y1 * stride + x0] * (1 - wx) + part.Samples[y1 * stride + x1] * wx;

                return (byte)(top * (1 - wy) + bottom * wy + 0.5);
            }

            private static int ClampIndex(int value, int limit) =>
                value < 0 ? 0 : value >= limit ? limit - 1 : value;

            private static readonly double[][] Basis = BuildBasis();

            private static double[][] BuildBasis()
            {
                var table = new double[8][];

                for (int v = 0; v < 8; v++)
                {
                    table[v] = new double[8];
                    double scale = v == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;

                    for (int u = 0; u < 8; u++)
                        table[v][u] = scale * Math.Cos((2 * u + 1) * v * Math.PI / 16.0) / 2.0;
                }

                return table;
            }

            private void Reconstruct(Component part)
            {
                int stride = part.BlocksX * 8;
                int[] quant = _quant[part.Tq] ?? _quant[0];

                var block = new double[64];
                var pass = new double[64];

                for (int by = 0; by < part.BlocksY; by++)
                {
                    for (int bx = 0; bx < part.BlocksX; bx++)
                    {
                        int offset = (by * part.BlocksX + bx) * 64;

                        for (int i = 0; i < 64; i++)
                            block[i] = quant == null ? part.Coefficients[offset + i]
                                                     : part.Coefficients[offset + i] * quant[i];

                        for (int row = 0; row < 8; row++)
                        {
                            for (int u = 0; u < 8; u++)
                            {
                                double sum = 0;
                                for (int v = 0; v < 8; v++) sum += Basis[v][u] * block[row * 8 + v];
                                pass[row * 8 + u] = sum;
                            }
                        }

                        for (int column = 0; column < 8; column++)
                        {
                            for (int u = 0; u < 8; u++)
                            {
                                double sum = 0;
                                for (int v = 0; v < 8; v++) sum += Basis[v][u] * pass[v * 8 + column];

                                int value = (int)(sum + 128.5);
                                part.Samples[(by * 8 + u) * stride + bx * 8 + column] =
                                    (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
                            }
                        }
                    }
                }
            }
        }
    }
}
