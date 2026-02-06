using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppInlineArrayLength : ReadableClass
{
    public int typeIndex;
    public int length; // local offset into type fields

    public Il2CppType? Type => LibCpp2IlMain.Binary?.GetType(typeIndex);
    public Il2CppTypeDefinition? TypeDefinition => LibCpp2IlMain.TheMetadata?.typeDefs[Type!.Data.ClassIndex];

    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = reader.ReadTypeIndex();
        length = reader.ReadInt32();
    }
}
