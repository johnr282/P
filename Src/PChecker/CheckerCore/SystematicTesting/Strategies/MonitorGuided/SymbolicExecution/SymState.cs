using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Continuations;
using Plang.Compiler.TypeChecker.AST.States;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal sealed class SymState
    {
        public State CurrentState { get; }
        public Stack<StackFrame> CallStack { get; }
        public Control Control { get; set; }
        public Dictionary<string, SymExpr> Fields { get; }
        public PathCondition PathCondition { get; }
        public uint ObservedEvents { get; set; }

        /// <summary>
        /// Whether a monitor in this state is waiting for an event.
        /// </summary>
        public bool WaitingForEvent => Control is WaitingForEventControl;

        public SymState(
            State currentState,
            Stack<StackFrame> callStack,
            Control control,
            Dictionary<string, SymExpr> fields,
            PathCondition pathCondition,
            uint observedEvents)
        {
            CurrentState = currentState;
            CallStack = callStack;
            Control = control;
            Fields = fields;
            PathCondition = pathCondition;
            ObservedEvents = observedEvents;
        }

        /// <summary>
        /// Returns a deep copy of the given symbolic state.
        /// </summary>
        public static SymState Clone(SymState state)
        {
            Stack<StackFrame> clonedCallStack = new Stack<StackFrame>(
                state.CallStack.Select(StackFrame.Clone).Reverse());

            return new SymState(
                state.CurrentState,
                clonedCallStack,
                state.Control,
                new Dictionary<string, SymExpr>(state.Fields),
                state.PathCondition,
                state.ObservedEvents);
        }
    }

    internal sealed class StackFrame
    {
        public Dictionary<string, SymExpr> Locals { get; }
        public string FunctionName { get; }
        public RValueContinuation ReturnTo { get; }

        public StackFrame(
            Dictionary<string, SymExpr> locals,
            string functionName,
            RValueContinuation returnTo)
        {
            Locals = locals;
            FunctionName = functionName;
            ReturnTo = returnTo;
        }

        public static StackFrame Clone(StackFrame frame)
        {
            return new StackFrame(
                new Dictionary<string, SymExpr>(frame.Locals),
                frame.FunctionName,
                frame.ReturnTo);
        }
    }
}
