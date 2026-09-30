using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Z3;
using NUnit.Framework;
using Plang.Compiler;
using PChecker.Runtime.Values;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.Types;
using NamedTupleExpr = PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.NamedTupleExpr;
using EnumDeclaration = Plang.Compiler.TypeChecker.AST.Declarations.PEnum;

namespace UnitTests
{
    [TestFixture]
    public class Z3TranslatorTests
    {
        private static ConcreteExpr Int(long value) => new(new PInt(value), PrimitiveType.Int);
        private static ConcreteExpr Float(double value) => new(new PFloat(value), PrimitiveType.Float);
        private static ConcreteExpr Bool(bool value) => new((PBool)value, PrimitiveType.Bool);
        private static ConcreteExpr String(string value) => new((PString)value, PrimitiveType.String);
        private static SymbolExpr Symbol(long id, PLanguageType type) => new(new SymbolID(id), type);
        private static BinaryExpr Binary(BinOpType op, SymExpr left, SymExpr right, PLanguageType type) => new(op, left, right, type);
        private static BinaryExpr Eq(SymExpr left, SymExpr right) => Binary(BinOpType.Eq, left, right, PrimitiveType.Bool);
        private static UnaryExpr Not(SymExpr expr) => new(UnaryOpType.Not, expr, PrimitiveType.Bool);
        private static void Check(SymExpr formula, Status expected = Status.SATISFIABLE)
        {
            using var context = new Context();
            using var solver = context.MkSolver();
            solver.Set("timeout", 10000u);
            solver.Assert(new Z3Translator(context).Translate(formula));
            Assert.That(solver.Check(), Is.EqualTo(expected), solver.ReasonUnknown);
        }

        [TestCase(BinOpType.Add, long.MaxValue, 1, long.MinValue)]
        [TestCase(BinOpType.Sub, long.MinValue, 1, long.MaxValue)]
        [TestCase(BinOpType.Mul, -3, 7, -21)]
        [TestCase(BinOpType.Div, -7, 3, -2)]
        [TestCase(BinOpType.Mod, -7, 3, -1)]
        [TestCase(BinOpType.Mod, 7, -3, 1)]
        public void IntegerArithmetic(BinOpType op, long left, long right, long expected)
        {
            Check(Not(Eq(Binary(op, Int(left), Int(right), PrimitiveType.Int), Int(expected))), Status.UNSATISFIABLE);
        }

        [TestCase(BinOpType.Lt, -1, 0, true)]
        [TestCase(BinOpType.Le, -1, -1, true)]
        [TestCase(BinOpType.Gt, long.MaxValue, long.MinValue, true)]
        [TestCase(BinOpType.Ge, -1, 0, false)]
        public void SignedIntegerComparisons(BinOpType op, long left, long right, bool expected)
        {
            Check(Not(Eq(Binary(op, Int(left), Int(right), PrimitiveType.Bool), Bool(expected))), Status.UNSATISFIABLE);
        }

        [TestCase(BinOpType.And, true, false, false)]
        [TestCase(BinOpType.Or, true, false, true)]
        [TestCase(BinOpType.Neq, true, false, true)]
        public void BooleanOperators(BinOpType op, bool left, bool right, bool expected)
        {
            Check(Not(Eq(Binary(op, Bool(left), Bool(right), PrimitiveType.Bool), Bool(expected))), Status.UNSATISFIABLE);
        }

        [TestCase(BinOpType.Then)]
        [TestCase(BinOpType.Iff)]
        public void VerifierOnlyOperatorsAreUnsupported(BinOpType op)
        {
            using var context = new Context();
            var translator = new Z3Translator(context);
            Assert.Throws<NotSupportedException>(() =>
                translator.Translate(Binary(op, Bool(true), Bool(false), PrimitiveType.Bool)));
        }

        [TestCase(BinOpType.Add, 0.1, 0.2)]
        [TestCase(BinOpType.Sub, 1.0, 0.25)]
        [TestCase(BinOpType.Mul, -2.0, 3.0)]
        [TestCase(BinOpType.Div, 1.0, 0.0)]
        [TestCase(BinOpType.Mod, 5.0, 3.0)]
        [TestCase(BinOpType.Mod, -5.0, 3.0)]
        [TestCase(BinOpType.Mod, 5.0, double.PositiveInfinity)]
        public void FloatArithmetic(BinOpType op, double left, double right)
        {
            var expected = op switch
            {
                BinOpType.Add => left + right,
                BinOpType.Sub => left - right,
                BinOpType.Mul => left * right,
                BinOpType.Div => left / right,
                _ => left % right
            };
            Check(Not(Eq(Binary(op, Float(left), Float(right), PrimitiveType.Float), Float(expected))), Status.UNSATISFIABLE);
        }

