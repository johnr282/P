using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal static class SymExprFactory
    {
        internal static SymExpr True => new ConcreteExpr((PBool)true, PrimitiveType.Bool);

        internal static SymExpr And(SymExpr left, SymExpr right)
        {
            if (!left.Type.IsSameTypeAs(PrimitiveType.Bool) ||
                !right.Type.IsSameTypeAs(PrimitiveType.Bool))
            {
                throw new ArgumentException(
                    $"Both operands must be of boolean type. Types: {left.Type}, {right.Type}");
            }

            return new BinaryExpr(BinOpType.And, left, right, PrimitiveType.Bool);
        }

        internal static SymExpr Or(SymExpr left, SymExpr right)
        {
            if (!left.Type.IsSameTypeAs(PrimitiveType.Bool) ||
                !right.Type.IsSameTypeAs(PrimitiveType.Bool))
            {
                throw new ArgumentException(
                    $"Both operands must be of boolean type. Types: {left.Type}, {right.Type}");
            }

            return new BinaryExpr(BinOpType.Or, left, right, PrimitiveType.Bool);
        }

        internal static SymExpr Equal(SymExpr left, SymExpr right)
        {
            if (!left.Type.IsAssignableFrom(right.Type) &&
                !right.Type.IsAssignableFrom(left.Type))
            {
                throw new ArgumentException(
                    $"Both operands must be of the same type. Types: {left.Type}, {right.Type}");
            }

            return new BinaryExpr(BinOpType.Eq, left, right, PrimitiveType.Bool);
        }

        internal static SymExpr CloneSymExpr(SymExpr expr)
        {
            return expr switch
            {
                ConcreteExpr concrete => new ConcreteExpr(concrete.Value.Clone(), concrete.Type),
                SymbolExpr symbol => new SymbolExpr(symbol.ID, symbol.Type),
                BinaryExpr binary => new BinaryExpr(
                    binary.Op,
                    CloneSymExpr(binary.Left),
                    CloneSymExpr(binary.Right),
                    binary.Type),
                UnaryExpr unary => new UnaryExpr(
                    unary.Op,
                    CloneSymExpr(unary.SubExpr),
                    unary.Type),
                TupleExpr tuple => new TupleExpr(
                    tuple.Fields.Select(CloneSymExpr).ToImmutableArray(),
                    tuple.TupleType),
                NamedTupleExpr namedTuple => new NamedTupleExpr(
                    namedTuple.Fields.ToImmutableDictionary(
                        field => field.Key,
                        field => CloneSymExpr(field.Value),
                        namedTuple.Fields.KeyComparer),
                    namedTuple.TupleType),
                SequenceExpr sequence => new SequenceExpr(
                    sequence.Elements.Select(CloneSymExpr).ToImmutableArray(),
                    sequence.SequenceType),
                SetExpr set => new SetExpr(
                    set.Elements.Select(CloneSymExpr).ToImmutableArray(),
                    set.SetType),
                MapExpr map => new MapExpr(
                    map.Entries
                        .Select(entry => new MapEntry(
                            CloneSymExpr(entry.Key),
                            CloneSymExpr(entry.Value)))
                        .ToImmutableArray(),
                    map.MapType),
                CollectionAccessExpr access => new CollectionAccessExpr(
                    CloneSymExpr(access.Collection),
                    CloneSymExpr(access.Index),
                    access.ElementType),
                CollectionSizeExpr size => new CollectionSizeExpr(
                    CloneSymExpr(size.Collection)),
                CollectionContainsExpr contains => new CollectionContainsExpr(
                    CloneSymExpr(contains.Collection),
                    CloneSymExpr(contains.Item)),
                MapKeysExpr keys => new MapKeysExpr(
                    CloneSymExpr(keys.Map),
                    keys.SequenceType),
                MapValuesExpr values => new MapValuesExpr(
                    CloneSymExpr(values.Map),
                    values.SequenceType),
                CollectionUpdateExpr update => new CollectionUpdateExpr(
                    CloneSymExpr(update.Collection),
                    CloneSymExpr(update.Index),
                    CloneSymExpr(update.Value)),
                CollectionInsertExpr insert => new CollectionInsertExpr(
                    CloneSymExpr(insert.Collection),
                    CloneSymExpr(insert.Index),
                    CloneSymExpr(insert.Value)),
                SetAddExpr add => new SetAddExpr(
                    CloneSymExpr(add.Set),
                    CloneSymExpr(add.Value)),
                CollectionRemoveExpr remove => new CollectionRemoveExpr(
                    CloneSymExpr(remove.Collection),
                    CloneSymExpr(remove.Item)),
                _ => throw new NotSupportedException(
                    $"Cannot clone symbolic expression type '{expr.GetType().Name}'.")
            };
        }
    }
}
