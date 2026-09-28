using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Z3;
using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.Types;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver
{
    internal partial class Z3Translator
    {
        // Sequences retain order. Sets are duplicate-free enumerations; maps are
        // enumerations of key/value pairs. Their equality ignores enumeration order.
        // Native Z3 sets/arrays alone would lose size, enumeration, and P equality
        // (notably IEEE float equality and structural equality of nested values).

        private static readonly int MaxUnrolledCollectionLength = 128;

        private readonly Dictionary<string, TupleSort> _mapEntrySorts = new();
        private readonly Dictionary<string, FuncDecl> _collectionSearches = new();
        private readonly Dictionary<string, FuncDecl> _mapProjections = new();
        private readonly Dictionary<string, FuncDecl> _collectionConversions = new();
        private readonly Dictionary<
            SymExpr, (SeqExpr Result, BoolExpr Constraint)>
            _collectionOrders = new();

        private static bool IsCollectionType(PLanguageType type) =>
            type is SequenceType or SetType or MapType;

        private static PLanguageType CollectionItemType(PLanguageType type) =>
            type switch
            {
                SequenceType sequence => sequence.ElementType.Canonicalize(),
                SetType set => set.ElementType.Canonicalize(),
                MapType map => map.KeyType.Canonicalize(),
                _ => throw new ArgumentException(
                    $"Expected a collection type, got {type}.")
            };

        private Sort CreateCollectionSort(PLanguageType type)
        {
            return _context.MkSeqSort(CollectionItemSort(type));
        }

        private Sort CollectionItemSort(PLanguageType collectionType)
        {
            if (collectionType is not MapType map)
                return TranslateSort(CollectionItemType(collectionType));

            var key = map.CanonicalRepresentation;
            if (!_mapEntrySorts.TryGetValue(key, out var entry))
            {
                var sorts = new[] { TranslateSort(map.KeyType),
                    TranslateSort(map.ValueType) };
                var name = $"p_map_entry_{_mapEntrySorts.Count}";
                entry = _context.MkTupleSort(
                    _context.MkSymbol(name),
                    new[] { _context.MkSymbol(name + "_key"),
                        _context.MkSymbol(name + "_value") },
                    sorts);
                _mapEntrySorts.Add(key, entry);
            }

            return entry;
        }

        private TupleSort MapEntrySort(MapType type)
        {
            // Create map entry sort first if necessary
            TranslateSort(type);
            return _mapEntrySorts[type.CanonicalRepresentation];
        }

        private Expr EntryKey(PLanguageType type, Expr entry) => type is MapType map
            ? MapEntrySort(map).FieldDecls[0].Apply(entry) : entry;

        private Expr EntryValue(MapType type, Expr entry) =>
            MapEntrySort(type).FieldDecls[1].Apply(entry);

        private SeqExpr EmptyCollection(PLanguageType type) =>
            _context.MkEmptySeq(TranslateSort(type));

        private IntExpr Length(SeqExpr value) => _context.MkLength(value);

        private IntExpr Add(IntExpr a, int b) =>
            (IntExpr)_context.MkAdd(a, _context.MkInt(b));

        private SeqExpr Slice(SeqExpr value, IntExpr offset, IntExpr length) =>
            _context.MkExtract(value, offset, length);

        private Expr At(SeqExpr value, IntExpr index)
        {
            // Manual implementation because MkNth has issues with conditional
            // sequences with tuple elements, see Z3SequenceIndexingRegressionTests.
            // Upgrading Z3 to 5.1.0 did not fix the issue.
            if (value.IsITE)
                // Args[0], [1], [2] are condition, then-branch, else-branch.
                return _context.MkITE(
                    (BoolExpr)value.Args[0],
                    At((SeqExpr)value.Args[1], index),
                    At((SeqExpr)value.Args[2], index));

            // Singleton sequence; return its only element.
            if (value.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_SEQ_UNIT)
                return value.Args[0];

            // Concatenation of sequences
            if (value.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_SEQ_CONCAT)
            {
                var parts = value.Args.Cast<SeqExpr>().ToArray();
                Expr Select(int part, IntExpr offset)
                {
                    if (part == parts.Length - 1)
                        return At(parts[part], offset);

                    var length = Length(parts[part]);
                    var within = (BoolExpr)_context.MkLt(offset, length).Simplify();
                    if (within.IsTrue)
                        return At(parts[part], offset);

                    var rest = Select(
                        part + 1,
                        (IntExpr)_context.MkSub(offset, length).Simplify());
                    return within.IsFalse ? rest : _context.MkITE(
                        within,
                        At(parts[part], offset),
                        rest);
                }
                return Select(0, index);
            }

            return _context.MkNth(value, index);
        }

        private BoolExpr InBounds(SeqExpr value, IntExpr index) => _context.MkAnd(
            _context.MkGe(index, _context.MkInt(0)),
            _context.MkLt(index, Length(value)));

        // Expand small, statically known lengths to keep ordinary literal/update
        // formulas quantifier-free. Unknown and larger lengths stay unbounded.
        private int? KnownLength(SeqExpr value) =>
            Length(value).Simplify() is IntNum n &&
            n.BigInteger <= MaxUnrolledCollectionLength
                ? n.Int : null;

        private BoolExpr AllElements(
            SeqExpr value, Func<IntExpr, BoolExpr> predicate)
        {
            if (KnownLength(value) is int count)
                return _context.MkAnd(Enumerable.Range(0, count)
                    .Select(i => predicate(_context.MkInt(i))).ToArray());
            var index = (IntExpr)_context.MkFreshConst(
                "all_index", _context.IntSort);
            return _context.MkForall(
                new Expr[] { index },
                _context.MkImplies(InBounds(value, index), predicate(index)));
        }

        private BoolExpr AnyElement(
            SeqExpr value, Func<IntExpr, BoolExpr> predicate)
        {
            if (KnownLength(value) is int count)
                return _context.MkOr(Enumerable.Range(0, count)
                    .Select(i => predicate(_context.MkInt(i))).ToArray());
            var index = (IntExpr)_context.MkFreshConst(
                "any_index", _context.IntSort);
            return _context.MkExists(
                new Expr[] { index },
                _context.MkAnd(InBounds(value, index), predicate(index)));
        }

        private void RequireDefined(BoolExpr condition) =>
            _domainConstraints.Add(_context.MkImplies(_evaluationGuard, condition));

        private static void RequireSameType(
            PLanguageType expected, PLanguageType actual)
        {
            if (!expected.Canonicalize().IsSameTypeAs(actual.Canonicalize()))
                throw new ArgumentException(
                    $"Expected type {expected}, got {actual}.");
        }

        private Expr TranslateAsType(PLanguageType expected, SymExpr expr)
        {
            expected = expected.Canonicalize();
            var actual = expr.Type.Canonicalize();
            if (!expected.IsAssignableFrom(actual))
                throw new ArgumentException(
                    $"Cannot assign {actual} to collection element type {expected}.");
            var value = TranslateExpr(expr);
            var sort = TranslateSort(expected);
            if (value.Sort.Equals(sort)) return value;

            // Covariant collection/tuple assignments can have distinct tuple
            // sorts, e.g. seq[(null,)] assigned into seq[(machine,)].
            var key = actual.CanonicalRepresentation + " -> " +
                expected.CanonicalRepresentation;
            if (!_collectionConversions.TryGetValue(key, out var convert))
            {
                convert = _context.MkFreshFuncDecl(
                    "p_collection_value", new[] { value.Sort }, sort);
                _collectionConversions.Add(key, convert);
            }
            var result = convert.Apply(value);
            RequireDefined(ConversionRelation(expected, actual, result, value));
            return result;
        }

        private BoolExpr ConversionRelation(
            PLanguageType expected,
            PLanguageType actual,
            Expr target,
            Expr source)
        {
            expected = expected.Canonicalize();
            actual = actual.Canonicalize();
            if (target.Sort.Equals(source.Sort))
                return _context.MkEq(target, source);
            if (expected is TupleType or NamedTupleType)
            {
                var targetFields = ((TupleSort)TranslateSort(expected)).FieldDecls;
                var sourceFields = ((TupleSort)TranslateSort(actual)).FieldDecls;
                var sourceTypes = GetFieldTypes(actual);
                return _context.MkAnd(GetFieldTypes(expected).Select((t, i) =>
                    ConversionRelation(
                        t, sourceTypes[i],
                        targetFields[i].Apply(target),
                        sourceFields[i].Apply(source)))
                    .ToArray());
            }
            if (IsCollectionType(expected))
            {
                var from = (SeqExpr)source;
                var to = (SeqExpr)target;
                return _context.MkAnd(
                    _context.MkEq(Length(to), Length(from)),
                    AllElements(from, i =>
                    {
                        var targetItem = At(to, i);
                        var sourceItem = At(from, i);
                        var key = ConversionRelation(
                            CollectionItemType(expected), CollectionItemType(actual),
                            EntryKey(expected, targetItem),
                            EntryKey(actual, sourceItem));
                        return expected is MapType tm && actual is MapType sm
                            ? _context.MkAnd(key, ConversionRelation(
                                tm.ValueType, sm.ValueType,
                                EntryValue(tm, targetItem),
                                EntryValue(sm, sourceItem)))
                            : key;
                    }));
            }
            throw new ArgumentException(
                $"Unsupported collection element conversion: {actual} to {expected}.");
        }

        private SeqExpr CollectionOperand(SymExpr expr)
        {
            if (!IsCollectionType(expr.Type.Canonicalize()))
                throw new ArgumentException(
                    $"Expected a collection expression, got {expr.Type}.");
            return (SeqExpr)TranslateExpr(expr);
        }

        private IntExpr CollectionIndex(SymExpr expr)
        {
            RequireSameType(PrimitiveType.Int, expr.Type);
            // PChecker passes PInt through its implicit int conversion to List/
            // HashSet indexing. This truncates the 64-bit value to signed 32 bits.
            return _context.MkBV2Int(
                _context.MkExtract(31, 0, (BitVecExpr)TranslateExpr(expr)), true);
        }

        private BoolExpr NonNull(PLanguageType type, Expr value) =>
            IsNullableIdentityType(type.Canonicalize())
            ? _context.MkNot(_context.MkEq(value, _context.MkInt(0)))
            : _context.MkTrue();

        private Expr TranslateCollection(SymExpr expr)
        {
            if (expr is SymbolExpr symbol) return TranslateSymbol(symbol);
            var type = expr.Type.Canonicalize();
            var result = EmptyCollection(type);
            IEnumerable<SymExpr> elements;
            switch (expr)
            {
                case SequenceExpr sequence when type is SequenceType:
                    elements = sequence.Elements;
                    break;
                case SetExpr set when type is SetType:
                    elements = set.Elements;
                    break;
                case MapExpr map when type is MapType mapType:
                    foreach (var entry in map.Entries)
                    {
                        var key = TranslateAsType(mapType.KeyType, entry.Key);
                        var value = TranslateAsType(mapType.ValueType, entry.Value);
                        RequireDefined(NonNull(mapType.KeyType, key));
                        RequireDefined(_context.MkNot(Contains(type, result, key)));
                        result = _context.MkConcat(
                            result,
                            _context.MkUnit(
                                MapEntrySort(mapType).MkDecl.Apply(key, value)));
                    }
                    return result;
                case ConcreteExpr { Value: PSeq sequence } when type is SequenceType:
                    elements = sequence.Select(v =>
                        new ConcreteExpr(v, CollectionItemType(type)));
                    break;
                case ConcreteExpr { Value: PSet set } when type is SetType:
                    elements = set.Select(v =>
                        new ConcreteExpr(v, CollectionItemType(type)));
                    break;
                case ConcreteExpr { Value: PMap map }
                    when type is MapType mapType:
                    foreach (var entry in map)
                    {
                        var key = TranslateExpr(
                            new ConcreteExpr(entry.Key, mapType.KeyType));
                        var value = TranslateExpr(
                            new ConcreteExpr(entry.Value, mapType.ValueType));
                        result = _context.MkConcat(
                            result,
                            _context.MkUnit(
                                MapEntrySort(mapType).MkDecl.Apply(key, value)));
                    }
                    return result;
                default:
                    throw new ArgumentException(
                        $"Expected a collection value matching {type}.",
                        nameof(expr));
            }

            foreach (var element in elements)
            {
                var value = TranslateAsType(CollectionItemType(type), element);
                var appended = _context.MkConcat(result, _context.MkUnit(value));
                // Literal set elements denote membership; symbolic duplicates
                // must be resolved by P equality, not C# record equality.
                result = type is SetType
                    ? (SeqExpr)_context.MkITE(
                        Contains(type, result, value), result, appended)
                    : appended;
            }
            return result;
        }

        private Expr TranslateCollectionAccess(CollectionAccessExpr expr)
        {
            var collection = CollectionOperand(expr.Collection);
            var type = expr.Collection.Type.Canonicalize();
            if (type is MapType map)
            {
                RequireSameType(map.ValueType, expr.Type);
                var key = TranslateAsType(map.KeyType, expr.Index);
                RequireDefined(NonNull(map.KeyType, key));
                // Read-after-write needs no search when the key expression is
                // unchanged. Still evaluate the collection above for its guards.
                SymExpr writtenValue = expr.Collection switch
                {
                    CollectionUpdateExpr update
                        when Equals(update.Index, expr.Index) => update.Value,
                    CollectionInsertExpr insert
                        when Equals(insert.Index, expr.Index) => insert.Value,
                    _ => null
                };
                if (writtenValue != null)
                {
                    // A NaN key does not match itself under P equality.
                    RequireDefined(TranslateEquality(
                        map.KeyType.Canonicalize(),
                        map.KeyType.Canonicalize(), key, key));
                    return TranslateAsType(map.ValueType, writtenValue);
                }
                var index = Find(type, collection, key);
                RequireDefined(InBounds(collection, index));
                return EntryValue(map, At(collection, index));
            }
            RequireSameType(CollectionItemType(type), expr.Type);
            var position = CollectionIndex(expr.Index);
            RequireDefined(InBounds(collection, position));
            return At(collection, position);
        }

        private Expr TranslateCollectionSize(CollectionSizeExpr expr)
        {
            var length = Length(CollectionOperand(expr.Collection));
            var size = _context.MkZeroExt(32, _context.MkInt2BV(32, length));
            // State the inverse conversion too, so a comparison such as size == 0
            // immediately constrains sequence length without modular arithmetic.
            RequireDefined(_context.MkEq(_context.MkBV2Int(size, false), length));
            return size;
        }

        private Expr TranslateCollectionContains(CollectionContainsExpr expr)
        {
            var collection = CollectionOperand(expr.Collection);
            var type = expr.Collection.Type.Canonicalize();
            var item = TranslateAsType(CollectionItemType(type), expr.Item);
            if (type is MapType)
                RequireDefined(NonNull(CollectionItemType(type), item));
            return Contains(type, collection, item);
        }

        private BoolExpr Contains(
            PLanguageType type, SeqExpr collection, Expr item)
        {
            var itemType = CollectionItemType(type);
            if (type is not MapType && UsesSmtEquality(itemType))
                return _context.MkContains(collection, _context.MkUnit(item));
            return AnyElement(collection, index =>
                TranslateEquality(
                    itemType, itemType, EntryKey(type, At(collection, index)),
                    item));
        }

        private IntExpr Find(PLanguageType type, SeqExpr collection, Expr item)
        {
            var itemType = CollectionItemType(type);
            if (KnownLength(collection) is int count)
            {
                IntExpr position = _context.MkInt(-1);
                for (var i = count - 1; i >= 0; i--)
                    position = (IntExpr)_context.MkITE(
                        TranslateEquality(itemType, itemType,
                            EntryKey(type, At(collection, _context.MkInt(i))),
                            item),
                        _context.MkInt(i), position);
                return position;
            }
            if (type is not MapType && UsesSmtEquality(itemType))
                return _context.MkIndexOf(
                    collection, _context.MkUnit(item), _context.MkInt(0));

            var key = type.CanonicalRepresentation;
            if (!_collectionSearches.TryGetValue(key, out var find))
            {
                find = _context.MkFuncDecl($"p_find_{_collectionSearches.Count}",
                    new[] { TranslateSort(type), TranslateSort(itemType) },
                    _context.IntSort);
                _collectionSearches.Add(key, find);
            }
            var result = (IntExpr)find.Apply(collection, item);
            // Sets and maps have unique matching entries. A constrained witness
            // avoids recursively unfolding an arbitrary-length symbolic sequence.
            RequireDefined(_context.MkOr(
                _context.MkAnd(
                    _context.MkEq(result, _context.MkInt(-1)),
                    _context.MkNot(Contains(type, collection, item))),
                _context.MkAnd(InBounds(collection, result),
                    TranslateEquality(
                        itemType, itemType,
                        EntryKey(type, At(collection, result)), item))));
            return result;
        }

        private static bool UsesSmtEquality(PLanguageType type)
        {
            type = type.Canonicalize();
            if (type is TupleType or NamedTupleType)
                return GetFieldTypes(type).All(UsesSmtEquality);
            if (type is SequenceType sequence)
                return UsesSmtEquality(sequence.ElementType);
            return !IsCollectionType(type) &&
                !type.IsSameTypeAs(PrimitiveType.Float);
        }

        private BoolExpr CollectionEqual(
            PLanguageType leftType,
            PLanguageType rightType,
            SeqExpr left,
            SeqExpr right)
        {
            if (leftType is SequenceType &&
                left.Sort.Equals(right.Sort) && UsesSmtEquality(leftType))
                return _context.MkEq(left, right);

            // Equal lengths let a literal on either side bound both enumerations.
            var range = KnownLength(left).HasValue ? left : right;
            return _context.MkAnd(
                _context.MkEq(Length(left), Length(right)),
                AllElements(range, i =>
                {
                    var l = At(left, i);
                    if (leftType is SequenceType)
                        return TranslateEquality(
                            CollectionItemType(leftType),
                            CollectionItemType(rightType), l, At(right, i));
                    return AnyElement(range, j =>
                    {
                        var r = At(right, j);
                        var equal = TranslateEquality(
                            CollectionItemType(leftType),
                            CollectionItemType(rightType),
                            EntryKey(leftType, l), EntryKey(rightType, r));
                        return leftType is MapType lm && rightType is MapType rm
                            ? _context.MkAnd(equal, TranslateEquality(
                                lm.ValueType.Canonicalize(),
                                rm.ValueType.Canonicalize(),
                                EntryValue(lm, l), EntryValue(rm, r)))
                            : equal;
                    });
                }));
        }

        private BoolExpr ValueDomain(PLanguageType type, Expr value)
        {
            type = type.Canonicalize();
            if (type is EnumType enumeration)
                return _context.MkOr(enumeration.EnumDecl.Values.Select(e =>
                    _context.MkEq(value, _context.MkBV(e.Value, IntWidth)))
                    .ToArray());
            if (type is TupleType or NamedTupleType)
            {
                var fields = ((TupleSort)TranslateSort(type)).FieldDecls;
                return _context.MkAnd(GetFieldTypes(type)
                    .Select((t, i) => ValueDomain(t, fields[i].Apply(value)))
                    .ToArray());
            }
            if (type.IsSameTypeAs(PrimitiveType.Null))
                return _context.MkEq(value, _context.MkInt(0));
            if (IsNullableIdentityType(type))
                return _context.MkGe((IntExpr)value, _context.MkInt(0));
            if (!IsCollectionType(type)) return _context.MkTrue();

            var sequence = (SeqExpr)value;
            var i = (IntExpr)_context.MkFreshConst("domain_index", _context.IntSort);
            var item = At(sequence, i);
            var key = EntryKey(type, item);
            var valid = ValueDomain(CollectionItemType(type), key);
            if (type is MapType map)
                valid = _context.MkAnd(
                    valid, NonNull(map.KeyType, key),
                    ValueDomain(map.ValueType, EntryValue(map, item)));
            if (type is not SequenceType)
            {
                var j = (IntExpr)_context.MkFreshConst(
                    "distinct_index", _context.IntSort);
                valid = _context.MkAnd(valid, _context.MkForall(
                    new Expr[] { j }, _context.MkImplies(
                        _context.MkAnd(_context.MkGt(j, i), InBounds(sequence, j)),
                        _context.MkNot(TranslateEquality(
                            CollectionItemType(type), CollectionItemType(type),
                            key, EntryKey(type, At(sequence, j)))))));
            }
            // Runtime collection counts and indexes are signed 32-bit integers.
            return _context.MkAnd(
                _context.MkLe(Length(sequence), _context.MkInt(int.MaxValue)),
                _context.MkForall(
                    new Expr[] { i },
                    _context.MkImplies(InBounds(sequence, i), valid)));
        }

        private SeqExpr ReplaceAt(SeqExpr source, IntExpr index, Expr value) =>
            _context.MkConcat(
                Slice(source, _context.MkInt(0), index), _context.MkUnit(value),
                Slice(source, Add(index, 1),
                    (IntExpr)_context.MkSub(Length(source), Add(index, 1))));

        private SeqExpr InsertAt(SeqExpr source, IntExpr index, Expr value) =>
            _context.MkConcat(
                Slice(source, _context.MkInt(0), index), _context.MkUnit(value),
                Slice(source, index, (IntExpr)_context.MkSub(Length(source), index)));

        private SeqExpr RemoveAt(SeqExpr source, IntExpr index) =>
            _context.MkConcat(
                Slice(source, _context.MkInt(0), index),
                Slice(source, Add(index, 1),
                    (IntExpr)_context.MkSub(Length(source), Add(index, 1))));

        private Expr TranslateCollectionUpdate(CollectionUpdateExpr expr)
        {
            var collection = CollectionOperand(expr.Collection);
            var type = expr.Collection.Type.Canonicalize();
            if (type is SequenceType sequence)
            {
                var index = CollectionIndex(expr.Index);
                var value = TranslateAsType(sequence.ElementType, expr.Value);
                RequireDefined(InBounds(collection, index));
                return ReplaceAt(collection, index, value);
            }
            if (type is not MapType map)
                throw new ArgumentException(
                    "Indexed assignment requires a sequence or map.");
            var key = TranslateAsType(map.KeyType, expr.Index);
            var mappedValue = TranslateAsType(map.ValueType, expr.Value);
            RequireDefined(NonNull(map.KeyType, key));
            var position = Find(type, collection, key);
            var exists = _context.MkGe(position, _context.MkInt(0));
            // Updating an existing entry preserves the stored key
            // (e.g. its sign of zero).
            var entry = MapEntrySort(map).MkDecl.Apply(
                _context.MkITE(exists, EntryKey(type, At(collection, position)),
                    key), mappedValue);
            RequireDefined(_context.MkOr(
                exists,
                _context.MkLt(Length(collection), _context.MkInt(int.MaxValue))));
            return (SeqExpr)_context.MkITE(
                exists, ReplaceAt(collection, position, entry),
                InsertUnordered(expr, collection, entry));
        }

        private Expr TranslateCollectionInsert(CollectionInsertExpr expr)
        {
            var collection = CollectionOperand(expr.Collection);
            var type = expr.Collection.Type.Canonicalize();
            RequireDefined(_context.MkLt(
                Length(collection), _context.MkInt(int.MaxValue)));
            if (type is SequenceType sequence)
            {
                var index = CollectionIndex(expr.Index);
                var value = TranslateAsType(sequence.ElementType, expr.Value);
                RequireDefined(_context.MkAnd(
                    _context.MkGe(index, _context.MkInt(0)),
                    _context.MkLe(index, Length(collection))));
                return InsertAt(collection, index, value);
            }
            if (type is not MapType map)
                throw new ArgumentException(
                    "Insertion requires a sequence or map.");
            var key = TranslateAsType(map.KeyType, expr.Index);
            var mappedValue = TranslateAsType(map.ValueType, expr.Value);
            RequireDefined(NonNull(map.KeyType, key));
            RequireDefined(_context.MkNot(Contains(type, collection, key)));
            return InsertUnordered(
                expr, collection, MapEntrySort(map).MkDecl.Apply(key, mappedValue));
        }

        private Expr TranslateSetAdd(SetAddExpr expr)
        {
            var collection = CollectionOperand(expr.Set);
            if (expr.Set.Type.Canonicalize() is not SetType type)
                throw new ArgumentException("Set addition requires a set.");
            var value = TranslateAsType(type.ElementType, expr.Value);
            // PSet.Add invokes Clone on the value without a null check.
            RequireDefined(NonNull(type.ElementType, value));
            var exists = Contains(type, collection, value);
            RequireDefined(_context.MkOr(
                exists,
                _context.MkLt(Length(collection), _context.MkInt(int.MaxValue))));
            return (SeqExpr)_context.MkITE(exists, collection,
                InsertUnordered(expr, collection, value));
        }

        private Expr TranslateCollectionRemove(CollectionRemoveExpr expr)
        {
            var collection = CollectionOperand(expr.Collection);
            var type = expr.Collection.Type.Canonicalize();
            if (type is SequenceType)
            {
                var index = CollectionIndex(expr.Item);
                RequireDefined(InBounds(collection, index));
                return RemoveAt(collection, index);
            }
            var item = TranslateAsType(CollectionItemType(type), expr.Item);
            if (type is MapType)
                RequireDefined(NonNull(CollectionItemType(type), item));
            var position = Find(type, collection, item);
            return (SeqExpr)_context.MkITE(
                _context.MkGe(position, _context.MkInt(0)),
                RemoveAt(collection, position), collection);
        }

        private Expr TranslateMapProjection(
            SymExpr expr, SequenceType resultType, bool values)
        {
            var collection = CollectionOperand(expr);
            if (expr.Type.Canonicalize() is not MapType map)
                throw new ArgumentException("keys/values requires a map.");
            var itemType = values ? map.ValueType : map.KeyType;
            RequireSameType(itemType, resultType.ElementType);
            var key = map.CanonicalRepresentation + (values ? ":values" : ":keys");
            SeqExpr result;
            if (KnownLength(collection) is int count)
            {
                result = EmptyCollection(resultType);
                for (var i = 0; i < count; i++)
                {
                    var entry = At(collection, _context.MkInt(i));
                    result = _context.MkConcat(
                        result,
                        _context.MkUnit(
                            values ? EntryValue(map, entry) : EntryKey(map, entry)));
                }
            }
            else
            {
                if (!_mapProjections.TryGetValue(key, out var project))
                {
                    project = _context.MkFreshFuncDecl(
                        "p_project", new[] { TranslateSort(map) },
                        TranslateSort(resultType));
                    _mapProjections.Add(key, project);
                }
                result = (SeqExpr)project.Apply(collection);
                RequireDefined(_context.MkAnd(
                    _context.MkEq(Length(result), Length(collection)),
                    AllElements(collection, i =>
                    {
                        var entry = At(collection, i);
                        return _context.MkEq(
                            At(result, i),
                            values ? EntryValue(map, entry) : EntryKey(map, entry));
                    })));
            }
            // PMap.CloneKeys/CloneValues calls Clone without checking for null.
            if (IsNullableIdentityType(itemType.Canonicalize()))
            {
                var i = (IntExpr)_context.MkFreshConst(
                    "projection_index", _context.IntSort);
                var entry = At(collection, i);
                RequireDefined(_context.MkForall(
                    new Expr[] { i },
                    _context.MkImplies(InBounds(collection, i),
                        NonNull(
                            itemType,
                            values
                                ? EntryValue(map, entry)
                                : EntryKey(map, entry)))));
            }
            return result;
        }

        private SeqExpr InsertUnordered(
            SymExpr operation, SeqExpr source, Expr value)
        {
            // HashSet/Dictionary can reuse a removed slot. Model the new entry's
            // position nondeterministically while retaining existing entries' order.
            // Fresh collection symbols already admit every initial enumeration.
            if (_collectionOrders.TryGetValue(operation, out var cached))
            {
                RequireDefined(cached.Constraint);
                return cached.Result;
            }
            var position = (IntExpr)_context.MkFreshConst(
                "insertion_position", _context.IntSort);
            var bounds = _context.MkAnd(_context.MkGe(position, _context.MkInt(0)),
                _context.MkLe(position, Length(source)));
            SeqExpr result;
            if (KnownLength(source) is int count)
            {
                result = _context.MkEmptySeq(source.Sort);
                for (var i = 0; i <= count; i++)
                {
                    var index = _context.MkInt(i);
                    var item = _context.MkITE(
                        _context.MkLt(index, position), At(source, index),
                        _context.MkITE(
                            _context.MkEq(index, position), value,
                            At(source, _context.MkInt(i - 1))));
                    result = _context.MkConcat(result, _context.MkUnit(item));
                }
            }
            else result = InsertAt(source, position, value);
            var constraint = _context.MkAnd(
                bounds, _context.MkEq(Length(result), Add(Length(source), 1)));
            _collectionOrders.Add(operation, (result, constraint));
            RequireDefined(constraint);
            return result;
        }
    }
}