        [Test]
        public void FloatingPointEqualityAndNegation()
        {
            Check(Eq(Float(double.NaN), Float(double.NaN)), Status.UNSATISFIABLE);
            Check(Not(Eq(Float(+0.0), Float(-0.0))), Status.UNSATISFIABLE);
            Check(Not(Eq(new UnaryExpr(UnaryOpType.Negate, Float(3), PrimitiveType.Float), Float(-3))), Status.UNSATISFIABLE);
            Check(Not(Eq(new UnaryExpr(UnaryOpType.Negate, Int(long.MinValue), PrimitiveType.Int), Int(long.MinValue))), Status.UNSATISFIABLE);
            Check(Binary(BinOpType.Lt, Float(double.NaN), Float(0), PrimitiveType.Bool), Status.UNSATISFIABLE);
            Check(Binary(BinOpType.Ge, Float(double.PositiveInfinity), Float(0), PrimitiveType.Bool));
        }

        [TestCase("hello", " world")]
        [TestCase("a\0", "b")]
        [TestCase("\\u{61}", "\u03bb\U0001f600")]
        public void Strings(string left, string right)
        {
            Check(Not(Eq(Binary(BinOpType.Add, String(left), String(right), PrimitiveType.String), String(left + right))), Status.UNSATISFIABLE);
            Check(Eq(Symbol(1, PrimitiveType.String), String(left)));
        }

        [Test]
        public void StringEncodingPreservesNulUnicodeAndLiteralEscapes()
        {
            Check(Eq(String("a\0b"), String("a")), Status.UNSATISFIABLE);
            Check(Eq(String("\\u{61}"), String("a")), Status.UNSATISFIABLE);
            Check(Not(Eq(Binary(BinOpType.Add, String("\ud83d"), String("\ude00"), PrimitiveType.String),
                String("\U0001f600"))), Status.UNSATISFIABLE);
        }

        [Test]
        public void IntegerDivisionMustBeDefinedUnlessShortCircuited()
        {
            var invalid = Eq(Binary(BinOpType.Div, Int(1), Int(0), PrimitiveType.Int), Int(-1));
            Check(invalid, Status.UNSATISFIABLE);
            Check(Not(invalid), Status.UNSATISFIABLE);
            Check(Binary(BinOpType.Or, Bool(true), invalid, PrimitiveType.Bool));
            Check(Not(Binary(BinOpType.And, Bool(false), invalid, PrimitiveType.Bool)));
            Check(Eq(Binary(BinOpType.Div, Int(long.MinValue), Int(-1), PrimitiveType.Int), Int(long.MinValue)), Status.UNSATISFIABLE);
        }

        private static EnumType MakeEnum()
        {
            var declaration = new EnumDeclaration("Sparse", new PParser.EnumTypeDefDeclContext(null, 0));
            declaration.AddElement(new EnumElem("Negative", new PParser.EnumElemContext(null, 0)) { Value = -4 });
            declaration.AddElement(new EnumElem("Positive", new PParser.EnumElemContext(null, 0)) { Value = 9 });
            return new EnumType(declaration);
        }

        [Test]
        public void EnumsAreRestrictedToDeclaredValuesIncludingInsideTuples()
        {
            var type = MakeEnum();
            var symbol = Symbol(1, type);
            var negative = new ConcreteExpr((PInt)(-4), type);
            var positive = new ConcreteExpr((PInt)9, type);
            Check(Eq(symbol, negative));
            Check(Eq(symbol, positive));
            Check(Binary(BinOpType.And, Not(Eq(symbol, negative)), Not(Eq(symbol, positive)), PrimitiveType.Bool), Status.UNSATISFIABLE);
            var tuple = new TupleType(type);
            var tupleSymbol = Symbol(2, tuple);
            Check(Binary(BinOpType.And,
                Not(Eq(tupleSymbol, new ConcreteExpr(new PTuple((PInt)(-4)), tuple))),
                Not(Eq(tupleSymbol, new ConcreteExpr(new PTuple((PInt)9), tuple))), PrimitiveType.Bool), Status.UNSATISFIABLE);

            using var context = new Context();
            var translator = new Z3Translator(context);
            Assert.Throws<ArgumentException>(() => translator.Translate(Eq(symbol, new ConcreteExpr((PInt)0, type))));
            Assert.Throws<ArgumentException>(() => translator.Translate(Eq(symbol, Int(-4))));
        }

