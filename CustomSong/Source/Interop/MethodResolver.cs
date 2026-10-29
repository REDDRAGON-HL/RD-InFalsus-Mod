using System;
using System.Collections.Generic;
using System.Reflection;

namespace CustomSong
{
    internal static unsafe class MethodResolver
    {
        private const BindingFlags AnyMethod =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        internal static IntPtr ByName(string typeName, string methodName)
        {
            IntPtr p = ViaMethodInfo(typeName, methodName);
            if (p != IntPtr.Zero)
            {
                return p;
            }

            ModLog.Error($"{typeName}.{methodName}: no method by that name in this build");
            return IntPtr.Zero;
        }

        internal static IntPtr MethodInfoOf(string typeName, string methodName)
        {
            try
            {
                if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                    return IntPtr.Zero;

                foreach (Type t in types)
                {
                    foreach (MethodInfo mi in t.GetMethods(AnyMethod | BindingFlags.DeclaredOnly))
                    {
                        if (mi.Name != methodName) continue;

                        FieldInfo f = Il2CppInterop.Common.Il2CppInteropUtils
                            .GetIl2CppMethodInfoPointerFieldForGeneratedMethod(mi);
                        if (f == null) continue;
                        IntPtr info = (IntPtr)f.GetValue(null);
                        if (info != IntPtr.Zero) return info;
                    }
                }
            }
            catch (Exception e)
            {
                ModLog.Warn($"MethodInfo lookup for {typeName}.{methodName} failed: " +
                                 ModLog.Describe(e));
            }
            return IntPtr.Zero;
        }

        private static IntPtr ViaMethodInfo(string typeName, string methodName)
        {
            IntPtr info = MethodInfoOf(typeName, methodName);
            if (info == IntPtr.Zero) return IntPtr.Zero;
            return *(IntPtr*)info;
        }

        internal static IntPtr ByRuntime(string typeName, string methodName, string image, string namespaze)
        {
            IntPtr klass = ClassOf(typeName, image, namespaze);
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                IntPtr found = IntPtr.Zero;
                IntPtr matched = IntPtr.Zero;

                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    string name = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method);
                    if (name != methodName) continue;
                    IntPtr code = *(IntPtr*)method;

                    if (code != IntPtr.Zero && found == IntPtr.Zero)
                    {
                        found = code;
                        matched = method;
                    }
                }

                if (found != IntPtr.Zero)
                {
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_generic(matched) &&
                        Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_inflated(matched) == false)
                    {
                        IntPtr inflated = Inflated(klass, matched, typeName, methodName);
                        if (inflated != IntPtr.Zero && inflated != found)
                        {
                            return inflated;
                        }
                    }

                    return found;
                }

                ModLog.Warn($"the runtime walk of {typeName} found no {methodName} with a code " +
                                 "pointer of its own");
            }
            catch (Exception e)
            {
                ModLog.Warn($"the runtime walk for {typeName}.{methodName} failed: {ModLog.Describe(e)}");
            }

            return IntPtr.Zero;
        }

        private static IntPtr Inflated(IntPtr klass, IntPtr method, string typeName, string methodName)
        {
            IntPtr native = InflatedMethod(klass, method, typeName, methodName, IntPtr.Zero);
            return native == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)native;
        }

        internal static IntPtr InflatedMethod(IntPtr klass, IntPtr method, string typeName, string methodName,
                                              IntPtr typeArgument)
        {
            try
            {
                IntPtr argument = typeArgument;
                if (argument == IntPtr.Zero)
                {
                    argument = klass;
                    for (IntPtr parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(argument);
                         parent != IntPtr.Zero; )
                    {
                        argument = parent;
                        parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(argument);
                    }
                }

                IntPtr argumentType = Il2CppInterop.Runtime.IL2CPP.il2cpp_type_get_object(
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(argument));
                if (argumentType == IntPtr.Zero)
                {
                    ModLog.Warn($"could not get a Type for the argument of {typeName}.{methodName}");
                    return IntPtr.Zero;
                }

                IntPtr typeArrayClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_class_get(
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(argumentType), 1);
                if (typeArrayClass == IntPtr.Zero) return IntPtr.Zero;
                IntPtr arguments = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(typeArrayClass, 1);
                if (arguments == IntPtr.Zero) return IntPtr.Zero;
                *(IntPtr*)(arguments + Offsets.Runtime.ArrayDataOffset) = argumentType;
                IntPtr reflection = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_object(method, klass);
                if (reflection == IntPtr.Zero)
                {
                    ModLog.Warn($"{typeName}.{methodName} could not be turned into a reflection method");
                    return IntPtr.Zero;
                }

                IntPtr reflectionClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(reflection);
                IntPtr make = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    reflectionClass, "MakeGenericMethod", 1);
                if (make == IntPtr.Zero)
                {
                    ModLog.Warn("the reflection method has no MakeGenericMethod");
                    return IntPtr.Zero;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = arguments;
                IntPtr raised = IntPtr.Zero;
                IntPtr inflated = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                    make, reflection, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                {
                    ModLog.Warn($"MakeGenericMethod raised for {typeName}.{methodName}: {Raised.Text(raised)}");
                    return IntPtr.Zero;
                }
                if (inflated == IntPtr.Zero) return IntPtr.Zero;
                IntPtr native = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_from_reflection(inflated);
                if (native == IntPtr.Zero)
                {
                    ModLog.Warn($"the inflated {typeName}.{methodName} could not be turned back " +
                                     "into a native method");
                    return IntPtr.Zero;
                }

                string argumentName = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(argument) ?? "?";
                IntPtr code = *(IntPtr*)native;
                return native;
            }
            catch (Exception e)
            {
                ModLog.Warn($"inflating {typeName}.{methodName} failed: {ModLog.Describe(e)}");
                return IntPtr.Zero;
            }
        }

        internal static IntPtr MethodInfoByRuntime(string typeName, string methodName, string image, string namespaze)
        {
            IntPtr klass = ClassOf(typeName, image, namespaze);
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method) != methodName) continue;
                    return method;
                }

                ModLog.Warn($"the runtime walk of {typeName} found no method named {methodName}");
            }
            catch (Exception e)
            {
                ModLog.Warn($"the runtime walk for {typeName}.{methodName} failed: {ModLog.Describe(e)}");
            }

            return IntPtr.Zero;
        }

        private static IntPtr ClassOf(string typeName, string image, string namespaze)
        {
            IntPtr klass = FieldResolver.ClassPointer(typeName);
            if (klass != IntPtr.Zero) return klass;
            klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(image, namespaze, typeName);
            if (klass != IntPtr.Zero) return klass;
            ModLog.Warn($"{typeName} could not be found, by generated store or by image '{image}'");
            return IntPtr.Zero;
        }
    }
}
