using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.Configuration
{
    /// <summary>
    /// Scheduling strategy types.
    /// </summary>
    public enum StrategyType
    {
        Random, 
        POS,
        PCT,
        FairPCT,
        Feedback,
        FeedbackPOS,
        FeedbackPCT,
        FeedbackPCTCP,
        MonitorGuided,
        Probabilistic,
        RL,
        DFS,
        AStar,
        Replay
    }
}
