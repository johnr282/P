using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Z3;
using NUnit.Framework;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using Plang.Compiler;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.Types;

namespace UnitTests
{
    [TestFixture]
    public class Z3CollectionTranslatorTests
    {
        private static readonly SequenceType IntSeq = new(PrimitiveType.Int);
        private static readonly SetType IntSet = new(PrimitiveType.Int);
        private static readonly MapType IntMap = new(PrimitiveType.Int, PrimitiveType.Int);
        private static SymExpr I(long value) => new ConcreteExpr((PInt)value, PrimitiveType.Int);
        private static SymExpr F(double value) => new ConcreteExpr((PFloat)value, PrimitiveType.Float);
        private static SymExpr B(bool value) => new ConcreteExpr((PBool)value, PrimitiveType.Bool);
        private static SymExpr Eq(SymExpr l, SymExpr r) => new BinaryExpr(BinOpType.Eq, l, r, PrimitiveType.Bool);
        private static SymExpr Not(SymExpr e) => new UnaryExpr(UnaryOpType.Not, e, PrimitiveType.Bool);
        private static SymExpr And(SymExpr l, SymExpr r) => new BinaryExpr(BinOpType.And, l, r, PrimitiveType.Bool);
        private static SymExpr Or(SymExpr l, SymExpr r) => new BinaryExpr(BinOpType.Or, l, r, PrimitiveType.Bool);
        private static SymExpr Seq(params long[] values) => new SequenceExpr(values.Select(I).ToImmutableArray(), IntSeq);
        private static SymExpr Set(params long[] values) => new SetExpr(values.Select(I).ToImmutableArray(), IntSet);
        private static SymExpr Map(params (long Key, long Value)[] entries) => new MapExpr(
            entries.Select(e => new MapEntry(I(e.Key), I(e.Value))).ToImmutableArray(), IntMap);
        private static SymExpr Size(SymExpr e) => new CollectionSizeExpr(e);
        private static SymExpr Has(SymExpr e, long value) => new CollectionContainsExpr(e, I(value));
        private static SymExpr At(SymExpr e, long index, PLanguageType type = null) =>
            new CollectionAccessExpr(e, I(index), type ?? PrimitiveType.Int);

        private static void Check(SymExpr formula, Status expected = Status.SATISFIABLE)
        {
            using var context = new Context();
            using var solver = context.MkSolver();
            solver.Set("timeout", 5000u);
            solver.Assert(new Z3Translator(context).Translate(formula));
            var status = solver.Check();
            if (status == Status.SATISFIABLE)
                Assert.That(((BoolExpr)solver.Model.Evaluate(solver.Assertions[0], true)).IsFalse, Is.False,
                    "Z3 returned a model that does not satisfy the translated formula.");
            if (status != expected && status == Status.SATISFIABLE)
            {
                TestContext.WriteLine(solver.Model);
            }
            Assert.That(status, Is.EqualTo(expected), solver.ReasonUnknown);
        }

        private static void Prove(SymExpr formula) => Check(Not(formula), Status.UNSATISFIABLE);

        [Test]
        public void SequenceLiteralsAndConcreteValuesAgree()
        {
            Prove(Eq(Seq(3, 1, 3), new ConcreteExpr(new PSeq(new IPValue[] { (PInt)3, (PInt)1, (PInt)3 }), IntSeq)));
            Prove(Eq(Size(Seq()), I(0)));
            Prove(Eq(Size(Seq(3, 1, 3)), I(3)));
            Prove(Eq(At(Seq(3, 1, 3), 1), I(1)));
            Prove(Has(Seq(3, 1, 3), 3));
            Prove(Not(Has(Seq(3, 1, 3), 9)));
            Check(Eq(Seq(1, 2), Seq(2, 1)), Status.UNSATISFIABLE);
        }

