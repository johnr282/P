using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Z3;
using Plang.Compiler.TypeChecker.Types;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver
{
    internal sealed class Z3Solver : ISolver
    {
        private readonly uint _timeoutMilliseconds;

        internal Z3Solver(uint timeoutMilliseconds = 1000)
        {
            _timeoutMilliseconds = timeoutMilliseconds;
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

            using var context = new Context();
            using var solver = context.MkSolver();

            solver.Set("timeout", _timeoutMilliseconds);

            var translator = new Z3Translator(context);
            BoolExpr translated = translator.Translate(formula);

            solver.Assert(translated);
            return solver.Check() switch
            {
                Status.SATISFIABLE => SatResult.Sat,
                Status.UNSATISFIABLE => SatResult.Unsat,
                _ => SatResult.Unknown
            };
        }
    }
}
