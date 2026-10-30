using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CustomSong
{
    internal static unsafe class JacketFactory
    {
        private static readonly int[] TextureFormats = { 4 , 5 , 3  };
        private const int Color32Size = 4;
        private static readonly string[] ShaderNames = { "Unlit/Texture", "Sprites/Default", "UI/Default" };
        private static IntPtr _textureClass;
        private static IntPtr _textureBaseClass;
        private static IntPtr _materialClass;
        private static IntPtr _shaderClass;
        private static IntPtr _color32Class;
        private static bool _resolved;

        private static bool Resolve()
        {
            if (_resolved) return true;
            _textureClass = Require("Texture2D");
            _textureBaseClass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, "Texture");
            _materialClass = Require("Material");
            _shaderClass = Require("Shader");
            _color32Class = Require("Color32");

            _resolved = _textureClass != IntPtr.Zero && _materialClass != IntPtr.Zero &&
                        _shaderClass != IntPtr.Zero && _color32Class != IntPtr.Zero;

#if DEBUG
            ArtworkProbe.Report();
#endif

            return _resolved;
        }

        private const string Assembly = "UnityEngine.CoreModule.dll";
        private const string Namespace = "UnityEngine";
        private const string AssemblyName = "UnityEngine.CoreModule";

        private static readonly string[] ConstructorWanted =
            { "Int32", "Int32", "TextureFormat", "Boolean", "Boolean" };
        private static IntPtr Require(string typeName) => Require(typeName, Assembly, Namespace);

        private static IntPtr Require(string typeName, string assembly, string namespaze)
        {
            IntPtr klass = FieldResolver.ClassPointer(typeName);
            if (klass != IntPtr.Zero) return klass;
            klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(assembly, namespaze, typeName);
            if (klass != IntPtr.Zero)
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                return klass;
            }

            ModLog.Warn($"the game's {typeName} type could not be found, by store or by assembly");
            return IntPtr.Zero;
        }

        internal static IntPtr Build(string pngPath, out string reason) =>
            Build(pngPath, "jacket", out reason);

        internal static IntPtr Build(string pngPath, string what, out string reason)
        {
            reason = null;
            string key = what + "\n" + pngPath;
            if (Built.TryGetValue(key, out IntPtr known)) return known;
            IntPtr material = BuildUncached(pngPath, what, out reason);
            if (material != IntPtr.Zero) Built[key] = material;
            return material;
        }

        private static readonly Dictionary<string, IntPtr> Built =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        private static Png.Image Decode(byte[] bytes, out string reason) =>
            Jpeg.Looks(bytes) ? Jpeg.Decode(bytes, out reason) : Png.Decode(bytes, out reason);

        private static IntPtr BuildUncached(string pngPath, string what, out string reason)
        {
            reason = null;

            if (!Resolve())
            {
#if DEBUG
                reason = "the game's texture and material types could not be found";
#endif
                return IntPtr.Zero;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{Path.GetFileName(pngPath)} could not be read: {ModLog.Describe(e)}";
#else
                _ = e;
#endif
                return IntPtr.Zero;
            }

            Png.Image image = Decode(bytes, out reason);
            if (image == null) return IntPtr.Zero;
            IntPtr texture = BuildTexture(image, out reason);
            if (texture == IntPtr.Zero) return IntPtr.Zero;
            IntPtr material = BuildMaterial(texture, what, out reason);
            if (material == IntPtr.Zero) return IntPtr.Zero;
            return material;
        }

        internal static IntPtr Cover(string path, IntPtr sourceMaterial, out string reason)
        {
            reason = null;
            if (!Resolve())
            {
                reason = "the game's texture and material types could not be found";
                return IntPtr.Zero;
            }

            if (!Memory.LooksLikeObject(sourceMaterial))
            {
                reason = "there is no material to clone";
                return IntPtr.Zero;
            }

            IntPtr sourceClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(sourceMaterial);
            string sourceName = sourceClass == IntPtr.Zero ? null :
                Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(sourceClass);
            if (sourceName != "Material")
            {
                reason = $"the pack row holds {sourceName ?? "an unnamed object"}, not a Material";
                return IntPtr.Zero;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                reason = $"{Path.GetFileName(path)} could not be read: {ModLog.Describe(e)}";
                return IntPtr.Zero;
            }

            Png.Image image = Decode(bytes, out reason);
            if (image == null) return IntPtr.Zero;
            IntPtr texture = BuildTexture(image, out reason);
            if (texture == IntPtr.Zero) return IntPtr.Zero;
            IntPtr material = CloneFrom(sourceMaterial, out reason);
            if (material == IntPtr.Zero) return IntPtr.Zero;
            if (!SetMainTexture(material, texture, out reason)) return IntPtr.Zero;
            MakeTransparent(material, sourceMaterial);

            Keep(texture, material);
            return material;
        }

        private static double TransparentShare(byte[] rgba)
        {
            int clear = 0;
            for (int i = 3; i < rgba.Length; i += Color32Size)
                if (rgba[i] < 128) clear++;
            return rgba.Length == 0 ? 0 : (double)clear / (rgba.Length / Color32Size);
        }

