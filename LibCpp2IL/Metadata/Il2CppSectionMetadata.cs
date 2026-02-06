using System.Runtime.CompilerServices;

namespace LibCpp2IL.Metadata;

public class Il2CppSectionMetadata
{
    // Prior to version 38, this class didn't exist; it was just a pair of ints.

    internal Il2CppMetadataVersion MetadataVersion { get; set; }

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

    public void Read(EndianAwareBinaryReader reader)
    {
        Offset = reader.ReadInt32();
        Size = reader.ReadInt32();
        if (MetadataVersion.IsAtLeast(38))
        {
            Count = reader.ReadInt32();
        }
    }

    public int GetCount<T>() where T : unmanaged
    {
        return MetadataVersion.IsAtLeast(38) ? Count : Size / Unsafe.SizeOf<T>();
    }
}
