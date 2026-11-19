using System;
using System.Collections.Generic;

namespace AutoRef
{
    /// <summary>
    /// The referee's score bar, and what it shares with the other Devicular mods through <see cref="Link"/> when
    /// they're installed: the score (Devicular Caster's score bar shows it), the teams (the caster's player list and
    /// recordings use them) and whether a round is running (Devicular Replay records each round).
    /// </summary>
    internal static class ScoreFeed
    {
        public static void Publish()
        {
            Link.Put("Referee.Score", (Func<object[]>)(() => Read(out var view) ? view.Pack() : null));
            Link.Put("Referee.Teams", (Func<object[]>)PackTeams);
            Link.Put("Referee.Roles", (Func<object[]>)PackRoles);
        }

        /// <summary>
        /// While a round runs: the actor numbers of the taggers and of the runners still in (HashSet&lt;int&gt; each),
        /// for the caster's auto director. Null between rounds or with the referee off.
        /// </summary>
        private static object[] PackRoles()
        {
            var rc = RefereeController.Instance;
            if (!Available || rc == null || rc.Round == null || !rc.Round.IsActive || rc.Game == null || rc.Teams == null) return null;
            var runners = new HashSet<int>();
            var outs = new HashSet<int>();
            foreach (var r in rc.Round.Runners)
            {
                if (r.IsAlive) runners.Add(r.Actor);
                else outs.Add(r.Actor);
            }
            var taggers = new HashSet<int>();
            var taggerTeam = rc.Round.TaggerTeam;
            foreach (var s in rc.Game.Players)
                if (!s.IsLocal && rc.Teams.TeamIdOf(s) == taggerTeam && !runners.Contains(s.Actor)) taggers.Add(s.Actor);
            // [2]: the runners already out (tagged, or stopped by the referee), for the player list's TAGGED tag.
            return new object[] { taggers, runners, outs };
        }

        public static bool Available => Plugin.Enabled && RefereeController.Instance != null && Settings.RefereeOn;

        public static bool Read(out ScoreView view)
        {
            view = default;
            var c = RefereeController.Instance;
            if (!Available || c == null || c.Round == null || c.Teams == null) return false;
            var round = c.Round;
            var teams = c.Teams;
            var score = round.Score;
            var state = round.State;
            bool active = round.IsActive;
            var runner = active || state == RoundState.Finished ? round.RunnerTeam : teams.RunnersTeam;
            double lap = score.PendingTeam != TeamId.None && score.PendingTeam != runner ? score.PendingTime : double.NaN;
            view = new ScoreView
            {
                NameA = teams.ShortName(TeamId.A),
                NameB = teams.ShortName(TeamId.B),
                PointsA = score.A,
                PointsB = score.B,
                ColorA = teams.DisplayColor(TeamId.A),
                ColorB = teams.DisplayColor(TeamId.B),
                Time = round.Clock,
                Lap = lap,
                Last = round.LastResult != null ? round.LastResult.Duration : double.NaN,
                Running = active && !round.Paused && state != RoundState.Starting,
                Paused = round.Paused,
                HeadStart = state == RoundState.Starting || state == RoundState.Countdown,
                Finished = state == RoundState.Finished,
                Linked = true,
                Margin = Settings.ReplayMarginSeconds != null ? Settings.ReplayMarginSeconds.Value : 2f,
                Note = state == RoundState.Starting ? "GET READY" : null
            };
            return true;
        }

        /// <summary>Actor number to team (1 = A, 2 = B), both names, both colors and where they came from.</summary>
        private static object[] PackTeams()
        {
            var rc = RefereeController.Instance;
            if (!Plugin.Enabled || rc == null || rc.Game == null || rc.Teams == null) return null;
            var map = new Dictionary<int, int>();
            foreach (var s in rc.Game.Players)
            {
                if (s.IsLocal) continue;
                var t = rc.Teams.TeamIdOf(s);
                if (t == TeamId.A) map[s.Actor] = 1;
                else if (t == TeamId.B) map[s.Actor] = 2;
            }
            if (map.Count == 0) return null;
            return new object[]
            {
                map, rc.Teams.ShortName(TeamId.A), rc.Teams.ShortName(TeamId.B),
                rc.Teams.DisplayColor(TeamId.A), rc.Teams.DisplayColor(TeamId.B), "Teams from the auto referee."
            };
        }
    }
}
