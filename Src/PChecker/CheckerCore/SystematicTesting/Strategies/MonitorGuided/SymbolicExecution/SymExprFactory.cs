using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Collections.Generic;
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
    }
}
