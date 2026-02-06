using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AssetRipper.Primitives;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata;

public class Il2CppMetadata : ClassReadingBinaryReader
{
    public const uint MetadataMagic = 0xFAB11BAF;
    public override float MetadataVersion { get; }
    public UnityVersion UnityVersion { get; }
    public sealed override Il2CppGlobalMetadataHeader MetadataHeader => metadataHeader;

    //Disable null check as this stuff is reflected.
    public Il2CppGlobalMetadataHeader metadataHeader;
    public Il2CppAssemblyDefinition[] AssemblyDefinitions;
    public Il2CppImageDefinition[] imageDefinitions;
    public Il2CppTypeDefinition[] typeDefs;
    internal Il2CppInterfaceOffset[] interfaceOffsets;
    public uint[] VTableMethodIndices;
    public Il2CppMethodDefinition[] methodDefs;
    public Il2CppParameterDefinition[] parameterDefs;
    public Il2CppFieldDefinition[] fieldDefs;
    private Il2CppFieldDefaultValue[] fieldDefaultValues;
    private Il2CppParameterDefaultValue[] parameterDefaultValues;
    public Il2CppPropertyDefinition[] propertyDefs;
    public List<Il2CppCustomAttributeTypeRange>? attributeTypeRanges; //Removed in v29
    public Il2CppStringLiteral[] stringLiterals;
    public Il2CppMetadataUsageList[]? metadataUsageLists; //Removed in v27
    private Il2CppMetadataUsagePair[]? metadataUsagePairs; //Removed in v27
    public Il2CppRGCTXDefinition[]? RgctxDefinitions; //Moved to binary in v24.2
    
    public int[]? attributeTypes; //Removed in v29
    public List<Il2CppCustomAttributeDataRange>? AttributeDataRanges; //Added in v29
    
    public int[] interfaceIndices;

    //Moved to binary in v27.
    public Dictionary<uint, SortedDictionary<uint, uint>>? metadataUsageDic;

    public int[] nestedTypeIndices;
    public Il2CppEventDefinition[] eventDefs;
    public Il2CppGenericContainer[] genericContainers;
    public Il2CppFieldRef[] fieldRefs;
    public Il2CppGenericParameter[] genericParameters;
    public int[] constraintIndices;

    public int[] referencedAssemblies;

    private readonly Dictionary<int, Il2CppFieldDefaultValue> _fieldDefaultValueLookup = new Dictionary<int, Il2CppFieldDefaultValue>();
    private readonly Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue> _fieldDefaultLookupNew = new Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue>();

    public static Il2CppMetadata ReadFrom(byte[] bytes, UnityVersion unityVersion)
    {
        var actualVersion = Il2CppMetadataVersion.FromGlobalMetadata(bytes, unityVersion);
        var globalMetadataHeader = Il2CppGlobalMetadataHeader.ReadFrom(bytes, actualVersion, out var bytesRead);

        using var stream = new MemoryStream(bytes);
        stream.Position = bytesRead;
        return new Il2CppMetadata(stream, globalMetadataHeader, unityVersion, actualVersion);
    }