#if DEBUG

        internal static string UnityName(IntPtr obj)
        {
            if (!Memory.LooksLikeObject(obj)) return "<none>";
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(obj);
            IntPtr getter = klass == IntPtr.Zero ? IntPtr.Zero : Entry(klass, "get_name", 0);
            if (getter == IntPtr.Zero) return "<no name getter>";
            IntPtr raised = IntPtr.Zero;
            IntPtr text = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, obj, null, ref raised);
            if (raised != IntPtr.Zero || text == IntPtr.Zero) return "<unnamed>";
            return Memory.Text(text);
        }

        internal static string TextureProperties(IntPtr material)
        {
            IntPtr list = Entry(_materialClass, "GetTexturePropertyNames", 0);
            if (list == IntPtr.Zero) return "<no property list>";
            IntPtr raised = IntPtr.Zero;
            IntPtr names = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(list, material, null, ref raised);
            if (raised != IntPtr.Zero || !Memory.LooksLikeObject(names)) return "<none>";
            IntPtr getTexture = Entry(_materialClass, "GetTexture", 1);
            if (getTexture == IntPtr.Zero) return "<no GetTexture>";
            int count = Memory.I32(names + Offsets.Runtime.ArrayLength);
            var text = new System.Text.StringBuilder();
            IntPtr* args = stackalloc IntPtr[1];

            for (int i = 0; i < count; i++)
            {
                IntPtr name = Memory.Ptr(names + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(name)) continue;
                args[0] = name;
                IntPtr error = IntPtr.Zero;
                IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                    getTexture, material, (void**)args, ref error);
                if (text.Length > 0) text.Append(", ");
                text.Append(Memory.Text(name)).Append('=');
                text.Append(error != IntPtr.Zero || texture == IntPtr.Zero
                    ? "<null>" : UnityName(texture));
            }

            return text.ToString();
        }
