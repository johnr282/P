using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal static class ExpressionEvaluator
    {
        internal sealed record ResolvedLValue();
        
        internal sealed record EvalResult(
            SymState State,
            SymValue Value);

        internal sealed record LValueResult(
            SymState State,
            ResolvedLValue Location);
    }
}
