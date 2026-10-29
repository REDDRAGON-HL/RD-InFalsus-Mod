using System;

namespace CustomSong
{
    internal static unsafe class Raised
    {
        internal static string Text(IntPtr exception)
        {
            if (exception == IntPtr.Zero) return null;

            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(exception);
                if (klass == IntPtr.Zero) return "<exception with no class>";
                IntPtr toString = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "ToString", 0);
                if (toString != IntPtr.Zero)
                {
                    IntPtr again = IntPtr.Zero;
                    IntPtr text = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(toString, exception, null, ref again);

                    if (again == IntPtr.Zero && text != IntPtr.Zero)
                    {
                        string rendered = Il2CppInterop.Runtime.IL2CPP.Il2CppStringToManaged(text);
                        if (!string.IsNullOrEmpty(rendered)) return rendered;
                    }
                }

                return ClassName(klass);
            }
            catch (Exception e)
            {
                return $"<exception could not be read: {ModLog.Describe(e)}>";
            }
        }

        private static string ClassName(IntPtr klass)
        {
            try
            {
                string name = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
            }
            return "<exception whose class could not be named>";
        }
    }
}
