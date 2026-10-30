// Pre-extracts the icon and thumbnail that bundled plugins embed in their
// assemblies, writing them beside the plugin under the exact name
// Shoko.Server's PackageImageLoader would write on first start.
//
// The server only writes those files when none exists beside the plugin. Under
// /usr/lib the service user cannot write, so without this step the first
// ScanForPlugins() throws UnauthorizedAccessException and the server never
// finishes starting.
//
// Usage (.NET 10 file-based app):
//   dotnet run extract-plugin-images.cs -- <plugins-dir>
//
// Layouts, mirroring PackageImageLoader.Load:
//   plugins/<Name>/<Name>.dll  ->  plugins/<Name>/icon.<ext>, thumbnail.<ext>
//   plugins/<Name>.dll         ->  plugins/<Name>.icon.<ext>, <Name>.thumbnail.<ext>
//
// Which resource holds each image, in the order the server resolves it:
//   1. the PackageIcon / PackageThumbnail assembly metadata stamped by
//      Shoko.BuildTools.Targets (resource named <Assembly>.PluginIcon.<ext>)
//   2. a resource whose name ends in .icon.<ext> / .thumbnail.<ext>, the
//      convention plugins built before the build tools used

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: dotnet run extract-plugin-images.cs -- <plugins-dir>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var written = 0;

foreach (var dir in Directory.GetDirectories(root))
    foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        Extract(dll, kind => Path.Combine(dir, kind));

foreach (var dll in Directory.GetFiles(root, "*.dll"))
    Extract(dll, kind => Path.ChangeExtension(dll, "." + kind));

Console.WriteLine($"extract-plugin-images: wrote {written} file(s) under {root}");
return 0;

void Extract(string dll, Func<string, string> stemFor)
{
    using var stream = File.OpenRead(dll);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata)
        return;

    var md = pe.GetMetadataReader();
    var assemblyName = md.GetString(md.GetAssemblyDefinition().Name);

    var stamped = ReadStampedImageNames(md);

    foreach (var handle in md.ManifestResources)
    {
        var resource = md.GetManifestResource(handle);
        // Only resources stored in this file, not forwarded to another module.
        if (!resource.Implementation.IsNil)
            continue;

        var name = md.GetString(resource.Name);
        // Same rule PackageImageLoader.ReadEmbedded applies: a plugin may only
        // name resources rooted in its own assembly name.
        if (!name.StartsWith(assemblyName + ".", StringComparison.Ordinal))
            continue;

        var kind = stamped.GetValueOrDefault(name) ?? KindFromName(name);
        if (kind is null)
            continue;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        var stem = stemFor(kind);
        var stemDir = Path.GetDirectoryName(stem)!;
        var stemName = Path.GetFileName(stem);
        // A file already beside the plugin wins, exactly as in the server.
        if (Directory.EnumerateFiles(stemDir, stemName + ".*").Any())
            continue;

        var target = stem + ext;
        File.WriteAllBytes(target, ReadResource(pe, resource.Offset));
        Console.WriteLine($"  {Path.GetFileName(dll)}: {name} -> {Path.GetRelativePath(root, target)}");
        written++;
    }
}

// <...>.<kind>.<ext> with kind icon/thumbnail, or the build tools' PluginIcon/PluginThumbnail.
string? KindFromName(string name)
{
    var parts = name.Split('.');
    if (parts.Length < 3)
        return null;
    return parts[^2].ToLowerInvariant() switch
    {
        "icon" or "pluginicon" => "icon",
        "thumbnail" or "pluginthumbnail" => "thumbnail",
        _ => null,
    };
}

// [assembly: AssemblyMetadata("PackageIcon", "<resource>")] and PackageThumbnail,
// as Shoko.BuildTools.Targets stamps them and PluginManager reads them first.
static Dictionary<string, string> ReadStampedImageNames(MetadataReader md)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = md.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            continue;
        var ctor = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (ctor.Parent.Kind != HandleKind.TypeReference
            || md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name) != "AssemblyMetadataAttribute")
            continue;

        // Blob: prolog 0x0001, then the two string arguments (key, value).
        var reader = md.GetBlobReader(attribute.Value);
        if (reader.ReadUInt16() != 1)
            continue;
        var key = reader.ReadSerializedString();
        var value = reader.ReadSerializedString();
        if (string.IsNullOrEmpty(value))
            continue;
        if (key == "PackageIcon")
            result[value] = "icon";
        else if (key == "PackageThumbnail")
            result[value] = "thumbnail";
    }
    return result;
}

static byte[] ReadResource(PEReader pe, long offset)
{
    var resources = pe.PEHeaders.CorHeader!.ResourcesDirectory;
    var section = pe.GetSectionData(resources.RelativeVirtualAddress);
    var reader = section.GetReader((int)offset, section.Length - (int)offset);
    var length = reader.ReadInt32();
    return reader.ReadBytes(length);
}