        [Test]
        public void TupleExpressionsConcreteValuesAndWholeTupleSymbolsAgree()
        {
            var innerType = new TupleType(PrimitiveType.Int, PrimitiveType.Float);
            var inner = new TupleExpr(ImmutableArray.Create<SymExpr>(Int(7), Float(-0.0)), innerType);
            var type = new NamedTupleType(new[]
            {
                new NamedTupleEntry { Name = "z", Type = innerType, FieldNo = 0 },
                new NamedTupleEntry { Name = "a", Type = PrimitiveType.Bool, FieldNo = 1 }
            });
            var expression = new NamedTupleExpr(new Dictionary<string, SymExpr> { ["a"] = Bool(true), ["z"] = inner }.ToImmutableDictionary(), type);
            var concrete = new ConcreteExpr(new PNamedTuple(new[] { "z", "a" }, new PTuple((PInt)7, (PFloat)0.0), (PBool)true), type);
            Check(Not(Eq(expression, concrete)), Status.UNSATISFIABLE);
            Check(Eq(Symbol(1, type), expression));
            var nanTuple = new TupleExpr(ImmutableArray.Create<SymExpr>(Float(double.NaN)), new TupleType(PrimitiveType.Float));
            Check(Eq(nanTuple, nanTuple), Status.UNSATISFIABLE);
        }

        [TestCase("any")]
        [TestCase("data")]
        [TestCase("event")]
        [TestCase("Opaque")]
        public void UnsupportedTypesAreRejectedIncludingAliasesAndNestedTuples(string typeName)
        {
            PLanguageType type = typeName switch
            {
                "any" => PrimitiveType.Any,
                "data" => PrimitiveType.Data,
                "event" => PrimitiveType.Event,
                _ => new ForeignType(typeName)
            };
            IPValue value = typeName switch
            {
                "event" => new PChecker.Runtime.Events.Event((PInt)1),
                "Opaque" => new OpaqueValue(42),
                _ => (PInt)1
            };
            var alias = new TypeDefType(new TypeDef("Dynamic", new PParser.TypeDefDeclContext(null, 0)) { Type = type });
            var tupleType = new TupleType(alias);
            var namedType = new NamedTupleType(new[]
            {
                new NamedTupleEntry { Name = "payload", Type = tupleType, FieldNo = 0 }
            });
            var tuple = new TupleExpr(ImmutableArray.Create<SymExpr>(Symbol(2, alias)), tupleType);
            var named = new NamedTupleExpr(new Dictionary<string, SymExpr> { ["payload"] = tuple }.ToImmutableDictionary(), namedType);
            var expressions = new SymExpr[]
            {
                Symbol(1, type), new ConcreteExpr(value, type), new ConcreteExpr(null, type),
                Symbol(1, alias), new ConcreteExpr(value, alias), new ConcreteExpr(null, alias),
                Symbol(1, tupleType), new ConcreteExpr(new PTuple(value), tupleType), tuple,
                Symbol(1, namedType), new ConcreteExpr(new PNamedTuple(new[] { "payload" }, new PTuple(value)), namedType), named
            };
            using var context = new Context();
            var translator = new Z3Translator(context);
            foreach (var expression in expressions)
            {
                var error = Assert.Throws<NotSupportedException>(() => translator.Translate(Eq(expression, expression)));
                Assert.That(error.Message, Does.Contain(typeName));
            }
            Assert.Throws<NotSupportedException>(() => translator.Translate(Symbol(1, type)));
            Assert.Throws<NotSupportedException>(() => translator.Translate(
                Binary(BinOpType.Or, Bool(true), Eq(Symbol(1, type), Int(1)), PrimitiveType.Bool)));
        }

        [Test]
        public void NullAndIdentityTypesHaveDistinctDomains()
        {
            var nil = new ConcreteExpr(null, PrimitiveType.Null);
            Check(Eq(Symbol(1, PrimitiveType.Null), nil));
            Check(Not(Eq(Symbol(1, PrimitiveType.Null), nil)), Status.UNSATISFIABLE);
            Check(Eq(Symbol(1, PrimitiveType.Machine), nil));
            using var context = new Context();
            var translator = new Z3Translator(context);
            Assert.Throws<ArgumentException>(() => translator.Translate(Eq(Symbol(1, PrimitiveType.Machine), Int(0))));
        }

