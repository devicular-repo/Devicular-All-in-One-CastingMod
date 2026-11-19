using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AutoRef
{
    internal sealed class SubManager
    {
        public const float WaitSeconds = 180f;
        private const float Debounce = 4f;

        internal sealed class Gap
        {
            public TeamId Team;
            public int Missing;
            public float Deadline;
            public bool Expired;
            public bool Proceed;
            public int ProceedMissing;
            public bool WarnedMinute;
            public float Left => Mathf.Max(0f, Deadline - Time.unscaledTime);
        }

        private readonly RefereeController c;
        private readonly Dictionary<TeamId, Gap> gaps = new Dictionary<TeamId, Gap>();
        private readonly Dictionary<TeamId, float> shortSince = new Dictionary<TeamId, float>();
        private readonly HashSet<string> announcedNewcomers = new HashSet<string>();
        private int lockedSize;
        private bool lockedPick;
        private bool rolesDrawn;

        public bool Locked { get; private set; }
        public readonly List<string> Newcomers = new List<string>();

        public SubManager(RefereeController controller)
        {
            c = controller;
        }

        public IEnumerable<Gap> Gaps => gaps.Values.OrderBy(g => g.Team);

        public Gap GapOf(TeamId id) => gaps.TryGetValue(id, out var g) ? g : null;

        public void Reset()
        {
            Locked = false;
            rolesDrawn = false;
            gaps.Clear();
            shortSince.Clear();
            Newcomers.Clear();
            announcedNewcomers.Clear();
        }

        private void Unlock()
        {
            Locked = false;
            gaps.Clear();
            shortSince.Clear();
            Newcomers.Clear();
            announcedNewcomers.Clear();
        }

        private int CountOf(TeamId id)
        {
            int n = 0;
            foreach (var p in c.Game.Players)
                if (!p.IsLocal && c.Teams.TeamIdOf(p) == id) n++;
            return n;
        }

        private string Name(TeamId id) => c.Teams.NameOf(id);

        private void Say(string text) => c.Announcer.Say(text);

        private void Note(string text, Color color) => c.Log.Add("--", text, color);

        public void Tick()
        {
            if (!c.Game.InRoom || !Settings.FormatChosen)
            {
                if (Locked) Unlock();
                return;
            }
            int req = Settings.RequiredTeamSize;
            if (Locked && (req != lockedSize || TeamManager.PickMode != lockedPick)) Unlock();

            int a = CountOf(TeamId.A), b = CountOf(TeamId.B);
            if (!Locked)
            {
                if (a == req && b == req && c.Teams.TeamsFound)
                {
                    Locked = true;
                    lockedSize = req;
                    lockedPick = TeamManager.PickMode;
                    DrawRoles();
                }
                return;
            }

            if (c.Round.IsActive) return;
            Check(TeamId.A, a, req);
            Check(TeamId.B, b, req);
            UpdateNewcomers();
        }

        private void DrawRoles()
        {
            if (rolesDrawn || c.Round.History.Count > 0 || c.Round.IsActive) return;
            rolesDrawn = true;
            var taggers = Random.value < 0.5f ? TeamId.A : TeamId.B;
            Settings.TaggersTeam.Value = taggers;
            var runners = taggers == TeamId.A ? TeamId.B : TeamId.A;
            Note($"Teams are set. Coin flip: {Name(taggers)} tag first, {Name(runners)} run first.", Colors.Info);
            int firstTo = Settings.FirstTo.Value;
            Say($"Teams are set. {Name(taggers)} will tag first, {Name(runners)} will run first." + (firstTo > 0 ? $" First to {firstTo} wins the game." : ""));
        }

        private void Check(TeamId id, int count, int req)
        {
            gaps.TryGetValue(id, out var g);
            if (count >= req)
            {
                shortSince.Remove(id);
                if (g != null)
                {
                    gaps.Remove(id);
                    Note($"{Name(id)} is full again.", Colors.Good);
                    Say($"{Name(id)} is full again.");
                }
                return;
            }

            int missing = req - count;
            if (g == null)
            {
                if (!shortSince.TryGetValue(id, out float since))
                {
                    shortSince[id] = Time.unscaledTime;
                    return;
                }
                if (Time.unscaledTime - since < Debounce) return;
                shortSince.Remove(id);
                g = new Gap { Team = id, Missing = missing, Deadline = Time.unscaledTime + WaitSeconds };
                gaps[id] = g;
                AnnounceMissing(g);
                return;
            }

            g.Missing = missing;
            if (g.Proceed)
            {
                if (missing <= g.ProceedMissing) return;
                g.Proceed = false;
                g.Expired = false;
                g.WarnedMinute = false;
                g.Deadline = Time.unscaledTime + WaitSeconds;
                AnnounceMissing(g);
                return;
            }
            if (g.Expired) return;
            float left = g.Deadline - Time.unscaledTime;
            if (!g.WarnedMinute && left <= 60f)
            {
                g.WarnedMinute = true;
                Say($"One minute left to find a sub for {Name(id)}.");
            }
            if (left <= 0f)
            {
                g.Expired = true;
                Note($"{Name(id)} ran out of time to find a sub. Choose Play short or Keep waiting on the Match page.", Colors.Warn);
                Say($"Time is up to find a sub for {Name(id)}. The referee will decide what happens next.");
                c.Announcer.Alert();
            }
        }

        private void AnnounceMissing(Gap g)
        {
            string players = g.Missing == 1 ? "a player" : g.Missing.ToString(CultureInfo.InvariantCulture) + " players";
            string how = TeamManager.PickMode ? "" : " The sub needs to wear the team's color code.";
            Note($"{Name(g.Team)} is missing {players}. 3 minutes to find a sub.", Colors.Warn);
            Say($"{Name(g.Team)} is missing {players}. You have 3 minutes to find a sub.{how}");
        }

        private void UpdateNewcomers()
        {
            Newcomers.Clear();
            if (!TeamManager.PickMode || gaps.Count == 0) return;
            foreach (var p in c.Game.Players)
            {
                if (p.IsLocal || c.Teams.TeamIdOf(p) != TeamId.None) continue;
                Newcomers.Add(p.Name);
                if (announcedNewcomers.Add(p.Key))
                {
                    Note($"{p.Name} is in the lobby without a team. Pick them for the short team on the Teams page.", Colors.Warn);
                    c.Announcer.Alert();
                }
            }
        }

        public void PlayShort(TeamId id)
        {
            if (!gaps.TryGetValue(id, out var g)) return;
            g.Proceed = true;
            g.Expired = false;
            g.ProceedMissing = g.Missing;
            string players = g.Missing == 1 ? "one player" : g.Missing.ToString(CultureInfo.InvariantCulture) + " players";
            Note($"{Name(id)} will play {players} short.", Colors.Info);
            Say($"{Name(id)} will play {players} short. The match continues.");
        }

        public void KeepWaiting(TeamId id)
        {
            if (!gaps.TryGetValue(id, out var g)) return;
            g.Expired = false;
            g.WarnedMinute = false;
            g.Deadline = Time.unscaledTime + WaitSeconds;
            Note($"Waiting 3 more minutes for a sub for {Name(id)}.", Colors.Info);
            Say($"Waiting 3 more minutes for a sub for {Name(id)}.");
        }

        public static string Minutes(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public bool SizeOk(TeamId id, int count, int req)
        {
            if (count == req) return true;
            var g = GapOf(id);
            return g != null && g.Proceed && count >= 1 && count >= req - g.ProceedMissing && count < req;
        }

        public void AddIssues(List<string> issues)
        {
            foreach (var g in Gaps)
            {
                if (g.Proceed) continue;
                string players = g.Missing == 1 ? "a player" : g.Missing.ToString(CultureInfo.InvariantCulture) + " players";
                issues.Add(g.Expired
                    ? $"{Name(g.Team)} is missing {players} and the 3 minutes are up - choose Play short or Keep waiting."
                    : $"{Name(g.Team)} is missing {players} - {Minutes(g.Left)} left to find a sub.");
            }
        }
    }
}
