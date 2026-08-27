using PChecker.Runtime.Exceptions;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal class SymbolFactory
    {
        private long _nextSymbolId = 0;

        internal SymExpr CreateFresh(PLanguageType type)
        {
            switch (type)
            {
                case PrimitiveType primitive when primitive == PrimitiveType.Null:
                    return new ConcreteExpr(null, primitive);

                case PrimitiveType:
                    // JR TODO: Add special handling for Event, Machine, and Any 
                case EnumType:
                    return NewSymbol(type);

                case TupleType tuple:
                    {
                        var fields = tuple.Types
                            .Select(CreateFresh)
                            .ToImmutableArray();
                        return new TupleExpr(fields, tuple);
                    }

                case NamedTupleType namedTuple:
                    {
                        Dictionary<string, SymExpr> fields = new();
                        foreach (var entry in namedTuple.Fields)
                        {
                            fields[entry.Name] = CreateFresh(entry.Type);
                        }
                        return new NamedTupleExpr(
                            fields.ToImmutableDictionary(),
                            namedTuple);
                    }

                case TypeDefType typeDef:
                    return CreateFresh(typeDef.Canonicalize());

                default:
                    throw new PInternalException(
                        $"Unsupported type for fresh symbolic expression: {type.GetType().Name}");
            }
        }

        private SymbolExpr NewSymbol(PLanguageType type)
        {
            var id = new SymbolID(_nextSymbolId++);
            return new SymbolExpr(id, type);
        }
    }
}
