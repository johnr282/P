using Plang.Compiler.TypeChecker.AST;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Continuations
{
    /// <summary>
    /// Continuation for after an lvalue is resolved.
    /// </summary>
    internal abstract record LValueContinuation;

    /// <summary>
    /// Location of assign is being resolved; next, evaluate Value.
    /// </summary>
    internal sealed record AssignLocationContinuation(
        IPExpr Value,
        StmtContinuation Next) : LValueContinuation;

    /// <summary>
    /// Base of a named-tuple field access is being resolved; next, finish 
    /// resolving the lvalue by getting the field corresponding to Entry.
    /// </summary>
    internal sealed record NamedTupleFieldContinuation(
        NamedTupleEntry Entry,
        LValueContinuation Next) : LValueContinuation;

    /// <summary>
    /// Base of a tuple field access is being resolved; next, finish resolving 
    /// the lvalue by getting the field corresponding to FieldNo.
    /// </summary>
    internal sealed record TupleFieldContinuation(
        int FieldNo,
        PLanguageType FieldType,
        LValueContinuation Next) : LValueContinuation;
}
