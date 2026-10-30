using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: HookScan <plugin.dll> [--all]");
            return 2;
        }

        using FileStream stream = File.OpenRead(args[0]);
        using var pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();

        foreach (CustomAttributeHandle handle in md.CustomAttributes)
        {
            CustomAttribute attribute = md.GetCustomAttribute(handle);
            string type = AttributeTypeName(md, attribute);
            if (type != "BepInPlugin" && type != "MelonInfo" && type != "MelonModInfo") continue;
            BlobReader blob = md.GetBlobReader(attribute.Value);
            if (blob.Length < 2) continue;
            blob.ReadUInt16();
            var parts = new List<string>();
            while (blob.RemainingBytes > 0 && parts.Count < 4)
            {
                string text;
                try { text = blob.ReadSerializedString(); }
                catch { break; }
                if (text != null) parts.Add(text);
            }

            Console.WriteLine($"== {type}: {string.Join(" | ", parts)}");
        }

        var types = new SortedSet<string>(StringComparer.Ordinal);
        var members = new SortedSet<string>(StringComparer.Ordinal);

        foreach (TypeReferenceHandle handle in md.TypeReferences)
        {
            TypeReference reference = md.GetTypeReference(handle);
            string space = reference.Namespace.IsNil ? "" : md.GetString(reference.Namespace);
            string name = md.GetString(reference.Name);
            types.Add(space.Length == 0 ? name : space + "." + name);
        }

        foreach (MemberReferenceHandle handle in md.MemberReferences)
        {
            MemberReference reference = md.GetMemberReference(handle);
            string name = md.GetString(reference.Name);
            string owner = Owner(md, reference.Parent);
            members.Add(owner + "." + name);
        }

        Console.WriteLine("== types referenced");
        foreach (string type in types) Console.WriteLine("  " + type);
        Console.WriteLine("== members referenced");
        foreach (string member in members) Console.WriteLine("  " + member);
        Console.WriteLine("== user strings");
        Console.WriteLine("  (not readable through System.Reflection.Metadata; use --all with a strings dump)");
        return 0;
    }

    private static string AttributeTypeName(MetadataReader md, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind == HandleKind.MemberReference)
        {
            MemberReference reference = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (reference.Parent.Kind == HandleKind.TypeReference)
                return md.GetString(md.GetTypeReference((TypeReferenceHandle)reference.Parent).Name);
        }

        if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
            return md.GetString(md.GetTypeDefinition(method.GetDeclaringType()).Name);
        }

        return "";
    }

    private static bool LooksLikeTarget(string text)
    {
        if (text.Contains("Scene") || text.Contains("Track") || text.Contains("Gameplay")) return true;
        if (text.Contains("Awake") || text.Contains("OnEnable") || text.Contains("OnDestroy")) return true;
        if (text.StartsWith('_') && text.Length <= 6) return true;
        return false;
    }

    private static string Owner(MetadataReader md, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeReference => TypeName(md, (TypeReferenceHandle)parent),
        HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
        HandleKind.TypeSpecification => "(spec)",
        _ => "(?)",
    };

    private static string TypeName(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference reference = md.GetTypeReference(handle);
        string space = reference.Namespace.IsNil ? "" : md.GetString(reference.Namespace);
        string name = md.GetString(reference.Name);
        return space.Length == 0 ? name : space + "." + name;
    }
}
