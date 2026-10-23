using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Z3;
using PChecker.IO.Debugging;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.Types;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver
{
    internal sealed class Z3Solver : ISolver
    {
        private const int FormulaHistoryLimit = 10_000;

        private readonly uint _timeoutMilliseconds;

        private readonly Context _context;
        private readonly BoundedResultCache _resultCache = new(FormulaHistoryLimit);
        private readonly RecentFormulaTracker _recentExactFormulas = new(FormulaHistoryLimit);
        private readonly RecentFormulaTracker _recentAlphaEquivalentFormulas = new(FormulaHistoryLimit);
        private long _formulaCheckCount;
        private long _exactFormulaRepeatCount;
        private long _alphaEquivalentFormulaRepeatCount;
        private long _resultCacheHitCount;
        private long _solverCheckCount;
        private readonly MonitorGuidedStatistics _statistics;

        internal Z3Solver(uint timeoutMilliseconds = 1000, MonitorGuidedStatistics statistics = null)
        {
            _timeoutMilliseconds = timeoutMilliseconds;
            _statistics = statistics;
            _context = new Context();
        }

        public SatResult CheckSat(SymExpr formula)
        {
            ArgumentNullException.ThrowIfNull(formula);

            if (!formula.Type.IsSameTypeAs(PrimitiveType.Bool))
            {
                throw new ArgumentException(
                    "The formula must have Boolean type.",
                    nameof(formula));
            }

            string exactKey = CreateFormulaKey(formula, alphaNormalizeSymbols: false);
            bool cacheHit = _resultCache.TryGet(exactKey, out SatResult cachedResult);
            _statistics?.RecordSolverCheck(exactKey, cacheHit);

            if (Debug.IsEnabled)
            {
                string alphaKey = CreateFormulaKey(formula, alphaNormalizeSymbols: true);
                _formulaCheckCount++;
                if (_recentExactFormulas.ContainsOrAdd(exactKey))
                    _exactFormulaRepeatCount++;
                if (_recentAlphaEquivalentFormulas.ContainsOrAdd(alphaKey))
                    _alphaEquivalentFormulaRepeatCount++;

                Debug.WriteLine(
                    "<Z3Solver> Formula repetition over {0} CheckSat calls (last {1} unique keys): exact repeats={2}, alpha-equivalent repeats={3}.",
                    _formulaCheckCount,
                    FormulaHistoryLimit,
                    _exactFormulaRepeatCount,
                    _alphaEquivalentFormulaRepeatCount);
                Debug.WriteLine(
                    "<Z3Solver> Result cache: hits={0}, solver checks={1}.",
                    _resultCacheHitCount,
                    _solverCheckCount);
                Debug.WriteLine("<Z3Solver> Input formula ({0} SymExpr nodes):", CountSymExprNodes(formula));
                Debug.WriteLine("{0}", FormatSymExpr(formula));
            }

            if (cacheHit)
            {
                _resultCacheHitCount++;
                if (Debug.IsEnabled)
                {
                    Debug.WriteLine("<Z3Solver> Result cache hit: {0}.", cachedResult);
                    Debug.WriteLine(
                        "<Z3Solver> Result cache totals: hits={0}, solver checks={1}.",
                        _resultCacheHitCount,
                        _solverCheckCount);
                }
                return cachedResult;
            }

            var translator = new Z3Translator(_context);
            BoolExpr translated = translator.Translate(formula);

            if (Debug.IsEnabled)
            {
                Debug.WriteLine("<Z3Solver> Translated formula ({0} unique Z3 AST nodes):", CountZ3AstNodes(translated));
                Debug.WriteLine("{0}", translated);
            }

            using var solver = _context.MkSolver();
            solver.Set("timeout", _timeoutMilliseconds);
            solver.Assert(translated);
            _solverCheckCount++;
            SatResult result = solver.Check() switch
            {
                Status.SATISFIABLE => SatResult.Sat,
                Status.UNSATISFIABLE => SatResult.Unsat,
                _ => SatResult.Unknown
            };

            if (result != SatResult.Unknown)
                _resultCache.Add(exactKey, result);

            return result;
        }

        private static int CountSymExprNodes(SymExpr expr) =>
            1 + GetChildren(expr).Sum(CountSymExprNodes);

        private static string FormatSymExpr(SymExpr expr) => expr switch
        {
            ConcreteExpr concrete => FormatConcreteValue(concrete.Value),
            SymbolExpr symbol => $"s_{symbol.ID.Value}",
            BinaryExpr binary =>
                $"({FormatSymExpr(binary.Left)} {FormatBinaryOperator(binary.Op)} {FormatSymExpr(binary.Right)})",
            UnaryExpr { Op: UnaryOpType.Not } unary => $"(!{FormatSymExpr(unary.SubExpr)})",
            UnaryExpr unary => $"(-{FormatSymExpr(unary.SubExpr)})",
            TupleExpr tuple =>
                $"({string.Join(", ", tuple.Fields.Select(FormatSymExpr))})",
            NamedTupleExpr tuple =>
                $"{{{string.Join(", ", tuple.Fields.Select(field => $"{field.Key}: {FormatSymExpr(field.Value)}"))}}}",
            SequenceExpr sequence =>
                $"[{string.Join(", ", sequence.Elements.Select(FormatSymExpr))}]",
            SetExpr set =>
                $"{{{string.Join(", ", set.Elements.Select(FormatSymExpr))}}}",
            MapExpr map =>
                $"{{{string.Join(", ", map.Entries.Select(entry => $"{FormatSymExpr(entry.Key)} -> {FormatSymExpr(entry.Value)}"))}}}",
            CollectionAccessExpr access =>
                $"{FormatSymExpr(access.Collection)}[{FormatSymExpr(access.Index)}]",
            CollectionSizeExpr size => $"|{FormatSymExpr(size.Collection)}|",
            CollectionContainsExpr contains =>
                $"({FormatSymExpr(contains.Item)} in {FormatSymExpr(contains.Collection)})",
            MapKeysExpr keys => $"keys({FormatSymExpr(keys.Map)})",
            MapValuesExpr values => $"values({FormatSymExpr(values.Map)})",
            CollectionUpdateExpr update =>
                $"update({FormatSymExpr(update.Collection)}, {FormatSymExpr(update.Index)}, {FormatSymExpr(update.Value)})",
            CollectionInsertExpr insert =>
                $"insert({FormatSymExpr(insert.Collection)}, {FormatSymExpr(insert.Index)}, {FormatSymExpr(insert.Value)})",
            SetAddExpr add => $"add({FormatSymExpr(add.Set)}, {FormatSymExpr(add.Value)})",
            CollectionRemoveExpr remove =>
                $"remove({FormatSymExpr(remove.Collection)}, {FormatSymExpr(remove.Item)})",
            _ => expr.ToString()
        };

        private static string FormatConcreteValue(IPValue value) => value switch
        {
            null => "null",
            PNamedTuple namedTuple =>
                $"{{{string.Join(", ", namedTuple.fieldNames.Select((name, index) => $"{name}: {FormatConcreteValue(namedTuple.fieldValues[index])}"))}}}",
            PTuple tuple =>
                $"({string.Join(", ", tuple.fieldValues.Select(FormatConcreteValue))})",
            _ => value.ToString()
        };

        private static string CreateFormulaKey(SymExpr expr, bool alphaNormalizeSymbols)
        {
            var builder = new StringBuilder();
            var symbols = new Dictionary<SymbolID, int>();
            AppendFormulaKey(builder, expr, symbols, alphaNormalizeSymbols);
            return builder.ToString();
        }

        private static void AppendFormulaKey(
            StringBuilder builder,
            SymExpr expr,
            Dictionary<SymbolID, int> symbols,
            bool alphaNormalizeSymbols)
        {
            AppendKeyToken(builder, expr.GetType().Name);
            AppendKeyToken(builder, expr.Type.Canonicalize().CanonicalRepresentation);

            switch (expr)
            {
                case ConcreteExpr concrete:
                    AppendConcreteValueKey(builder, concrete.Value);
                    break;
                case SymbolExpr symbol:
                    if (alphaNormalizeSymbols)
                    {
                        if (!symbols.TryGetValue(symbol.ID, out int normalizedId))
                        {
                            normalizedId = symbols.Count;
                            symbols.Add(symbol.ID, normalizedId);
                        }
                        AppendKeyToken(builder, normalizedId.ToString());
                    }
                    else
                    {
                        AppendKeyToken(builder, symbol.ID.Value.ToString());
                    }
                    break;
                case BinaryExpr binary:
                    AppendKeyToken(builder, binary.Op.ToString());
                    AppendFormulaKey(builder, binary.Left, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, binary.Right, symbols, alphaNormalizeSymbols);
                    break;
                case UnaryExpr unary:
                    AppendKeyToken(builder, unary.Op.ToString());
                    AppendFormulaKey(builder, unary.SubExpr, symbols, alphaNormalizeSymbols);
                    break;
                case TupleExpr tuple:
                    AppendKeyToken(builder, tuple.Fields.Length.ToString());
                    foreach (SymExpr field in tuple.Fields)
                        AppendFormulaKey(builder, field, symbols, alphaNormalizeSymbols);
                    break;
                case NamedTupleExpr tuple:
                    AppendKeyToken(builder, tuple.Fields.Count.ToString());
                    foreach (var field in tuple.Fields.OrderBy(field => field.Key, StringComparer.Ordinal))
                    {
                        AppendKeyToken(builder, field.Key);
                        AppendFormulaKey(builder, field.Value, symbols, alphaNormalizeSymbols);
                    }
                    break;
                case SequenceExpr sequence:
                    AppendFormulaChildren(builder, sequence.Elements, symbols, alphaNormalizeSymbols);
                    break;
                case SetExpr set:
                    AppendFormulaChildren(builder, set.Elements, symbols, alphaNormalizeSymbols);
                    break;
                case MapExpr map:
                    AppendKeyToken(builder, map.Entries.Length.ToString());
                    foreach (MapEntry entry in map.Entries)
                    {
                        AppendFormulaKey(builder, entry.Key, symbols, alphaNormalizeSymbols);
                        AppendFormulaKey(builder, entry.Value, symbols, alphaNormalizeSymbols);
                    }
                    break;
                case CollectionAccessExpr access:
                    AppendFormulaKey(builder, access.Collection, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, access.Index, symbols, alphaNormalizeSymbols);
                    break;
                case CollectionSizeExpr size:
                    AppendFormulaKey(builder, size.Collection, symbols, alphaNormalizeSymbols);
                    break;
                case CollectionContainsExpr contains:
                    AppendFormulaKey(builder, contains.Collection, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, contains.Item, symbols, alphaNormalizeSymbols);
                    break;
                case MapKeysExpr keys:
                    AppendFormulaKey(builder, keys.Map, symbols, alphaNormalizeSymbols);
                    break;
                case MapValuesExpr values:
                    AppendFormulaKey(builder, values.Map, symbols, alphaNormalizeSymbols);
                    break;
                case CollectionUpdateExpr update:
                    AppendFormulaKey(builder, update.Collection, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, update.Index, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, update.Value, symbols, alphaNormalizeSymbols);
                    break;
                case CollectionInsertExpr insert:
                    AppendFormulaKey(builder, insert.Collection, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, insert.Index, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, insert.Value, symbols, alphaNormalizeSymbols);
                    break;
                case SetAddExpr add:
                    AppendFormulaKey(builder, add.Set, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, add.Value, symbols, alphaNormalizeSymbols);
                    break;
                case CollectionRemoveExpr remove:
                    AppendFormulaKey(builder, remove.Collection, symbols, alphaNormalizeSymbols);
                    AppendFormulaKey(builder, remove.Item, symbols, alphaNormalizeSymbols);
                    break;
                default:
                    AppendKeyToken(builder, expr.ToString());
                    break;
            }
        }

        private static void AppendFormulaChildren(
            StringBuilder builder,
            IEnumerable<SymExpr> children,
            Dictionary<SymbolID, int> symbols,
            bool alphaNormalizeSymbols)
        {
            SymExpr[] childArray = children.ToArray();
            AppendKeyToken(builder, childArray.Length.ToString());
            foreach (SymExpr child in childArray)
                AppendFormulaKey(builder, child, symbols, alphaNormalizeSymbols);
        }

        private static void AppendKeyToken(StringBuilder builder, string token) =>
            builder.Append(token.Length).Append(':').Append(token);

        private static void AppendConcreteValueKey(StringBuilder builder, IPValue value)
        {
            switch (value)
            {
                case null:
                    AppendKeyToken(builder, "null");
                    break;
                case PBool boolean:
                    AppendKeyToken(builder, "bool");
                    AppendKeyToken(builder, boolean.ToString());
                    break;
                case PInt integer:
                    AppendKeyToken(builder, "int");
                    AppendKeyToken(builder, ((long)integer).ToString(CultureInfo.InvariantCulture));
                    break;
                case PFloat floatingPoint:
                    AppendKeyToken(builder, "float");
                    AppendKeyToken(
                        builder,
                        BitConverter.DoubleToInt64Bits((double)floatingPoint)
                            .ToString(CultureInfo.InvariantCulture));
                    break;
                case PString text:
                    AppendKeyToken(builder, "string");
                    AppendKeyToken(builder, text.ToString());
                    break;
                case PMachineValue machine:
                    AppendKeyToken(builder, "machine");
                    AppendKeyToken(builder, machine.Id.IsNameUsedForHashing ? "name" : "id");
                    AppendKeyToken(
                        builder,
                        machine.Id.IsNameUsedForHashing
                            ? machine.Id.NameValue
                            : machine.Id.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case PNamedTuple namedTuple:
                    AppendKeyToken(builder, "named-tuple");
                    AppendKeyToken(builder, namedTuple.fieldNames.Count.ToString(CultureInfo.InvariantCulture));
                    for (int i = 0; i < namedTuple.fieldNames.Count; i++)
                    {
                        AppendKeyToken(builder, namedTuple.fieldNames[i]);
                        AppendConcreteValueKey(builder, namedTuple.fieldValues[i]);
                    }
                    break;
                case PTuple tuple:
                    AppendKeyToken(builder, "tuple");
                    AppendKeyToken(builder, tuple.fieldValues.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (IPValue field in tuple.fieldValues)
                        AppendConcreteValueKey(builder, field);
                    break;
                case PSeq sequence:
                    AppendKeyToken(builder, "sequence");
                    AppendKeyToken(builder, sequence.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (IPValue item in sequence)
                        AppendConcreteValueKey(builder, item);
                    break;
                case PSet set:
                    AppendKeyToken(builder, "set");
                    AppendSortedConcreteValues(builder, set);
                    break;
                case PMap map:
                    AppendKeyToken(builder, "map");
                    var entries = map.Select(entry =>
                    {
                        var entryKey = new StringBuilder();
                        AppendConcreteValueKey(entryKey, entry.Key);
                        AppendConcreteValueKey(entryKey, entry.Value);
                        return entryKey.ToString();
                    }).OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
                    AppendKeyToken(builder, entries.Length.ToString(CultureInfo.InvariantCulture));
                    foreach (string entry in entries)
                        AppendKeyToken(builder, entry);
                    break;
                default:
                    // Z3 translation currently accepts only the concrete value types above.
                    // Preserve a type-tagged key for any future value type.
                    AppendKeyToken(builder, value.GetType().FullName ?? value.GetType().Name);
                    AppendKeyToken(builder, value.ToString());
                    break;
            }
        }

        private static void AppendSortedConcreteValues(StringBuilder builder, IEnumerable<IPValue> values)
        {
            string[] items = values.Select(value =>
            {
                var itemKey = new StringBuilder();
                AppendConcreteValueKey(itemKey, value);
                return itemKey.ToString();
            }).OrderBy(item => item, StringComparer.Ordinal).ToArray();

            AppendKeyToken(builder, items.Length.ToString(CultureInfo.InvariantCulture));
            foreach (string item in items)
                AppendKeyToken(builder, item);
        }

        private static string FormatBinaryOperator(BinOpType op) => op switch
        {
            BinOpType.Add => "+",
            BinOpType.Sub => "-",
            BinOpType.Mul => "*",
            BinOpType.Div => "/",
            BinOpType.Mod => "%",
            BinOpType.Eq => "==",
            BinOpType.Neq => "!=",
            BinOpType.Lt => "<",
            BinOpType.Le => "<=",
            BinOpType.Gt => ">",
            BinOpType.Ge => ">=",
            BinOpType.And => "&&",
            BinOpType.Or => "||",
            BinOpType.Then => "=>",
            BinOpType.Iff => "<=>",
            _ => op.ToString()
        };

        private static IEnumerable<SymExpr> GetChildren(SymExpr expr) => expr switch
        {
            BinaryExpr binary => new[] { binary.Left, binary.Right },
            UnaryExpr unary => new[] { unary.SubExpr },
            TupleExpr tuple => tuple.Fields,
            NamedTupleExpr tuple => tuple.Fields.Values,
            SequenceExpr sequence => sequence.Elements,
            SetExpr set => set.Elements,
            MapExpr map => map.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
            CollectionAccessExpr access => new[] { access.Collection, access.Index },
            CollectionSizeExpr size => new[] { size.Collection },
            CollectionContainsExpr contains => new[] { contains.Collection, contains.Item },
            MapKeysExpr keys => new[] { keys.Map },
            MapValuesExpr values => new[] { values.Map },
            CollectionUpdateExpr update => new[] { update.Collection, update.Index, update.Value },
            CollectionInsertExpr insert => new[] { insert.Collection, insert.Index, insert.Value },
            SetAddExpr add => new[] { add.Set, add.Value },
            CollectionRemoveExpr remove => new[] { remove.Collection, remove.Item },
            _ => Array.Empty<SymExpr>()
        };

        private static int CountZ3AstNodes(Expr root)
        {
            var visited = new HashSet<AST>();
            var pending = new Stack<Expr>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                Expr current = pending.Pop();
                if (!visited.Add(current))
                    continue;

                foreach (Expr child in current.Args)
                    pending.Push(child);
            }

            return visited.Count;
        }

        private sealed class RecentFormulaTracker
        {
            private readonly int _capacity;
            private readonly Queue<string> _keys = new();
            private readonly HashSet<string> _keySet = new(StringComparer.Ordinal);

            internal RecentFormulaTracker(int capacity)
            {
                _capacity = capacity;
            }

            internal bool ContainsOrAdd(string key)
            {
                if (!_keySet.Add(key))
                    return true;

                _keys.Enqueue(key);
                if (_keys.Count > _capacity)
                    _keySet.Remove(_keys.Dequeue());

                return false;
            }
        }

        private sealed class BoundedResultCache
        {
            private readonly int _capacity;
            private readonly Queue<string> _keys = new();
            private readonly Dictionary<string, SatResult> _results = new(StringComparer.Ordinal);

            internal BoundedResultCache(int capacity)
            {
                _capacity = capacity;
            }

            internal bool TryGet(string key, out SatResult result) =>
                _results.TryGetValue(key, out result);

            internal void Add(string key, SatResult result)
            {
                if (_results.ContainsKey(key))
                    return;

                _results.Add(key, result);
                _keys.Enqueue(key);
                if (_keys.Count > _capacity)
                    _results.Remove(_keys.Dequeue());
            }
        }
    }
}
