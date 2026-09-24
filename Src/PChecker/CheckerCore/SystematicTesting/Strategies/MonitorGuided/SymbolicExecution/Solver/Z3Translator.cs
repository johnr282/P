using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Z3;
using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.Types;
using Plang.Compiler.TypeChecker.AST.Expressions;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver
{
    internal class Z3Translator
    {
        private readonly Context _context;
        private const uint IntWidth = 64;
        private readonly Dictionary<PLanguageType, Sort> _sorts = new();
        private readonly List<IPValue> _identities = new();
        private BoolExpr _evaluationGuard;
        private readonly List<BoolExpr> _domainConstraints = new();
        private readonly Dictionary<
            SymbolID,
            (PLanguageType Type, Expr Expression)> _symbols = new();

        internal Z3Translator(Context context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        internal BoolExpr Translate(SymExpr expr)
        {
            ArgumentNullException.ThrowIfNull(expr);
            RejectUnsupportedTypes(expr.Type);
            if (!expr.Type.Canonicalize().IsSameTypeAs(PrimitiveType.Bool))
            {
                throw new ArgumentException(
                    "The formula must have Boolean type.",
                    nameof(expr));
            }

            // Constraints belong to this formula, including when the translator is reused.
            _domainConstraints.Clear();
            _symbols.Clear();
            _evaluationGuard = _context.MkTrue();
            BoolExpr boolExpr = TranslateBoolean(expr);

            return _context.MkAnd(_domainConstraints.Append(boolExpr).ToArray());
        }

        private Expr TranslateExpr(SymExpr expr)
        {
            var type = expr.Type.Canonicalize();
            RejectUnsupportedTypes(type);

            return type switch
            {
                PrimitiveType => TranslatePrimitive(expr),
                EnumType => TranslateEnum(expr),
                TupleType or NamedTupleType => TranslateTuple(expr),
                PermissionType => TranslateIdentity(expr),
                _ => throw new NotSupportedException($"Unsupported P type: {type}")
            };
        }

        private Expr TranslatePrimitive(SymExpr primitiveExpr)
        {
            var type = (PrimitiveType)primitiveExpr.Type.Canonicalize();

            if (type.IsSameTypeAs(PrimitiveType.Bool))
            {
                return TranslateBoolean(primitiveExpr);
            }
            else if (type.IsSameTypeAs(PrimitiveType.Int))
            {
                return TranslateInt(primitiveExpr);
            }
            else if (type.IsSameTypeAs(PrimitiveType.Float))
            {
                return TranslateFloat(primitiveExpr);
            }
            else if (type.IsSameTypeAs(PrimitiveType.String))
            {
                return TranslateString(primitiveExpr);
            }
            else if (IsNullableIdentityType(type))
            {
                return TranslateIdentity(primitiveExpr);
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported primitive expression: {primitiveExpr}");
            }
        }

        private BoolExpr TranslateBoolean(SymExpr boolExpr)
        {
            return boolExpr switch
            {
                ConcreteExpr concrete => TranslateBooleanConcrete(concrete),
                SymbolExpr symbol => (BoolExpr)TranslateSymbol(symbol),
                BinaryExpr binary => TranslateBooleanBinary(binary),
                UnaryExpr unary => TranslateBooleanUnary(unary),

                _ => throw new NotSupportedException(
                    $"Unsupported Boolean expression: {boolExpr.GetType().Name}")
            };
        }

        private BoolExpr TranslateBooleanConcrete(ConcreteExpr concrete)
        {
            return concrete.Value switch
            {
                PBool value => _context.MkBool((bool)value),

                _ => throw new ArgumentException(
                    "Expected a concrete PBool value.", nameof(concrete)),
            };
        }

        private BoolExpr TranslateBooleanBinary(BinaryExpr binary)
        {
            var leftType = binary.Left.Type.Canonicalize();
            var rightType = binary.Right.Type.Canonicalize();
            var left = TranslateExpr(binary.Left);
            // _evaluationGuard allows short-circuiting; example: x == 0 || 10 / x > 2
            var savedGuard = _evaluationGuard;
            Expr right;
            try
            {
                if (binary.Op == BinOpType.And)
                    _evaluationGuard = _context.MkAnd(savedGuard, (BoolExpr)left);
                else if (binary.Op == BinOpType.Or)
                    _evaluationGuard = _context.MkAnd(
                        savedGuard,
                        _context.MkNot((BoolExpr)left));
                right = TranslateExpr(binary.Right);
            }
            finally
            {
                _evaluationGuard = savedGuard;
            }
            return binary.Op switch
            {
                BinOpType.Eq => TranslateEquality(leftType, rightType, left, right),
                BinOpType.Neq => _context.MkNot(TranslateEquality(leftType, rightType, left, right)),
                BinOpType.Lt when left is BitVecExpr l && right is BitVecExpr r => _context.MkBVSLT(l, r),
                BinOpType.Le when left is BitVecExpr l && right is BitVecExpr r => _context.MkBVSLE(l, r),
                BinOpType.Gt when left is BitVecExpr l && right is BitVecExpr r => _context.MkBVSGT(l, r),
                BinOpType.Ge when left is BitVecExpr l && right is BitVecExpr r => _context.MkBVSGE(l, r),
                BinOpType.Lt when left is FPExpr l && right is FPExpr r => _context.MkFPLt(l, r),
                BinOpType.Le when left is FPExpr l && right is FPExpr r => _context.MkFPLEq(l, r),
                BinOpType.Gt when left is FPExpr l && right is FPExpr r => _context.MkFPGt(l, r),
                BinOpType.Ge when left is FPExpr l && right is FPExpr r => _context.MkFPGEq(l, r),
                BinOpType.And when left is BoolExpr l && right is BoolExpr r => _context.MkAnd(l, r),
                BinOpType.Or when left is BoolExpr l && right is BoolExpr r => _context.MkOr(l, r),

                _ => throw new NotSupportedException(
                    $"Unsupported BinOpType: {binary.Op}")
            };
        }

        private BoolExpr TranslateBooleanUnary(UnaryExpr unary)
        {
            return unary.Op switch
            {
                UnaryOpType.Not => _context.MkNot((BoolExpr)TranslateExpr(unary.SubExpr)),

                _ => throw new NotSupportedException(
                    $"Unsupported Boolean unary operator: {unary.Op}")
            };
        }

        private BitVecExpr TranslateInt(SymExpr expr)
        {
            return expr switch
            {
                ConcreteExpr { Value: PInt value } =>
                    _context.MkBV((long)value, IntWidth),
                SymbolExpr symbol => (BitVecExpr)TranslateSymbol(symbol),
                UnaryExpr { Op: UnaryOpType.Negate } unary =>
                    _context.MkBVNeg((BitVecExpr)TranslateExpr(unary.SubExpr)),
                BinaryExpr binary => TranslateIntBinary(binary),

                _ => throw new NotSupportedException(
                    $"Unsupported integer expression: {expr}")
            };
        }

        private BitVecExpr TranslateIntBinary(BinaryExpr binary)
        {
            var left = (BitVecExpr)TranslateExpr(binary.Left);
            var right = (BitVecExpr)TranslateExpr(binary.Right);
            if (binary.Op is BinOpType.Div or BinOpType.Mod)
            {
                // If _evaluationGuard holds, this expression will be evaluated,
                // so we must restrict Z3 to avoid division by zero and signed overflow.
                _domainConstraints.Add(_context.MkImplies(_evaluationGuard, _context.MkAnd(
                    _context.MkNot(_context.MkEq(right, _context.MkBV(0, IntWidth))),
                    _context.MkBVSDivNoOverflow(left, right))));
            }

            return binary.Op switch
            {
                BinOpType.Add => _context.MkBVAdd(left, right),
                BinOpType.Sub => _context.MkBVSub(left, right),
                BinOpType.Mul => _context.MkBVMul(left, right),
                BinOpType.Div => _context.MkBVSDiv(left, right),
                BinOpType.Mod => _context.MkBVSRem(left, right),

                _ => throw new NotSupportedException(
                    $"Unsupported integer binary operator: {binary.Op}")
            };
        }

        private FPExpr TranslateFloat(SymExpr expr)
        {
            return expr switch
            {
                ConcreteExpr { Value: PFloat value } =>
                    _context.MkFPToFP(
                        _context.MkBV(
                            unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)),
                            IntWidth),
                        _context.MkFPSort64()),
                SymbolExpr symbol => (FPExpr)TranslateSymbol(symbol),
                UnaryExpr { Op: UnaryOpType.Negate } unary =>
                    _context.MkFPNeg((FPExpr)TranslateExpr(unary.SubExpr)),
                BinaryExpr binary => TranslateFloatBinary(binary),

                _ => throw new NotSupportedException(
                    $"Unsupported float expression: {expr}")
            };
        }

        private FPExpr TranslateFloatBinary(BinaryExpr binary)
        {
            var left = (FPExpr)TranslateExpr(binary.Left);
            var right = (FPExpr)TranslateExpr(binary.Right);
            var rounding = _context.MkFPRoundNearestTiesToEven();
            return binary.Op switch
            {
                BinOpType.Add => _context.MkFPAdd(rounding, left, right),
                BinOpType.Sub => _context.MkFPSub(rounding, left, right),
                BinOpType.Mul => _context.MkFPMul(rounding, left, right),
                BinOpType.Div => _context.MkFPDiv(rounding, left, right),
                BinOpType.Mod => TranslateFloatRemainder(left, right),

                _ => throw new NotSupportedException(
                    $"Unsupported float binary operator: {binary.Op}")
            };
        }

        private FPExpr TranslateFloatRemainder(FPExpr left, FPExpr right)
        {
            // P's % truncates the quotient; fp.rem instead rounds it to the nearest integer.
            var l = _context.MkFPToReal(left);
            var r = _context.MkFPToReal(right);
            var quotient = (RealExpr)_context.MkDiv(l, r);
            var integral = (IntExpr)_context.MkITE(
                _context.MkLt(quotient, _context.MkReal(0)),
                _context.MkUnaryMinus(_context.MkReal2Int((RealExpr)_context.MkUnaryMinus(quotient))),
                _context.MkReal2Int(quotient));
            var remainder = (RealExpr)_context.MkSub(
                l,
                _context.MkMul(_context.MkInt2Real(integral), r));
            var sort = _context.MkFPSort64();
            var zero = (FPExpr)_context.MkITE(
                _context.MkFPIsNegative(left),
                _context.MkFPZero(sort, true),
                _context.MkFPZero(sort, false));
            // Handle special cases for NaN, infinite, or zero operands.
            return (FPExpr)_context.MkITE(
                _context.MkOr(
                    _context.MkFPIsNaN(left),
                    _context.MkFPIsNaN(right),
                    _context.MkFPIsInfinite(left),
                    _context.MkFPIsZero(right)),
                _context.MkFPNaN(sort),
                _context.MkITE(
                    _context.MkOr(
                        _context.MkFPIsZero(left),
                        _context.MkFPIsInfinite(right)),
                    left,
                    _context.MkITE(
                        _context.MkEq(remainder, _context.MkReal(0)),
                        zero,
                        _context.MkFPToFP(
                            _context.MkFPRoundNearestTiesToEven(),
                            remainder,
                            sort))));
        }

        private SeqExpr TranslateString(SymExpr expr)
        {
            return expr switch
            {
                ConcreteExpr { Value: PString value } =>
                    TranslateStringConcrete((string)value),
                SymbolExpr symbol => (SeqExpr)TranslateSymbol(symbol),
                BinaryExpr { Op: BinOpType.Add } binary => _context.MkConcat(
                    (SeqExpr)TranslateExpr(binary.Left),
                    (SeqExpr)TranslateExpr(binary.Right)),

                _ => throw new NotSupportedException(
                    $"Unsupported string expression: {expr}")
            };
        }

        private SeqExpr TranslateStringConcrete(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            // Encode UTF-16 code units explicitly: the native string API truncates
            // at NUL and interprets backslash Unicode escapes in its input.
            return _context.MkString(
                string.Concat(value.Select(c => $"\\u{{{(int)c:x}}}")));
        }

        private Expr TranslateEnum(SymExpr expr)
        {
            var type = (EnumType)expr.Type.Canonicalize();
            return expr switch
            {
                SymbolExpr symbol => TranslateSymbol(symbol),
                // Enum's concrete value must match a declared value.
                ConcreteExpr { Value: PInt value }
                    when type.EnumDecl.Values.Any(e => e.Value == (long)value) =>
                    _context.MkBV((long)value, IntWidth),

                _ => throw new ArgumentException(
                    $"Expected a value of enum {type}.", nameof(expr))
            };
        }

        private Expr TranslateTuple(SymExpr expr)
        {
            if (expr is SymbolExpr symbol)
            {
                return TranslateSymbol(symbol);
            }

            var type = expr.Type.Canonicalize();
            var fieldTypes = GetFieldTypes(type);
            SymExpr[] fields;
            switch (expr)
            {
                case TupleExpr tuple:
                    fields = tuple.Fields.ToArray();
                    break;

                case NamedTupleExpr named
                when named.Fields.Count == fieldTypes.Count:
                    fields = ((NamedTupleType)type).Fields.Select(
                        f => named.Fields[f.Name]).ToArray();
                    break;

                case ConcreteExpr { Value: PTuple tuple }
                when type is TupleType &&
                    tuple.fieldValues.Count == fieldTypes.Count:
                    fields = tuple.fieldValues.Select(
                        (v, i) => (SymExpr)new ConcreteExpr(v, fieldTypes[i])).ToArray();
                    break;

                case ConcreteExpr { Value: PNamedTuple named }
                when type is NamedTupleType namedType &&
                    named.fieldValues.Count == fieldTypes.Count &&
                    named.fieldNames.SequenceEqual(namedType.Names):
                    fields = namedType.Fields.Select(
                        f => (SymExpr)new ConcreteExpr(named[f.Name], f.Type)).ToArray();
                    break;

                default:
                    throw new ArgumentException(
                        "Expected a tuple value matching its type.", nameof(expr));
            }

            if (fields.Length != fieldTypes.Count ||
                fields.Where((f, i) => !f.Type.Canonicalize().IsSameTypeAs(
                    fieldTypes[i].Canonicalize())).Any())
            {
                throw new ArgumentException(
                    "Tuple fields must match the declared tuple type.", nameof(expr));
            }

            var translatedFields = fields.Select(TranslateExpr).ToArray();
            return ((TupleSort)TranslateSort(type)).MkDecl.Apply(translatedFields);
        }

        private static IReadOnlyList<PLanguageType> GetFieldTypes(PLanguageType type)
        {
            return type switch
            {
                TupleType tuple => tuple.Types,
                NamedTupleType named => named.Types,
                _ => throw new ArgumentException("Expected a tuple type.", nameof(type))
            };
        }

        private BoolExpr TranslateEquality(
            PLanguageType leftType,
            PLanguageType rightType,
            Expr left,
            Expr right)
        {
            // Equality allows different left/right types as long as they are
            // assignable
            if (!leftType.IsAssignableFrom(rightType) &&
                !rightType.IsAssignableFrom(leftType))
            {
                throw new ArgumentException(
                    "Equality expressions must have compatible types.",
                    nameof(leftType));
            }

            if (leftType is TupleType or NamedTupleType)
            {
                var leftFields = GetFieldTypes(leftType);
                var rightFields = GetFieldTypes(rightType);
                var leftAccessors = ((TupleSort)TranslateSort(leftType)).FieldDecls;
                var rightAccessors = ((TupleSort)TranslateSort(rightType)).FieldDecls;

                // Recursively add equality constraints for each tuple field pairing
                return _context.MkAnd(
                    leftFields.Select((t, i) => TranslateEquality(
                        t.Canonicalize(), 
                        rightFields[i].Canonicalize(),
                        leftAccessors[i].Apply(left), 
                        rightAccessors[i].Apply(right))
                    ).ToArray());
            }

            if (leftType.IsSameTypeAs(PrimitiveType.Float))
                return _context.MkFPEq((FPExpr)left, (FPExpr)right);

            // All other supported types can use default equality.
            // Because left and right are already translated, we know leftType 
            // and rightType are supported
            return _context.MkEq(left, right);
        }

        private Expr TranslateSymbol(SymbolExpr symbol)
        {
            if (_symbols.TryGetValue(symbol.ID, out var cached))
            {
                if (!cached.Type.Canonicalize().IsSameTypeAs(symbol.Type.Canonicalize()))
                {
                    throw new ArgumentException(
                        $"Symbol {symbol.ID.Value} appears with inconsistent types.");
                }

                return cached.Expression;
            }

            var expression = _context.MkConst(
                $"s_{symbol.ID.Value}",
                TranslateSort(symbol.Type));

            AddDomainConstraints(symbol.Type, expression);

            _symbols.Add(symbol.ID, (symbol.Type, expression));
            return expression;
        }

        private Sort TranslateSort(PLanguageType type)
        {
            type = type.Canonicalize();
            RejectUnsupportedTypes(type);
            if (_sorts.TryGetValue(type, out var sort))
            {
                return sort;
            }

            if (type.IsSameTypeAs(PrimitiveType.Bool)) 
                sort = _context.BoolSort;
            else if (type.IsSameTypeAs(PrimitiveType.Int) || type is EnumType) 
                sort = _context.MkBitVecSort(IntWidth);
            else if (type.IsSameTypeAs(PrimitiveType.Float)) 
                sort = _context.MkFPSort64();
            else if (type.IsSameTypeAs(PrimitiveType.String)) 
                sort = _context.StringSort;
            else if (IsNullableIdentityType(type)) 
                sort = _context.IntSort;
            else if (type is TupleType or NamedTupleType)
            {
                var fieldSorts = GetFieldTypes(type).Select(TranslateSort).ToArray();
                var name = $"p_tuple_{_sorts.Count}";
                sort = _context.MkTupleSort(
                    _context.MkSymbol(name),
                    fieldSorts.Select(
                        (_, i) => _context.MkSymbol($"{name}_field_{i}")).ToArray(), 
                    fieldSorts);
            }
            else throw new NotSupportedException($"Unsupported P type: {type}");

            _sorts.Add(type, sort);
            return sort;
        }

        private void AddDomainConstraints(PLanguageType type, Expr expression)
        {
            type = type.Canonicalize();
            if (type is EnumType enumType)
            {
                // Restrict enums to valid values according to P declaration
                _domainConstraints.Add(_context.MkOr(
                    enumType.EnumDecl.Values.Select(
                        e => _context.MkEq(
                            expression, 
                            _context.MkBV(e.Value, IntWidth))
                        ).ToArray()));
            }
            else if (type is TupleType or NamedTupleType)
            {
                var fields = ((TupleSort)TranslateSort(type)).FieldDecls;
                var types = GetFieldTypes(type);
                for (var i = 0; i < fields.Length; i++)
                {
                    AddDomainConstraints(types[i], fields[i].Apply(expression));
                }
            }
            else if (type.IsSameTypeAs(PrimitiveType.Null))
            {
                _domainConstraints.Add(_context.MkEq(expression, _context.MkInt(0)));
            }
            else if (IsNullableIdentityType(type))
            {
                _domainConstraints.Add(_context.MkGe((IntExpr)expression, _context.MkInt(0)));
            }
        }

        private static bool IsMachineType(PLanguageType type) =>
            type is PermissionType || type.IsSameTypeAs(PrimitiveType.Machine);

        private static bool IsNullableIdentityType(PLanguageType type) =>
            IsMachineType(type) ||
            type.IsSameTypeAs(PrimitiveType.Null);

        private static void RejectUnsupportedTypes(PLanguageType type)
        {
            type = type.Canonicalize();
            if (type is DataType or ForeignType ||
                type.IsSameTypeAs(PrimitiveType.Any) ||
                type.IsSameTypeAs(PrimitiveType.Event))
                throw new NotSupportedException($"P type '{type.CanonicalRepresentation}' is not supported by the Z3 translator.");

            if (type is TupleType or NamedTupleType)
            {
                foreach (var fieldType in GetFieldTypes(type)) RejectUnsupportedTypes(fieldType);
            }
        }

        private Expr TranslateIdentity(SymExpr expr)
        {
            var type = expr.Type.Canonicalize();
            if (!IsNullableIdentityType(type))
                throw new NotSupportedException($"Unsupported P type: {type}");

            // Machines, interfaces, and null are represented by
            // integer labels. Zero represents null. Non-null identities are
            // positive and preserve runtime equality.
            if (expr is SymbolExpr symbol) return TranslateSymbol(symbol);

            // Identity types are not compound expressions, so expr must either be 
            // a symbol or concrete
            if (expr is not ConcreteExpr concrete)
                throw new NotSupportedException(
                    $"Unsupported identity expression: {expr.GetType().Name}");

            var value = concrete.Value;
            if (value == null) return _context.MkInt(0);

            if (!IsMachineType(type) || value is not PMachineValue)
                throw new ArgumentException($"Expected a concrete value of type {type}.", nameof(expr));

            var id = _identities.FindIndex(v => v.Equals(value));
            if (id < 0)
            {
                id = _identities.Count;
                _identities.Add(value);
            }
            return _context.MkInt(id + 1);
        }
    }
}
