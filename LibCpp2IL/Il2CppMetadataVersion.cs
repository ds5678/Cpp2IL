using System;
using System.Buffers.Binary;
using AssetRipper.Primitives;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;

namespace LibCpp2IL;

public readonly record struct Il2CppMetadataVersion(float Value)
{
    public bool IsAtLeast(float vers) => Value >= vers;
    public bool IsLessThan(float vers) => Value < vers;
    public bool IsAtMost(float vers) => Value <= vers;
    public bool IsNot(float vers) => Math.Abs(Value - vers) > 0.001f;
    public bool Is(float vers) => Math.Abs(Value - vers) < 0.001f;

    public static implicit operator float(Il2CppMetadataVersion version) => version.Value;
    public static implicit operator Il2CppMetadataVersion(float version) => new(version);

    public static Il2CppMetadataVersion FromGlobalMetadata(ReadOnlySpan<byte> bytes, UnityVersion unityVersion)
    {
        if (bytes.Length < 8)
        {
            throw new FormatException("Metadata file too small to contain valid header");
        }

        if (!Il2CppGlobalMetadataHeader.HasMetadataHeader(bytes))
        {
            //Magic number is wrong
            throw new FormatException("Invalid or corrupt metadata (magic number check failed)");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (version is < 23 or > 105)
        {
            throw new FormatException("Unsupported metadata version found! We support 23-105, got " + version);
        }

        LibLogger.VerboseNewline($"\tIL2CPP Metadata Declares its version as {version}");

        float actualVersion;
        if (version == 24)
        {
            if (unityVersion.GreaterThanOrEquals(2020, 1, 11))
                actualVersion = 24.4f; //2020.1.11-17 were released prior to 2019.4.21, so are still on 24.4
            else if (unityVersion.GreaterThanOrEquals(2020))
                actualVersion = 24.3f; //2020.1.0-10 were released prior to to 2019.4.15, so are still on 24.3
            else if (unityVersion.GreaterThanOrEquals(2019, 4, 21))
                actualVersion = 24.5f; //2019.4.21 introduces v24.5
            else if (unityVersion.GreaterThanOrEquals(2019, 4, 15))
                actualVersion = 24.4f; //2019.4.15 introduces v24.4
            else if (unityVersion.GreaterThanOrEquals(2019, 3, 7))
                actualVersion = 24.3f; //2019.3.7 introduces v24.3
            else if (unityVersion.GreaterThanOrEquals(2019))
                actualVersion = 24.2f; //2019.1.0 introduces v24.2
            else if (unityVersion.GreaterThanOrEquals(2018, 4, 34))
                actualVersion = 24.15f; //2018.4.34 made a tiny little change which just removes HashValueIndex from AssemblyNameDefinition
            else if (unityVersion.GreaterThanOrEquals(2018, 3))
                actualVersion = 24.1f; //2018.3.0 introduces v24.1
            else
                actualVersion = version; //2017.1.0 was the first v24 version
        }
        else if (version == 27)
        {
            if (unityVersion.GreaterThanOrEquals(2021, 1))
                actualVersion = 27.2f; //2021.1 and up is v27.2, which just changes Il2CppType to have one new bit
            else if (unityVersion.GreaterThanOrEquals(2020, 2, 4))
                actualVersion = 27.1f; //2020.2.4 and above is v27.1
            else
                actualVersion = version; //2020.2 and above is v27
        }
        else if (version == 29)
        {
            if (unityVersion.GreaterThanOrEquals(2022, 1, 0, UnityVersionType.Beta, 7))
                actualVersion = 29.1f; //2022.1.0b7 introduces v29.1 which adds two new pointers to codereg
            else
                actualVersion = 29; //2021.3.0 introduces v29
        }
        else if (version == 31)
        {
            //2022.3.33 introduces v31. Unity why would you bump this on a minor version.
            //Adds one new field (return type token) to method def
            //2021.3.40 backported the new field but NOT the changes from v29.1, so there's a 31.1 now.
            if (unityVersion.GreaterThanOrEquals(2022, 3, 33, UnityVersionType.Final, 1))
                //V31 with changes in codereg
                actualVersion = 31.1f;
            else
                //v31 WITHOUT changes in codereg 
                actualVersion = 31;
        }
        else
        {
            // 6000.3.0a2 introduces v35
            // 6000.3.0a5 introduces v38
            // 6000.3.0b1 introduces v39
            // 6000.5.0a3 introduces v104
            // 6000.5.0a5 introduces v105
            actualVersion = version;
        }

        LibLogger.InfoNewline($"\tUsing actual IL2CPP Metadata version {actualVersion}");

        return actualVersion;
    }
}
