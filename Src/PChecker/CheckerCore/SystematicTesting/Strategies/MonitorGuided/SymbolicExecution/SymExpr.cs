using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.Types;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal abstract record SymExpr(PLanguageType Type);

    internal sealed record ConcreteExpr(IPValue Value, PLanguageType Type) 
        : SymExpr(Type);

    internal sealed record SymbolExpr(SymbolID ID, PLanguageType Type)
        : SymExpr(Type);

    internal sealed record SymbolID(long Value);

    internal sealed record BinaryExpr(
        BinOpType Op,
        SymExpr Left,
        SymExpr Right,
        PLanguageType Type) : SymExpr(Type);

    internal sealed record UnaryExpr(
        UnaryOpType Op,
        SymExpr SubExpr,
        PLanguageType Type) : SymExpr(Type);

    internal sealed record TupleExpr(
        ImmutableArray<SymExpr> Fields, 
        TupleType TupleType) : SymExpr(TupleType);

    internal sealed record NamedTupleExpr(
        ImmutableDictionary<string, SymExpr> Fields,
        NamedTupleType TupleType) : SymExpr(TupleType);

    internal sealed record SymEvent(Event Event, SymExpr Payload);

    // JR TODO
    internal class PathCondition
    {
    }
}