        [Test]
        public void SequenceUpdatesPreserveOldValuesAndShiftPositions()
        {
            var original = Seq(10, 20);
            var inserted = new CollectionInsertExpr(original, I(1), I(15));
            Prove(Eq(inserted, Seq(10, 15, 20)));
            Prove(Eq(new CollectionUpdateExpr(inserted, I(2), I(30)), Seq(10, 15, 30)));
            Prove(Eq(new CollectionRemoveExpr(inserted, I(1)), original));
            Prove(Eq(new CollectionInsertExpr(original, I(2), I(30)), Seq(10, 20, 30)));
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void InvalidSequenceAccessIsGuarded(long index)
        {
            var invalid = Eq(At(Seq(1, 2), index), I(0));
            Check(invalid, Status.UNSATISFIABLE);
            Check(Not(invalid), Status.UNSATISFIABLE);
            Check(Or(B(true), invalid));
            Check(Not(And(B(false), invalid)));
        }

        [Test]
        public void IndexConversionMatchesPIntRuntimeConversion()
        {
            Prove(Eq(At(Seq(7), 1L << 32), I(7)));
            Check(Eq(At(Seq(7), uint.MaxValue), I(7)), Status.UNSATISFIABLE);
        }

        [Test]
        public void InvalidSequenceUpdatesAndRemovalsAreGuarded()
        {
            foreach (var invalid in new SymExpr[]
            {
                new CollectionUpdateExpr(Seq(), I(0), I(2)),
                new CollectionInsertExpr(Seq(), I(1), I(2)),
                new CollectionRemoveExpr(Seq(), I(0))
            })
            {
                Check(Eq(Size(invalid), I(0)), Status.UNSATISFIABLE);
                Check(Or(B(true), Eq(Size(invalid), I(0))));
            }
        }

        [Test]
        public void SetDuplicatesAndEqualityUseMembership()
        {
            Prove(Eq(Size(Set(3, 1, 3)), I(2)));
            Prove(Eq(Set(3, 1, 3), Set(1, 3)));
            Prove(Eq(Set(3, 1), new ConcreteExpr(new PSet(new HashSet<IPValue> { (PInt)1, (PInt)3 }), IntSet)));
            Check(Eq(Set(1), Set(2)), Status.UNSATISFIABLE);
        }

        [Test]
        public void SetAddRemoveAndRepeatedReads()
        {
            var added = new SetAddExpr(Set(1, 2), I(3));
            Prove(Eq(Size(added), I(3)));
            Prove(Has(added, 3));
            Prove(Eq(At(added, 0), At(added, 0)));
            Prove(Eq(new CollectionRemoveExpr(added, I(2)), Set(1, 3)));
            Prove(Eq(new SetAddExpr(Set(1), I(1)), Set(1)));
            Prove(Eq(new CollectionRemoveExpr(Set(1), I(9)), Set(1)));
        }

        [Test]
        public void MapLiteralsAccessAndEquality()
        {
            Prove(Eq(Map((1, 10), (2, 20)), Map((2, 20), (1, 10))));
            Prove(Eq(Size(Map((1, 10))), I(1)));
            Prove(Has(Map((1, 10)), 1));
            Prove(Not(Has(Map((1, 10)), 10)));
            Prove(Eq(At(Map((1, 10)), 1), I(10)));
            Prove(Eq(Map((1, 10)), new ConcreteExpr(new PMap(new Dictionary<IPValue, IPValue> { [(PInt)1] = (PInt)10 }), IntMap)));
            Check(Eq(Map((1, 10)), Map((1, 11))), Status.UNSATISFIABLE);
        }

        [Test]
        public void MapUpdatesInsertOrReplaceAndInsertionRejectsDuplicates()
        {
            Prove(Eq(new CollectionUpdateExpr(Map((1, 10)), I(1), I(11)), Map((1, 11))));
            Prove(Eq(new CollectionUpdateExpr(Map((1, 10)), I(2), I(20)), Map((1, 10), (2, 20))));
            Prove(Eq(new CollectionInsertExpr(Map((1, 10)), I(2), I(20)), Map((1, 10), (2, 20))));
            var duplicate = Eq(Size(new CollectionInsertExpr(Map((1, 10)), I(1), I(99))), I(2));
            Check(duplicate, Status.UNSATISFIABLE);
            Check(Or(B(true), duplicate));
            Check(Eq(Size(Map((1, 10), (1, 20))), I(2)), Status.UNSATISFIABLE);
        }

        [Test]
        public void MissingMapKeysAndRemovals()
        {
            var invalid = Eq(At(Map((1, 10)), 2), I(0));
            Check(invalid, Status.UNSATISFIABLE);
            Check(Or(B(true), invalid));
            Prove(Eq(new CollectionRemoveExpr(Map((1, 10), (2, 20)), I(1)), Map((2, 20))));
            Prove(Eq(new CollectionRemoveExpr(Map((1, 10)), I(2)), Map((1, 10))));
        }

        [Test]
        public void MapProjectionsKeepKeyValuePairing()
        {
            var map = new CollectionInsertExpr(Map((1, 10)), I(2), I(20));
            var keys = new MapKeysExpr(map, IntSeq);
            var values = new MapValuesExpr(map, IntSeq);
            Prove(Eq(Size(keys), I(2)));
            Prove(Eq(new CollectionAccessExpr(map, At(keys, 0), PrimitiveType.Int), At(values, 0)));
            Prove(Eq(new MapKeysExpr(Map(), IntSeq), Seq()));
        }

        [TestCase("seq")]
        [TestCase("set")]
        [TestCase("map")]
        public void WholeCollectionSymbolsHaveUnknownSizeAndContents(string kind)
        {
            PLanguageType type = kind == "seq" ? IntSeq : kind == "set" ? IntSet : IntMap;
            var symbol = new SymbolFactory().CreateFresh(type);
            Check(Eq(Size(symbol), I(0)));
            Check(Eq(Size(symbol), I(2)));
            Check(And(Eq(Size(symbol), I(0)), Has(symbol, 1)), Status.UNSATISFIABLE);
            Check(And(Eq(Size(symbol), I(1)), Has(symbol, 1)));
            Check(Eq(Size(symbol), I(-1)), Status.UNSATISFIABLE);
        }

        [Test]
        public void SymbolicSetElementsCanAlias()
        {
            var x = new SymbolExpr(new SymbolID(1), PrimitiveType.Int);
            var set = new SetExpr(ImmutableArray.Create(x, I(1)), IntSet);
            Check(And(Eq(x, I(1)), Not(Eq(Size(set), I(1)))), Status.UNSATISFIABLE);
            Check(And(Eq(x, I(2)), Not(Eq(Size(set), I(2)))), Status.UNSATISFIABLE);
        }

        [Test]
        public void NestedCollectionsAndTuples()
        {
            var type = new SequenceType(IntSeq);
            var nested = new SequenceExpr(ImmutableArray.Create(Seq(1, 2), Seq(3)), type);
            Prove(Eq(At(nested, 0, IntSeq), Seq(1, 2)));
            Prove(Eq(new CollectionRemoveExpr(At(nested, 0, IntSeq), I(0)), Seq(2)));
            var tupleType = new TupleType(IntSeq, IntMap);
            var tuple = new TupleExpr(ImmutableArray.Create(Seq(1), Map((1, 10))), tupleType);
            Check(Eq(new SymbolFactory().CreateFresh(tupleType), tuple));
        }

        [Test]
        public void CollectionQueriesCanReturnBooleansAndStrings()
        {
            var booleans = new SequenceExpr(ImmutableArray.Create(B(true)), new SequenceType(PrimitiveType.Bool));
            Prove(At(booleans, 0, PrimitiveType.Bool));
            var text = new ConcreteExpr((PString)"a\0b", PrimitiveType.String);
            var strings = new SequenceExpr(ImmutableArray.Create<SymExpr>(text), new SequenceType(PrimitiveType.String));
            Prove(Eq(At(strings, 0, PrimitiveType.String), text));
        }

        [Test]
        public void FloatEqualityAndMembershipUsePEquality()
        {
            var floats = new SequenceType(PrimitiveType.Float);
            SymExpr SeqFloat(double v) => new SequenceExpr(ImmutableArray.Create(F(v)), floats);
            Prove(Eq(SeqFloat(-0.0), SeqFloat(0.0)));
            Check(Eq(SeqFloat(double.NaN), SeqFloat(double.NaN)), Status.UNSATISFIABLE);
            Prove(new CollectionContainsExpr(SeqFloat(-0.0), F(0.0)));
            Prove(Not(new CollectionContainsExpr(SeqFloat(double.NaN), F(double.NaN))));
            var set = new SetExpr(ImmutableArray.Create(F(0.0), F(-0.0)), new SetType(PrimitiveType.Float));
            Prove(Eq(Size(set), I(1)));
        }

        [Test]
        public void SymbolicEnumElementsAreConstrainedRecursively()
        {
            var decl = new Plang.Compiler.TypeChecker.AST.Declarations.PEnum("Sparse", new PParser.EnumTypeDefDeclContext(null, 0));
            decl.AddElement(new EnumElem("One", new PParser.EnumElemContext(null, 0)) { Value = 1 });
            var type = new EnumType(decl);
            var symbol = new SymbolExpr(new SymbolID(1), new SequenceType(new TupleType(type)));
            var tuple = new TupleExpr(ImmutableArray.Create<SymExpr>(new ConcreteExpr((PInt)1, type)), new TupleType(type));
            Check(And(Eq(Size(symbol), I(1)), Eq(At(symbol, 0, tuple.Type), tuple)));
            Prove(Or(Eq(Size(symbol), I(0)), Eq(At(symbol, 0, tuple.Type), tuple)));
        }

        [TestCase("any")]
        [TestCase("data")]
        [TestCase("event")]
        [TestCase("foreign")]
        public void UnsupportedTypesAreRejectedInsideCollections(string name)
        {
            PLanguageType bad = name switch { "any" => PrimitiveType.Any, "data" => PrimitiveType.Data,
                "event" => PrimitiveType.Event, _ => new ForeignType("Opaque") };
            var alias = new TypeDefType(new TypeDef("Alias", new PParser.TypeDefDeclContext(null, 0)) { Type = bad });
            using var context = new Context();
            foreach (var type in new PLanguageType[] { new SequenceType(alias), new SetType(alias),
                new MapType(PrimitiveType.Int, new SequenceType(alias)), new MapType(alias, PrimitiveType.Int) })
            {
                var symbol = new SymbolExpr(new SymbolID(1), type);
                Assert.Throws<NotSupportedException>(() => new Z3Translator(context).Translate(Eq(Size(symbol), I(0))));
            }
        }

        [Test]
        public void TypeMismatchesFailBeforeZ3SortErrors()
        {
            using var context = new Context();
            var translator = new Z3Translator(context);
            Assert.Throws<ArgumentException>(() => translator.Translate(new CollectionContainsExpr(Seq(1), B(true))));
            Assert.Throws<ArgumentException>(() => translator.Translate(Eq(At(Seq(1), 0, PrimitiveType.Bool), B(true))));
            Assert.Throws<ArgumentException>(() => translator.Translate(Eq(Size(new CollectionUpdateExpr(Set(1), I(0), I(2))), I(1))));
        }

        [Test]
        public void TranslatorReuseDoesNotLeakInvalidCollectionGuards()
        {
            using var context = new Context();
            var translator = new Z3Translator(context);
            translator.Translate(Eq(At(Seq(), 0), I(0)));
            using var solver = context.MkSolver();
            solver.Assert(translator.Translate(B(true)));
            Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
        }

        [Test]
        public void UpdatedCollectionsHaveSatisfiableModels()
        {
            Check(Eq(new CollectionUpdateExpr(Seq(1, 2), I(0), I(9)), Seq(9, 2)));
            Check(Eq(new SetAddExpr(Set(1), I(2)), Set(2, 1)));
            Check(Eq(new CollectionUpdateExpr(Map((1, 10)), I(2), I(20)), Map((2, 20), (1, 10))));
            Check(Eq(new CollectionInsertExpr(Map(), I(1), I(10)), Map((1, 10))));
        }

        [Test]
        public void EnumerationIsArbitraryButConsistent()
        {
            var added = new SetAddExpr(Set(1), I(2));
            Check(Eq(At(added, 0), I(1)));
            Check(Eq(At(added, 0), I(2)));
            Check(And(Eq(At(added, 0), I(1)), Eq(At(added, 0), I(2))), Status.UNSATISFIABLE);
        }

        [Test]
        public void SymbolicSequenceUpdatesAndNestedQueries()
        {
            var source = new SymbolExpr(new SymbolID(1), IntSeq);
            var updated = new CollectionUpdateExpr(source, I(0), I(42));
            Check(And(Eq(Size(source), I(2)), Eq(updated, Seq(42, 8))));
            Prove(Or(Eq(Size(source), I(0)), Eq(At(updated, 0), I(42))));
            var type = new MapType(PrimitiveType.Int, IntSeq);
            var map = new MapExpr(ImmutableArray.Create(new MapEntry(I(1), Seq(4, 5))), type);
            Prove(Eq(Size(At(map, 1, IntSeq)), I(2)));
        }

        [Test]
        public void SymbolicMapsSupportUpdateLookupAndProjection()
        {
            var source = new SymbolExpr(new SymbolID(1), IntMap);
            var updated = new CollectionUpdateExpr(source, I(1), I(42));
            Check(And(Eq(Size(source), I(0)), Eq(At(updated, 1), I(42))));
            Prove(Or(Not(Has(source, 1)), Eq(At(updated, 1), I(42))));
            var keys = new MapKeysExpr(source, IntSeq);
            var values = new MapValuesExpr(source, IntSeq);
            Check(And(Eq(source, Map((1, 10))), Eq(values, Seq(10))));
            Prove(Or(Eq(Size(source), I(0)), Eq(new CollectionAccessExpr(source, At(keys, 0), PrimitiveType.Int), At(values, 0))));
        }

        [Test]
        public void CollectionAssignmentsAcceptCompatibleReferenceTypes()
        {
            var nil = new ConcreteExpr(null, PrimitiveType.Null);
            var machineSequence = new SequenceExpr(ImmutableArray.Create<SymExpr>(nil), new SequenceType(PrimitiveType.Machine));
            Prove(new CollectionContainsExpr(machineSequence, nil));
            Prove(Eq(At(machineSequence, 0, PrimitiveType.Machine), nil));

            var nullTuple = new TupleExpr(ImmutableArray.Create<SymExpr>(nil), new TupleType(PrimitiveType.Null));
            var machineTupleType = new TupleType(PrimitiveType.Machine);
            var source = new SequenceExpr(ImmutableArray.Create<SymExpr>(nullTuple), new SequenceType(machineTupleType));
            Check(Eq(At(source, 0, machineTupleType), nullTuple));
            Prove(Eq(At(source, 0, machineTupleType), nullTuple));

            var nested = new SequenceExpr(ImmutableArray.Create<SymExpr>(
                new SequenceExpr(ImmutableArray.Create<SymExpr>(nullTuple), new SequenceType(nullTuple.Type))),
                new SequenceType(new SequenceType(machineTupleType)));
            Check(Eq(Size(nested), I(1)));
            Prove(Eq(new CollectionAccessExpr(At(nested, 0, new SequenceType(machineTupleType)), I(0), machineTupleType), nullTuple));
        }

        [Test]
        public void NullKeyAndNullCloneFailuresAreGuarded()
        {
            var nil = new ConcreteExpr(null, PrimitiveType.Null);
            var map = new MapExpr(ImmutableArray.Create(new MapEntry(nil, I(1))), new MapType(PrimitiveType.Machine, PrimitiveType.Int));
            Check(Eq(Size(map), I(1)), Status.UNSATISFIABLE);
            Check(Or(B(true), Eq(Size(map), I(1))));
            var set = new SetAddExpr(new SetExpr(ImmutableArray<SymExpr>.Empty, new SetType(PrimitiveType.Machine)), nil);
            Check(Eq(Size(set), I(1)), Status.UNSATISFIABLE);
            var valueMap = new MapExpr(ImmutableArray.Create(new MapEntry(I(1), nil)), new MapType(PrimitiveType.Int, PrimitiveType.Machine));
            Check(Eq(At(valueMap, 1, PrimitiveType.Machine), nil));
            var invalidProjection = Eq(Size(new MapValuesExpr(valueMap, new SequenceType(PrimitiveType.Machine))), I(1));
            Check(invalidProjection, Status.UNSATISFIABLE);
            Check(Or(B(true), invalidProjection));
        }

        [Test]
        public void LengthOptimizationDoesNotBoundCollections()
        {
            var longer = Seq(Enumerable.Range(0, 129).Select(i => (long)i).ToArray());
            Prove(Eq(Size(longer), I(129)));
            Prove(Has(longer, 128));
            var symbol = new SymbolExpr(new SymbolID(1), IntSeq);
            Check(Eq(Size(symbol), I(129)));
        }

        [Test]
        public void FloatingMapKeysAndSetAddPreserveIeeeEquality()
        {
            var type = new MapType(PrimitiveType.Float, PrimitiveType.Int);
            var map = new MapExpr(ImmutableArray.Create(new MapEntry(F(-0.0), I(7))), type);
            Prove(new CollectionContainsExpr(map, F(0.0)));
            Prove(Eq(new CollectionAccessExpr(map, F(0.0), PrimitiveType.Int), I(7)));
            var duplicate = new CollectionInsertExpr(map, F(0.0), I(8));
            Check(Eq(Size(duplicate), I(2)), Status.UNSATISFIABLE);
            var set = new SetAddExpr(new SetExpr(ImmutableArray.Create(F(1.0)), new SetType(PrimitiveType.Float)), F(double.NaN));
            Check(Eq(Size(set), I(2)));
            Prove(Eq(Size(set), I(2)));
            Prove(Not(new CollectionContainsExpr(set, F(double.NaN))));
        }
    }
}
