using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    /// <summary>
    /// Interface representing a frontier data structure used in a search algorithm.
    /// </summary>
    internal interface IFrontier<T>
    {
        /// <summary>
        /// Adds an item to the frontier.
        /// </summary>
        void Add(T item);

        /// <summary>
        /// Removes the next item from the frontier.
        /// </summary>
        /// <param name="item">The removed next item.</param>
        /// <returns>True if an item was removed, false otherwise.</returns>
        bool TryRemoveNext(out T item);

        /// <summary>
        /// Removes all items from the frontier.
        /// </summary>
        void Clear();
    }

    internal class QueueFrontier<T> : IFrontier<T>
    {
        private readonly Queue<T> queue = new();

        /// <inheritdoc/>
        public void Add(T item)
        {
            queue.Enqueue(item);
        }

        /// <inheritdoc/>
        public bool TryRemoveNext(out T item)
        {
            return queue.TryDequeue(out item);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            queue.Clear();
        }
    }

    internal class StackFrontier<T> : IFrontier<T>
    {
        private readonly Stack<T> stack = new();

        /// <inheritdoc/>
        public void Add(T item)
        {
            stack.Push(item);
        }

        /// <inheritdoc/>
        public bool TryRemoveNext(out T item)
        {
            return stack.TryPop(out item);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            stack.Clear();
        }
    }
}
