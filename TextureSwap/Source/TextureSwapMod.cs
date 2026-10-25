using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(InFalsusTextureSwap.TextureSwapMod), "InFalsusTextureSwap", "1.2.0", "infalsus", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusTextureSwap
{
    public class TextureSwapMod : MelonMod
    {
        private const string ConfigName = "InFalsusTextureSwap.txt";
        private const float Interval = 1f;

        private static readonly Dictionary<string, string> BindGlowNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "nia", "nia_bind_glow" },
                { "mei", "mei_bind_glow" },
                { "ayame", "ayame_bind" },
                { "iris", "bind_white_blur_50" },
                { "story", "story_bind" },
            };

        private sealed class Replacement
        {
            public byte[] Data;          
            public TextureFormat Format;
        }

        private readonly Dictionary<string, Replacement> _wanted =
            new Dictionary<string, Replacement>();
        private readonly HashSet<IntPtr> _done = new HashSet<IntPtr>();
        private readonly List<Texture2D> _pending = new List<Texture2D>();
        private float _next;

        public override void OnInitializeMelon()
        {
            string dir = Path.GetDirectoryName(typeof(TextureSwapMod).Assembly.Location) ?? ".";
            string config = Path.Combine(dir, ConfigName);
            if (!File.Exists(config))
            {
                LoggerInstance.Warning($"没有配置文件 {config}，什么也不换");
                return;
            }

            foreach (string raw in File.ReadAllLines(config))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] pair = line.Split(new[] { '=' }, 2);
                if (pair.Length != 2)
                {
                    LoggerInstance.Warning($"看不懂这一行，跳过: {line}");
                    continue;
                }

                string name = pair[0].Trim();
                string file = pair[1].Trim();

                if (file == "#blank")
                {
                    _wanted[name] = new Replacement { Data = null, Format = TextureFormat.RGBA32 };
                    LoggerInstance.Msg($"{name}  <-  #blank（置空，同尺寸全透明）");
                    continue;
                }

                string path = Path.IsPathRooted(file) ? file
                    : File.Exists(Path.Combine(dir, file)) ? Path.Combine(dir, file)
                    : Path.Combine(MelonEnvironment.GameRootDirectory, file);

                if (!File.Exists(path))
                {
                    LoggerInstance.Error($"{name}: 找不到 {path}");
                    continue;
                }

                TextureFormat format =
                    file.EndsWith(".dxt5", StringComparison.OrdinalIgnoreCase) ? TextureFormat.DXT5
                    : file.EndsWith(".bc7", StringComparison.OrdinalIgnoreCase) ? TextureFormat.BC7
                    : TextureFormat.RGBA32;
                _wanted[name] = new Replacement { Data = File.ReadAllBytes(path), Format = format };
                LoggerInstance.Msg($"{name}  <-  {path}  {format} {_wanted[name].Data.Length} 字节");
                BlankBindGlow(name);
            }

            if (_wanted.Count == 0) LoggerInstance.Warning("配置里没有可换的贴图");
        }

        private void BlankBindGlow(string illustName)
        {
            string prefix = null;
            foreach (string suffix in new[] { "_UIcharacterillust", "_UI_characterillust" })
            {
                if (illustName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = illustName.Substring(0, illustName.Length - suffix.Length);
                    break;
                }
            }
            if (string.IsNullOrEmpty(prefix)) return;

            string glow = BindGlowNames.TryGetValue(prefix, out string mapped)
                ? mapped
                : prefix.ToLowerInvariant() + "_bind_glow";
            if (_wanted.ContainsKey(glow)) return;

            _wanted[glow] = new Replacement { Data = null, Format = TextureFormat.RGBA32 };
            LoggerInstance.Msg($"{glow}  <-  #blank（随 {illustName} 自动置空）");
        }

        public override void OnUpdate()
        {
            if (_wanted.Count == 0) return;

            if (_pending.Count > 0)
            {
                foreach (Texture2D t in _pending) UnityEngine.Object.Destroy(t);
                _pending.Clear();
            }

            _next += Time.unscaledDeltaTime;
            if (_next < Interval) return;
            _next = 0f;

            foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture2D>()))
            {
                Texture2D tex = obj.TryCast<Texture2D>();
                if (tex == null || !_wanted.TryGetValue(tex.name, out Replacement rep)) continue;
                if (!_done.Add(tex.Pointer)) continue;

                int width = tex.width, height = tex.height;
                TextureFormat format = rep.Data == null ? tex.format : rep.Format;
                if (format != TextureFormat.DXT5 && format != TextureFormat.BC7 &&
                    format != TextureFormat.RGBA32)
                {
                    LoggerInstance.Error($"{tex.name}: 只支持 DXT5 / BC7 / RGBA32 目标，实际是 {format}，跳过");
                    continue;
                }
                int expected = ExpectedLength(format, width, height);
                byte[] data = rep.Data;
                if (data == null)
                {
                    if (format == TextureFormat.BC7)
                    {
                        LoggerInstance.Error($"{tex.name}: BC7 目标不能用 #blank（全零块是不透明黑），" +
                                             "请用 png_to_bc7.py 压一张全透明图");
                        continue;
                    }
                    data = new byte[expected];
                }
                else if (data.Length != expected)
                {
                    LoggerInstance.Error($"{tex.name}: {format} 数据 {data.Length} 字节，" +
                                         $"与 {width}x{height} 的 {expected} 不符，跳过");
                    continue;
                }

                try
                {
                    if (format != tex.format)
                    {
                        LoggerInstance.Error($"{tex.name}: 目标格式是 {tex.format}，源数据是 {format}。" +
                                             "压缩贴图要先用 png_to_dxt5.py / png_to_bc7.py 转成对应格式，放弃");
                        continue;
                    }

                    Texture2D source = new Texture2D(width, height, format, false);
                    source.LoadRawTextureData(data);
                    source.Apply(false, false);

                    Graphics.CopyTexture(source, tex);
                    _pending.Add(source);
                    LoggerInstance.Msg($"{tex.name}: 已写入 {format}");
                }
                catch (Exception e)
                {
                    LoggerInstance.Error($"{tex.name}: 替换失败 {e.GetType().Name}: {e.Message}");
                }
            }
        }

        private static int ExpectedLength(TextureFormat format, int width, int height)
        {
            return format == TextureFormat.RGBA32
                ? width * height * 4
                : ((width + 3) / 4) * ((height + 3) / 4) * 16;
        }

    }
}
