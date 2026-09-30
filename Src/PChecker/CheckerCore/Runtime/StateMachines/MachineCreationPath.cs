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
        public IReadOnlyList<uint> CreationPath { get; }

        public MachineCreationPath(IReadOnlyList<uint> creationPath)
        {
            ArgumentNullException.ThrowIfNull(creationPath);
            CreationPath = creationPath.ToList().AsReadOnly();
        }

        public override bool Equals(object obj)
        {
            if (obj is not MachineCreationPath path) return false;
            return CreationPath.SequenceEqual(path.CreationPath);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var x in CreationPath)
            {
                hash.Add(x);
            }
            return hash.ToHashCode();
        }
    }

    public class MachineCreationPathFactory
    {
        private uint _rootCreationCounter = 0;

        private readonly Dictionary<MachineCreationPath, uint> _creationCounters = new();

        /// <summary>
        /// Creates and returns a new creation path for a machine created by 
        /// creator. If creator is null, returns a new root path. 
        /// </summary>
        public MachineCreationPath NewPath(StateMachine creator)
        {
            if (creator == null) return NewRootPath();

            var creatorPath = creator.Id.CreationPath;

            if (!_creationCounters.TryGetValue(creatorPath, out uint counter))
            {
                counter = 0;
                _creationCounters[creatorPath] = counter;
            }

            var path = creatorPath.CreationPath.ToList();
            path.Add(counter);
            _creationCounters[creatorPath]++;
            return new MachineCreationPath(path);
        }

        private MachineCreationPath NewRootPath()
        {
            uint rootCount = _rootCreationCounter;
            _rootCreationCounter++;
            return new MachineCreationPath(new List<uint> { rootCount });
        }
    }
}
