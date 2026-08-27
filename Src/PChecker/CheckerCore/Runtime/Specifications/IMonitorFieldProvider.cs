using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PChecker.Runtime.Values;

namespace PChecker.Runtime.Specifications
{
    public interface IMonitorFieldProvider
    {
        /// <summary>
        /// Returns a dictionary containing the current values of all monitor fields.
        /// </summary>
        public IReadOnlyDictionary<string, IPValue> GetFieldValues();
    }
}