#endif

        private static readonly List<IntPtr> Kept = new List<IntPtr>();

        private static void Keep(IntPtr texture, IntPtr material)
        {
            Kept.Add(texture);
            Kept.Add(material);
        }

        internal static IntPtr BuildFromTexture(IntPtr texture, string what, out string reason)
        {
            reason = null;
            if (!Resolve())
            {
#if DEBUG
                reason = "the game's texture and material types could not be found";
#endif
                return IntPtr.Zero;
            }

            IntPtr material = BuildMaterial(texture, what, out reason);
            return material;
        }

        private static IntPtr BuildTexture(Png.Image image, out string reason)
        {
            reason = null;
            int pixels = image.Width * image.Height;
            IntPtr colors = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(_color32Class, (ulong)pixels);
            if (colors == IntPtr.Zero)
            {
#if DEBUG
                reason = "the jacket's pixel array could not be allocated";
#endif
                return IntPtr.Zero;
            }

            byte* destination = (byte*)(colors + Offsets.Runtime.ArrayDataOffset);
            long rowBytes = (long)image.Width * Color32Size;

            fixed (byte* source = image.AsColor32)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Buffer.MemoryCopy(source + (image.Height - 1 - y) * rowBytes,
                                      destination + (long)y * rowBytes,
                                      rowBytes, rowBytes);
                }
            }

            IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_textureClass);
            if (texture == IntPtr.Zero)
            {
#if DEBUG
                reason = "the jacket's texture could not be created";
#endif
                return IntPtr.Zero;
            }

            IntPtr constructor = ConstructorBySignature("Texture2D", ConstructorWanted);
            if (constructor == IntPtr.Zero)
            {
#if DEBUG
                reason = "Texture2D's constructor could not be found";
#endif
                return IntPtr.Zero;
            }

            int format = SupportedFormat();
            if (format == 0)
            {
#if DEBUG
                reason = "the device supports none of the texture formats a jacket can use";
#endif
                return IntPtr.Zero;
            }

            int width = image.Width;
            int height = image.Height;
            int textureFormat = format;
            byte mipChain = 0;
            byte linear = 0;
            IntPtr* ctorArgs = stackalloc IntPtr[5];
            ctorArgs[0] = (IntPtr)(&width);
            ctorArgs[1] = (IntPtr)(&height);
            ctorArgs[2] = (IntPtr)(&textureFormat);
            ctorArgs[3] = (IntPtr)(&mipChain);
            ctorArgs[4] = (IntPtr)(&linear);
            if (!Invoke(constructor, texture, ctorArgs, 5, out reason)) return IntPtr.Zero;

            if (IsKnownToHaveNoNativeObject(texture))
            {
#if DEBUG
                reason = "Texture2D's constructor returned but the texture has no native object";
#endif
                return IntPtr.Zero;
            }

            int built = WidthOf(texture);
            if (built != image.Width)
            {
#if DEBUG
                reason = $"Texture2D's constructor returned but the texture reports {built} pixels " +
                         $"wide instead of {image.Width}";
#endif
                return IntPtr.Zero;
            }

            ClampWrapping(texture);
            IntPtr setPixels = Entry(_textureClass, "SetPixels32", 1);
            if (setPixels == IntPtr.Zero)
            {
#if DEBUG
                reason = "Texture2D.SetPixels32 could not be found";
#endif
                return IntPtr.Zero;
            }

            IntPtr* pixelsArgs = stackalloc IntPtr[1];
            pixelsArgs[0] = colors;
            if (!Invoke(setPixels, texture, pixelsArgs, 1, out reason)) return IntPtr.Zero;
            IntPtr apply = Entry(_textureClass, "Apply", 0);
            if (apply == IntPtr.Zero)
            {
#if DEBUG
                reason = "Texture2D.Apply could not be found";
#endif
                return IntPtr.Zero;
            }

            if (!Invoke(apply, texture, null, 0, out reason)) return IntPtr.Zero;
            return texture;
        }

        private const int WrapClamp = 1;

        private static void ClampWrapping(IntPtr texture)
        {
            if (_textureBaseClass == IntPtr.Zero) return;
            IntPtr setter = Entry(_textureBaseClass, "set_wrapMode", 1);
            if (setter == IntPtr.Zero) return;
            int clamp = WrapClamp;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&clamp);
            Invoke(setter, texture, args, 1, out _);
        }

        private static IntPtr BuildMaterial(IntPtr texture, string what, out string reason)
        {
            reason = null;
            IntPtr shader = IntPtr.Zero;
            IntPtr find = Entry(_shaderClass, "Find", 1);
            IntPtr* findArgs = stackalloc IntPtr[1];

            if (find != IntPtr.Zero)
            {
                foreach (string name in ShaderNames)
                {
                    findArgs[0] = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
                    if (findArgs[0] == IntPtr.Zero) continue;
                    IntPtr raised = IntPtr.Zero;
                    IntPtr found = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                        find, IntPtr.Zero, (void**)findArgs, ref raised);

                    if (raised == IntPtr.Zero && found != IntPtr.Zero && Memory.LooksLikeObject(found))
                    {
                        shader = found;
                        break;
                    }
                }
            }

            if (shader == IntPtr.Zero) ModLog.Warn("no shader could be found for the jacket material");
            IntPtr material = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_materialClass);
            if (material == IntPtr.Zero)
            {
#if DEBUG
                reason = "the jacket's material could not be created";
#endif
                return IntPtr.Zero;
            }

            IntPtr constructor = Entry(_materialClass, ".ctor", 1);
            if (constructor == IntPtr.Zero)
            {
#if DEBUG
                reason = "Material's constructor could not be found";
#endif
                return IntPtr.Zero;
            }

            IntPtr* ctorArgs = stackalloc IntPtr[1];
            ctorArgs[0] = shader;
            if (!Invoke(constructor, material, ctorArgs, 1, out reason)) return IntPtr.Zero;

            if (IsKnownToHaveNoNativeObject(material))
            {
#if DEBUG
                reason = "Material's constructor returned but the material has no native object";
#endif
                return IntPtr.Zero;
            }

            IntPtr setTexture = Entry(_materialClass, "set_mainTexture", 1);
            if (setTexture == IntPtr.Zero)
            {
#if DEBUG
                reason = "Material.set_mainTexture could not be found";
#endif
                return IntPtr.Zero;
            }

            IntPtr* textureArgs = stackalloc IntPtr[1];
            textureArgs[0] = texture;
            if (!Invoke(setTexture, material, textureArgs, 1, out reason)) return IntPtr.Zero;
            return material;
        }

        internal static unsafe IntPtr CloneFrom(IntPtr source, out string reason)
        {
            reason = null;

            if (!Memory.LooksLikeObject(source))
            {
                reason = "there is no material to clone";
                return IntPtr.Zero;
            }

            IntPtr material = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_materialClass);
            if (material == IntPtr.Zero)
            {
                reason = "the clone material could not be created";
                return IntPtr.Zero;
            }

            IntPtr constructor = ConstructorBySignature("Material", MaterialCopyWanted);
            if (constructor == IntPtr.Zero)
            {
                reason = "Material's copy constructor could not be found";
                return IntPtr.Zero;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = source;
            if (!Invoke(constructor, material, args, 1, out reason)) return IntPtr.Zero;

            if (IsKnownToHaveNoNativeObject(material))
            {
                reason = "the clone returned but has no native object";
                return IntPtr.Zero;
            }

            return material;
        }

        private static readonly string[] MaterialCopyWanted = { "Material" };

        internal static IntPtr MainTexture(IntPtr material, out string reason)
        {
            reason = null;
            IntPtr getter = Entry(_materialClass, "get_mainTexture", 0);
            if (getter == IntPtr.Zero)
            {
                reason = "Material.get_mainTexture could not be found";
                return IntPtr.Zero;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, material, null, ref raised);
            if (raised != IntPtr.Zero || !Memory.LooksLikeObject(texture))
            {
                reason = "the material's main texture could not be read";
                return IntPtr.Zero;
            }

            return texture;
        }

        internal static unsafe bool SetMainTexture(IntPtr material, IntPtr texture, out string reason)
        {
            reason = null;
            IntPtr setter = Entry(_materialClass, "set_mainTexture", 1);
            if (setter == IntPtr.Zero)
            {
                reason = "Material.set_mainTexture could not be found";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = texture;
            return Invoke(setter, material, args, 1, out reason);
        }

        private static readonly string[] BlendProperties =
            { "_AlphaClip", "_Cutoff", "_Lowiro_AlphaMaskThreshold", "_Surface" };

        private static unsafe void MakeTransparent(IntPtr material, IntPtr source)
        {
            var before = new System.Text.StringBuilder();

            for (int i = 0; i < BlendProperties.Length; i++)
            {
                if (i > 0) before.Append(' ');
                before.Append(BlendProperties[i]).Append('=').Append(GetFloat(source, BlendProperties[i]));
            }

            if (HasProperty(material, "_Cutoff"))
            {
                SetFloat(material, "_AlphaClip", 1);
                SetFloat(material, "_Cutoff", 0.5f);
                EnableKeyword(material, "_ALPHATEST_ON");
            }

        }

        private static unsafe bool HasProperty(IntPtr material, string name)
        {
            IntPtr method = Entry(_materialClass, "HasProperty", 1);
            if (method == IntPtr.Zero) return false;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                method, material, (void**)args, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return false;
            return *(byte*)(boxed + Offsets.Runtime.BoxedData) != 0;
        }

        private static unsafe void SetFloat(IntPtr material, string name, float value)
        {
            IntPtr setter = Entry(_materialClass, "SetFloat", 2);
            if (setter == IntPtr.Zero) return;
            IntPtr key = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = key;
            args[1] = (IntPtr)(&value);
            Invoke(setter, material, args, 2, out _);
        }

        private static float GetFloat(IntPtr material, string name)
        {
            IntPtr getter = Entry(_materialClass, "GetFloat", 1);
            if (getter == IntPtr.Zero) return float.NaN;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, material, (void**)args, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return float.NaN;
            return *(float*)(boxed + Offsets.Runtime.BoxedData);
        }

        private static unsafe void EnableKeyword(IntPtr material, string keyword)
        {
            IntPtr method = Entry(_materialClass, "EnableKeyword", 1);
            if (method == IntPtr.Zero) return;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(keyword);
            Invoke(method, material, args, 1, out _);
        }

        private static IntPtr Entry(IntPtr klass, string name, int parameters) =>
            Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, parameters);

        private static IntPtr ConstructorBySignature(string typeName, string[] wanted)
        {
            if (Constructors.TryGetValue(typeName, out IntPtr known)) return known;
            IntPtr found = FindConstructorBySignature(typeName, wanted);
            Constructors[typeName] = found;
            return found;
        }

        private static readonly Dictionary<string, IntPtr> Constructors =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        private static IntPtr FindConstructorBySignature(string typeName, string[] wanted)
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, typeName);
                if (klass == IntPtr.Zero)
                {
                    ModLog.Warn($"{typeName}'s class could not be reached for its constructors");
                    return IntPtr.Zero;
                }

                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                int walked = 0;
                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    walked++;
                    if (walked > MethodsWalkLimit)
                    {
                        ModLog.Warn($"{typeName}'s method walk passed {MethodsWalkLimit} entries; stopping");
                        return IntPtr.Zero;
                    }

                    string name = Name(method);
                    if (name != ".ctor") continue;
                    int count = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method);
                    if (count != wanted.Length) continue;
                    if (!Matches(method, wanted)) continue;
                    string owner = DeclaringTypeName(method);
                    if (owner != typeName)
                    {
                        ModLog.Warn($"a {typeName} constructor was found but reports {owner ?? "(null)"} " +
                                         $"as its class; it is not one that can be called");
                        return IntPtr.Zero;
                    }

                    return method;
                }

                return IntPtr.Zero;
            }
            catch (Exception e)
            {
                ModLog.Warn($"{typeName}'s constructors could not be read: {ModLog.Describe(e)}");
                return IntPtr.Zero;
            }
        }

        private static bool Matches(IntPtr method, string[] wanted)
        {
            for (int i = 0; i < wanted.Length; i++)
            {
                IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param(method, (uint)i);
                if (type == IntPtr.Zero) return false;
                if (TypeName(type) != wanted[i]) return false;
            }
            return true;
        }

        private static string Name(IntPtr method) =>
            Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method);

        private static int SupportedFormat()
        {
            if (_format != int.MinValue) return _format;
            _format = 0;
            foreach (int candidate in TextureFormats)
            {
                if (!SupportsFormat(candidate)) continue;
                _format = candidate;
                break;
            }

            if (_format == 0)
                ModLog.Warn("the device reports no support for any texture format a jacket can use");
            return _format;
        }

        private static int _format = int.MinValue;

        private static bool SupportsFormat(int format)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, "SystemInfo");
            if (klass == IntPtr.Zero)
            {
                ModLog.Warn("SystemInfo could not be reached, so the texture format was never asked about");
                return false;
            }

            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
            IntPtr method = Entry(klass, "SupportsTextureFormat", 1);
            if (method == IntPtr.Zero)
            {
                ModLog.Warn("SystemInfo.SupportsTextureFormat could not be found, so the texture " +
                                 "format was never asked about");
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&format);
            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                ModLog.Warn($"asking about texture format {format} raised: {Raised.Text(raised)}");
                return false;
            }
            if (boxed == IntPtr.Zero)
            {
                ModLog.Warn($"asking about texture format {format} answered with nothing");
                return false;
            }

            return *(byte*)(boxed + Offsets.Runtime.BoxedData) != 0;
        }

        internal static string Words(IntPtr address, int count)
        {
#if DEBUG
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                IntPtr at = address + i * IntPtr.Size;
                if (i > 0) text.Append(' ');
                text.Append(Memory.Ptr(at).ToInt64().ToString("X"));
            }
            return text.ToString();
