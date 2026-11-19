using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Devicular
{
    internal sealed class CastPlayer
    {
        public VRRig Rig;
        public Transform Head, Body, LeftHand, RightHand;
        public float Scale = 1f;
        public bool Ghost;
        public int Actor;
        public int Index = -1;
        public string Name;
        public Color Color;
        public bool Tagged;
        public bool IsLocal;
        public int Team;
        public string TeamName = "";
        public Color TeamColor;

        /// <summary>For handing a replay gorilla to another Devicular mod through <see cref="Link"/>. Fields are only ever added at the end.</summary>
        public object[] Pack() => new object[]
        {
            Actor, Name, Color, IsLocal, Team, TeamName, TeamColor, Head, Body, LeftHand, RightHand, Scale, Tagged
        };

        public void Unpack(object[] a)
        {
            Actor = Link.At(a, 0, 0);
            Name = Link.At(a, 1, "");
            Color = Link.At(a, 2, Color.white);
            IsLocal = Link.At(a, 3, false);
            Team = Link.At(a, 4, 0);
            TeamName = Link.At(a, 5, "");
            TeamColor = Link.At(a, 6, Color.white);
            Head = Link.At<Transform>(a, 7);
            Body = Link.At<Transform>(a, 8);
            LeftHand = Link.At<Transform>(a, 9);
            RightHand = Link.At<Transform>(a, 10);
            Scale = Link.At(a, 11, 1f);
            Tagged = Link.At(a, 12, false);
            Ghost = true;
        }
    }

    internal static class Roster
    {
        public static readonly List<CastPlayer> Players = new List<CastPlayer>();
        public static readonly List<CastPlayer> Ordered = new List<CastPlayer>();
        public static CastPlayer Local;
        public static bool TeamFilter;
        public static bool PickTeams;
        public static readonly Dictionary<int, int> Picks = new Dictionary<int, int>();
        public static Func<List<CastPlayer>> ReplaySource;
        public static Func<CastPlayer> ReplayLocal;
        public static bool Replaying => ReplaySource != null;
        public static bool TeamsKnown { get; private set; }

        /// <summary>
        /// Other Devicular mods to take teams from, in order (see <see cref="Link"/>). The caster's teams come first,
        /// since they include the players you picked; the caster itself only reads the referee's.
        /// </summary>
        public static string[] TeamLinks = { "Caster.Teams", "Referee.Teams" };
        public static string TeamSource { get; private set; } = "";

        private static int frame = -1;

        public static void Refresh()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            Players.Clear();
            Ordered.Clear();
            Local = null;
            TeamsKnown = false;
            if (ReplaySource != null)
            {
                foreach (var g in ReplaySource()) if (!g.IsLocal) Players.Add(g);
                Local = ReplayLocal?.Invoke();
                if (Local != null) Local.Index = 0;
                AssignRecordedTeams();
                Order();
                return;
            }
            var ns = NetworkSystem.Instance;
            if (ns == null || !ns.InRoom)
            {
                Picks.Clear();
                return;
            }
            IReadOnlyList<VRRig> rigs;
            try { rigs = VRRigCache.ActiveRigs; }
            catch { return; }
            if (rigs == null) return;
            var local = VRRig.LocalRig;
            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null || rig == local || !rig.gameObject.activeInHierarchy) continue;
                NetPlayer np = null;
                try { np = rig.Creator ?? rig.OwningNetPlayer; } catch { }
                if (np == null || np.IsNull || np.IsLocal) continue;
                string name = !string.IsNullOrEmpty(rig.playerNameVisible) ? rig.playerNameVisible : np.NickName;
                Players.Add(new CastPlayer
                {
                    Rig = rig,
                    Head = rig.headMesh != null ? rig.headMesh.transform : rig.transform,
                    Body = rig.bodyTransform != null ? rig.bodyTransform : rig.transform,
                    LeftHand = rig.leftHandTransform,
                    RightHand = rig.rightHandTransform,
                    Scale = Mathf.Max(0.05f, rig.transform.localScale.y),
                    Actor = np.ActorNumber,
                    Name = string.IsNullOrEmpty(name) ? "Player " + np.ActorNumber : name,
                    Color = rig.playerColor,
                    Tagged = rig.setMatIndex != 0
                });
            }

            var own = local != null ? local : (GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null);
            if (own != null)
            {
                var lp = ns.LocalPlayer;
                string name = lp != null && !string.IsNullOrEmpty(lp.NickName) ? lp.NickName : own.playerNameVisible;
                Local = new CastPlayer
                {
                    Rig = own,
                    Head = own.headMesh != null ? own.headMesh.transform : own.transform,
                    Body = own.bodyTransform != null ? own.bodyTransform : own.transform,
                    LeftHand = own.leftHandTransform,
                    RightHand = own.rightHandTransform,
                    Scale = Mathf.Max(0.05f, own.transform.localScale.y),
                    Actor = lp != null ? lp.ActorNumber : 0,
                    Name = string.IsNullOrEmpty(name) ? "Caster" : name,
                    Color = own.playerColor,
                    Tagged = own.setMatIndex != 0,
                    IsLocal = true,
                    Index = 0
                };
            }

            AssignTeams();
            Order();
        }

        private static void AssignRecordedTeams()
        {
            foreach (var p in Players)
                if (p.Team > 0) TeamsKnown = true;
            if (TeamsKnown)
            {
                TeamSource = "Teams as recorded.";
                return;
            }
            AssignColorTeams();
        }

        public static int PickOf(int actor) => Picks.TryGetValue(actor, out int t) ? t : 0;

        public static void SetPick(int actor, int team)
        {
            if (team == 0) Picks.Remove(actor);
            else Picks[actor] = team;
        }

        private static void AssignPickedTeams()
        {
            Color? colorA = null, colorB = null;
            foreach (var p in Players.OrderBy(x => x.Actor))
            {
                int t = PickOf(p.Actor);
                if (t == 1 && colorA == null) colorA = p.Color;
                if (t == 2 && colorB == null) colorB = p.Color;
            }
            foreach (var p in Players)
            {
                int t = PickOf(p.Actor);
                if (t == 0) continue;
                p.Team = t;
                p.TeamName = t == 1 ? "TEAM A" : "TEAM B";
                p.TeamColor = (t == 1 ? colorA : colorB) ?? p.Color;
                TeamsKnown = true;
            }
            TeamSource = TeamsKnown ? "Teams picked by you." : "No teams yet: pick A or B for each player below.";
        }

        private static void AssignTeams()
        {
            if (PickTeams)
            {
                AssignPickedTeams();
                return;
            }
            foreach (var name in TeamLinks)
            {
                var t = Link.Call(name);
                var map = Link.At<Dictionary<int, int>>(t, 0);
                if (map == null) continue;
                string nameA = Link.At(t, 1, "TEAM A"), nameB = Link.At(t, 2, "TEAM B");
                Color colorA = Link.At(t, 3, Color.white), colorB = Link.At(t, 4, Color.white);
                foreach (var p in Players)
                {
                    if (!map.TryGetValue(p.Actor, out int team) || (team != 1 && team != 2)) continue;
                    p.Team = team;
                    p.TeamName = team == 1 ? nameA : nameB;
                    p.TeamColor = team == 1 ? colorA : colorB;
                    TeamsKnown = true;
                }
                if (TeamsKnown)
                {
                    TeamSource = Link.At(t, 5, "Teams from the auto referee.");
                    return;
                }
            }

            AssignColorTeams();
        }

        private static void AssignColorTeams()
        {
            var groups = Players
                .GroupBy(p => ColorCode.FromColor(p.Color))
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.Compact, StringComparer.Ordinal)
                .Take(2)
                .ToList();
            for (int i = 0; i < groups.Count; i++)
            {
                var code = groups[i].Key;
                foreach (var p in groups[i])
                {
                    p.Team = i + 1;
                    p.TeamName = code.Name.ToUpperInvariant();
                    p.TeamColor = code.ToColor();
                }
            }
            TeamsKnown = groups.Count > 0;
            bool refOn = Link.Has("Referee.Teams");
            TeamSource = TeamsKnown
                ? (refOn ? "The auto referee has no teams yet, so teams come from matching color codes." : "The auto referee is off, so teams come from matching color codes.")
                : (refOn ? "No teams yet: the auto referee hasn't found any and nobody shares a color code." : "No teams yet: the auto referee is off and nobody shares a color code.");
        }

        /// <summary>
        /// The teams this list uses, packed for other Devicular mods (Caster.Teams in <see cref="Link"/>):
        /// actor number to team (1 or 2), both names, both colors, and where they came from. Null when there are none.
        /// </summary>
        public static object[] PackTeams()
        {
            if (!TeamsKnown || Replaying) return null;
            var map = new Dictionary<int, int>();
            string nameA = null, nameB = null;
            Color colorA = Color.white, colorB = Color.white;
            foreach (var p in Players)
            {
                if (p.Team != 1 && p.Team != 2) continue;
                map[p.Actor] = p.Team;
                if (p.Team == 1 && nameA == null)
                {
                    nameA = p.TeamName;
                    colorA = p.TeamColor;
                }
                else if (p.Team == 2 && nameB == null)
                {
                    nameB = p.TeamName;
                    colorB = p.TeamColor;
                }
            }
            if (map.Count == 0) return null;
            return new object[] { map, nameA ?? "TEAM A", nameB ?? "TEAM B", colorA, colorB, TeamSource };
        }

        private static void Order()
        {
            if (Local != null && !TeamFilter) Ordered.Add(Local);
            IEnumerable<CastPlayer> others = TeamsKnown
                ? Players.OrderBy(p => p.Team == 0 ? 3 : p.Team).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : Players.OrderBy(p => p.Actor);
            int n = 1;
            foreach (var p in others)
            {
                if (TeamFilter && p.Team == 0)
                {
                    p.Index = -1;
                    continue;
                }
                p.Index = n <= 9 ? n : -1;
                n++;
                Ordered.Add(p);
            }
        }

        // ---------- All-in-One: teams for recordings and the live stream ----------
        // The caster's own list only uses the teams you pick (PickTeams). As separate DLLs, Replay and Live Replay each
        // had a roster of their own that fell back to the referee's teams, then matching color codes. In one DLL they
        // share the caster's, so when you haven't picked anyone, recordings and the stream get those teams from here.
        private static int captureFrame = -1;
        private static readonly Dictionary<int, (int Team, string Name, Color Color)> captureTeams = new Dictionary<int, (int, string, Color)>();

        /// <summary>The team a recording or the live stream saves for this player.</summary>
        public static void CaptureTeam(CastPlayer p, out int team, out string name, out Color color)
        {
            team = p.Team;
            name = p.TeamName;
            color = p.TeamColor;
            if (team > 0 || !PickTeams || TeamsKnown || Replaying) return;
            if (captureFrame != Time.frameCount)
            {
                captureFrame = Time.frameCount;
                BuildCaptureTeams();
            }
            if (captureTeams.TryGetValue(p.Actor, out var t))
            {
                team = t.Team;
                name = t.Name;
                color = t.Color;
            }
        }

        private static void BuildCaptureTeams()
        {
            captureTeams.Clear();
            var t = Link.Call("Referee.Teams");
            var map = Link.At<Dictionary<int, int>>(t, 0);
            if (map != null)
            {
                string nameA = Link.At(t, 1, "TEAM A"), nameB = Link.At(t, 2, "TEAM B");
                Color colorA = Link.At(t, 3, Color.white), colorB = Link.At(t, 4, Color.white);
                foreach (var p in Players)
                {
                    if (!map.TryGetValue(p.Actor, out int team) || (team != 1 && team != 2)) continue;
                    captureTeams[p.Actor] = (team, team == 1 ? nameA : nameB, team == 1 ? colorA : colorB);
                }
                if (captureTeams.Count > 0) return;
            }
            var groups = Players
                .GroupBy(p => ColorCode.FromColor(p.Color))
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.Compact, StringComparer.Ordinal)
                .Take(2)
                .ToList();
            for (int i = 0; i < groups.Count; i++)
                foreach (var p in groups[i])
                    captureTeams[p.Actor] = (i + 1, groups[i].Key.Name.ToUpperInvariant(), groups[i].Key.ToColor());
        }

        public static CastPlayer Find(VRRig rig)
        {
            if (rig == null) return null;
            if (Local != null && Local.Rig == rig) return Local;
            foreach (var p in Players)
                if (p.Rig == rig) return p;
            return null;
        }

        public static CastPlayer ByActor(int actor)
        {
            if (Local != null && Local.Actor == actor) return Local;
            foreach (var p in Players)
                if (p.Actor == actor) return p;
            return null;
        }

        public static CastPlayer ByIndex(int index)
        {
            if (index == 0) return Local;
            foreach (var p in Ordered)
                if (p.Index == index) return p;
            return null;
        }
    }
}
