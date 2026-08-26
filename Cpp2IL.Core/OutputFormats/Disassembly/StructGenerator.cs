using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cpp2IL.Core;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class StructGenerator
    {
        private Il2CppMetadata metadata => Cpp2IlApi.CurrentAppContext!.Metadata;
        private Il2CppBinary il2Cpp => Cpp2IlApi.CurrentAppContext!.Binary;
        private readonly Dictionary<Il2CppTypeDefinition, string> typeDefImageNames = new();
        private readonly HashSet<string> structNameHashSet = new(StringComparer.Ordinal);
        private readonly List<StructInfo> structInfoList = new();
        private readonly Dictionary<string, StructInfo> structInfoWithStructName = new();
        private readonly HashSet<StructInfo> structCache = new();
        private readonly Dictionary<Il2CppTypeDefinition, string> structNameDic = new();
        private readonly Dictionary<ulong, string> genericClassStructNameDic = new();
        private readonly Dictionary<string, Il2CppType> nameGenericClassDic = new();
        private readonly List<ulong> genericClassList = new();
        private readonly StringBuilder arrayClassHeader = new();
        private readonly StringBuilder methodInfoHeader = new();
        private static readonly HashSet<ulong> methodInfoCache = new();
        private static readonly HashSet<string> keyword = new(StringComparer.Ordinal)
        { "klass", "monitor", "register", "_cs", "auto", "friend", "template", "flat", "default", "_ds", "interrupt",
            "unsigned", "signed", "asm", "if", "case", "break", "continue", "do", "new", "_", "short", "union", "class", "namespace"};
        private static readonly HashSet<string> specialKeywords = new(StringComparer.Ordinal)
        { "inline", "near", "far" };

        private Dictionary<Il2CppMethodSpec, ulong> methodSpecGenericMethodPointers = new();

        public void WriteScript(string outputDir)
        {
            var json = new ScriptJson();
            // Generate a unique name
            for (var imageIndex = 0; imageIndex < metadata.imageDefinitions.Length; imageIndex++)
            {
                var imageDef = metadata.imageDefinitions[imageIndex];
                var imageName = imageDef.Name!;
                foreach (var typeDef in imageDef.Types ?? [])
                {
                    typeDefImageNames.Add(typeDef, imageName);
                    CreateStructNameDic(typeDef);
                }
            }

            foreach (var table in metadata.genericMethodTables)
            {
                var genericMethodIndex = table.GenericMethodIndex;
                var genericMethodPointerIndex = table.methodIndex;

                var methodSpec = metadata.GetMethodSpec(table.GenericMethodIndex);

                methodSpecGenericMethodPointers.Add(methodSpec, il2Cpp._genericMethodPointers[table.methodIndex]);
            }

            // Generate a dictionary that will be used later to process the generalized instances.
            foreach (var il2CppType in il2Cpp.AllTypes.Where(x => x.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST))
            {
                var genericClass = il2Cpp.ReadReadableAtVirtualAddress<Il2CppGenericClass>(il2CppType.Data.GenericClass);
                var typeDef = genericClass.TypeDefinition;
                if (typeDef == null)
                {
                    continue;
                }
                var typeBaseName = structNameDic[typeDef];
                var typeToReplaceName = FixName(GetTypeDefName(typeDef, true, true));
                var typeReplaceName = FixName(GetTypeName(il2CppType, true, false));
                var typeStructName = typeBaseName.Replace(typeToReplaceName, typeReplaceName);
                nameGenericClassDic[typeStructName] = il2CppType;
                genericClassStructNameDic[il2CppType.Data.GenericClass] = typeStructName;
            }
            // handler
            foreach (var imageDef in metadata.imageDefinitions)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                foreach (var typeDef in imageDef.Types ?? [])
                {
                    AddStruct(typeDef);
                    var typeName = GetTypeDefName(typeDef, true, true);
                    foreach (var methodDef in typeDef.Methods ?? [])
                    {
                        var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                        var methodPointer = methodDef.MethodPointer;
                        if (methodPointer > 0)
                        {
                            var methodTypeSignature = new List<Il2CppTypeEnum>();
                            var scriptMethod = new ScriptMethod();
                            json.ScriptMethod.Add(scriptMethod);
                            scriptMethod.Address = il2Cpp.GetRva(methodPointer);
                            var methodFullName = typeName + "$$" + methodName;
                            scriptMethod.Name = methodFullName;

                            var methodReturnType = methodDef.RawReturnType!;
                            var returnType = ParseType(methodReturnType);
                            if (methodReturnType.Byref == 1)
                            {
                                returnType += "*";
                            }
                            methodTypeSignature.Add(methodReturnType.Byref == 1 ? Il2CppTypeEnum.IL2CPP_TYPE_PTR : methodReturnType.Type);
                            var signature = $"{returnType} {FixName(methodFullName)} (";
                            var parameterStrs = new List<string>();
                            if ((methodDef.flags & METHOD_ATTRIBUTE_STATIC) == 0)
                            {
                                var thisType = ParseType(il2Cpp.GetType(typeDef.ByvalTypeIndex));
                                methodTypeSignature.Add(il2Cpp.GetType(typeDef.ByvalTypeIndex).Type);
                                parameterStrs.Add($"{thisType} __this");
                            }
                            else if (metadata.MetadataVersion <= 24)
                            {
                                methodTypeSignature.Add(Il2CppTypeEnum.IL2CPP_TYPE_PTR);
                                parameterStrs.Add($"Il2CppObject* __this");
                            }
                            foreach (var parameterDef in methodDef.InternalParameterData ?? [])
                            {
                                var parameterName = parameterDef.Name!;
                                var parameterType = parameterDef.RawType!;
                                var parameterCType = ParseType(parameterType);
                                if (parameterType.Byref == 1)
                                {
                                    parameterCType += "*";
                                }
                                methodTypeSignature.Add(parameterType.Byref == 1 ? Il2CppTypeEnum.IL2CPP_TYPE_PTR : parameterType.Type);
                                parameterStrs.Add($"{parameterCType} {FixName(parameterName)}");
                            }
                            methodTypeSignature.Add(Il2CppTypeEnum.IL2CPP_TYPE_PTR);
                            parameterStrs.Add("const MethodInfo* method");
                            signature += string.Join(", ", parameterStrs);
                            signature += ");";
                            scriptMethod.Signature = signature;
                            scriptMethod.TypeSignature = GetMethodTypeSignature(methodTypeSignature);
                        }
                        //Generic Instance Functions
                        if (il2Cpp.ConcreteGenericMethods.TryGetValue(methodDef, out var methodSpecs))
                        {
                            foreach (var methodSpec in methodSpecs)
                            {
                                var genericMethodPointer = methodSpecGenericMethodPointers[methodSpec.MethodSpec];
                                if (genericMethodPointer > 0)
                                {
                                    var methodTypeSignature = new List<Il2CppTypeEnum>();
                                    var scriptMethod = new ScriptMethod();
                                    json.ScriptMethod.Add(scriptMethod);
                                    scriptMethod.Address = il2Cpp.GetRva(genericMethodPointer);
                                    var methodInfoName = $"MethodInfo_{scriptMethod.Address:X}";
                                    var structTypeName = structNameDic[typeDef];
                                    var rgctxs = GenerateRGCTX(imageName, methodDef);
                                    if (methodInfoCache.Add(genericMethodPointer))
                                    {
                                        GenerateMethodInfo(methodInfoName, structTypeName, rgctxs);
                                    }
                                    (var methodSpecTypeName, var methodSpecMethodName) = GetMethodSpecName(methodSpec.MethodSpec, true);
                                    var methodFullName = methodSpecTypeName + "$$" + methodSpecMethodName;
                                    scriptMethod.Name = methodFullName;

                                    (Il2CppGenericInst? classInst, Il2CppGenericInst? methodInst) = GetMethodSpecGenericContext(methodSpec.MethodSpec);
                                    var methodReturnType = methodDef.RawReturnType!;
                                    var returnType = ParseType(methodReturnType, classInst, methodInst);
                                    if (methodReturnType.Byref == 1)
                                    {
                                        returnType += "*";
                                    }
                                    methodTypeSignature.Add(methodReturnType.Byref == 1 ? Il2CppTypeEnum.IL2CPP_TYPE_PTR : methodReturnType.Type);
                                    var signature = $"{returnType} {FixName(methodFullName)} (";
                                    var parameterStrs = new List<string>();
                                    if ((methodDef.flags & METHOD_ATTRIBUTE_STATIC) == 0)
                                    {
                                        string thisType;
                                        if (methodSpec.MethodSpec.classIndexIndex.IsNonNull)
                                        {
                                            var typeBaseName = structNameDic[typeDef];
                                            var typeToReplaceName = FixName(typeName);
                                            var typeReplaceName = FixName(methodSpecTypeName);
                                            var typeStructName = typeBaseName.Replace(typeToReplaceName, typeReplaceName);
                                            if (nameGenericClassDic.TryGetValue(typeStructName, out var il2CppType))
                                            {
                                                thisType = ParseType(il2CppType);
                                                methodTypeSignature.Add(il2CppType.Type);
                                            }
                                            else
                                            {
                                                //There is no separate generic instance class
                                                thisType = ParseType(il2Cpp.GetType(typeDef.ByvalTypeIndex));
                                                methodTypeSignature.Add(il2Cpp.GetType(typeDef.ByvalTypeIndex).Type);
                                            }
                                        }
                                        else
                                        {
                                            thisType = ParseType(il2Cpp.GetType(typeDef.ByvalTypeIndex));
                                            methodTypeSignature.Add(il2Cpp.GetType(typeDef.ByvalTypeIndex).Type);
                                        }
                                        parameterStrs.Add($"{thisType} __this");
                                    }
                                    else if (metadata.MetadataVersion <= 24)
                                    {
                                        methodTypeSignature.Add(Il2CppTypeEnum.IL2CPP_TYPE_PTR);
                                        parameterStrs.Add($"Il2CppObject* __this");
                                    }
                                    foreach (var parameterDef in methodDef.InternalParameterData ?? [])
                                    {
                                        var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                                        var parameterType = parameterDef.RawType!;
                                        var parameterCType = ParseType(parameterType, classInst, methodInst);
                                        if (parameterType.Byref == 1)
                                        {
                                            parameterCType += "*";
                                        }
                                        methodTypeSignature.Add(parameterType.Byref == 1 ? Il2CppTypeEnum.IL2CPP_TYPE_PTR : parameterType.Type);
                                        parameterStrs.Add($"{parameterCType} {FixName(parameterName)}");
                                    }
                                    methodTypeSignature.Add(Il2CppTypeEnum.IL2CPP_TYPE_PTR);
                                    parameterStrs.Add($"const {methodInfoName}* method");
                                    signature += string.Join(", ", parameterStrs);
                                    signature += ");";
                                    scriptMethod.Signature = signature;
                                    scriptMethod.TypeSignature = GetMethodTypeSignature(methodTypeSignature);
                                }
                            }
                        }
                    }
                }
            }
            //Handling function range
            List<ulong> orderedPointers;
            if (metadata.MetadataVersion >= 24.2)
            {
                orderedPointers = new List<ulong>();
                for (var i = 0; i < il2Cpp.CodeGenModulesCount; i++)
                {
                    orderedPointers.AddRange(il2Cpp.GetCodegenModuleMethodPointers(i));
                }
            }
            else
            {
                orderedPointers = il2Cpp._methodPointers.ToList();
            }
            orderedPointers.AddRange(il2Cpp._genericMethodPointers);
            orderedPointers.AddRange(il2Cpp._invokerPointers);
            if (metadata.MetadataVersion < 29)
            {
                orderedPointers.AddRange(il2Cpp.AllCustomAttributeGenerators);
            }
            if (metadata.MetadataVersion >= 22)
            {
                if (il2Cpp._codeRegistration.reversePInvokeWrappers != 0)
                {
                    var pointers = il2Cpp.ReadNUintArrayAtVirtualAddress(il2Cpp._codeRegistration.reversePInvokeWrappers, (long)il2Cpp._codeRegistration.reversePInvokeWrapperCount);
                    orderedPointers.AddRange(pointers);
                }

                if (il2Cpp._codeRegistration.unresolvedVirtualCallPointers != 0)
                {
                    var pointers = il2Cpp.ReadNUintArrayAtVirtualAddress(il2Cpp._codeRegistration.unresolvedVirtualCallPointers, (long)il2Cpp._codeRegistration.unresolvedVirtualCallCount);
                    orderedPointers.AddRange(pointers);
                }
            }
            //TODO interopData also contains the function
            orderedPointers = orderedPointers.Distinct().OrderBy(x => x).ToList();
            orderedPointers.Remove(0);
            json.Addresses = new ulong[orderedPointers.Count];
            for (int i = 0; i < orderedPointers.Count; i++)
            {
                json.Addresses[i] = il2Cpp.GetRva(orderedPointers[i]);
            }
            // Processing MetadataUsage
            //if (metadata.MetadataVersion >= 27)
            if (false)
            {
                //var sectionHelper = GetSectionHelper();
                //foreach (var sec in sectionHelper.Data)
                {
                    //il2Cpp.Position = sec.offset;
                    //var end = Math.Min(sec.offsetEnd, il2Cpp.Length) - il2Cpp.PointerSize;
                    //while (il2Cpp.Position < end)
                    {
                        var addr = il2Cpp.Position;
                        var metadataValue = il2Cpp.ReadNUint();
                        var position = il2Cpp.Position;
                        if (metadataValue < uint.MaxValue)
                        {
                            var encodedToken = (uint)metadataValue;
                            var usage = metadata.GetEncodedIndexType(encodedToken);
                            if (usage > 0 && usage <= 6)
                            {
                                var decodedIndex = metadata.GetDecodedMethodIndex(encodedToken);
                                if (metadataValue == ((usage << 29) | (decodedIndex << 1)) + 1)
                                {
                                    var va = il2Cpp.MapRawAddressToVirtual((uint)addr);
                                    if (va > 0)
                                    {
                                        switch ((MetadataUsageType)usage)
                                        {
                                            case 0:
                                                break;
                                            case MetadataUsageType.TypeInfo:
                                                if (decodedIndex < il2Cpp.AllTypes.Length)
                                                {
                                                    AddMetadataUsageTypeInfo(json, decodedIndex, va);
                                                }
                                                break;
                                            case MetadataUsageType.Type:
                                                if (decodedIndex < il2Cpp.AllTypes.Length)
                                                {
                                                    AddMetadataUsageIl2CppType(json, decodedIndex, va);
                                                }
                                                break;
                                            case MetadataUsageType.MethodDef:
                                                if (decodedIndex < metadata.methodDefs.Length)
                                                {
                                                    AddMetadataUsageMethodDef(json, decodedIndex, va);
                                                }
                                                break;
                                            case MetadataUsageType.FieldInfo:
                                                if (decodedIndex < metadata.fieldRefs.Length)
                                                {
                                                    AddMetadataUsageFieldInfo(json, decodedIndex, va);
                                                }
                                                break;
                                            case MetadataUsageType.StringLiteral:
                                                //if (decodedIndex < metadata.stringLiterals.Length)
                                                {
                                                    AddMetadataUsageStringLiteral(json, decodedIndex, va);
                                                }
                                                break;
                                            case MetadataUsageType.MethodRef:
                                                if (decodedIndex < metadata.AllGenericMethodSpecs.Length)
                                                {
                                                    AddMetadataUsageMethodRef(json, decodedIndex, va);
                                                }
                                                break;
                                        }
                                        if (il2Cpp.Position != position)
                                        {
                                            il2Cpp.Position = position;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (metadata.MetadataVersion > 16 && metadata.MetadataVersion < 27)
            {
                foreach (var i in metadata.metadataUsageDic![(uint)MetadataUsageType.TypeInfo])
                {
                    AddMetadataUsageTypeInfo(json, i.Value, il2Cpp.GetRawMetadataUsage(i.Key));
                }
                foreach (var i in metadata.metadataUsageDic[(uint)MetadataUsageType.Type])
                {
                    AddMetadataUsageIl2CppType(json, i.Value, il2Cpp.GetRawMetadataUsage(i.Key));
                }
                foreach (var i in metadata.metadataUsageDic[(uint)MetadataUsageType.MethodDef])
                {
                    AddMetadataUsageMethodDef(json, i.Value, il2Cpp.GetRawMetadataUsage(i.Key));
                }
                foreach (var i in metadata.metadataUsageDic[(uint)MetadataUsageType.FieldInfo])
                {
                    AddMetadataUsageFieldInfo(json, i.Value, il2Cpp.GetRawMetadataUsage(i.Key));
                }
                foreach (var i in metadata.metadataUsageDic[(uint)MetadataUsageType.StringLiteral])
                {
                    AddMetadataUsageStringLiteral(json, i.Value, il2Cpp.GetRawMetadataUsage(i.Key));
                }
                foreach (var i in metadata.metadataUsageDic[(uint)MetadataUsageType.MethodRef])
                {
                    AddMetadataUsageMethodRef(json, i.Value, il2Cpp.GetRawMetadataUsage(i.Key));
                }
            }
            //Outputs a separate StringLiteral
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "stringliteral.json"), JsonSerializer.Serialize(json.ScriptString, DisassemblySerializerContext.Default.ListScriptString), new UTF8Encoding(false));
            //write to a file
            File.WriteAllText(Path.Combine(outputDir, "script.json"), JsonSerializer.Serialize(json, DisassemblySerializerContext.Default.ScriptJson), new UTF8Encoding(false));

            //il2cpp.h
            for (int i = 0; i < genericClassList.Count; i++)
            {
                var pointer = genericClassList[i];
                AddGenericClassStruct(pointer);
            }
            var headerStruct = new StringBuilder();
            foreach (var info in structInfoList)
            {
                structInfoWithStructName.Add(info.TypeName + "_o", info);
            }
            foreach (var info in structInfoList)
            {
                headerStruct.Append(RecursionStructInfo(info));
            }
            var sb = new StringBuilder();
            sb.Append(HeaderConstants.GenericHeader);
            switch (metadata.MetadataVersion)
            {
                case 22f:
                    sb.Append(HeaderConstants.HeaderV22);
                    break;
                case 23f:
                case 24f:
                    sb.Append(HeaderConstants.HeaderV240);
                    break;
                case 24.1f:
                    sb.Append(HeaderConstants.HeaderV241);
                    break;
                case 24.2f:
                case 24.3f:
                case 24.4f:
                case 24.5f:
                    sb.Append(HeaderConstants.HeaderV242);
                    break;
                case 27f:
                case 27.1f:
                case 27.2f:
                    sb.Append(HeaderConstants.HeaderV27);
                    break;
                case 29f:
                case 29.1f:
                case 31f:
                    sb.Append(HeaderConstants.HeaderV29);
                    break;
                default:
                    sb.Append(HeaderConstants.HeaderV29);
                    Console.WriteLine($"WARNING: This il2cpp version [{metadata.MetadataVersion}] does not support generating .h files");
                    break;
                    //return;
            }
            sb.Append(headerStruct);
            sb.Append(arrayClassHeader);
            sb.Append(methodInfoHeader);
            File.WriteAllText(Path.Combine(outputDir, "il2cpp.h"), sb.ToString());
        }

        public (Il2CppGenericInst? ClassInst, Il2CppGenericInst? MethodInst) GetMethodSpecGenericContext(Il2CppMethodSpec methodSpec)
        {
            return (methodSpec.classIndexIndex.IsNonNull ? methodSpec.GenericClassInst : null, methodSpec.methodIndexIndex.IsNonNull ? methodSpec.GenericMethodInst : null);
        }

        private void AddMetadataUsageTypeInfo(ScriptJson json, uint index, ulong address)
        {
            var type = il2Cpp.AllTypes[index];
            var typeName = GetTypeName(type, true, false);
            var scriptMetadata = new ScriptMetadata();
            json.ScriptMetadata.Add(scriptMetadata);
            scriptMetadata.Address = il2Cpp.GetRva(address);
            scriptMetadata.Name = typeName + "_TypeInfo";
            var signature = GetIl2CppStructName(type);
            if (signature.EndsWith("_array"))
            {
                scriptMetadata.Signature = "Il2CppClass*";
            }
            else
            {
                scriptMetadata.Signature = FixName(signature) + "_c*";
            }
        }

        private void AddMetadataUsageIl2CppType(ScriptJson json, uint index, ulong address)
        {
            var type = il2Cpp.AllTypes[index];
            var typeName = GetTypeName(type, true, false);
            var scriptMetadata = new ScriptMetadata();
            json.ScriptMetadata.Add(scriptMetadata);
            scriptMetadata.Address = il2Cpp.GetRva(address);
            scriptMetadata.Name = typeName + "_var";
            scriptMetadata.Signature = "Il2CppType*";
        }

        private void AddMetadataUsageMethodDef(ScriptJson json, uint index, ulong address)
        {
            var methodDef = metadata.methodDefs[index];
            var typeDef = methodDef.DeclaringType!;
            var typeName = GetTypeDefName(typeDef, true, true);
            var methodName = typeName + "." + metadata.GetStringFromIndex(methodDef.nameIndex) + "()";
            var scriptMetadataMethod = new ScriptMetadataMethod();
            json.ScriptMetadataMethod.Add(scriptMetadataMethod);
            scriptMetadataMethod.Address = il2Cpp.GetRva(address);
            scriptMetadataMethod.Name = "Method$" + methodName;
            var imageName = typeDefImageNames[typeDef];
            var methodPointer = methodDef.MethodPointer;
            if (methodPointer > 0)
            {
                scriptMetadataMethod.MethodAddress = il2Cpp.GetRva(methodPointer);
            }
        }

        private void AddMetadataUsageFieldInfo(ScriptJson json, uint index, ulong address)
        {
            var fieldRef = metadata.fieldRefs[index];
            var type = il2Cpp.GetType(fieldRef.typeIndex);
            var typeDef = GetTypeDefinition(type);
            var fieldDef = metadata.GetFieldDefinitionFromIndex(typeDef.FirstFieldIdx + fieldRef.fieldIndex);
            var fieldName = GetTypeName(type, true, false) + "." + metadata.GetStringFromIndex(fieldDef.nameIndex);
            var scriptMetadata = new ScriptMetadata();
            json.ScriptMetadata.Add(scriptMetadata);
            scriptMetadata.Address = il2Cpp.GetRva(address);
            scriptMetadata.Name = "Field$" + fieldName;
        }

        private void AddMetadataUsageStringLiteral(ScriptJson json, uint index, ulong address)
        {
            var scriptString = new ScriptString();
            json.ScriptString.Add(scriptString);
            scriptString.Address = il2Cpp.GetRva(address);
            scriptString.Value = metadata.GetStringLiteralFromIndex(index);
        }

        private void AddMetadataUsageMethodRef(ScriptJson json, uint index, ulong address)
        {
            var methodSpec = metadata.AllGenericMethodSpecs[index];
            var scriptMetadataMethod = new ScriptMetadataMethod();
            json.ScriptMetadataMethod.Add(scriptMetadataMethod);
            scriptMetadataMethod.Address = il2Cpp.GetRva(address);
            (var methodSpecTypeName, var methodSpecMethodName) = GetMethodSpecName(methodSpec, true);
            scriptMetadataMethod.Name = "Method$" + methodSpecTypeName + "." + methodSpecMethodName + "()";
            if (methodSpecGenericMethodPointers.TryGetValue(methodSpec, out var genericMethodPointer))
            {
                if (genericMethodPointer > 0)
                {
                    scriptMetadataMethod.MethodAddress = il2Cpp.GetRva(genericMethodPointer);
                }
            }
        }

        private static string FixName(string str)
        {
            if (keyword.Contains(str))
            {
                str = "_" + str;
            }
            else if (specialKeywords.Contains(str))
            {
                str = "_" + str + "_";
            }

            if (Regex.IsMatch(str, "^[0-9]"))
            {
                return "_" + str;
            }
            else
            {
                return Regex.Replace(str, "[^a-zA-Z0-9_]", "_");
            }
        }

        private string ParseType(Il2CppType il2CppType, Il2CppGenericInst? classInst = null, Il2CppGenericInst? methodInst = null)
        {
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return "void";
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return "bool";
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return "uint16_t"; //Il2CppChar
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return "int8_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return "uint8_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return "int16_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return "uint16_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return "int32_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return "uint32_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return "int64_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return "uint64_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return "float";
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return "double";
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return "System_String_o*";
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        return ParseType(oriType) + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = il2CppType.CoerceToUnderlyingTypeDefinition();
                        if (typeDef.IsEnumType)
                        {
                            return ParseType(typeDef.EnumUnderlyingType);
                        }
                        return structNameDic[typeDef] + "_o";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                    {
                        var typeDef = il2CppType.CoerceToUnderlyingTypeDefinition();
                        return structNameDic[typeDef] + "_o*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (classInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = classInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return ParseType(type);
                        }
                        return "Il2CppObject*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2CppType.GetArrayType();
                        var elementType = il2Cpp.GetIl2CppTypeFromPointer(arrayType.etype);
                        var elementStructName = GetIl2CppStructName(elementType, classInst, methodInst);
                        var typeStructName = elementStructName + "_array";
                        if (structNameHashSet.Add(typeStructName))
                        {
                            ParseArrayClassStruct(elementType, classInst, methodInst);
                        }
                        return typeStructName + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2CppType.GetGenericClass();
                        var typeDef = genericClass.TypeDefinition;//genericClass.TypeDefinition;
                        var typeStructName = genericClassStructNameDic[il2CppType.Data.GenericClass];
                        if (structNameHashSet.Add(typeStructName))
                        {
                            genericClassList.Add(il2CppType.Data.GenericClass);
                        }
                        if (typeDef.IsValueType)
                        {
                            if (typeDef.IsEnumType)
                            {
                                return ParseType(typeDef.EnumUnderlyingType);
                            }
                            return typeStructName + "_o";
                        }
                        return typeStructName + "_o*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return "Il2CppObject*";
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return "intptr_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return "uintptr_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return "Il2CppObject*";
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        var elementStructName = GetIl2CppStructName(elementType, classInst, methodInst);
                        var typeStructName = elementStructName + "_array";
                        if (structNameHashSet.Add(typeStructName))
                        {
                            ParseArrayClassStruct(elementType, classInst, methodInst);
                        }
                        return typeStructName + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (methodInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = methodInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return ParseType(type);
                        }
                        return "Il2CppObject*";
                    }
                default:
                    throw new NotSupportedException();
            }
        }
        public static string GetMethodTypeSignature(List<Il2CppTypeEnum> types)
        {
            string signature = string.Empty;
            foreach (Il2CppTypeEnum type in types)
            {
                signature += type switch
                {
                    Il2CppTypeEnum.IL2CPP_TYPE_VOID => "v",
                    Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN or Il2CppTypeEnum.IL2CPP_TYPE_CHAR or Il2CppTypeEnum.IL2CPP_TYPE_I1 or Il2CppTypeEnum.IL2CPP_TYPE_U1 or Il2CppTypeEnum.IL2CPP_TYPE_I2 or Il2CppTypeEnum.IL2CPP_TYPE_U2 or Il2CppTypeEnum.IL2CPP_TYPE_I4 or Il2CppTypeEnum.IL2CPP_TYPE_U4 => "i",
                    Il2CppTypeEnum.IL2CPP_TYPE_I8 or Il2CppTypeEnum.IL2CPP_TYPE_U8 => "j",
                    Il2CppTypeEnum.IL2CPP_TYPE_R4 => "f",
                    Il2CppTypeEnum.IL2CPP_TYPE_R8 => "d",
                    Il2CppTypeEnum.IL2CPP_TYPE_STRING or Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE or Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_ARRAY or Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST or Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF or Il2CppTypeEnum.IL2CPP_TYPE_I or Il2CppTypeEnum.IL2CPP_TYPE_U or Il2CppTypeEnum.IL2CPP_TYPE_OBJECT or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY or Il2CppTypeEnum.IL2CPP_TYPE_MVAR => "i",
                    _ => throw new NotSupportedException(),
                };
            }
            return signature;
        }

        private void AddStruct(Il2CppTypeDefinition typeDef)
        {
            var structInfo = new StructInfo();
            structInfoList.Add(structInfo);
            structInfo.TypeName = structNameDic[typeDef];
            structInfo.IsValueType = typeDef.IsValueType;
            AddParents(typeDef, structInfo);
            AddFields(typeDef, structInfo, null, null);
            AddVTableMethod(structInfo, typeDef);
            AddRGCTX(structInfo, typeDef);
        }

        private void AddGenericClassStruct(ulong pointer)
        {
            var genericClass = il2Cpp.ReadReadableAtVirtualAddress<Il2CppGenericClass>(pointer);
            var typeDef = genericClass.TypeDefinition;
            var structInfo = new StructInfo();
            structInfoList.Add(structInfo);
            structInfo.TypeName = genericClassStructNameDic[pointer];
            structInfo.IsValueType = typeDef.IsValueType;
            AddParents(typeDef, structInfo);
            AddFields(typeDef, structInfo, genericClass.Context.ClassInst, null);
            AddVTableMethod(structInfo, typeDef);
        }

        private void AddParents(Il2CppTypeDefinition typeDef, StructInfo structInfo)
        {
            if (!typeDef.IsValueType && !typeDef.IsEnumType)
            {
                if (typeDef.ParentIndex.IsNonNull)
                {
                    var parent = il2Cpp.GetType(typeDef.ParentIndex);
                    if (parent.Type != Il2CppTypeEnum.IL2CPP_TYPE_OBJECT)
                    {
                        structInfo.Parent = GetIl2CppStructName(parent);
                    }
                }
            }
        }

        private void AddFields(Il2CppTypeDefinition typeDef, StructInfo structInfo, Il2CppGenericInst? classInst, Il2CppGenericInst? methodInst)
        {
            var fields = typeDef.Fields;
            if (fields is { Length: > 0 })
            {
                var cache = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < fields.Length; ++i)
                {
                    var fieldDef = fields[i];
                    var fieldType = fieldDef.RawFieldType!;
                    if ((fieldType.Attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                    {
                        continue;
                    }
                    var structFieldInfo = new StructFieldInfo
                    {
                        FieldTypeName = ParseType(fieldType, classInst, methodInst)
                    };
                    var fieldName = FixName(metadata.GetStringFromIndex(fieldDef.nameIndex));
                    if (!cache.Add(fieldName))
                    {
                        fieldName = $"_{i}_{fieldName}";
                    }
                    structFieldInfo.FieldName = fieldName;
                    structFieldInfo.IsValueType = IsValueType(fieldType, classInst, methodInst);
                    structFieldInfo.IsCustomType = IsCustomType(fieldType, classInst, methodInst);
                    if ((fieldType.Attrs & FIELD_ATTRIBUTE_STATIC) != 0)
                    {
                        structInfo.StaticFields.Add(structFieldInfo);
                    }
                    else
                    {
                        structInfo.Fields.Add(structFieldInfo);
                    }
                }
            }
        }

        private void AddVTableMethod(StructInfo structInfo, Il2CppTypeDefinition typeDef)
        {
            var dic = new SortedDictionary<int, Il2CppMethodDefinition>();
            var vtable = typeDef.VTable;
            foreach (var vtableEntry in vtable)
            {
                var methodDef = vtableEntry?.Type switch
                {
                    MetadataUsageType.MethodDef => vtableEntry.AsMethod(),
                    MetadataUsageType.MethodRef => vtableEntry.AsGenericMethodRef().BaseMethod,
                    _ => null
                };
                if (methodDef is not null && methodDef.slot != ushort.MaxValue)
                {
                    dic[methodDef.slot] = methodDef;
                }
            }
            if (dic.Count > 0)
            {
                structInfo.VTableMethod = new StructVTableMethodInfo[dic.Last().Key + 1];
                foreach (var i in dic)
                {
                    var methodInfo = new StructVTableMethodInfo();
                    structInfo.VTableMethod[i.Key] = methodInfo;
                    var methodDef = i.Value;
                    methodInfo.MethodName = $"{FixName(metadata.GetStringFromIndex(methodDef.nameIndex))}";
                }
            }
        }

        private void AddRGCTX(StructInfo structInfo, Il2CppTypeDefinition typeDef)
        {
            var imageName = typeDefImageNames[typeDef];
            var collection = typeDef.RgctXs;
            if (collection != null)
            {
                foreach (var definitionData in collection)
                {
                    var structRGCTXInfo = new StructRGCTXInfo();
                    structInfo.RGCTXs.Add(structRGCTXInfo);
                    structRGCTXInfo.Type = definitionData.type;
                    switch (definitionData.type)
                    {
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE:
                            {
                                var il2CppType = il2Cpp.AllTypes[definitionData.TypeIndex];
                                structRGCTXInfo.TypeName = FixName(GetTypeName(il2CppType, true, false));
                                break;
                            }
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS:
                            {
                                var il2CppType = il2Cpp.AllTypes[definitionData.TypeIndex];
                                structRGCTXInfo.ClassName = FixName(GetTypeName(il2CppType, true, false));
                                break;
                            }
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD:
                            {
                                var methodSpec = metadata.GetMethodSpec(definitionData.MethodIndex);
                                (var methodSpecTypeName, var methodSpecMethodName) = GetMethodSpecName(methodSpec, true);
                                structRGCTXInfo.MethodName = FixName(methodSpecTypeName + "." + methodSpecMethodName);
                                break;
                            }
                    }
                }
            }
        }

        public Il2CppRGCTXDefinition[]? GetRGCTXDefinition(string imageName, Il2CppMethodDefinition methodDef)
        {
            Il2CppRGCTXDefinition[]? collection = null;
            if (il2Cpp.MetadataVersion >= 24.2)
            {
                var codegenModule = il2Cpp.GetCodegenModuleByName(imageName);
                if (codegenModule == null)
                    return null;

                var rangePair = il2Cpp.GetRgctxRangePairsForModule(codegenModule).FirstOrDefault(x => x.token == methodDef.token);
                if (rangePair is null)
                    return null;

                return il2Cpp.GetRgctxDataForPair(codegenModule, rangePair);
            }
            else
            {
                if (methodDef.rgctxCount > 0)
                {
                    collection = new Il2CppRGCTXDefinition[methodDef.rgctxCount];
                    Array.Copy(metadata.RgctxDefinitions!, methodDef.rgctxStartIndex, collection, 0, methodDef.rgctxCount);
                }
            }
            return collection;
        }

        private List<StructRGCTXInfo> GenerateRGCTX(string imageName, Il2CppMethodDefinition methodDef)
        {
            var rgctxs = new List<StructRGCTXInfo>();
            var collection = GetRGCTXDefinition(imageName, methodDef);
            if (collection != null)
            {
                foreach (var definitionData in collection)
                {
                    var structRGCTXInfo = new StructRGCTXInfo();
                    rgctxs.Add(structRGCTXInfo);
                    structRGCTXInfo.Type = definitionData.type;
                    switch (definitionData.type)
                    {
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE:
                            {
                                var il2CppType = il2Cpp.AllTypes[definitionData.TypeIndex];
                                structRGCTXInfo.TypeName = FixName(GetTypeName(il2CppType, true, false));
                                break;
                            }
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS:
                            {
                                var il2CppType = il2Cpp.AllTypes[definitionData.TypeIndex];
                                structRGCTXInfo.ClassName = FixName(GetTypeName(il2CppType, true, false));
                                break;
                            }
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD:
                            {
                                var methodSpec = metadata.GetMethodSpec(definitionData.MethodIndex);
                                (var methodSpecTypeName, var methodSpecMethodName) = GetMethodSpecName(methodSpec, true);
                                structRGCTXInfo.MethodName = FixName(methodSpecTypeName + "." + methodSpecMethodName);
                                break;
                            }
                    }
                }
            }
            return rgctxs;
        }

        private void ParseArrayClassStruct(Il2CppType il2CppType, Il2CppGenericInst? classInst = null, Il2CppGenericInst? methodInst = null)
        {
            var structName = GetIl2CppStructName(il2CppType, classInst, methodInst);
            arrayClassHeader.Append($"struct {structName}_array {{\n" +
                $"\tIl2CppObject obj;\n" +
                $"\tIl2CppArrayBounds *bounds;\n" +
                $"\til2cpp_array_size_t max_length;\n" +
                $"\t{ParseType(il2CppType, classInst, methodInst)} m_Items[65535];\n" +
                $"}};\n");
        }

        private Il2CppTypeDefinition GetTypeDefinition(Il2CppType il2CppType)
        {
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return il2CppType.CoerceToUnderlyingTypeDefinition();
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    var genericClass = il2Cpp.ReadReadableAtVirtualAddress<Il2CppGenericClass>(il2CppType.Data.GenericClass);
                    return genericClass.TypeDefinition;
                default:
                    throw new NotSupportedException();
            }
        }

        private void CreateStructNameDic(Il2CppTypeDefinition typeDef)
        {
            var typeName = GetTypeDefName(typeDef, true, true);
            var typeStructName = FixName(typeName);
            var uniqueName = GetUniqueName(typeStructName);
            structNameDic.Add(typeDef, uniqueName);
        }

        private string GetUniqueName(string name)
        {
            var fixName = name;
            int i = 1;
            while (!structNameHashSet.Add(fixName))
            {
                fixName = $"{name}_{i++}";
            }
            return fixName;
        }

        private string RecursionStructInfo(StructInfo info)
        {
            if (!structCache.Add(info))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var pre = new StringBuilder();

            if (info.Parent != null)
            {
                var parentStructName = info.Parent + "_o";
                pre.Append(RecursionStructInfo(structInfoWithStructName[parentStructName]));
                sb.Append($"struct {info.TypeName}_Fields : {info.Parent}_Fields {{\n");
                // C style
                //sb.Append($"struct {info.TypeName}_Fields {{\n");
                //sb.Append($"\t{info.Parent}_Fields _;\n");
            }
            else
            {
                if (il2Cpp is PE && !info.IsValueType)
                {
                    if (il2Cpp.is32Bit)
                    {
                        sb.Append($"struct __declspec(align(4)) {info.TypeName}_Fields {{\n");
                    }
                    else
                    {
                        sb.Append($"struct __declspec(align(8)) {info.TypeName}_Fields {{\n");
                    }
                }
                else
                {
                    sb.Append($"struct {info.TypeName}_Fields {{\n");
                }
            }
            foreach (var field in info.Fields)
            {
                if (field.IsValueType)
                {
                    var fieldInfo = structInfoWithStructName[field.FieldTypeName];
                    pre.Append(RecursionStructInfo(fieldInfo));
                }
                if (field.IsCustomType)
                {
                    sb.Append($"\tstruct {field.FieldTypeName} {field.FieldName};\n");
                }
                else
                {
                    sb.Append($"\t{field.FieldTypeName} {field.FieldName};\n");
                }
            }
            sb.Append("};\n");

            if (info.RGCTXs.Count > 0)
            {
                sb.Append($"struct {info.TypeName}_RGCTXs {{\n");
                for (int i = 0; i < info.RGCTXs.Count; i++)
                {
                    var rgctx = info.RGCTXs[i];
                    switch (rgctx.Type)
                    {
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE:
                            sb.Append($"\tIl2CppType* _{i}_{rgctx.TypeName};\n");
                            break;
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS:
                            sb.Append($"\tIl2CppClass* _{i}_{rgctx.ClassName};\n");
                            break;
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD:
                            sb.Append($"\tMethodInfo* _{i}_{rgctx.MethodName};\n");
                            break;
                    }
                }
                sb.Append("};\n");
            }

            if (info.VTableMethod.Length > 0)
            {
                sb.Append($"struct {info.TypeName}_VTable {{\n");
                for (int i = 0; i < info.VTableMethod.Length; i++)
                {
                    sb.Append($"\tVirtualInvokeData _{i}_");
                    var method = info.VTableMethod[i];
                    if (method != null)
                    {
                        sb.Append(method.MethodName);
                    }
                    else
                    {
                        sb.Append("unknown");
                    }
                    sb.Append(";\n");
                }
                sb.Append("};\n");
            }

            sb.Append($"struct {info.TypeName}_c {{\n");
            sb.Append($"\tIl2CppClass_1 _1;\n");
            if (info.StaticFields.Count > 0)
            {
                sb.Append($"\tstruct {info.TypeName}_StaticFields* static_fields;\n");
            }
            else
            {
                sb.Append("\tvoid* static_fields;\n");
            }
            if (info.RGCTXs.Count > 0)
            {
                sb.Append($"\t{info.TypeName}_RGCTXs* rgctx_data;\n");
            }
            else
            {
                sb.Append("\tIl2CppRGCTXData* rgctx_data;\n");
            }
            sb.Append($"\tIl2CppClass_2 _2;\n");
            if (info.VTableMethod.Length > 0)
            {
                sb.Append($"\t{info.TypeName}_VTable vtable;\n");
            }
            else
            {
                sb.Append("\tVirtualInvokeData vtable[32];\n");
            }
            sb.Append($"}};\n");

            sb.Append($"struct {info.TypeName}_o {{\n");
            if (!info.IsValueType)
            {
                sb.Append($"\t{info.TypeName}_c *klass;\n");
                sb.Append($"\tvoid *monitor;\n");
            }
            sb.Append($"\t{info.TypeName}_Fields fields;\n");
            sb.Append("};\n");

            if (info.StaticFields.Count > 0)
            {
                sb.Append($"struct {info.TypeName}_StaticFields {{\n");
                foreach (var field in info.StaticFields)
                {
                    if (field.IsValueType)
                    {
                        var fieldInfo = structInfoWithStructName[field.FieldTypeName];
                        pre.Append(RecursionStructInfo(fieldInfo));
                    }
                    if (field.IsCustomType)
                    {
                        sb.Append($"\tstruct {field.FieldTypeName} {field.FieldName};\n");
                    }
                    else
                    {
                        sb.Append($"\t{field.FieldTypeName} {field.FieldName};\n");
                    }
                }
                sb.Append("};\n");
            }

            return pre.Append(sb).ToString();
        }

        private string GetIl2CppStructName(Il2CppType il2CppType, Il2CppGenericInst? classInst = null, Il2CppGenericInst? methodInst = null)
        {
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    {
                        var typeDef = il2CppType.CoerceToUnderlyingTypeDefinition();
                        return structNameDic[typeDef];
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        return GetIl2CppStructName(oriType);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.ReadReadableAtVirtualAddress<Il2CppArrayType>(il2CppType.Data.Array);
                        var elementType = il2Cpp.GetIl2CppTypeFromPointer(arrayType.etype);
                        var elementStructName = GetIl2CppStructName(elementType, classInst, methodInst);
                        var typeStructName = elementStructName + "_array";
                        if (structNameHashSet.Add(typeStructName))
                        {
                            ParseArrayClassStruct(elementType, classInst, methodInst);
                        }
                        return typeStructName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        var elementStructName = GetIl2CppStructName(elementType, classInst, methodInst);
                        var typeStructName = elementStructName + "_array";
                        if (structNameHashSet.Add(typeStructName))
                        {
                            ParseArrayClassStruct(elementType, classInst, methodInst);
                        }
                        return typeStructName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var typeStructName = genericClassStructNameDic[il2CppType.Data.GenericClass];
                        if (structNameHashSet.Add(typeStructName))
                        {
                            genericClassList.Add(il2CppType.Data.GenericClass);
                        }
                        return typeStructName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (classInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = classInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return GetIl2CppStructName(type);
                        }
                        return "System_Object";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (methodInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = methodInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return GetIl2CppStructName(type);
                        }
                        return "System_Object";
                    }
                default:
                    throw new NotSupportedException();
            }
        }

        private bool IsValueType(Il2CppType il2CppType, Il2CppGenericInst? classInst, Il2CppGenericInst? methodInst)
        {
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = il2CppType.CoerceToUnderlyingTypeDefinition();
                        return !typeDef.IsEnumType;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.ReadReadableAtVirtualAddress<Il2CppGenericClass>(il2CppType.Data.GenericClass);
                        var typeDef = genericClass.TypeDefinition;
                        return typeDef.IsValueType && !typeDef.IsEnumType;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (classInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = classInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return IsValueType(type, null, null);
                        }
                        return false;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (methodInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = methodInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return IsValueType(type, null, null);
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        private bool IsCustomType(Il2CppType il2CppType, Il2CppGenericInst? classInst, Il2CppGenericInst? methodInst)
        {
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        return IsCustomType(oriType, classInst, methodInst);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        return true;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = il2CppType.CoerceToUnderlyingTypeDefinition();
                        if (typeDef.IsEnumType)
                        {
                            return IsCustomType(typeDef.EnumUnderlyingType, classInst, methodInst);
                        }
                        return true;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2CppType.GetGenericClass();
                        var typeDef = genericClass.TypeDefinition;
                        if (typeDef.IsEnumType)
                        {
                            return IsCustomType(typeDef.EnumUnderlyingType, classInst, methodInst);
                        }
                        return true;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (classInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = classInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return IsCustomType(type, null, null);
                        }
                        return false;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (methodInst != null)
                        {
                            var genericParameter = il2CppType.GetGenericParameterDef();
                            var pointers = methodInst.Pointers;
                            var pointer = pointers[genericParameter.genericParameterIndexInOwner];
                            var type = il2Cpp.GetIl2CppTypeFromPointer(pointer);
                            return IsCustomType(type, null, null);
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static readonly Dictionary<int, string> TypeString = new()
        {
            {1,"void"},
            {2,"bool"},
            {3,"char"},
            {4,"sbyte"},
            {5,"byte"},
            {6,"short"},
            {7,"ushort"},
            {8,"int"},
            {9,"uint"},
            {10,"long"},
            {11,"ulong"},
            {12,"float"},
            {13,"double"},
            {14,"string"},
            {22,"TypedReference"},
            {24,"IntPtr"},
            {25,"UIntPtr"},
            {28,"object"},
        };

        public string GetTypeName(Il2CppType il2CppType, bool addNamespace, bool is_nested)
        {
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2CppType.GetArrayType();
                        var elementType = il2CppType.GetArrayElementType();
                        return $"{GetTypeName(elementType, addNamespace, false)}[{new string(',', arrayType.rank - 1)}]";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        return $"{GetTypeName(elementType, addNamespace, false)}[]";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppTypeFromPointer(il2CppType.Data.Type);
                        return $"{GetTypeName(oriType, addNamespace, false)}*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        var param = il2CppType.GetGenericParameterDef();
                        return metadata.GetStringFromIndex(param.nameIndex);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        string str = string.Empty;
                        Il2CppTypeDefinition typeDef;
                        Il2CppGenericClass? genericClass = null;
                        if (il2CppType.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                        {
                            genericClass = il2CppType.GetGenericClass();
                            typeDef = genericClass.TypeDefinition;
                        }
                        else
                        {
                            typeDef = il2CppType.CoerceToUnderlyingTypeDefinition();
                        }
                        if (typeDef.DeclaringTypeIndex.IsNonNull)
                        {
                            str += GetTypeName(il2Cpp.GetType(typeDef.DeclaringTypeIndex), addNamespace, true);
                            str += '.';
                        }
                        else if (addNamespace)
                        {
                            var @namespace = metadata.GetStringFromIndex(typeDef.NamespaceIndex);
                            if (@namespace != "")
                            {
                                str += @namespace + ".";
                            }
                        }

                        var typeName = metadata.GetStringFromIndex(typeDef.NameIndex);
                        var index = typeName.IndexOf("`");
                        if (index != -1)
                        {
                            str += typeName[..index];
                        }
                        else
                        {
                            str += typeName;
                        }

                        if (is_nested)
                            return str;

                        if (genericClass != null)
                        {
                            var genericInst = il2Cpp.ReadReadableAtVirtualAddress<Il2CppGenericInst>(genericClass.Context.class_inst);
                            str += GetGenericInstParams(genericInst);
                        }
                        else if (typeDef.GenericContainerIndex.IsNonNull)
                        {
                            var genericContainer = metadata.GetGenericContainerFromIndex(typeDef.GenericContainerIndex);
                            str += GetGenericContainerParams(genericContainer);
                        }

                        return str;
                    }
                default:
                    return TypeString[(int)il2CppType.Type];
            }
        }

        public string GetTypeDefName(Il2CppTypeDefinition typeDef, bool addNamespace, bool genericParameter)
        {
            var prefix = string.Empty;
            if (typeDef.DeclaringTypeIndex.IsNonNull)
            {
                prefix = GetTypeName(il2Cpp.GetType(typeDef.DeclaringTypeIndex), addNamespace, true) + ".";
            }
            else if (addNamespace)
            {
                var @namespace = metadata.GetStringFromIndex(typeDef.NamespaceIndex);
                if (@namespace != "")
                {
                    prefix = @namespace + ".";
                }
            }
            var typeName = metadata.GetStringFromIndex(typeDef.NameIndex);
            if (typeDef.GenericContainerIndex.IsNonNull)
            {
                var index = typeName.IndexOf('`');
                if (index != -1)
                {
                    typeName = typeName[..index];
                }
                if (genericParameter)
                {
                    var genericContainer = metadata.GetGenericContainerFromIndex(typeDef.GenericContainerIndex);
                    typeName += GetGenericContainerParams(genericContainer);
                }
            }
            return prefix + typeName;
        }

        public string GetGenericInstParams(Il2CppGenericInst genericInst)
        {
            var genericParameterNames = new List<string>();
            var pointers = genericInst.Pointers;
            for (int i = 0; i < pointers.Length; i++)
            {
                var il2CppType = il2Cpp.GetIl2CppTypeFromPointer(pointers[i]);
                genericParameterNames.Add(GetTypeName(il2CppType, false, false));
            }
            return $"<{string.Join(", ", genericParameterNames)}>";
        }

        public string GetGenericContainerParams(Il2CppGenericContainer genericContainer)
        {
            var genericParameterNames = new List<string?>();
            foreach (var genericParameter in genericContainer.GenericParameters)
            {
                genericParameterNames.Add(genericParameter.Name);
            }
            return $"<{string.Join(", ", genericParameterNames)}>";
        }

        public (string, string) GetMethodSpecName(Il2CppMethodSpec methodSpec, bool addNamespace = false)
        {
            var methodDef = methodSpec.MethodDefinition!;
            var typeDef = methodDef.DeclaringType!;
            var typeName = GetTypeDefName(typeDef, addNamespace, false);
            if (methodSpec.classIndexIndex.IsNonNull)
            {
                var classInst = methodSpec.GenericClassInst!;
                typeName += GetGenericInstParams(classInst);
            }
            var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
            if (methodSpec.methodIndexIndex.IsNonNull)
            {
                var methodInst = methodSpec.GenericMethodInst!;
                methodName += GetGenericInstParams(methodInst);
            }
            return (typeName, methodName);
        }

        private void GenerateMethodInfo(string methodInfoName, string structTypeName, List<StructRGCTXInfo> rgctxs)
        {
            if (rgctxs.Count > 0)
            {
                methodInfoHeader.Append($"struct {methodInfoName}_RGCTXs {{\n");
                for (int i = 0; i < rgctxs.Count; i++)
                {
                    var rgctx = rgctxs[i];
                    switch (rgctx.Type)
                    {
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE:
                            methodInfoHeader.Append($"\tIl2CppType* _{i}_{rgctx.TypeName};\n");
                            break;
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS:
                            methodInfoHeader.Append($"\tIl2CppClass* _{i}_{rgctx.ClassName};\n");
                            break;
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD:
                            methodInfoHeader.Append($"\tMethodInfo* _{i}_{rgctx.MethodName};\n");
                            break;
                    }
                }
                methodInfoHeader.Append("};\n");
            }

            methodInfoHeader.Append($"struct {methodInfoName} {{\n");
            methodInfoHeader.Append($"\tIl2CppMethodPointer methodPointer;\n");
            if (metadata.MetadataVersion >= 29)
            {
                methodInfoHeader.Append($"\tIl2CppMethodPointer virtualMethodPointer;\n");
                methodInfoHeader.Append($"\tInvokerMethod invoker_method;\n");
            }
            else
            {
                methodInfoHeader.Append($"\tvoid* invoker_method;\n"); //TODO
            }
            methodInfoHeader.Append($"\tconst char* name;\n");
            if (metadata.MetadataVersion <= 24)
            {
                methodInfoHeader.Append($"\t{structTypeName}_c *declaring_type;\n");
            }
            else
            {
                methodInfoHeader.Append($"\t{structTypeName}_c *klass;\n");
            }
            methodInfoHeader.Append($"\tconst Il2CppType *return_type;\n");
            if (metadata.MetadataVersion >= 29)
            {
                methodInfoHeader.Append($"\tconst Il2CppType** parameters;\n");
            }
            else
            {
                methodInfoHeader.Append($"\tconst void* parameters;\n"); //ParameterInfo*
            }
            if (rgctxs.Count > 0)
            {
                methodInfoHeader.Append($"\tconst {methodInfoName}_RGCTXs* rgctx_data;\n");
            }
            else
            {
                methodInfoHeader.Append($"\tconst Il2CppRGCTXData* rgctx_data;\n");
            }
            methodInfoHeader.Append($"\tunion\n");
            methodInfoHeader.Append($"\t{{\n");
            methodInfoHeader.Append($"\t\tconst void* genericMethod;\n");
            if (metadata.MetadataVersion >= 27)
            {
                methodInfoHeader.Append($"\t\tconst void* genericContainerHandle;\n");
            }
            else
            {
                methodInfoHeader.Append($"\t\tconst void* genericContainer;\n");
            }
            methodInfoHeader.Append($"\t}};\n");
            if (metadata.MetadataVersion <= 24)
            {
                methodInfoHeader.Append($"\tint32_t customAttributeIndex;\n");
            }
            methodInfoHeader.Append($"\tuint32_t token;\n");
            methodInfoHeader.Append($"\tuint16_t flags;\n");
            methodInfoHeader.Append($"\tuint16_t iflags;\n");
            methodInfoHeader.Append($"\tuint16_t slot;\n");
            methodInfoHeader.Append($"\tuint8_t parameters_count;\n");
            methodInfoHeader.Append($"\tuint8_t bitflags;\n");
            methodInfoHeader.Append($"}};\n");
        }
    }
}
