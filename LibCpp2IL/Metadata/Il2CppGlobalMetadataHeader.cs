using System;
using System.Buffers.Binary;
using System.IO;

namespace LibCpp2IL.Metadata;

public class Il2CppGlobalMetadataHeader
{
    internal Il2CppMetadataVersion MetadataVersion { get; set; }

    public uint magicNumber;
    public int version;

#nullable disable
    public Il2CppSectionMetadata stringLiterals; // string data for managed code
    public Il2CppSectionMetadata stringLiteralData;
    public Il2CppSectionMetadata strings; // string data for metadata
    public Il2CppSectionMetadata events; // Il2CppEventDefinition
    public Il2CppSectionMetadata properties; // Il2CppPropertyDefinition
    public Il2CppSectionMetadata methods; // Il2CppMethodDefinition
    public Il2CppSectionMetadata parameterDefaultValues; // Il2CppParameterDefaultValue
    public Il2CppSectionMetadata fieldDefaultValues; // Il2CppFieldDefaultValue
    public Il2CppSectionMetadata fieldAndParameterDefaultValueData; // uint8_t
    public Il2CppSectionMetadata fieldMarshaledSizes; // Il2CppFieldMarshaledSize
    public Il2CppSectionMetadata parameters; // Il2CppParameterDefinition
    public Il2CppSectionMetadata fields; // Il2CppFieldDefinition
    public Il2CppSectionMetadata genericParameters; // Il2CppGenericParameter
    public Il2CppSectionMetadata genericParameterConstraints; // TypeIndex
    public Il2CppSectionMetadata genericContainers; // Il2CppGenericContainer
    public Il2CppSectionMetadata nestedTypes; // TypeDefinitionIndex
    public Il2CppSectionMetadata interfaces; // TypeIndex
    public Il2CppSectionMetadata vtableMethods; // EncodedMethodIndex
    public Il2CppSectionMetadata interfaceOffsets; // Il2CppInterfacePair
    public Il2CppSectionMetadata typeDefinitions; // Il2CppTypeDefinition
#nullable enable

    [Version(Max = 24.15f)] public Il2CppSectionMetadata? rgctxEntries; // Il2CppRGCTXDefinition

    [Version(Min = 104)] public Il2CppSectionMetadata? typeInlineArrays; //Il2CppInlineArrayLength

#nullable disable
    public Il2CppSectionMetadata images; // Il2CppImageDefinition
    public Il2CppSectionMetadata assemblies; // Il2CppAssemblyDefinition
#nullable enable

    [Version(Max = 24.5f)] public Il2CppSectionMetadata? metadataUsageLists; // Il2CppMetadataUsageList, Removed in v27 //Removed in v27
    [Version(Max = 24.5f)] public Il2CppSectionMetadata? metadataUsagePairs; // Il2CppMetadataUsagePair, Removed in v27 //Removed in v27

#nullable disable
    public Il2CppSectionMetadata fieldRefs; // Il2CppFieldRef
    public Il2CppSectionMetadata referencedAssemblies; // int32_t
#nullable enable

    //Pre-29 attribute data
    [Version(Max = 27.9f)] public Il2CppSectionMetadata? attributesInfo; // Il2CppCustomAttributeTypeRange
    [Version(Max = 27.9f)] public Il2CppSectionMetadata? attributeTypes; // TypeIndex

    //Post-29 attribute data
    [Version(Min = 27.9f)] public Il2CppSectionMetadata? attributeData; //uint8_t
    [Version(Min = 27.9f)] public Il2CppSectionMetadata? attributeDataRanges; //Il2CppCustomAttributeDataRange

#nullable disable
    public Il2CppSectionMetadata unresolvedVirtualCallParameterTypes; // TypeIndex
    public Il2CppSectionMetadata unresolvedVirtualCallParameterRanges; // Il2CppRange
#nullable enable

    [Version(Min = 23)] public Il2CppSectionMetadata? windowsRuntimeTypeNames; // Il2CppWindowsRuntimeTypeNamePair

