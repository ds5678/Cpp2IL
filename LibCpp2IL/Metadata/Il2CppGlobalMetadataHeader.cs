namespace LibCpp2IL.Metadata;

public class Il2CppGlobalMetadataHeader : ReadableClass
{
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

    public override void Read(ClassReadingBinaryReader reader)
    {
        magicNumber = reader.ReadUInt32();
        version = reader.ReadInt32();
        stringLiterals = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        stringLiteralData = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        strings = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        events = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        properties = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        methods = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        parameterDefaultValues = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        fieldDefaultValues = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        fieldAndParameterDefaultValueData = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        fieldMarshaledSizes = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        parameters = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        fields = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        genericParameters = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        genericParameterConstraints = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        genericContainers = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        nestedTypes = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        interfaces = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        vtableMethods = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        interfaceOffsets = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        typeDefinitions = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();

        if (IsAtMost(24.15f))
        {
            rgctxEntries = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        }

        images = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        assemblies = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();

        if (IsLessThan(27f))
        {
            metadataUsageLists = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
            metadataUsagePairs = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        }

        fieldRefs = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        referencedAssemblies = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();

        if (IsLessThan(29f))
        {
            attributesInfo = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
            attributeTypes = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        }
        else
        {
            attributeData = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
            attributeDataRanges = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        }

        unresolvedVirtualCallParameterTypes = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        unresolvedVirtualCallParameterRanges = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        windowsRuntimeTypeNames = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();

        if (IsAtLeast(27f))
        {
            windowsRuntimeStrings = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        }

        if (IsAtLeast(24f))
        {
            exportedTypeDefinitions = reader.ReadReadableHereNoLock<Il2CppSectionMetadata>();
        }
    }
}
