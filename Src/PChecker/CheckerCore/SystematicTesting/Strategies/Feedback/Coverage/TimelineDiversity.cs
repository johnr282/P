using System;
using System.Collections.Generic;

namespace PChecker.Feedback;

/// <summary>
/// Tracks timeline diversity for one fuzzing campaign.
/// </summary>
internal sealed class TimelineDiversity
{
    private const int NovelTimelineBonus = 20;
    private readonly HashSet<int> _visitedTimelines = new();
    private readonly List<IReadOnlyList<int>> _admittedMinHashes = new();

    /// <summary>
    /// Marks the timeline as seen and compares it with admitted timelines. A duplicate scores zero.
    /// </summary>
    internal (int Score, IReadOnlyList<int> MinHash) Measure(TimelineObserver timeline)
    {
        var isNew = _visitedTimelines.Add(timeline.GetTimelineHash());
        var minHash = timeline.GetTimelineMinhash();
        if (!isNew)
        {
            return (0, minHash);
        }

        var minDistance = 0;
        if (_admittedMinHashes.Count > 0)
        {
            minDistance = minHash.Count;
            foreach (var admitted in _admittedMinHashes)
            {
                var distance = 0;
                for (var i = 0; i < minHash.Count; i++)
                {
                    if (minHash[i] != admitted[i])
                    {
                        distance++;
                    }
                }

                minDistance = Math.Min(minDistance, distance);
            }
        }

        return (NovelTimelineBonus + minDistance, minHash);
    }

    /// <summary>
    /// Includes the measured timeline in future comparisons after the policy admits its seed.
    /// </summary>
    internal void RecordAdmission(IReadOnlyList<int> minHash) => _admittedMinHashes.Add(minHash);
}