        [Test]
        public void CompatibleTupleTypesCompareTheirFieldsWithoutDynamicBoxing()
        {
            var machineType = new TupleType(PrimitiveType.Machine, PrimitiveType.Float);
            var nullType = new TupleType(PrimitiveType.Null, PrimitiveType.Float);
            var machineTuple = new ConcreteExpr(new PTuple(null, (PFloat)(-0.0)), machineType);
            var nullTuple = new ConcreteExpr(new PTuple(null, (PFloat)0.0), nullType);
            Check(Not(Eq(machineTuple, nullTuple)), Status.UNSATISFIABLE);
            Check(Eq(Symbol(1, machineType), nullTuple));
            Check(Eq(new ConcreteExpr(new PTuple(null, (PFloat)double.NaN), machineType),
                new ConcreteExpr(new PTuple(null, (PFloat)double.NaN), nullType)), Status.UNSATISFIABLE);

            var nestedMachineType = new NamedTupleType(new[]
            {
                new NamedTupleEntry { Name = "payload", Type = machineType, FieldNo = 0 }
            });
            var nestedNullType = new NamedTupleType(new[]
            {
                new NamedTupleEntry { Name = "payload", Type = nullType, FieldNo = 0 }
            });
            Check(Not(Eq(
                new ConcreteExpr(new PNamedTuple(new[] { "payload" }, machineTuple.Value), nestedMachineType),
                new ConcreteExpr(new PNamedTuple(new[] { "payload" }, nullTuple.Value), nestedNullType))), Status.UNSATISFIABLE);
        }

        [Test]
        public void MachinesAndInterfacesUseMachineIdentity()
        {
            var id = new StateMachineId(typeof(Z3TranslatorTests), "one", null, null, useNameForHashing: true);
            var otherId = new StateMachineId(typeof(Z3TranslatorTests), "two", null, null, useNameForHashing: true);
            var first = new PMachineValue(id, new List<string> { "event" });
            var second = new PMachineValue(id, new List<string>());
            var permission = new PermissionType(new NamedEventSet("Interface", new PParser.EventSetDeclContext(null, 0)));
            Check(Not(Eq(new ConcreteExpr(first, PrimitiveType.Machine), new ConcreteExpr(second, permission))), Status.UNSATISFIABLE);
            Check(Eq(new ConcreteExpr(first, PrimitiveType.Machine),
                new ConcreteExpr(new PMachineValue(otherId, new List<string>()), PrimitiveType.Machine)), Status.UNSATISFIABLE);
            Check(Eq(Symbol(1, permission), new ConcreteExpr(first, PrimitiveType.Machine)));
        }

        private sealed class OpaqueValue : IPValue
        {
            private readonly int value;
            internal OpaqueValue(int value) => this.value = value;
            public bool Equals(IPValue other) => other is OpaqueValue opaque && value == opaque.value;
            public IPValue Clone() => new OpaqueValue(value);
        }

        [Test]
        public void ReuseDoesNotLeakDefinednessAndRestoresEnumConstraints()
        {
            using var context = new Context();
            using var solver = context.MkSolver();
            var translator = new Z3Translator(context);
            var invalid = Eq(Binary(BinOpType.Div, Int(1), Int(0), PrimitiveType.Int), Int(-1));
            translator.Translate(invalid);
            solver.Assert(translator.Translate(Bool(true)));
            Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
            solver.Reset();
            var type = MakeEnum();
            var symbol = Symbol(1, type);
            var negative = new ConcreteExpr((PInt)(-4), type);
            var positive = new ConcreteExpr((PInt)9, type);
            translator.Translate(Eq(symbol, negative));
            solver.Assert(translator.Translate(Binary(BinOpType.And,
                Not(Eq(symbol, negative)), Not(Eq(symbol, positive)), PrimitiveType.Bool)));
            Assert.That(solver.Check(), Is.EqualTo(Status.UNSATISFIABLE));
        }

        [Test]
        public void AliasesAndSymbolConsistency()
        {
            var alias = new TypeDefType(new TypeDef("Integer", new PParser.TypeDefDeclContext(null, 0)) { Type = PrimitiveType.Int });
            Check(Not(Eq(Symbol(1, alias), Symbol(1, PrimitiveType.Int))), Status.UNSATISFIABLE);
            using var context = new Context();
            var translator = new Z3Translator(context);
            Assert.Throws<ArgumentException>(() => translator.Translate(Eq(Symbol(1, PrimitiveType.Int), Symbol(1, PrimitiveType.Bool))));
            Assert.Throws<ArgumentException>(() => translator.Translate(Int(1)));
            Check(Eq(Symbol(1, new SequenceType(PrimitiveType.Int)), Symbol(2, new SequenceType(PrimitiveType.Int))));
        }
    }
}
