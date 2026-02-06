using System;
using System.Linq;

namespace LibCpp2IL.Metadata;

public class Il2CppAssemblyDefinition : ReadableClass
{
    public int ImageIndex;
    [Version(Min = 24.1f)] public uint Token;
    [Version(Max = 24.0f)] public int CustomAttributeIndex;
    [Version(Min = 38f)] public uint ModuleToken;
    public int ReferencedAssemblyStart;
    public int ReferencedAssemblyCount;
    public Il2CppAssemblyNameDefinition AssemblyName = null!; //Late-read

    public Il2CppImageDefinition Image => LibCpp2IlMain.TheMetadata!.imageDefinitions[ImageIndex];

    public Il2CppAssemblyDefinition[] ReferencedAssemblies => ReferencedAssemblyStart < 0
        ? []
        : LibCpp2IlMain.TheMetadata!.referencedAssemblies.SubArray(ReferencedAssemblyStart, ReferencedAssemblyCount)
            .Select(idx => LibCpp2IlMain.TheMetadata.AssemblyDefinitions[idx])
            .ToArray();

    public override string ToString() => AssemblyName.ToString();

    public override void Read(ClassReadingBinaryReader reader)
    {
        ImageIndex = reader.ReadImageIndex();
        if (IsAtLeast(24.1f))
            Token = reader.ReadUInt32();
        if (IsAtMost(24.0f))
            CustomAttributeIndex = reader.ReadCustomAttributeIndex();
        if (IsAtLeast(38f))
            ModuleToken = reader.ReadUInt32();
        ReferencedAssemblyStart = reader.ReadInt32();
        ReferencedAssemblyCount = reader.ReadInt32();

        //We use ReadReadableHereNoLock because we're already in a lock, because we're in Read.
        AssemblyName = reader.ReadReadableHereNoLock<Il2CppAssemblyNameDefinition>();
    }
}
