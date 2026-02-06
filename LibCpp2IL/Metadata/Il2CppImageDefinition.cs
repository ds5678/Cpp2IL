using System.Linq;

namespace LibCpp2IL.Metadata;

public class Il2CppImageDefinition : ReadableClass
{
    public int nameIndex;
    public int assemblyIndex;

    public int firstTypeIndex;
    public uint typeCount;

    [Version(Min = 24)] public int exportedTypeStart;
    [Version(Min = 24)] public uint exportedTypeCount;

    public int entryPointIndex;
    public uint token;

    [Version(Min = 24.1f)] public int customAttributeStart;
    [Version(Min = 24.1f)] public uint customAttributeCount;

    public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

    public Il2CppTypeDefinition[]? Types => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.typeDefs.Skip(firstTypeIndex).Take((int)typeCount).ToArray();

    public override string ToString()
    {
        return $"Il2CppImageDefinition[Name={Name}]";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadStringIndex();
        assemblyIndex = reader.ReadAssemblyIndex();

        firstTypeIndex = reader.ReadTypeDefinitionIndex();
        typeCount = reader.ReadUInt32();

        if (IsAtLeast(24f))
        {
            exportedTypeStart = reader.ReadTypeDefinitionIndex();
            exportedTypeCount = reader.ReadUInt32();
        }

        entryPointIndex = reader.ReadMethodIndex();
        token = reader.ReadUInt32();

        if (IsAtLeast(24.1f))
        {
            customAttributeStart = reader.ReadCustomAttributeIndex();
            customAttributeCount = reader.ReadUInt32();
        }
    }
}
