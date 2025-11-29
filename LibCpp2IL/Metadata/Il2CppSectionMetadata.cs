namespace LibCpp2IL.Metadata;

public class Il2CppSectionMetadata : ReadableClass
{
    // Prior to version 38, this class didn't exist; it was just a pair of ints.

    public int Offset;
    public int Size;
    [Version(Min = 38)] public int Count;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Offset = reader.ReadInt32();
        Size = reader.ReadInt32();
        if (IsAtLeast(38))
        {
            Count = reader.ReadInt32();
        }
    }
}
