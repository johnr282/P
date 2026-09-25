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

    internal sealed record SequenceExpr(
        ImmutableArray<SymExpr> Elements,
        SequenceType SequenceType) : SymExpr(SequenceType);

    internal sealed record SetExpr(
        ImmutableArray<SymExpr> Elements,
        SetType SetType) : SymExpr(SetType);

    internal sealed record MapEntry(SymExpr Key, SymExpr Value);

    internal sealed record MapExpr(
        ImmutableArray<MapEntry> Entries,
        MapType MapType) : SymExpr(MapType);

    // Index is an integer position for sequences/sets, or a key for maps.
    // Set indexing follows P's runtime enumeration, not a sorted order.
    internal sealed record CollectionAccessExpr(
        SymExpr Collection,
        SymExpr Index,
        PLanguageType ElementType) : SymExpr(ElementType);

    internal sealed record CollectionSizeExpr(SymExpr Collection)
        : SymExpr(PrimitiveType.Int);

    // Membership tests elements for sequences/sets and keys for maps.
    internal sealed record CollectionContainsExpr(SymExpr Collection, SymExpr Item)
        : SymExpr(PrimitiveType.Bool);

    internal sealed record MapKeysExpr(SymExpr Map, SequenceType SequenceType)
        : SymExpr(SequenceType);

    internal sealed record MapValuesExpr(SymExpr Map, SequenceType SequenceType)
        : SymExpr(SequenceType);

    // Updates describe new collection values; they never mutate the operand.
    // Indexed assignment applies to sequences and maps.
    internal sealed record CollectionUpdateExpr(
        SymExpr Collection,
        SymExpr Index,
        SymExpr Value) : SymExpr(Collection.Type);

    // Sequence insertion shifts subsequent elements. Map insertion is distinct
    // from assignment and must preserve the runtime's duplicate-key behavior.
    internal sealed record CollectionInsertExpr(
        SymExpr Collection,
        SymExpr Index,
        SymExpr Value) : SymExpr(Collection.Type);

    internal sealed record SetAddExpr(SymExpr Set, SymExpr Value)
        : SymExpr(Set.Type);

    // Remove a sequence position, a set element, or a map key.
    internal sealed record CollectionRemoveExpr(SymExpr Collection, SymExpr Item)
        : SymExpr(Collection.Type);

    internal sealed record SymEvent(Event Event, SymExpr Payload);

    internal sealed record SymbolID(long Value);
}