    [Version(Min = 27)] public Il2CppSectionMetadata? windowsRuntimeStrings; // const char*

    [Version(Min = 24)] public Il2CppSectionMetadata? exportedTypeDefinitions; // TypeDefinitionIndex

    public void Read(EndianAwareBinaryReader reader)
    {
        magicNumber = reader.ReadUInt32();
        version = reader.ReadInt32();
        stringLiterals = ReadSectionMetadata(reader);
        stringLiteralData = ReadSectionMetadata(reader);
        strings = ReadSectionMetadata(reader);
        events = ReadSectionMetadata(reader);
        properties = ReadSectionMetadata(reader);
        methods = ReadSectionMetadata(reader);
        parameterDefaultValues = ReadSectionMetadata(reader);
        fieldDefaultValues = ReadSectionMetadata(reader);
        fieldAndParameterDefaultValueData = ReadSectionMetadata(reader);
        fieldMarshaledSizes = ReadSectionMetadata(reader);
        parameters = ReadSectionMetadata(reader);
        fields = ReadSectionMetadata(reader);
        genericParameters = ReadSectionMetadata(reader);
        genericParameterConstraints = ReadSectionMetadata(reader);
        genericContainers = ReadSectionMetadata(reader);
        nestedTypes = ReadSectionMetadata(reader);
        interfaces = ReadSectionMetadata(reader);
        vtableMethods = ReadSectionMetadata(reader);
        interfaceOffsets = ReadSectionMetadata(reader);
        typeDefinitions = ReadSectionMetadata(reader);

        if (MetadataVersion.IsAtMost(24.15f))
        {
            rgctxEntries = ReadSectionMetadata(reader);
        }

        if (MetadataVersion.IsAtLeast(104))
        {
            typeInlineArrays = ReadSectionMetadata(reader);
        }

        images = ReadSectionMetadata(reader);
        assemblies = ReadSectionMetadata(reader);

        if (MetadataVersion.IsLessThan(27f))
        {
            metadataUsageLists = ReadSectionMetadata(reader);
            metadataUsagePairs = ReadSectionMetadata(reader);
        }

        fieldRefs = ReadSectionMetadata(reader);
        referencedAssemblies = ReadSectionMetadata(reader);

        if (MetadataVersion.IsLessThan(29f))
        {
            attributesInfo = ReadSectionMetadata(reader);
            attributeTypes = ReadSectionMetadata(reader);
        }
        else
        {
            attributeData = ReadSectionMetadata(reader);
            attributeDataRanges = ReadSectionMetadata(reader);
        }

        unresolvedVirtualCallParameterTypes = ReadSectionMetadata(reader);
        unresolvedVirtualCallParameterRanges = ReadSectionMetadata(reader);
        windowsRuntimeTypeNames = ReadSectionMetadata(reader);

        if (MetadataVersion.IsAtLeast(27f))
        {
            windowsRuntimeStrings = ReadSectionMetadata(reader);
        }

        if (MetadataVersion.IsAtLeast(24f))
        {
            exportedTypeDefinitions = ReadSectionMetadata(reader);
        }
    }

    private Il2CppSectionMetadata ReadSectionMetadata(EndianAwareBinaryReader reader)
    {
        var section = new Il2CppSectionMetadata() { MetadataVersion = MetadataVersion };
        section.Read(reader);
        return section;
    }

    public static Il2CppGlobalMetadataHeader ReadFrom(byte[] bytes, Il2CppMetadataVersion metadataVersion, out int bytesRead)
    {
        using var memoryStream = new MemoryStream(bytes);
        using var reader = new EndianAwareBinaryReader(memoryStream);
        var header = new Il2CppGlobalMetadataHeader { MetadataVersion = metadataVersion };
        header.Read(reader);
        bytesRead = (int)memoryStream.Position;
        return header;
    }

    public static bool HasMetadataHeader(ReadOnlySpan<byte> bytes) => bytes.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0xFAB11BAF;
}
