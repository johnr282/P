using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plang.Compiler.TypeChecker.AST.States;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal sealed class SymState
    {
        public State CurrentState { get; }
        public Stack<StackFrame> CallStack { get; }
        public Dictionary<string, SymValue> Globals { get; }
        public PathCondition PathCondition { get; }
        public uint ObservedEvents { get; set; }

        /// <summary>
        /// Whether a monitor in this state is handling an event.
        /// </summary>
        public bool IsHandlingEvent => CallStack.TryPeek(out var _);

        public SymState(
            State currentState,
            Stack<StackFrame> callStack,
            Dictionary<string, SymValue> globals,
            PathCondition pathCondition,
            uint observedEvents)
        {
            CurrentState = currentState;
            CallStack = callStack;
            Globals = globals;
            PathCondition = pathCondition;
            ObservedEvents = observedEvents;
        }

        /// <summary>
        /// Returns a deep copy of the given symbolic state.
        /// </summary>
        public static SymState Clone(SymState state)
        {
            return new SymState(
                state.CurrentState,
                new Stack<StackFrame>(state.CallStack.Reverse()),
                new Dictionary<string, SymValue>(state.Globals),
                state.PathCondition,
                state.ObservedEvents);
        }
    }

    internal sealed class StackFrame
    {
        public Dictionary<string, SymValue> Locals { get; }
        public string FunctionName { get; }
        public int ProgramCounter { get; }
    }
}
