namespace LibCpp2IL.Metadata;

public class Il2CppSectionMetadata : ReadableClass
{
    // Prior to version 38, this class didn't exist; it was just a pair of ints.

    /// <summary>
    /// The byte offset of the section
    /// </summary>
    public int Offset;
    /// <summary>
    /// The size of the section in bytes
    /// </summary>
    public int Size;
    /// <summary>
    /// The number of entries in the section
    /// </summary>
    /// <remarks>
    /// This is not present on versions before 38.
    /// </remarks>
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
