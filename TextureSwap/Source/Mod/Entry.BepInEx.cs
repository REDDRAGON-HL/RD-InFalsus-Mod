using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace InFalsusTextureSwap
{
    [BepInPlugin("com.infalsus.textureswap", "InFalsusTextureSwap", "1.3.0")]
    public class TextureSwapPlugin : BasePlugin
    {
        public override void Load()
        {
            TextureSwapCore.Init(
                Path.GetDirectoryName(typeof(TextureSwapPlugin).Assembly.Location) ?? Paths.PluginPath,
                m => Log.LogInfo(m),
                m => Log.LogWarning(m),
                m => Log.LogError(m));

            AddComponent<TextureSwapBehaviour>();
        }

        public override bool Unload()
        {
            TextureSwapCore.Shutdown();
            return true;
        }
    }

    public class TextureSwapBehaviour : MonoBehaviour
    {
        // 只用来把文件监听的改动落到主线程 / 跑一次性的延迟扫描，不做任何检测。
        private void Update() => TextureSwapCore.Tick();
    }
}
