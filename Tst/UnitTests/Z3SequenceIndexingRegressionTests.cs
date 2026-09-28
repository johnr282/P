using Microsoft.Z3;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class Z3SequenceIndexingRegressionTests
    {
        [Test]
        public void NthOfConcatenatedConditionalSingletonsSelectsSecondElement()
        {
            using var context = new Context();
            using var solver = context.MkSolver();
            var entry = context.MkTupleSort(
                context.MkSymbol("Entry"),
                new[] { context.MkSymbol("value") }, 
                new Sort[] { context.IntSort });
            Expr Entry(int n) => entry.MkDecl.Apply(context.MkInt(n));
            var condition = context.MkBoolConst("condition");
            var first = context.MkITE(condition, Entry(10), Entry(20));

            // Both branches produce a two-element sequence ending in Entry(30).
            var sequence = context.MkConcat(context.MkUnit(first), context.MkUnit(Entry(30)));
            // The second element should be Entry(30)
            var actual = entry.FieldDecls[0].Apply(context.MkNth(sequence, context.MkInt(1)));
            // A counterexample cannot exist; this fails on both Z3 4.12.2 and 5.1.0.
            var wrongElement = context.MkNot(context.MkEq(actual, context.MkInt(30)));
            solver.Assert(wrongElement);
            var status = solver.Check();
            TestContext.WriteLine($"Z3: {Microsoft.Z3.Version.FullVersion}");
            TestContext.WriteLine($"Expected UNSATISFIABLE; actual {status}");
            TestContext.WriteLine($"Simplified formula: {wrongElement.Simplify()}");
            if (status == Status.SATISFIABLE)
            {
                TestContext.WriteLine(solver.Model);
                TestContext.WriteLine($"Formula in returned model: {solver.Model.Evaluate(wrongElement, true)}");
            }
            Assert.That(status, Is.EqualTo(Status.UNSATISFIABLE));
        }
    }
}
