using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(InFalsusTextureSwap.TextureSwapMod), "InFalsusTextureSwap", "1.1.0", "infalsus", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusTextureSwap
{
    /// <summary>
    /// 把游戏里**已加载**的 Texture2D 换成同尺寸的像素，运行时生效、不动游戏文件。
    ///
    /// 一路上试错的结论（都实测过，别再走一遍）：
    /// <list type="bullet">
    /// <item><c>ImageConversion.LoadImage</c>：Unity 6 的参数是 `ReadOnlySpan&lt;byte&gt;`，
    /// Il2CppInterop 的 span 封送是坏的 → 运行时 MissingMethodException。</item>
    /// <item><c>LoadRawTextureData</c> 直接写目标：bundle 里的贴图**不可读**（isReadable 只在导入时
    /// 决定，运行时改不了）→ 抛 UnityException。</item>
    /// <item><c>Reinitialize</c> 改格式再 <c>CopyTexture</c>：Reinitialize 对 bundle 贴图无效，
    /// 目标仍是 DXT5，而 CopyTexture 只肯在**同一格式组**之间拷 → 拒绝，只在 Player.log 留一行
    /// "can only copy between same texture format groups"。</item>
    /// <item><c>ConvertTexture</c>（会做格式转换）：压缩格式不能当渲染目标 →
    /// "Unsupported destination format in Graphics.ConvertTexture"。</item>
    /// <item><c>SetPixels32</c> 往 DXT5 贴图塞像素：Unity 只允许 RGBA32/ARGB32/RGB24/Alpha8 →
    /// "texture uses an unsupported format"。</item>
    /// </list>
    ///
    /// 所以正解是**让源和目标同格式**：源数据由同目录的 `png_to_dxt5.py` / `png_to_bc7.py`
    /// 离线压好，这里新建一张同格式的贴图、`LoadRawTextureData` 塞进去（压缩格式只能这样写），
    /// 再 `CopyTexture` 进目标 —— 同格式，放行。换的是贴图本身，UI 的 Sprite / 材质 / 特效全都自然跟着变。
    ///
    /// 要换哪些写在 <c>Mods/InFalsusTextureSwap.txt</c>，一行一条 <c>贴图名 = 数据文件</c>；
    /// 后缀 `.dxt5` / `.bc7` 表示压缩贴图的原始数据，`.rgba` 表示 RGBA32 原始数据，
    /// **必须和目标贴图的格式一致**。值写 <c>#blank</c> 则把该贴图换成同尺寸全透明
    /// （用来去掉发光、遮罩之类的叠加层，格式按目标现算；BC7 不支持，全零块是不透明黑）。
    /// 相对路径先找本目录（Mods/）再找游戏根目录。
    ///
    /// 换了谁的立绘（`*_UIcharacterillust`），就自动把那个角色颈束的发光
    /// （`<角色>_bind_glow`）一起置空 —— 立绘里没有那条发光了，留着会很突兀。
    ///
    /// **尺寸必须和原贴图一致**：UI 的 Sprite 在创建时就把 UV 按当时的贴图尺寸烘好了，换尺寸会让
    /// 取样区域错位。数据长度对不上就拒绝并报错，不猜。
    ///
    /// 为什么轮询而不装钩子：贴图是 UI 打开时才从 bundle 载入的，载入时刻不确定，还可能被卸载后
    /// 重新载入。所以每秒扫一次已加载对象，遇到没换过的实例就换（同一个实例只换一次）。
    /// </summary>
    public class TextureSwapMod : MelonMod
    {
        private const string ConfigName = "InFalsusTextureSwap.txt";
        private const float Interval = 1f;

        private sealed class Replacement
        {
            public byte[] Data;          // null = 置空（换成同尺寸全透明，用来去掉发光之类的叠加层）
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

        /// <summary>
        /// 换了谁的立绘，就把谁的颈束发光一起置空：`Nia_UIcharacterillust` -> `nia_bind_glow`。
        /// 配置里显式写过同名条目就以配置为准。
        /// </summary>
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

            string glow = prefix.ToLowerInvariant() + "_bind_glow";
            if (_wanted.ContainsKey(glow)) return;

            _wanted[glow] = new Replacement { Data = null, Format = TextureFormat.RGBA32 };
            LoggerInstance.Msg($"{glow}  <-  #blank（随 {illustName} 自动置空）");
        }

        public override void OnUpdate()
        {
            if (_wanted.Count == 0) return;

            // 上一轮建的临时贴图，等 GPU 侧的拷贝真正落地后再销毁
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
                // 置空的条目按目标自己的格式现算，所以不用猜它是什么格式
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

        /// <summary>原始数据应有的字节数（DXT5 / BC7 都是 16 字节一个 4x4 块）。</summary>
        private static int ExpectedLength(TextureFormat format, int width, int height)
        {
            return format == TextureFormat.RGBA32
                ? width * height * 4
                : ((width + 3) / 4) * ((height + 3) / 4) * 16;
        }

    }
}
