using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoRef
{
    internal enum Team { None, Taggers, Runners }

    internal sealed class TeamManager
    {
        public ColorCode? ColorA;
        public ColorCode? ColorB;
        public readonly Dictionary<string, TeamId> Overrides = new Dictionary<string, TeamId>();
        public string DetectMessage = "";
        public bool TeamsFound;
        public bool RolesGuessed;

        private readonly EventLog log;

        public TeamManager(EventLog log)
        {
            this.log = log;
            ColorA = ColorCode.ParseOrNull(Settings.TeamAColor.Value);
            ColorB = ColorCode.ParseOrNull(Settings.TeamBColor.Value);
        }

        public TeamId TaggersTeam => Settings.TaggersTeam.Value == TeamId.B ? TeamId.B : TeamId.A;
        public TeamId RunnersTeam => TaggersTeam == TeamId.A ? TeamId.B : TeamId.A;

        public TeamId TeamFor(Team role) => role == Team.Taggers ? TaggersTeam : role == Team.Runners ? RunnersTeam : TeamId.None;

        public Team RoleOf(TeamId id) => id == TeamId.None ? Team.None : id == TaggersTeam ? Team.Taggers : Team.Runners;

        public ColorCode? ColorOf(TeamId id) => id == TeamId.A ? ColorA : id == TeamId.B ? ColorB : null;

        private static string DefaultName(TeamId id) => id == TeamId.A ? "Team A" : id == TeamId.B ? "Team B" : "No team";

        private static string RawName(TeamId id) =>
            ((id == TeamId.A ? Settings.TeamAName.Value : id == TeamId.B ? Settings.TeamBName.Value : "") ?? "").Trim();

        public bool HasCustomName(TeamId id)
        {
            string n = RawName(id);
            return n.Length > 0 && n != DefaultName(id);
        }

        public string ColorName(TeamId id)
        {
            var c = ColorOf(id);
            if (!c.HasValue) return null;
            string name = c.Value.Name;
            var other = ColorOf(id == TeamId.A ? TeamId.B : TeamId.A);
            if (!other.HasValue || other.Value.Name != name) return name;
            return other.Value == c.Value ? null : name + " " + c.Value.Compact;
        }

        public string NameOf(TeamId id)
        {
            if (id == TeamId.None) return "No team";
            if (HasCustomName(id)) return RawName(id);
            return ColorName(id) ?? DefaultName(id);
        }

        public string ShortName(TeamId id)
        {
            if (HasCustomName(id)) return RawName(id).ToUpperInvariant();
            return (ColorName(id) ?? DefaultName(id)).ToUpperInvariant();
        }

        public ColorCode? TaggerColor => ColorOf(TaggersTeam);
        public ColorCode? RunnerColor => ColorOf(RunnersTeam);

        public bool ColorsConflict => !PickMode && ColorA.HasValue && ColorB.HasValue && ColorA.Value == ColorB.Value;

        public void FreezeIntoPicks(IReadOnlyList<PlayerSnapshot> players)
        {
            foreach (var p in players)
            {
                if (p.IsLocal || Overrides.ContainsKey(p.Key)) continue;
                var id = TeamIdOf(p);
                if (id != TeamId.None) Overrides[p.Key] = id;
            }
        }

        public void UpdatePickedColors(IReadOnlyList<PlayerSnapshot> players)
        {
            ColorCode? Majority(TeamId id)
            {
                var counts = new Dictionary<ColorCode, int>();
                foreach (var p in players)
                {
                    if (p.IsLocal || !p.ColorKnown || TeamIdOf(p) != id) continue;
                    counts.TryGetValue(p.Color, out int n);
                    counts[p.Color] = n + 1;
                }
                if (counts.Count == 0) return null;
                return counts.OrderByDescending(kv => kv.Value).First().Key;
            }
            var a = Majority(TeamId.A);
            var b = Majority(TeamId.B);
            if (a != ColorA || b != ColorB)
            {
                ColorA = a;
                ColorB = b;
                Save();
            }
            TeamsFound = players.Any(p => !p.IsLocal && TeamIdOf(p) == TeamId.A) && players.Any(p => !p.IsLocal && TeamIdOf(p) == TeamId.B);
        }

        public void SwapRoles(bool log = true)
        {
            Settings.TaggersTeam.Value = RunnersTeam;
            RolesGuessed = false;
            if (log)
                this.log.Add("--", $"Roles switched: {NameOf(TaggersTeam)} are now Taggers, {NameOf(RunnersTeam)} are now Runners.", Colors.Info);
        }

        public void ResetForNewLobby()
        {
            ColorA = null;
            ColorB = null;
            Overrides.Clear();
            DetectMessage = "";
            TeamsFound = false;
            RolesGuessed = false;
        }

        public static bool PickMode => Settings.ColorMode.Value == ColorAssignMode.Manual;

        public TeamId TeamIdOf(PlayerSnapshot p)
        {
            if (p == null || p.IsLocal) return TeamId.None;
            if (Overrides.TryGetValue(p.Key, out var t)) return t;
            if (PickMode || !p.ColorKnown) return TeamId.None;
            if (ColorA.HasValue && p.Color == ColorA.Value) return TeamId.A;
            if (ColorB.HasValue && p.Color == ColorB.Value) return TeamId.B;
            return TeamId.None;
        }

        public Team TeamOf(PlayerSnapshot p) => RoleOf(TeamIdOf(p));

        public bool HasOverride(PlayerSnapshot p) => p != null && Overrides.ContainsKey(p.Key);

        public void SetOverride(PlayerSnapshot p, TeamId? team)
        {
            if (p == null || p.IsLocal) return;
            if (team.HasValue) Overrides[p.Key] = team.Value;
            else Overrides.Remove(p.Key);
        }

        public void SetTeamColor(TeamId id, ColorCode? code)
        {
            if (id == TeamId.A) ColorA = code;
            else if (id == TeamId.B) ColorB = code;
            Save();
        }

        private void Save()
        {
            Settings.TeamAColor.Value = ColorA.HasValue ? ColorA.Value.Compact : "";
            Settings.TeamBColor.Value = ColorB.HasValue ? ColorB.Value.Compact : "";
        }

        public Color DisplayColor(Team role) => DisplayColor(TeamFor(role), role);

        public Color DisplayColor(TeamId id, Team roleFallback = Team.None)
        {
            var c = ColorOf(id);
            if (c.HasValue) return Brighten(c.Value.ToColor());
            var role = roleFallback != Team.None ? roleFallback : RoleOf(id);
            return role == Team.Taggers ? Colors.TaggerDefault : role == Team.Runners ? Colors.RunnerDefault : Colors.Dim;
        }

        private static Color Brighten(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max < 0.55f) c = Color.Lerp(c, Color.white, 0.55f - max + 0.15f);
            return c;
        }

        public static string Describe(ColorCode? c) => c.HasValue ? c.Value.ToString() : "(not set)";

        public string DescribeRole(Team role)
        {
            var id = TeamFor(role);
            return $"{NameOf(id)} ({Describe(ColorOf(id))})";
        }

        public bool Detect(IReadOnlyList<PlayerSnapshot> players, int requiredSize, bool userInitiated, bool roundsPlayed)
        {
            var groups = new Dictionary<ColorCode, List<PlayerSnapshot>>();
            foreach (var p in players)
            {
                if (p.IsLocal || !p.ColorKnown) continue;
                if (!groups.TryGetValue(p.Color, out var list)) groups[p.Color] = list = new List<PlayerSnapshot>();
                list.Add(p);
            }

            bool SizeOk(int n) => requiredSize > 0 ? n == requiredSize : (n == 3 || n == 4);
            var candidates = groups.Where(kv => SizeOk(kv.Value.Count)).OrderByDescending(kv => kv.Value.Count).ToList();
            TeamsFound = false;

            if (candidates.Count == 2)
            {
                var a = candidates[0].Key;
                var b = candidates[1].Key;
                bool sameSet = ColorA.HasValue && ColorB.HasValue &&
                               ((ColorA.Value == a && ColorB.Value == b) || (ColorA.Value == b && ColorB.Value == a));
                if (sameSet)
                {
                    ResolveGuessedRoles(groups, roundsPlayed);
                    TeamsFound = true;
                    DetectMessage = $"Teams: {NameOf(TeamId.A)} {ColorA.Value} ({Count(groups, ColorA.Value)}) and {NameOf(TeamId.B)} {ColorB.Value} ({Count(groups, ColorB.Value)}).";
                    return false;
                }

                if (ColorA.HasValue && (ColorA.Value == a || ColorA.Value == b)) ColorB = ColorA.Value == a ? b : a;
                else if (ColorB.HasValue && (ColorB.Value == a || ColorB.Value == b)) ColorA = ColorB.Value == a ? b : a;
                else
                {
                    ColorA = a;
                    ColorB = b;
                }
                RolesGuessed = false;
                Save();
                TeamsFound = true;
                DetectMessage = $"Detected {NameOf(TeamId.A)} {ColorA.Value} ({Count(groups, ColorA.Value)}) and {NameOf(TeamId.B)} {ColorB.Value} ({Count(groups, ColorB.Value)}).";
                log.Add("--", DetectMessage, Colors.Info);
                return true;
            }

            if (candidates.Count == 1 && (ColorA.HasValue ^ ColorB.HasValue))
            {
                var found = candidates[0].Key;
                bool aSet = ColorA.HasValue;
                var known = aSet ? ColorA.Value : ColorB.Value;
                if (found != known)
                {
                    if (aSet) ColorB = found;
                    else ColorA = found;
                    Save();
                    DetectMessage = $"Detected {NameOf(aSet ? TeamId.B : TeamId.A)} {found} ({candidates[0].Value.Count}).";
                    log.Add("--", DetectMessage, Colors.Info);
                    return true;
                }
            }

            var described = groups.OrderByDescending(kv => kv.Value.Count).Select(kv => $"{kv.Key} x{kv.Value.Count}").ToList();
            string sizeText = requiredSize == 1 ? "1 player" : requiredSize > 0 ? $"{requiredSize} players" : "3 or 4 players";
            DetectMessage = $"No clear teams: need two color codes with {sizeText} each. Colors in lobby: " +
                            (described.Count > 0 ? string.Join(", ", described) : "none");
            if (userInitiated) log.Add("--", DetectMessage, Colors.Warn);
            return false;
        }

        private void ResolveGuessedRoles(Dictionary<ColorCode, List<PlayerSnapshot>> groups, bool roundsPlayed)
        {
            if (!RolesGuessed || roundsPlayed || !TaggerColor.HasValue || !RunnerColor.HasValue) return;
            float et = Evidence(groups.TryGetValue(TaggerColor.Value, out var t) ? t : new List<PlayerSnapshot>());
            float er = Evidence(groups.TryGetValue(RunnerColor.Value, out var r) ? r : new List<PlayerSnapshot>());
            if (Mathf.Abs(et - er) < 0.5f) return;
            if (er > et) SwapRoles(log: false);
            RolesGuessed = false;
            log.Add("--", $"{NameOf(TaggersTeam)} look like the taggers (lava / on the bench) - they tag first.", Colors.Info);
        }

        private static int Count(Dictionary<ColorCode, List<PlayerSnapshot>> groups, ColorCode c) =>
            groups.TryGetValue(c, out var l) ? l.Count : 0;

        private static float Evidence(List<PlayerSnapshot> group)
        {
            if (group.Count == 0) return 0f;
            float score = 0f;
            foreach (var p in group)
            {
                if (p.Infected) score += 1f;
                if (p.OnBench) score += 0.5f;
            }
            return score / group.Count;
        }
    }
}
