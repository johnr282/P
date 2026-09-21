using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Z3;
using Plang.Compiler.TypeChecker.AST.Statements;
using Plang.Compiler.TypeChecker.Types;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver
{
    internal class Z3Translator
    {
        private readonly Context _context;
        private readonly List<BoolExpr> _domainConstraints = new();
        private readonly Dictionary<
            SymbolID, 
            (PLanguageType Type, Expr Expression)> _symbols = new();

        internal Z3Translator(Context context)
        {
            _context = context;
        }

        internal BoolExpr Translate(SymExpr expr)
        {
            BoolExpr boolExpr = TranslateBoolean(expr);

            return _context.MkAnd(_domainConstraints.Append(boolExpr).ToArray());
        }

        private BoolExpr TranslateBoolean(SymExpr expr)
        {
            return expr switch
            {
                ConcreteExpr concrete => TranslateBooleanConcrete(concrete),
                SymbolExpr symbol => (BoolExpr)TranslateSymbol(symbol),
                BinaryExpr binary => TranslateBooleanBinary(binary),
                UnaryExpr unary => TranslateBooleanUnary(unary),

                _ => throw new NotSupportedException(
                    $"Unsupported Boolean expression: {expr.GetType().Name}")
            };
        }

        private BoolExpr TranslateBooleanConcrete(ConcreteExpr concrete)
        {

        }

        private BoolExpr TranslateBooleanBinary(BinaryExpr binary)
        {

        }

        private BoolExpr TranslateBooleanUnary(UnaryExpr unary)
        {

        }

        private Expr TranslateSymbol(SymbolExpr symbol)
        {
            if (_symbols.TryGetValue(symbol.ID, out var cached))
            {
                if (!cached.Type.IsSameTypeAs(symbol.Type))
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

        }

        private void AddDomainConstraints(PLanguageType type, Expr expression)
        {
            // JR TODO
        }
    }
}