#else
            return null;
#endif
        }

        private static string DeclaringTypeName(IntPtr method)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_declaring_type(method);
            if (klass == IntPtr.Zero) return null;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
        }

        private static string TypeName(IntPtr type)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(type);
            if (klass == IntPtr.Zero) return null;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
        }

        private static Type GeneratedType(string typeName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != AssemblyName) continue;
                return asm.GetType(Namespace + "." + typeName, throwOnError: false);
            }

            ModLog.Warn($"no loaded assembly is named '{AssemblyName}', so {typeName} cannot be reached");
            return null;
        }

        private static bool IsKnownToHaveNoNativeObject(IntPtr instance)
        {
            if (_cachedPtrOffset == int.MinValue) _cachedPtrOffset = CachedPointerOffset();
            if (_cachedPtrOffset <= 0) return false;
            return Memory.Ptr(instance + _cachedPtrOffset) == IntPtr.Zero;
        }

        private static int CachedPointerOffset()
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, "Object");
                if (klass == IntPtr.Zero)
                {
                    ModLog.Warn("UnityEngine.Object could not be reached, so whether a built object " +
                                     "has a native one will not be checked");
                    return 0;
                }

                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "m_CachedPtr");
                if (field == IntPtr.Zero)
                {
                    ModLog.Warn("UnityEngine.Object has no m_CachedPtr, so whether a built object " +
                                     "has a native one will not be checked");
                    return 0;
                }

                int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                if (offset <= 0)
                {
                    ModLog.Warn($"m_CachedPtr is at 0x{offset:X}, which cannot be an offset; whether a " +
                                     "built object has a native one will not be checked");
                    return 0;
                }

                return offset;
            }
            catch (Exception e)
            {
                ModLog.Warn($"Object.m_CachedPtr could not be resolved: {ModLog.Describe(e)}");
                return 0;
            }
        }

        private static int _cachedPtrOffset = int.MinValue;

        private static bool Invoke(IntPtr method, IntPtr instance, IntPtr* args, int count, out string reason)
        {
            reason = null;

            for (int i = 0; i < count; i++)
            {
                if (args[i] != IntPtr.Zero) continue;
#if DEBUG
                reason = $"argument {i} could not be built, so the call was not made";
#endif
                return false;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"a call into the game's rendering types raised: {Raised.Text(raised)}";
#endif
            return false;
        }

        private static int WidthOf(IntPtr texture)
        {
            IntPtr getter = Entry(_textureClass, "get_width", 0);
            if (getter == IntPtr.Zero)
            {
                ModLog.Warn("Texture2D.get_width could not be found, so the built texture cannot be checked");
                return -1;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, texture, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1;
            return *(int*)(boxed + Offsets.Runtime.BoxedData);
        }

        private const int MethodsWalkLimit = 4096;
    }
}
