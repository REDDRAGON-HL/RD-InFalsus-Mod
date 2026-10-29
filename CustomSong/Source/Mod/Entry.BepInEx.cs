#if BEPINEX
extern alias UnityEngineCore;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    [BepInPlugin("RedDragon.CustomSong", "Custom Song", CustomSongCore.Version)]
    [BepInProcess("infalsus.exe")]
    public class CustomSongPlugin : BasePlugin
    {
        public override void Load()
        {
            CustomSongCore.Loaded();
            CustomSongCore.Initialize();
            AddComponent<CustomSongTick>();
        }

        public override bool Unload()
        {
            CustomSongCore.Shutdown();
            return true;
        }
    }

    public sealed class CustomSongTick : MonoBehaviour
    {
        private void Update() => CustomSongCore.Update();
        private void OnGUI() => AutoOverlay.Draw();
    }
}
#endif
