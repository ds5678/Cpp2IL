using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;
using Il2CppDumper;

namespace Cpp2IL.Core.OutputFormats;

public class DisassemblyOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "disassembly";
    public override string OutputFormatName => "Disassembly";
    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        new StructGenerator().WriteScript(outputRoot);
    }
}
