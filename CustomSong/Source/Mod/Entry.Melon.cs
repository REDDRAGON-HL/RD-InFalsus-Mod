using MelonLoader;

[assembly: MelonInfo(typeof(CustomSong.CustomSongMod), "Custom Song",
                     CustomSong.CustomSongCore.Version, "RedDragon", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace CustomSong
{
    public class CustomSongMod : MelonMod
    {
        public override void OnInitializeMelon() => CustomSongCore.Loaded();
        public override void OnLateInitializeMelon() => CustomSongCore.Initialize();
        public override void OnUpdate() => CustomSongCore.Update();
        public override void OnGUI() => AutoOverlay.Draw();
        public override void OnDeinitializeMelon() => CustomSongCore.Shutdown();
    }
}
