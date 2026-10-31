using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

#pragma warning disable CA1416

namespace CustomSong
{
    internal static class Mp3Decoder
    {
        internal static bool IsMp3(string path) =>
            string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase);

        internal static int Measure(string path, out string reason)
        {
            reason = null;

            try
            {
                using Session session = Session.Open(path, out reason);
                if (session == null) return -1;
                long bytes = session.DataBytes;
                if (bytes <= 0)
                {
                    reason = $"{Path.GetFileName(path)} decoded to no samples";
                    return -1;
                }

                return (int)Math.Min(bytes + WavHeaderSize, int.MaxValue);
            }
            catch (Exception e)
            {
                reason = $"{Path.GetFileName(path)} could not be measured: " + ModLog.Describe(e);
                return -1;
            }
        }

        internal static byte[] Decode(string path, out string reason)
        {
            reason = null;

            try
            {
                using Session session = Session.Open(path, out reason);
                if (session == null) return null;
                byte[] pcm = session.ReadAll(out reason);
                if (pcm == null) return null;
                return Wrap(pcm, session);
            }
            catch (Exception e)
            {
                reason = $"{Path.GetFileName(path)} could not be decoded: " + ModLog.Describe(e);
                return null;
            }
        }

        private static byte[] Wrap(byte[] pcm, Session session)
        {
            int data = (int)session.DataBytes;
            if (pcm.Length < data) Array.Resize(ref pcm, data);
            var wav = new byte[WavHeaderSize + data];
            WriteAscii(wav, 0, "RIFF");
            WriteI32(wav, 4, wav.Length - 8);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteI32(wav, 16, 16);
            WriteI16(wav, 20, 1);
            WriteI16(wav, 22, session.Channels);
            WriteI32(wav, 24, session.SampleRate);
            WriteI32(wav, 28, session.SampleRate * session.BlockAlign);
            WriteI16(wav, 32, session.BlockAlign);
            WriteI16(wav, 34, session.Bits);
            WriteAscii(wav, 36, "data");
            WriteI32(wav, 40, data);
            Buffer.BlockCopy(pcm, 0, wav, WavHeaderSize, data);
            return wav;
        }

        private const int WavHeaderSize = 44;

