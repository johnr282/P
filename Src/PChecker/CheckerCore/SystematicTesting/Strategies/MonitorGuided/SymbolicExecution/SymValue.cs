using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.Types;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal class SymValue
    {
    }

    /// <summary>
    /// Storage dictionaries in <see cref="SymState"/> that can hold a P variable.
    /// </summary>
    internal enum VariableStorage
    {
        Global,
        Local
    }

    /// <summary>
    /// A typed address that an assignment may update.
    /// </summary>
    internal abstract record ResolvedLValue(PLanguageType Type);

    internal sealed record VariableLValue(
        Variable Variable,
        VariableStorage Storage) : ResolvedLValue(Variable.Type);

    /// <summary>
    /// A named-field projection rooted at another assignment target.
    /// </summary>
    internal sealed record NamedTupleFieldLValue(
        ResolvedLValue Tuple,
        NamedTupleEntry Entry) : ResolvedLValue(Entry.Type);

    /// <summary>
    /// A positional-field projection rooted at another assignment target.
    /// </summary>
    internal sealed record TupleFieldLValue(
        ResolvedLValue Tuple,
        int FieldNo,
        PLanguageType FieldType) : ResolvedLValue(FieldType);

    internal class SymEvent
    {
    }

    internal class PathCondition
    {
    }
}