    private Il2CppMetadata(MemoryStream stream, Il2CppGlobalMetadataHeader metadataHeader, UnityVersion unityVersion, float metadataVersion) : base(stream)
    {
        UnityVersion = unityVersion;
        MetadataVersion = metadataVersion;
        this.metadataHeader = metadataHeader;

        if (metadataHeader.magicNumber != MetadataMagic)
            throw new Exception($"ERROR: Magic number mismatch. Expecting 0x{MetadataMagic:X8} but got 0x{metadataHeader.magicNumber:X8}");

        LibLogger.Verbose("\tReading image definitions...");
        var start = DateTime.Now;
        imageDefinitions = ReadMetadataClassArray<Il2CppImageDefinition>(metadataHeader.images.Offset, metadataHeader.images.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading assembly definitions...");
        start = DateTime.Now;
        AssemblyDefinitions = ReadMetadataClassArray<Il2CppAssemblyDefinition>(metadataHeader.assemblies.Offset, metadataHeader.assemblies.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading type definitions...");
        start = DateTime.Now;
        typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(metadataHeader.typeDefinitions.Offset, metadataHeader.typeDefinitions.Size);
        LibLogger.VerboseNewline($"{typeDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading interface offsets...");
        start = DateTime.Now;
        interfaceOffsets = ReadMetadataClassArray<Il2CppInterfaceOffset>(metadataHeader.interfaceOffsets.Offset, metadataHeader.interfaceOffsets.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading vtable indices...");
        start = DateTime.Now;
        VTableMethodIndices = ReadClassArrayAtRawAddr<uint>(metadataHeader.vtableMethods.Offset, metadataHeader.vtableMethods.GetCount<uint>());
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading method definitions...");
        start = DateTime.Now;
        methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(metadataHeader.methods.Offset, metadataHeader.methods.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading method parameter definitions...");
        start = DateTime.Now;
        parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(metadataHeader.parameters.Offset, metadataHeader.parameters.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading field definitions...");
        start = DateTime.Now;
        fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(metadataHeader.fields.Offset, metadataHeader.fields.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading default field values...");
        start = DateTime.Now;
        fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(metadataHeader.fieldDefaultValues.Offset, metadataHeader.fieldDefaultValues.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading default parameter values...");
        start = DateTime.Now;
        parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(metadataHeader.parameterDefaultValues.Offset, metadataHeader.parameterDefaultValues.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading property definitions...");
        start = DateTime.Now;
        propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(metadataHeader.properties.Offset, metadataHeader.properties.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading interface definitions...");
        start = DateTime.Now;
        interfaceIndices = ReadClassArrayAtRawAddr<int>(metadataHeader.interfaces.Offset, metadataHeader.interfaces.GetCount<int>());
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading nested type definitions...");
        start = DateTime.Now;
        nestedTypeIndices = ReadClassArrayAtRawAddr<int>(metadataHeader.nestedTypes.Offset, metadataHeader.nestedTypes.GetCount<int>());
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading event definitions...");
        start = DateTime.Now;
        eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(metadataHeader.events.Offset, metadataHeader.events.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading generic container definitions...");
        start = DateTime.Now;
        genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(metadataHeader.genericContainers.Offset, metadataHeader.genericContainers.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading generic parameter definitions...");
        start = DateTime.Now;
        genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(metadataHeader.genericParameters.Offset, metadataHeader.genericParameters.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading generic parameter constraint indices...");
        start = DateTime.Now;
        constraintIndices = ReadClassArrayAtRawAddr<int>(metadataHeader.genericParameterConstraints.Offset, metadataHeader.genericParameterConstraints.GetCount<int>());
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading referenced assemblies...");
        start = DateTime.Now;
        referencedAssemblies = ReadClassArrayAtRawAddr<int>(metadataHeader.referencedAssemblies.Offset, metadataHeader.referencedAssemblies.GetCount<int>());
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        //v17+ fields
        LibLogger.Verbose("\tReading string definitions...");
        start = DateTime.Now;
        stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(metadataHeader.stringLiterals.Offset, metadataHeader.stringLiterals.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        if (MetadataVersion < 24.2f)
        {
            LibLogger.Verbose("\tReading RGCTX data...");
            start = DateTime.Now;

            RgctxDefinitions = ReadMetadataClassArray<Il2CppRGCTXDefinition>(metadataHeader.rgctxEntries!.Offset, metadataHeader.rgctxEntries.Size);

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        //Removed in v27 (2020.2) and also 24.5 (2019.4.21)
        if (MetadataVersion < 27f)
        {
            LibLogger.Verbose("\tReading usage data...");
            start = DateTime.Now;
            metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(metadataHeader.metadataUsageLists!.Offset, metadataHeader.metadataUsageLists.Size);
            metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(metadataHeader.metadataUsagePairs!.Offset, metadataHeader.metadataUsagePairs.Size);

            DecipherMetadataUsage();
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        LibLogger.Verbose("\tReading field references...");
        start = DateTime.Now;
        fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(metadataHeader.fieldRefs.Offset, metadataHeader.fieldRefs.Size);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        //v21+ fields

        if (MetadataVersion < 29)
        {
            //Removed in v29
            LibLogger.Verbose("\tReading attribute types...");
            start = DateTime.Now;
            attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(metadataHeader.attributesInfo!.Offset, metadataHeader.attributesInfo.Size).ToList();
            attributeTypes = ReadClassArrayAtRawAddr<int>(metadataHeader.attributeTypes!.Offset, metadataHeader.attributeTypes.GetCount<int>());
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }
        else
        {
            //Since v29
            LibLogger.Verbose("\tReading Attribute data...");
            start = DateTime.Now;

            //Pointer array
            AttributeDataRanges = ReadReadableArrayAtRawAddr<Il2CppCustomAttributeDataRange>(metadataHeader.attributeDataRanges!.Offset, metadataHeader.attributeDataRanges.GetCount<ulong>()).ToList();
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        LibLogger.Verbose("\tBuilding Lookup Table for field defaults...");
        start = DateTime.Now;
        foreach (var il2CppFieldDefaultValue in fieldDefaultValues)
        {
            _fieldDefaultValueLookup[il2CppFieldDefaultValue.fieldIndex] = il2CppFieldDefaultValue;
            _fieldDefaultLookupNew[fieldDefs[il2CppFieldDefaultValue.fieldIndex]] = il2CppFieldDefaultValue;
        }

        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        _hasFinishedInitialRead = true;
    }
#pragma warning restore 8618

    private T[] ReadMetadataClassArray<T>(int offset, int length) where T : ReadableClass, new()
    {
        //First things first, we're going to be moving the position around a lot, so we need to lock. 
        GetLockOrThrow();

        Position = offset;

        try
        {
            //Length is in bytes, not in elements, so we need to work out the element size to know how big of an array to allocate.
            //We do this by reading the first element, then count how many bytes we read.
            var first = ReadReadableHereNoLock<T>();

            //How many bytes did we read?
            var elementSize = (int)(Position - offset);

            //For build report purposes, we track that many bytes. FillReadableArrayHereNoLock will add the rest.
            TrackRead<T>(elementSize);

            //Now we can work out how many elements there are.
            var numElements = length / elementSize;

            if (numElements == 0) {
                return [];
            }

            //And so we can allocate an array of that length, and assign the first element.
            var arr = new T[numElements];
            arr[0] = first;

            //And finally, read the rest of the elements, starting at index 1.
            FillReadableArrayHereNoLock(arr, 1);

            return arr;
        }
        finally
        {
            ReleaseLock();
        }
    }

    private void DecipherMetadataUsage()
    {
        if(metadataUsageLists == null || metadataUsagePairs == null)
            throw new InvalidOperationException("Called DecipherMetadataUsage on v27 or newer metadata");
        
        metadataUsageDic = new();
        for (var i = 1u; i <= 6u; i++)
        {
            metadataUsageDic[i] = new();
        }

        foreach (var metadataUsageList in metadataUsageLists)
        {
            for (var i = 0; i < metadataUsageList.count; i++)
            {
                var offset = metadataUsageList.start + i;
                var metadataUsagePair = metadataUsagePairs[offset];
                var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                metadataUsageDic[usage][metadataUsagePair.destinationIndex] = decodedIndex;
            }
        }
    }

    public uint GetMaxMetadataUsages()
    {
        if (metadataUsageDic == null)
            //V27+
            return 0;

        return metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
    }

    private uint GetEncodedIndexType(uint index)
    {
        return (index & 0xE0000000) >> 29;
    }

    private uint GetDecodedMethodIndex(uint index)
    {
        return index & 0x1FFFFFFFU;
    }

    //Getters for human readability
    public Il2CppFieldDefaultValue? GetFieldDefaultValueFromIndex(int index)
    {
        return _fieldDefaultValueLookup.GetOrDefault(index);
    }

    public Il2CppFieldDefaultValue? GetFieldDefaultValue(Il2CppFieldDefinition field)
    {
        return _fieldDefaultLookupNew.GetOrDefault(field);
    }

    public (int ptr, int type) GetFieldDefaultValue(int fieldIdx)
    {
        var fieldDef = fieldDefs[fieldIdx];
        var fieldType = LibCpp2IlMain.Binary!.GetType(fieldDef.typeIndex);
        if ((fieldType.Attrs & (int)FieldAttributes.HasFieldRVA) != 0)
        {
            var fieldDefault = GetFieldDefaultValueFromIndex(fieldIdx);

            if (fieldDefault == null)
                return (-1, -1);

            return (ptr: fieldDefault.dataIndex, type: fieldDefault.typeIndex);
        }

        return (-1, -1);
    }

    public Il2CppParameterDefaultValue? GetParameterDefaultValueFromIndex(int index)
    {
        return parameterDefaultValues.FirstOrDefault(x => x.parameterIndex == index);
    }

    public int GetDefaultValueFromIndex(int index)
    {
        return metadataHeader.fieldAndParameterDefaultValueData.Offset + index;
    }

    /// <summary>
    /// Read a byte array from the string data section of the metadata.
    /// </summary>
    /// <param name="index">The offset relative to the start of the string section.</param>
    /// <returns>The </returns>
    public byte[] GetByteArrayFromIndex(int index)
    {
        var offset = metadataHeader.strings.Offset + index;
        var count = ReadUnityCompressedUIntAtRawAddr(offset, out var bytesRead);
        return ReadByteArrayAtRawAddress(offset + bytesRead, (int)count);
    }

    private ConcurrentDictionary<int, string> _cachedStrings = new ConcurrentDictionary<int, string>();

    public string GetStringFromIndex(int index)
    {
        GetLockOrThrow();
        try
        {
            return ReadStringFromIndexNoReadLock(index);
        }
        finally
        {
            ReleaseLock();
        }
    }

    internal string ReadStringFromIndexNoReadLock(int index)
    {
        if (!_cachedStrings.ContainsKey(index))
            _cachedStrings[index] = ReadStringToNullNoLock(metadataHeader.strings.Offset + index);
        return _cachedStrings[index];
    }

    public Il2CppCustomAttributeTypeRange? GetCustomAttributeData(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, out int idx)
    {
        if(MetadataVersion >= 29f)
            throw new("This method is not valid for metadata versions 29 and above");
        
        idx = -1;

        if (MetadataVersion <= 24f)
        {
            idx = customAttributeIndex;
            return attributeTypeRanges![customAttributeIndex]; //Not-null assertion because we've checked version
        }

        var target = new Il2CppCustomAttributeTypeRange { token = token };

        if (imageDef.customAttributeStart < 0)
            throw new("Image has customAttributeStart < 0");
        if (imageDef.customAttributeStart + imageDef.customAttributeCount > attributeTypeRanges!.Count) //Not-null assertion because we've checked version is < 29
            throw new($"Image has customAttributeStart + customAttributeCount > attributeTypeRanges.Count ({imageDef.customAttributeStart + imageDef.customAttributeCount} > {attributeTypeRanges.Count})");

        idx = attributeTypeRanges.BinarySearch(imageDef.customAttributeStart, (int)imageDef.customAttributeCount, target, new TokenComparer());

        return idx < 0 ? null : attributeTypeRanges[idx];
    }

    public string GetStringLiteralFromIndex(uint index)
    {
        var stringLiteral = stringLiterals[index];

        return Encoding.UTF8.GetString(ReadByteArrayAtRawAddress(metadataHeader.stringLiteralData.Offset + stringLiteral.dataIndex, (int)stringLiteral.length));
    }
}
