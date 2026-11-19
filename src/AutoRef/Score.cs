using System;
using System.Collections.Generic;

namespace AutoRef
{
    internal sealed class PointResult
    {
        public int RoundA, RoundB;
        public double TimeA, TimeB;
        public TeamId Winner;
        public bool Replay;
        public double Margin;
    }

    internal sealed class MatchScore
    {
        public int A, B;
        public readonly List<PointResult> Points = new List<PointResult>();

        public TeamId PendingTeam { get; private set; } = TeamId.None;
        public double PendingTime { get; private set; }
        public int PendingRound { get; private set; }

        public int Of(TeamId t) => t == TeamId.A ? A : t == TeamId.B ? B : 0;

        public static int FirstTo => Settings.FirstTo == null ? 0 : Settings.FirstTo.Value;

        public TeamId GameWinner
        {
            get
            {
                int n = FirstTo;
                if (n <= 0 || (A < n && B < n) || A == B) return TeamId.None;
                return A > B ? TeamId.A : TeamId.B;
            }
        }

        public bool GameOver => GameWinner != TeamId.None;

        public bool AtMatchPoint(TeamId t) => FirstTo > 0 && !GameOver && Of(t) == FirstTo - 1;

        public PointResult Record(RoundResult r, out bool replaced)
        {
            replaced = false;
            var team = r.RunnerTeam;
            if (team == TeamId.None) return null;
            if (PendingTeam == TeamId.None || PendingTeam == team)
            {
                replaced = PendingTeam == team;
                PendingTeam = team;
                PendingTime = r.Duration;
                PendingRound = r.Number;
                return null;
            }

            var p = new PointResult();
            if (team == TeamId.A)
            {
                p.TimeA = r.Duration; p.RoundA = r.Number;
                p.TimeB = PendingTime; p.RoundB = PendingRound;
            }
            else
            {
                p.TimeB = r.Duration; p.RoundB = r.Number;
                p.TimeA = PendingTime; p.RoundA = PendingRound;
            }
            long a = (long)Math.Floor(p.TimeA * 100 + 1e-6), b = (long)Math.Floor(p.TimeB * 100 + 1e-6);
            p.Margin = Math.Max(0.0, Settings.ReplayMarginSeconds.Value);
            long margin = (long)Math.Round(p.Margin * 100);
            p.Replay = Math.Abs(a - b) <= margin;
            p.Winner = p.Replay ? TeamId.None : a > b ? TeamId.A : TeamId.B;
            if (p.Winner == TeamId.A) A++;
            else if (p.Winner == TeamId.B) B++;
            Points.Add(p);
            PendingTeam = TeamId.None;
            return p;
        }

        public void Adjust(TeamId t, int delta)
        {
            if (t == TeamId.A) A = Math.Max(0, A + delta);
            else if (t == TeamId.B) B = Math.Max(0, B + delta);
        }

        public void Reset()
        {
            A = B = 0;
            Points.Clear();
            PendingTeam = TeamId.None;
            PendingTime = 0;
            PendingRound = 0;
        }
    }
}
