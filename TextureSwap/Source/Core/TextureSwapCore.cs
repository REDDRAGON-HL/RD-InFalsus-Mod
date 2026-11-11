using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InFalsusTextureSwap
{
    internal enum SourceKind
    {
        Dxt5,
        Bc7,
        Rgba,
        Blank,
    }

    internal sealed class Replacement
    {
        public string Name;
        public string Path;
        public SourceKind Kind;
        public byte[] Data;
        public long Stamp;
        public Texture2D Texture;
        public bool Failed;
    }

    internal sealed class Binding
    {
        public Material Material;
        public string Target;
        public Texture Original;
        public Texture Current;
    }

    /// <summary>
    /// 按 DLL 同目录的 <c>InFalsusTextureSwap.txt</c> 换立绘：扫已加载的材质，把用着目标贴图的
    /// 那个材质的 _MainTex 换成离线压好的贴图数据。材质是共享资源，换一次所有实例都跟着变；
    /// 不碰游戏自己的贴图，所以不用改包、也不用管原来的压缩格式。
    /// 触发只有事件：场景载入、配置文件或图变化；没有逐帧扫描，全部绑上以后切场景也不扫。
    /// </summary>
    internal static class TextureSwapCore
    {
        private const string ConfigName = "InFalsusTextureSwap.txt";

        private struct Character
        {
            public readonly string Alias;
            public readonly string Main;

            /// <summary>颈束的叠加层，换了立绘就一起置空；最后一项是发光层。</summary>
            public readonly string[] Bind;

            public Character(string alias, string main, params string[] bind)
            {
                Alias = alias;
                Main = main;
                Bind = bind;
            }
        }

        // Mei 的颈束在 prefab 里拆成了两层（实心带子 mei_bind + 外发光 mei_bind_glow），
        // 其余角色都是烘在一张图里的一层。少关一层就会剩一条亮带子。
        private static readonly Character[] Characters =
        {
            new Character("nia", "Nia_UIcharacterillust", "nia_bind_glow"),
            new Character("mei", "Mei_UIcharacterillust", "mei_bind", "mei_bind_glow"),
            new Character("ayame", "Ayame_UIcharacterillust", "ayame_bind"),
            new Character("iris", "Iris_UI_characterillust", "bind_white_blur_50"),
            new Character("story", "Story_UIcharacterillust", "story_bind"),
        };

        private static Action<string> _info;
        private static Action<string> _warn;
        private static Action<string> _error;

        private static Dictionary<string, Replacement> _wanted =
            new Dictionary<string, Replacement>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Binding> Bindings = new List<Binding>();
        private static readonly List<float> Scans = new List<float>();
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();

        private static string _dir;
        private static string _config;
        private static UnityEngine.Events.UnityAction<Scene, LoadSceneMode> _sceneHandler;
        private static volatile bool _dirty;
        private static float _dirtySince = -1f;

        internal static void Init(string dir, Action<string> info, Action<string> warn, Action<string> error)
        {
            _info = info;
            _warn = warn;
            _error = error;
            _dir = dir;
            _config = Path.Combine(dir, ConfigName);

            if (!File.Exists(_config))
                _warn("没有配置文件 " + _config + "，什么也不换（格式：贴图名 = 文件名）");

            LoadConfig();

            Action<Scene, LoadSceneMode> handler = OnSceneLoaded;
            _sceneHandler = handler;
            try
            {
                SceneManager.add_sceneLoaded(_sceneHandler);
            }
            catch (Exception e)
            {
                _warn("订阅 sceneLoaded 失败：" + e.Message);
            }

            Watch();

            ScanAll();
            if (Pending.Count > 0) ScanSoon();
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;

            // 文件监听在后台线程；统一在这里（主线程）落到 Unity 对象上。只是读一个 bool。
            if (_dirty)
            {
                if (_dirtySince < 0f)
                {
                    _dirtySince = now;
                }
                else if (now - _dirtySince >= 0.3f)
                {
                    _dirty = false;
                    _dirtySince = -1f;
                    Reload();
                }
            }

            if (Scans.Count == 0) return;
            if (Pending.Count == 0)
            {
                Scans.Clear();
                return;
            }

            bool due = false;
            for (int i = Scans.Count - 1; i >= 0; i--)
            {
                if (now < Scans[i]) continue;
                Scans.RemoveAt(i);
                due = true;
            }
            if (due) ScanAll();
        }

        internal static void Shutdown()
        {
            try
            {
                if (_sceneHandler != null) SceneManager.remove_sceneLoaded(_sceneHandler);
            }
            catch
            {
                // 退出阶段 Unity 可能已经不认这些对象了
            }
            _sceneHandler = null;

            foreach (FileSystemWatcher watcher in Watchers) watcher.Dispose();
            Watchers.Clear();
            _dirty = false;
            Scans.Clear();
            Pending.Clear();

            foreach (Binding b in Bindings)
            {
                if (b.Material != null && b.Current != null) b.Material.mainTexture = b.Original;
            }
            Bindings.Clear();

            foreach (Replacement rep in _wanted.Values) DestroyTexture(rep);
            _wanted.Clear();
        }

        /// <summary>监听配置文件所在目录，外加每张图自己的目录。</summary>
        private static void Watch()
        {
            foreach (FileSystemWatcher watcher in Watchers) watcher.Dispose();
            Watchers.Clear();

            List<string> dirs = new List<string> { _dir };
            foreach (Replacement rep in _wanted.Values)
            {
                string dir = rep.Path == null ? null : Path.GetDirectoryName(rep.Path);
                if (!string.IsNullOrEmpty(dir) && !dirs.Contains(dir)) dirs.Add(dir);
            }

            foreach (string dir in dirs)
            {
                try
                {
                    FileSystemWatcher watcher = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName |
                                       NotifyFilters.Size,
                    };
                    watcher.Changed += OnFileTouched;
                    watcher.Created += OnFileTouched;
                    watcher.Deleted += OnFileTouched;
                    watcher.Renamed += OnFileRenamed;
                    watcher.EnableRaisingEvents = true;
                    Watchers.Add(watcher);
                }
                catch (Exception e)
                {
                    _warn("监听 " + dir + " 失败，那边改动要重启游戏才生效：" + e.Message);
                }
            }
        }

        private static void OnFileTouched(object sender, FileSystemEventArgs args) => _dirty = true;

        private static void OnFileRenamed(object sender, RenamedEventArgs args) => _dirty = true;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_wanted.Count == 0) return;

            // 先做最便宜的一步：每条绑定就是几回指针比较，看看材质还在不在、还指不指着我们的图。
            // 都还在就直接返回 —— 材质是 bundle 里的共享资源，新场景用的是同一个材质对象，
            // 换过的 _MainTex 依然有效，之后实例化出来的也跟着。
            Prune();
            RefreshPending();
            if (Pending.Count == 0) return;

            // 有目标没绑上，说明它可能在刚载入的这个场景里，而 Unity 没有「材质被创建」事件。
            // 在回调里同步扫一遍：此时本来就在读盘建场景，这一趟遍历看不出来；
            // 扫完之后新场景第一帧画出来就是我们的图，不会闪一下原画。
            ScanAll();
            if (Pending.Count > 0) ScanSoon();
        }

        /// <summary>
        /// 补扫：立绘材质可能跟着异步 Addressables 才到位，场景载入那一刻还读不到。
        /// 队列不叠加（已经排着就不再排），跑完这三个点还没绑上就说明这个目标不在当前场景，
        /// 等下次场景载入再说。
        /// </summary>
        private static void ScanSoon()
        {
            if (Scans.Count > 0) return;

            float now = Time.realtimeSinceStartup;
            Scans.Add(now + 0.5f);
            Scans.Add(now + 1.5f);
            Scans.Add(now + 4f);
        }

        private static void ScanAll()
        {
            if (_wanted.Count == 0)
            {
                Pending.Clear();
                return;
            }
            Prune();

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> all;
            try
            {
                all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>());
            }
            catch (Exception e)
            {
                _error("列材质失败：" + e.Message);
                return;
            }

            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i] == null ? null : all[i].TryCast<Material>();
                if (m != null) ApplyMaterial(m);
            }

            RefreshPending();
        }

        /// <summary>还没绑定成功的目标。用来决定还要不要继续补扫。</summary>
        private static void RefreshPending()
        {
            Pending.Clear();
            foreach (string key in _wanted.Keys) Pending.Add(key);
            foreach (Binding b in Bindings) Pending.Remove(b.Target);
        }

        private static void ApplyMaterial(Material m)
        {
            Texture original;
            try
            {
                if (!m.HasProperty("_MainTex")) return;
                original = m.mainTexture;
            }
            catch
            {
                return;
            }
            if (original == null) return;

            string name = original.name;
            if (string.IsNullOrEmpty(name)) return;
            if (!_wanted.TryGetValue(name, out Replacement rep)) return;

            Texture2D tex = GetTexture(rep, original);
            if (tex == null) return;
            if (original.Pointer == tex.Pointer) return;

            Binding binding = FindBinding(m);
            if (binding == null)
            {
                binding = new Binding { Material = m, Target = rep.Name, Original = original };
                Bindings.Add(binding);
            }
            binding.Current = tex;
            try
            {
                m.mainTexture = tex;
            }
            catch
            {
                // 材质在扫描过程中被销毁；下轮 Prune 会清掉这条记录
            }
        }

        private static Binding FindBinding(Material m)
        {
            IntPtr pointer = m.Pointer;
            foreach (Binding b in Bindings)
            {
                if (b.Material != null && b.Material.Pointer == pointer) return b;
            }
            return null;
        }

        /// <summary>材质被销毁、或者已经不指我们给的图（游戏自己改了）就丢掉这条记录。</summary>
        private static void Prune()
        {
            for (int i = Bindings.Count - 1; i >= 0; i--)
            {
                Material m = Bindings[i].Material;
                Texture current = Bindings[i].Current;
                bool drop;
                try
                {
                    drop = m == null || current == null || m.mainTexture == null ||
                           m.mainTexture.Pointer != current.Pointer;
                }
                catch
                {
                    drop = true;
                }
                if (drop) Bindings.RemoveAt(i);
            }
        }

        private static void Reload()
        {
            List<Replacement> obsolete = LoadConfig();
            Reapply();
            Watch();
            ScanAll();
            if (Pending.Count > 0) ScanSoon();

            foreach (Replacement rep in obsolete)
            {
                if (IsReferenced(rep.Texture)) continue;
                DestroyTexture(rep);
            }

            _info("配置重新载入：" + _wanted.Count + " 项");
        }

        private static bool IsReferenced(Texture tex)
        {
            if (tex == null) return false;
            foreach (Binding b in Bindings)
            {
                if (b.Current != null && b.Current.Pointer == tex.Pointer) return true;
            }
            return false;
        }

        /// <summary>把已经换过的材质指向新图（文件内容变了）或还原（条目删了）。</summary>
        private static void Reapply()
        {
            for (int i = Bindings.Count - 1; i >= 0; i--)
            {
                Binding b = Bindings[i];
                if (b.Material == null)
                {
                    Bindings.RemoveAt(i);
                    continue;
                }

                if (!_wanted.TryGetValue(b.Target, out Replacement rep) || rep.Failed)
                {
                    try
                    {
                        b.Material.mainTexture = b.Original;
                    }
                    catch
                    {
                        // 材质已经没了，记录直接丢掉即可
                    }
                    Bindings.RemoveAt(i);
                    continue;
                }

                Texture2D tex = GetTexture(rep, b.Original);
                if (tex == null) continue;

                b.Current = tex;
                try
                {
                    b.Material.mainTexture = tex;
                }
                catch
                {
                    Bindings.RemoveAt(i);
                }
            }
        }

        /// <summary>读配置文件，返回不再需要的旧条目（调用方决定什么时候销毁它们的贴图）。</summary>
        private static List<Replacement> LoadConfig()
        {
            Dictionary<string, Replacement> next =
                new Dictionary<string, Replacement>(StringComparer.OrdinalIgnoreCase);

            string[] lines = null;
            try
            {
                if (File.Exists(_config)) lines = File.ReadAllLines(_config);
            }
            catch (Exception e)
            {
                _error("读不了 " + _config + "：" + e.Message);
            }

            foreach (string raw in lines ?? new string[0])
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] pair = line.Split(new[] { '=' }, 2);
                if (pair.Length != 2)
                {
                    _warn("看不懂这一行，跳过: " + line);
                    continue;
                }

                string target = ResolveTarget(pair[0].Trim());
                string file = pair[1].Trim();
                if (target.Length == 0) continue;

                if (file == "#blank")
                {
                    next[target] = new Replacement { Name = target, Kind = SourceKind.Blank };
                    _info(target + "  <-  #blank（全透明）");
                    continue;
                }

                string path = Path.IsPathRooted(file) ? file : Path.Combine(_dir, file);

                SourceKind kind;
                if (!KindOf(path, out kind))
                {
                    _warn(target + ": 只支持 .dxt5 / .bc7 / .rgba（先用 png_to_dxt5.py / " +
                          "png_to_bc7.py 压好）: " + path);
                    continue;
                }
                if (!File.Exists(path))
                {
                    _error(target + ": 找不到 " + path);
                    continue;
                }

                long stamp;
                try
                {
                    stamp = File.GetLastWriteTimeUtc(path).Ticks ^ new FileInfo(path).Length;
                }
                catch
                {
                    continue;
                }

                _wanted.TryGetValue(target, out Replacement existing);
                if (existing != null && existing.Path == path && existing.Stamp == stamp && !existing.Failed)
                {
                    next[target] = existing;
                    continue;
                }

                try
                {
                    Replacement rep = new Replacement
                    {
                        Name = target,
                        Path = path,
                        Kind = kind,
                        Data = File.ReadAllBytes(path),
                        Stamp = stamp,
                    };
                    next[target] = rep;
                    _info(target + "  <-  " + Path.GetFileName(path) + "  (" + kind + "，" +
                          rep.Data.Length + " 字节)");
                }
                catch (Exception e)
                {
                    _error(path + ": " + e.Message);
                    if (existing != null) next[target] = existing;
                }
            }

            foreach (Character c in Characters)
            {
                if (!next.ContainsKey(c.Main)) continue;
                foreach (string bind in c.Bind)
                {
                    if (next.ContainsKey(bind)) continue;
                    next[bind] = new Replacement { Name = bind, Kind = SourceKind.Blank };
                    _info(bind + "  <-  自动置空（随 " + c.Main + "）");
                }
            }

            List<Replacement> obsolete = new List<Replacement>();
            foreach (KeyValuePair<string, Replacement> kv in _wanted)
            {
                Replacement kept;
                if (next.TryGetValue(kv.Key, out kept) && ReferenceEquals(kept, kv.Value)) continue;
                obsolete.Add(kv.Value);
            }

            _wanted = next;
            if (_wanted.Count == 0) _warn("配置里没有可换的贴图");
            return obsolete;
        }

        private static bool KindOf(string path, out SourceKind kind)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".dxt5":
                    kind = SourceKind.Dxt5;
                    return true;
                case ".bc7":
                    kind = SourceKind.Bc7;
                    return true;
                case ".rgba":
                    kind = SourceKind.Rgba;
                    return true;
                default:
                    kind = SourceKind.Rgba;
                    return false;
            }
        }

        /// <summary>
        /// 配置左边写贴图名（<c>Nia_UIcharacterillust</c>），也可以只写角色名
        /// （<c>Nia</c> / <c>mei_bind</c> / <c>mei_glow</c>）；认不出来的原样当作贴图名。
        /// </summary>
        private static string ResolveTarget(string key)
        {
            string s = Normalize(key);

            foreach (Character c in Characters)
            {
                string alias = Normalize(c.Alias);
                if (s == alias || s == Normalize(c.Main)) return c.Main;

                foreach (string bind in c.Bind)
                {
                    if (s == Normalize(bind)) return bind;
                }

                // <角色>glow 指的是最后一层，也就是发光那层
                if (c.Bind.Length > 0 && s == alias + "glow") return c.Bind[c.Bind.Length - 1];
            }

            return key;
        }

        private static string Normalize(string value)
        {
            return value.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static Texture2D GetTexture(Replacement rep, Texture original)
        {
            if (rep.Texture != null) return rep.Texture;
            if (rep.Failed) return null;

            Texture2D tex = null;
            try
            {
                if (rep.Kind == SourceKind.Blank)
                {
                    tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                    tex.Apply(false, false);
                }
                else
                {
                    if (original == null) throw new Exception("读不出目标贴图的尺寸");
                    TextureFormat format = rep.Kind == SourceKind.Dxt5 ? TextureFormat.DXT5
                        : rep.Kind == SourceKind.Bc7 ? TextureFormat.BC7
                        : TextureFormat.RGBA32;
                    int w = original.width;
                    int h = original.height;
                    int expected = format == TextureFormat.RGBA32
                        ? w * h * 4
                        : ((w + 3) / 4) * ((h + 3) / 4) * 16;
                    if (rep.Data.Length != expected)
                    {
                        throw new Exception(format + " 数据 " + rep.Data.Length + " 字节，与 " +
                                            w + "x" + h + " 的 " + expected + " 不符");
                    }
                    tex = new Texture2D(w, h, format, false);
                    tex.LoadRawTextureData(rep.Data);
                    tex.Apply(false, false);
                }

                if (original != null)
                {
                    tex.filterMode = original.filterMode;
                    tex.wrapMode = original.wrapMode;
                    tex.anisoLevel = original.anisoLevel;
                }

                tex.name = rep.Name;
                rep.Texture = tex;
                return tex;
            }
            catch (Exception e)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                rep.Failed = true;
                _error(rep.Name + ": " + e.Message);
                return null;
            }
        }

        private static void DestroyTexture(Replacement rep)
        {
            if (rep == null || rep.Texture == null) return;
            UnityEngine.Object.Destroy(rep.Texture);
            rep.Texture = null;
        }
    }
}
