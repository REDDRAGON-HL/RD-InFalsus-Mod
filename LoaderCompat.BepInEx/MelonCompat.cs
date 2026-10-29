extern alias UnityEngineCore;

using System;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Hook;


namespace MelonLoader
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author, string downloadLink)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonGameAttribute : Attribute
    {
        public MelonGameAttribute(string developer, string game)
        {
        }
    }

    public sealed class MelonLoggerInstance
    {
        private readonly ManualLogSource _log;

        public MelonLoggerInstance(ManualLogSource log) => _log = log;

        public void Msg(string message) => _log.LogInfo(message);
        public void Warning(string message) => _log.LogWarning(message);
        public void Error(string message) => _log.LogError(message);
    }

    public static class MelonLogger
    {
        internal static MelonLoggerInstance Instance;

        public static void Msg(string message) => Instance?.Msg(message);
        public static void Warning(string message) => Instance?.Warning(message);
        public static void Error(string message) => Instance?.Error(message);
    }

    public abstract class MelonMod : BasePlugin
    {
        public MelonLoggerInstance LoggerInstance => new MelonLoggerInstance(Log);

        public override void Load()
        {
            MelonLogger.Instance = LoggerInstance;
            OnInitializeMelon();
            OnLateInitializeMelon();

            MelonUpdateBehaviour behaviour = AddComponent<MelonUpdateBehaviour>();
            behaviour.Owner = this;
        }

        public override bool Unload()
        {
            OnDeinitializeMelon();
            return true;
        }

        public virtual void OnInitializeMelon() { }
        public virtual void OnLateInitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnLateUpdate() { }
        public virtual void OnDeinitializeMelon() { }
    }

    public class MelonUpdateBehaviour : UnityEngineCore::UnityEngine.MonoBehaviour
    {
        internal MelonMod Owner;

        private void Update() => Owner?.OnUpdate();
        private void LateUpdate() => Owner?.OnLateUpdate();
    }
}

namespace MelonLoader.Utils
{
    public static class MelonEnvironment
    {
        public static string GameRootDirectory => BepInEx.Paths.GameRootPath;
        public static string UserDataDirectory => BepInEx.Paths.ConfigPath;
    }
}

namespace MelonLoader.NativeUtils
{
    public sealed class NativeHook<T> where T : Delegate
    {
        public IntPtr Target;
        public IntPtr Detour;
        public T Trampoline { get; private set; }

        private INativeDetour _hook;

        public void Attach()
        {
            T detour = Marshal.GetDelegateForFunctionPointer<T>(Detour);
            _hook = INativeDetour.CreateAndApply(Target, detour, out T trampoline);
            Trampoline = trampoline;
        }

        public void Detach()
        {
            _hook?.Dispose();
            _hook = null;
            Trampoline = null;
        }
    }
}
