// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using PChecker.Runtime.StateMachines;

namespace PChecker.SystematicTesting.Operations
{
    /// <summary>
    /// Contains information about an asynchronous state machine operation
    /// that can be controlled during testing.
    /// </summary>
    [DebuggerStepThrough]
    internal sealed class StateMachineOperation : AsyncOperation
    {
        /// <summary>
        /// The state machine that executes this operation.
        /// </summary>
        internal readonly StateMachine StateMachine;

        /// <summary>
        /// Unique id of the operation.
        /// </summary>
        public override ulong Id => StateMachine.Id.Value;

        /// <summary>
        /// Unique name of the operation.
        /// </summary>
        public override string Name => StateMachine.Id.Name;

        /// <summary>
        /// True if it should skip the next receive scheduling point,
        /// because it was already called in the end of the previous
        /// event handler.
        /// </summary>
        internal bool SkipNextReceiveSchedulingPoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="StateMachineOperation"/> class.
        /// </summary>
        internal StateMachineOperation(StateMachine stateMachine)
            : base()
        {
            StateMachine = stateMachine;
            SkipNextReceiveSchedulingPoint = false;
        }

        /// <summary>
        /// Invoked when the operation completes.
        /// </summary>
        internal override void OnCompleted()
        {
            SkipNextReceiveSchedulingPoint = true;
            base.OnCompleted();
        }
    }
}