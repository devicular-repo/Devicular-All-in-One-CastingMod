using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;

namespace AutoRef
{
    internal static class Stats
    {
        internal sealed class TeamLine
        {
            public string Name;
            public int RoundsRunning, RoundsTagging, Tags;
            public double TotalSurvival, TotalDuration, BestDuration;
            public int RunnerEntries;
            public double AvgSurvival => RunnerEntries > 0 ? TotalSurvival / RunnerEntries : 0;
        }

        internal sealed class PlayerLine
        {
            public string Name;
            public int RoundsRunning, Wins, Survived, Tags;
            public double TotalSurvival, Best;
            public double AvgSurvival => RoundsRunning > 0 ? TotalSurvival / RoundsRunning : 0;
        }

        public static List<TeamLine> Teams(IEnumerable<RoundResult> history)
        {
            var map = new Dictionary<string, TeamLine>();
            TeamLine Get(string n) => map.TryGetValue(n, out var t) ? t : map[n] = new TeamLine { Name = n };
            foreach (var r in history)
            {
                var run = Get(r.RunnerTeamName);
                run.RoundsRunning++;
                run.TotalDuration += r.Duration;
                run.BestDuration = Math.Max(run.BestDuration, r.Duration);
                foreach (var e in r.Ranked)
                {
                    run.TotalSurvival += e.OutTime;
                    run.RunnerEntries++;
                }
                var tag = Get(r.TaggerTeamName);
                tag.RoundsTagging++;
                tag.Tags += r.Taggers.Sum(t => t.Tags);
            }
            return map.Values.OrderBy(t => t.Name).ToList();
        }

        public static List<PlayerLine> Players(IEnumerable<RoundResult> history)
        {
            var map = new Dictionary<string, PlayerLine>();
            PlayerLine Get(string key, string name)
            {
                if (!map.TryGetValue(key, out var p)) map[key] = p = new PlayerLine { Name = name };
                p.Name = name;
                return p;
            }
            foreach (var r in history)
            {
                foreach (var e in r.Ranked)
                {
                    var p = Get(e.Key, e.Name);
                    p.RoundsRunning++;
                    p.TotalSurvival += e.OutTime;
                    p.Best = Math.Max(p.Best, e.OutTime);
                    if (e.Status == RunnerStatus.Survived) p.Survived++;
                    if (r.Winners.Contains(e)) p.Wins++;
                }
                foreach (var t in r.Taggers) Get(t.Key, t.Name).Tags += t.Tags;
            }
            return map.Values.OrderByDescending(p => p.AvgSurvival).ThenBy(p => p.Name).ToList();
        }
    }
}
