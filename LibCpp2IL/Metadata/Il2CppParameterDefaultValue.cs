namespace LibCpp2IL.Metadata;

public class Il2CppParameterDefaultValue : ReadableClass
{
    public int parameterIndex;
    public int typeIndex;
    public int dataIndex;

    public object? ContainedDefaultValue => LibCpp2ILUtils.GetDefaultValue(dataIndex, typeIndex);

    public override void Read(ClassReadingBinaryReader reader)
    {
        parameterIndex = reader.ReadParameterIndex();
        typeIndex = reader.ReadTypeIndex();
        dataIndex = reader.ReadDefaultValueDataIndex();
    }
}
