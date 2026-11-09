using System;
using System.IO;
using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusTextureSwap.TextureSwapMod), "InFalsusTextureSwap", "1.3.0", "infalsus", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusTextureSwap
{
    public class TextureSwapMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            TextureSwapCore.Init(
                Path.GetDirectoryName(typeof(TextureSwapMod).Assembly.Location) ?? ".",
                m => LoggerInstance.Msg(m),
                m => LoggerInstance.Warning(m),
                m => LoggerInstance.Error(m));
        }

        // 只用来把文件监听的改动落到主线程 / 跑一次性的延迟扫描，不做任何检测。
        public override void OnUpdate() => TextureSwapCore.Tick();

        public override void OnDeinitializeMelon() => TextureSwapCore.Shutdown();
    }
}
