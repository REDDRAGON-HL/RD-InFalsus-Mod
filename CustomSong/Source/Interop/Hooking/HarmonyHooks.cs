using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.InteropTypes;

namespace CustomSong
{
    internal static class HarmonyHooks
    {
        internal const string Id = "RedDragon.CustomSong";
        private static HarmonyLib.Harmony _harmony;

        internal static MethodInfo Handler(Type owner, string name) =>
            owner.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        internal static MethodInfo Target(string typeName, string methodName, string namespaceHint)
        {
            MethodInfo method = MethodResolver.ManagedMethod(typeName, methodName, namespaceHint);

            if (method == null)
                ModLog.WarnRelease($"{typeName}.{methodName} has no generated method to patch; " +
                                   "whatever it drives will silently do nothing");
            return method;
        }

        internal static bool Patch(MethodInfo target, MethodInfo prefix = null, MethodInfo postfix = null,
                                   MethodInfo finalizer = null)
        {
            if (target == null) return false;

            if (NativeEntry.JumpTarget(CodeOf(target)) != IntPtr.Zero)
                ModLog.Load($"{target.DeclaringType?.Name}.{target.Name} already has a detour at its " +
                            "entry; Harmony will chain onto it");

            try
            {
                _harmony ??= new HarmonyLib.Harmony(Id);
                _harmony.Patch(target,
                               prefix: prefix == null ? null : new HarmonyMethod(prefix),
                               postfix: postfix == null ? null : new HarmonyMethod(postfix),
                               finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
                bool applied = Ours(target);
                if (applied)
                    ModLog.Load($"{target.DeclaringType?.Name}.{target.Name} patched (Harmony)");
                else
                    ModLog.WarnRelease($"{target.DeclaringType?.Name}.{target.Name} was not patched; " +
                                       "Harmony kept no patch of ours");
                return applied;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease($"{target.DeclaringType?.Name}.{target.Name} could not be patched: " +
                                   ModLog.Describe(e));
                return false;
            }
        }

        private static bool Ours(MethodInfo target)
        {
            Patches info = HarmonyLib.Harmony.GetPatchInfo(target);
            if (info == null) return false;
            foreach (Patch patch in info.Prefixes) if (patch.owner == Id) return true;
            foreach (Patch patch in info.Postfixes) if (patch.owner == Id) return true;
            foreach (Patch patch in info.Finalizers) if (patch.owner == Id) return true;
            return false;
        }

        private static unsafe IntPtr CodeOf(MethodInfo method)
        {
            try
            {
                FieldInfo field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
                if (field == null) return IntPtr.Zero;
                IntPtr info = (IntPtr)field.GetValue(null);
                return info == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)info;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        internal static IntPtr Pointer(object instance) =>
            instance is Il2CppObjectBase proxy ? proxy.Pointer : IntPtr.Zero;

        internal static void Uninstall()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("the Harmony patches could not be removed: " + ModLog.Describe(e));
            }

            _harmony = null;
        }
    }
}
