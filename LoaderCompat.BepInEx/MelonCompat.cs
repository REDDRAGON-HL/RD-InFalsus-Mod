extern alias UnityEngineCore;

using System;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Hook;

// MelonLoader -> BepInEx 兼容层。
// 只包含现有三个 mod 实际用到的 API：MelonMod 生命周期、MelonLogger、MelonEnvironment、
// NativeHook<T>、[MelonInfo]/[MelonGame]。业务源码一行不改，BepInEx 工程直接 include 原文件。

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

    /// <summary>
    /// MelonMod 的最小替身。BepInEx 的插件类继承它，BepInEx 通过派生类上的 [BepInPlugin] 发现插件。
    /// Load() 的调用顺序和 MelonLoader 一致：OnInitializeMelon → OnLateInitializeMelon。
    /// </summary>
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
    /// <summary>
    /// NativeHook&lt;T&gt; 的 BepInEx 实现。BepInEx 6 自带 INativeDetour（Dobby/Funchook/Il2CppInterop
    /// 三种 provider），所以不需要额外引入 MinHook 或 MonoMod。
    /// </summary>
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
