using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.Runtime.StateMachines
{
    /// <summary>
    /// Used to identify machines across different runs for behavior recording.
    /// </summary>
    public class MachineCreationPath
    {
        /// <summary>
        /// Let n = CreationPath.Last(), 
        /// creatorPath = CreationPath[..(CreationPath.Count - 1)]. 
        /// This machine is the nth machine created by the machine identified
        /// by creatorPath. 
        /// </summary>
        public List<uint> CreationPath { get; }

        public MachineCreationPath(List<uint> creationPath)
        {
            ArgumentNullException.ThrowIfNull(creationPath);
            CreationPath = new(creationPath);
        }

        /// <summary>
        /// Returns the next path for a creation by this machine.
        /// </summary>
        public MachineCreationPath NewPath()
        {
            uint count = CreationCounter;
            CreationCounter++;
            var path = new List<uint>(CreationPath);
            path.Add(count);
            return new MachineCreationPath(path);
        }

        public override bool Equals(object obj)
        {
            if (obj is not MachineCreationPath path) return false;
            return CreationPath.SequenceEqual(path.CreationPath);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            CreationPath.ForEach(x => hash.Add(x));
            return hash.ToHashCode();
        }
    }

    public class MachineCreationPathFactory
    {
        private uint _rootCreationCounter = 0;

        private readonly Dictionary<MachineCreationPath, uint> _creationCounters = new();

        public MachineCreationPath NewRootPath()
        {
            uint rootCount = _rootCreationCounter;
            _rootCreationCounter++;
            return new MachineCreationPath(new List<uint> { rootCount });
        }

        public MachineCreationPath NewPath(MachineCreationPath creatorPath)
        {
            if (!_creationCounters.TryGetValue(creatorPath, out uint counter))
            {
                counter = 0;
                _creationCounters[creatorPath] = counter;
            }

            var path = new List<uint>(creatorPath.CreationPath);
            path.Add(counter);
            _creationCounters[creatorPath]++;
            return new MachineCreationPath(path);
        }
    }
}