        private static void WriteAscii(byte[] at, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++) at[offset + i] = (byte)text[i];
        }

        private static void WriteI32(byte[] at, int offset, int value)
        {
            at[offset] = (byte)value;
            at[offset + 1] = (byte)(value >> 8);
            at[offset + 2] = (byte)(value >> 16);
            at[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteI16(byte[] at, int offset, int value)
        {
            at[offset] = (byte)value;
            at[offset + 1] = (byte)(value >> 8);
        }

        private sealed class Session : IDisposable
        {
            internal int SampleRate, Channels, Bits, BlockAlign;
            private IMFSourceReader _reader;

            internal long DataBytes
            {
                get
                {
                    long blocks = Duration100ns * SampleRate / 10_000_000L;
                    return blocks * BlockAlign;
                }
            }

            internal long Duration100ns { get; private set; }

            internal static Session Open(string path, out string reason)
            {
                reason = null;

                try
                {
                    Media.EnsureStarted();
                    int hr = MFCreateSourceReaderFromURL(path, IntPtr.Zero, out IMFSourceReader reader);
                    if (hr < 0)
                    {
                        reason = $"{Path.GetFileName(path)}: Media Foundation would not open it (0x{hr:X8})";
                        return null;
                    }

                    var session = new Session { _reader = reader };
                    if (!session.Configure(out reason))
                    {
                        session.Dispose();
                        return null;
                    }
                    return session;
                }
                catch (Exception e)
                {
                    reason = $"{Path.GetFileName(path)}: Media Foundation failed: " + ModLog.Describe(e);
                    return null;
                }
            }

            private bool Configure(out string reason)
            {
                reason = null;

                if (MFCreateMediaType(out IMFMediaType wanted) < 0)
                {
                    reason = "Media Foundation could not create a media type";
                    return false;
                }

                try
                {
                    Guid major = MFMediaType_Audio, sub = MFAudioFormat_PCM;
                    Guid majorKey = MF_MT_MAJOR_TYPE, subKey = MF_MT_SUBTYPE;
                    wanted.SetGUID(ref majorKey, ref major);
                    wanted.SetGUID(ref subKey, ref sub);
                    int hr = _reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, wanted);
                    if (hr < 0)
                    {
                        reason = $"the PCM output type was refused (0x{hr:X8})";
                        return false;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(wanted);
                }

                if (_reader.GetCurrentMediaType(FirstAudio, out IMFMediaType current) < 0 || current == null)
                {
                    reason = "Media Foundation reported no output type";
                    return false;
                }

                try
                {
                    uint channels, rate, bits, align;
                    Guid rateKey = MF_MT_AUDIO_SAMPLES_PER_SECOND;
                    current.GetUINT32(ref rateKey, out rate);
                    Guid bitsKey = MF_MT_AUDIO_BITS_PER_SAMPLE;
                    current.GetUINT32(ref bitsKey, out bits);
                    Guid alignKey = MF_MT_AUDIO_BLOCK_ALIGNMENT;
                    current.GetUINT32(ref alignKey, out align);
                    channels = bits > 0 ? align / (bits / 8) : 0;
                    SampleRate = (int)rate;
                    Channels = (int)channels;
                    Bits = (int)bits;
                    BlockAlign = (int)align;

                    if (SampleRate <= 0 || Channels <= 0 || BlockAlign <= 0)
                    {
                        reason = "the decoded format is incomplete";
                        return false;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(current);
                }

                Guid duration = MF_PD_DURATION;
                if (_reader.GetPresentationAttribute(MediaSource, ref duration, out PropVariant value) >= 0 &&
                    value.vt == 21 )
                {
                    Duration100ns = (long)value.value;
                }

                if (Duration100ns <= 0)
                {
                    reason = "the file reports no duration";
                    return false;
                }

                return true;
            }

            internal byte[] ReadAll(out string reason)
            {
                reason = null;
                var chunks = new List<byte[]>();
                long total = 0;

                try
                {
                    while (true)
                    {
                        int hr = _reader.ReadSample(FirstAudio, 0, out _, out uint flags,
                                                    out _, out IMFSample sample);
                        if (hr < 0)
                        {
                            reason = $"reading the samples failed (0x{hr:X8})";
                            return null;
                        }

                        if ((flags & EndOfStream) != 0) break;
                        if (sample == null) continue;

                        try
                        {
                            if (sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer) < 0) continue;

                            try
                            {
                                if (buffer.Lock(out IntPtr at, out _, out uint length) < 0) continue;

                                try
                                {
                                    var chunk = new byte[length];
                                    Marshal.Copy(at, chunk, 0, (int)length);
                                    chunks.Add(chunk);
                                    total += length;
                                }
                                finally
                                {
                                    buffer.Unlock();
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(buffer);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(sample);
                        }
                    }

                    var pcm = new byte[total];
                    long at2 = 0;
                    foreach (byte[] chunk in chunks)
                    {
                        Buffer.BlockCopy(chunk, 0, pcm, (int)at2, chunk.Length);
                        at2 += chunk.Length;
                    }

                    return pcm;
                }
                catch (Exception e)
                {
                    reason = "reading the samples failed: " + ModLog.Describe(e);
                    return null;
                }
            }

            public void Dispose()
            {
                if (_reader == null) return;

                try { Marshal.ReleaseComObject(_reader); } catch (Exception) { }
                _reader = null;
            }
        }

        private const uint FirstAudio = 0xFFFFFFFD;
        private const uint MediaSource = 0xFFFFFFFF;
        private const uint EndOfStream = 0x2;

        private static readonly Guid MFMediaType_Audio =
            new Guid("73647561-0000-0010-8000-00AA00389B71");
        private static readonly Guid MFAudioFormat_PCM =
            new Guid("00000001-0000-0010-8000-00AA00389B71");
        private static readonly Guid MF_MT_MAJOR_TYPE =
            new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid MF_MT_SUBTYPE =
            new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND =
            new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE =
            new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        private static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT =
            new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        private static readonly Guid MF_PD_DURATION =
            new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] internal ushort vt;
            [FieldOffset(8)] internal ulong value;
        }

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(int version, int flags);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSourceReaderFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes,
            out IMFSourceReader reader);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IMFMediaType type);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, int mode);
        private static bool _started;

        private static class Media
        {
            internal static void EnsureStarted()
            {
                if (_started) return;
                _started = true;

                try { CoInitializeEx(IntPtr.Zero, 0 ); }
                catch (Exception) { }

                int hr = MFStartup(0x00020070 , 0);
                if (hr < 0) ModLog.Warn($"Media Foundation would not start (0x{hr:X8})");
            }
        }

        [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig] int GetItem(ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid key, out int type);
            [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IMFAttributes other, int match, out int result);
            [PreserveSig] int GetUINT32(ref Guid key, out uint value);
            [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
            [PreserveSig] int GetDouble(ref Guid key, out double value);
            [PreserveSig] int GetGUID(ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid key, out uint length);
            [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
            [PreserveSig] int GetBlob(ref Guid key, IntPtr value, uint size, out uint written);
            [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr value, out uint size);
            [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid key, uint value);
            [PreserveSig] int SetUINT64(ref Guid key, ulong value);
            [PreserveSig] int SetDouble(ref Guid key, double value);
            [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
            [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid key, IntPtr value, uint size);
            [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IMFAttributes destination);
        }

        [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType : IMFAttributes
        {
        }

        [ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSourceReader
        {
            [PreserveSig] int GetStreamSelection(uint stream, out int selected);
            [PreserveSig] int SetStreamSelection(uint stream, int selected);
            [PreserveSig] int GetNativeMediaType(uint stream, uint index, out IMFMediaType type);
            [PreserveSig] int GetCurrentMediaType(uint stream, out IMFMediaType type);
            [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
            [PreserveSig] int SetCurrentPosition(ref Guid format, IntPtr position);
            [PreserveSig] int ReadSample(uint stream, uint control, out uint actualStream,
                                         out uint flags, out long timestamp, out IMFSample sample);
            [PreserveSig] int Flush(uint stream);
            [PreserveSig] int GetServiceForStream(uint stream, ref Guid service, ref Guid iid,
                                                  out IntPtr value);
            [PreserveSig] int GetPresentationAttribute(uint stream, ref Guid attribute,
                                                       out PropVariant value);
        }

        [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            [PreserveSig] int GetItem(ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid key, out int type);
            [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IntPtr other, int match, out int result);
            [PreserveSig] int GetUINT32(ref Guid key, out uint value);
            [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
            [PreserveSig] int GetDouble(ref Guid key, out double value);
            [PreserveSig] int GetGUID(ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid key, out uint length);
            [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
            [PreserveSig] int GetBlob(ref Guid key, IntPtr value, uint size, out uint written);
            [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr value, out uint size);
            [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid key, uint value);
            [PreserveSig] int SetUINT64(ref Guid key, ulong value);
            [PreserveSig] int SetDouble(ref Guid key, double value);
            [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
            [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid key, IntPtr value, uint size);
            [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IntPtr destination);
            [PreserveSig] int GetSampleFlags(out uint flags);
            [PreserveSig] int SetSampleFlags(uint flags);
            [PreserveSig] int GetSampleTime(out long time);
            [PreserveSig] int SetSampleTime(long time);
            [PreserveSig] int GetSampleDuration(out long duration);
            [PreserveSig] int SetSampleDuration(long duration);
            [PreserveSig] int GetBufferCount(out uint count);
            [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
            [PreserveSig] int RemoveBufferByIndex(uint index);
            [PreserveSig] int RemoveAllBuffers();
            [PreserveSig] int GetTotalLength(out uint length);
            [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr at, out uint maxLength, out uint currentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out uint length);
            [PreserveSig] int SetCurrentLength(uint length);
            [PreserveSig] int GetMaxLength(out uint length);
        }
    }
}
