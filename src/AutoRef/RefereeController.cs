using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AutoRef
{
    internal sealed class RefereeController : MonoBehaviour
    {
        public static RefereeController Instance { get; private set; }

        public EventLog Log;
        public GameReader Game;
        public ZoneManager Zones;
        public TeamManager Teams;
        public HostAuthority Authority;
        public RoundManager Round;
        public Stunner Stunner;
        public SubManager Subs;
        public Announcer Announcer;
        public Overlay Overlay;
        public readonly ZoneVisuals ZoneVisuals = new ZoneVisuals();
        public readonly Readiness Ready = new Readiness();

        public bool ShowZoneBoxesNow => Settings.ShowZoneBoxes.Value && !Round.IsActive;

        public readonly List<PlayerSnapshot> TaggerPlayers = new List<PlayerSnapshot>();
        public readonly List<PlayerSnapshot> RunnerPlayers = new List<PlayerSnapshot>();
        public readonly List<PlayerSnapshot> OtherPlayers = new List<PlayerSnapshot>();

        private RefereeGui gui;
        private float nextZoneCheck, nextDetect, nextZoneError, nextCallout, nextPrewarm;
        private readonly Queue<Action> deferred = new Queue<Action>();
        private bool wasInRoom;
        private string lastRoom = "";

        public void Defer(Action action) => deferred.Enqueue(action);

        private void Awake()
        {
            Instance = this;
            Log = new EventLog();
            Game = new GameReader();
            Zones = new ZoneManager();
            Teams = new TeamManager(Log);
            Authority = new HostAuthority(this);
            Announcer = gameObject.AddComponent<Announcer>();
            Announcer.Init(Game, Log);
            Stunner = new Stunner(Log);
            Round = new RoundManager(Game, Announcer, Log, Teams, Authority, Stunner);
            Subs = new SubManager(this);
            Overlay = new Overlay(this);
            gui = new RefereeGui(this);
            Log.Add("--", $"Auto Referee {Plugin.Version} ready. Press {Settings.ToggleKey.Value} to open the panel.", Colors.Info);
        }

        private void Update()
        {
            ThemeStore.Poll();
            PlayerColors.UpdateColor();
            Game.Refresh();
            bool inRoom = Game.InRoom;
            if (inRoom && (!wasInRoom || Game.RoomName != lastRoom)) ResetForNewLobby();
            wasInRoom = inRoom;
            lastRoom = inRoom ? Game.RoomName : "";

            // Off means fully off: the referee's hooks are out of the game, nothing is checked, shown or sent, and
            // the lobby is the game's own (handed back if the referee had set it up). Only the panel's own actions
            // still run, so it can be turned back on.
            if (!Settings.RefereeOn)
            {
                Plugin.SetHooks(false);
                ZoneVisuals.Update(Zones, false);
                Authority.HandBack();
                AutoStartIn = NextRoundIn = -1f;
                readySince = finishedAt = -1f;
                RunDeferred();
                return;
            }
            Plugin.SetHooks(true);

            Zones.Tick();
            ZoneVisuals.Update(Zones, ShowZoneBoxesNow);

            if (Time.unscaledTime >= nextZoneCheck)
            {
                nextZoneCheck = Time.unscaledTime + 0.1f;
                foreach (var p in Game.Players)
                {
                    if (p.IsLocal) continue;
                    try
                    {
                        Zones.Evaluate(p);
                    }
                    catch (Exception e)
                    {
                        if (Time.unscaledTime > nextZoneError)
                        {
                            nextZoneError = Time.unscaledTime + 10f;
                            Plugin.Log.LogError("Zone check failed: " + e);
                        }
                    }
                }
            }

            if (Round.State == RoundState.Idle && Game.InRoom && Time.unscaledTime >= nextDetect)
            {
                nextDetect = Time.unscaledTime + 1f;
                if (TeamManager.PickMode) Teams.UpdatePickedColors(Game.Players);
                else if (Settings.FormatChosen) Teams.Detect(Game.Players, Settings.RequiredTeamSize, userInitiated: false, roundsPlayed: Round.History.Count > 0);
            }

            TaggerPlayers.Clear();
            RunnerPlayers.Clear();
            OtherPlayers.Clear();
            foreach (var p in Game.Players)
            {
                if (p.IsLocal) continue;
                switch (Teams.TeamOf(p))
                {
                    case Team.Taggers: TaggerPlayers.Add(p); break;
                    case Team.Runners: RunnerPlayers.Add(p); break;
                    default: OtherPlayers.Add(p); break;
                }
            }

            Subs.Tick();
            Ready.Evaluate(this);
            RunDeferred();
            // A panel action may have just turned the referee off: then nothing more this frame.
            if (!Settings.RefereeOn) return;

            Round.Tick();
            Stunner.Tick();
            Authority.Tick();
            UpdateAutoReferee();
            UpdateCallouts();
            UpdateHostCallout();
            PrewarmVoiceLines();
        }

        /// <summary>The panel's queued actions (buttons pressed this frame).</summary>
        private void RunDeferred()
        {
            while (deferred.Count > 0)
            {
                var action = deferred.Dequeue();
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Panel action failed: " + e);
                }
            }
        }

        private void OnGUI() => gui.OnGUI();

        public bool PanelVisible => gui != null && gui.Visible;

        public void TogglePanel() => Defer(gui.ToggleVisible);

        /// <summary>Opens the panel (from the Caster menu's Match column). The Caster menu closes.</summary>
        public void OpenPanel() => Defer(() =>
        {
            if (!gui.Visible) gui.ToggleVisible();
        });

        private void ResetForNewLobby()
        {
            deferred.Clear();
            Settings.ResetSession();
            Teams.ResetForNewLobby();
            Subs.Reset();
            Round.ClearForNewLobby();
            Authority.ResetForNewLobby();
            Announcer.StopAll();
            gui.ResetForNewLobby();
            readySince = finishedAt = -1f;
            AutoStartIn = NextRoundIn = -1f;
            nextDetect = 0f;
            nextCallout = Time.unscaledTime + Mathf.Max(10f, Settings.CalloutInterval.Value);
            Log.Add("--", $"Joined {Game.RoomName} - teams, score, rounds and panel settings reset to defaults.", Colors.Info);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (HostAuthority.Current == Authority) HostAuthority.Current = null;
            Stunner?.ReleaseAll();
            try { Announcer?.Voice.Restore(); } catch { }
            VoiceGuard.Release();
            ZoneVisuals.Destroy();
        }

        public void StartRound(bool force)
        {
            if (Round.IsActive) return;
            if (RunnerPlayers.Count == 0)
            {
                Log.Add("--", "Can't start: no runners assigned.", Colors.Bad);
                return;
            }
            string forcedReason = null;
            if (!Ready.AllReady)
            {
                if (!force) return;
                forcedReason = Ready.Issues.Count > 0
                    ? Ready.Issues[0] + (Ready.Issues.Count > 1 ? $" (+{Ready.Issues.Count - 1} more)" : "")
                    : "checks not met";
            }
            Round.Start(TaggerPlayers, RunnerPlayers, forcedReason);
        }

        public void SetFirstTo(int n)
        {
            if (Settings.FirstTo.Value == n) return;
            Settings.FirstTo.Value = n;
            Log.Add("--", n > 0 ? $"Game length: first to {n} points wins." : "Game length: no limit.", Colors.Info);
            if (n > 0 && Game.InRoom && !Round.IsActive && !Round.Score.GameOver) Announcer.Say($"First to {n} wins the game.");
        }

        public void NewGame()
        {
            if (Round.IsActive) return;
            if (Round.State == RoundState.Finished) Round.NextRound(quiet: true);
            Round.Score.Reset();
            int n = Settings.FirstTo.Value;
            Log.Add("--", "New game - the score is back to 0 : 0.", Colors.Info);
            Announcer.Say(n > 0 ? $"New game. First to {n} wins." : "New game.");
        }

        public void AdjustScore(TeamId id, int delta)
        {
            bool wasOver = Round.Score.GameOver;
            Round.Score.Adjust(id, delta);
            if (!wasOver && Round.Score.GameOver) Round.SayGameState(false);
        }

        public bool WillPrepareInfection =>
            Authority.Active && (Game.InfectionActive || (Game.ClassicTagMode && Settings.IsOneVsOneTest));

        public RunnerEntry RunnerEntryFor(PlayerSnapshot p) =>
            Round.State == RoundState.Idle ? null : Round.Runners.Find(r => r.Actor == p.Actor && r.Key == p.Key);

        public TaggerEntry TaggerEntryFor(PlayerSnapshot p) =>
            Round.State == RoundState.Idle ? null : Round.Taggers.Find(t => t.Actor == p.Actor && t.Key == p.Key);

        public float AutoStartIn { get; private set; } = -1f;
        public float NextRoundIn { get; private set; } = -1f;
        private float readySince = -1f, finishedAt = -1f;

        public void SetAutoReferee(bool on)
        {
            if (Settings.AutoReferee.Value == on) return;
            if (!on)
            {
                if (Round.IsActive) Round.Abort(quiet: true);
                if (Round.State == RoundState.Finished) Round.NextRound(quiet: true);
                Stunner.StopAll();
                Announcer.StopAll();
                Authority.HandBack();
            }
            Settings.AutoReferee.Value = on;
            // In while on, out while off (Update keeps this in step too).
            Plugin.SetHooks(on);
            nextCallout = Time.unscaledTime + Mathf.Max(10f, Settings.CalloutInterval.Value);
            Log.Add("--", on
                ? "Auto referee ON - it runs rounds, talks, controls tags and stuns."
                : "Auto referee OFF - it leaves the lobby alone: no talking, no tag or infection control, no stuns and no rounds.", on ? Colors.Good : Colors.Warn);
        }

        private void UpdateAutoReferee()
        {
            AutoStartIn = NextRoundIn = -1f;
            if (!Settings.AutoReferee.Value)
            {
                readySince = finishedAt = -1f;
                return;
            }

            if (Round.State == RoundState.Finished)
            {
                readySince = -1f;
                if (finishedAt < 0f) finishedAt = Time.unscaledTime;
                bool quiet = Announcer.QueueLength == 0 && Announcer.NowPlaying == null;
                float wait = Settings.AutoNextRoundSeconds.Value - (Time.unscaledTime - finishedAt);
                NextRoundIn = Mathf.Max(0f, wait);
                if (wait <= 0f && quiet)
                {
                    finishedAt = -1f;
                    Round.NextRound();
                }
                return;
            }
            finishedAt = -1f;

            if (Round.State != RoundState.Idle || !Game.InRoom || !Ready.AllReady)
            {
                readySince = -1f;
                return;
            }
            if (readySince < 0f) readySince = Time.unscaledTime;
            float left = Settings.AutoStartHoldSeconds.Value - (Time.unscaledTime - readySince);
            AutoStartIn = Mathf.Max(0f, left);
            if (left <= 0f)
            {
                readySince = -1f;
                Log.Add("--", "Auto referee: everyone is in position - starting the round.", Colors.Info);
                StartRound(false);
            }
        }

        private void UpdateCallouts()
        {
            if (Round.State != RoundState.Idle || !Game.InRoom || Round.Score.GameOver)
            {
                nextCallout = Time.unscaledTime + Mathf.Max(10f, Settings.CalloutInterval.Value);
                return;
            }
            if (!Settings.AutoCallouts.Value || Time.unscaledTime < nextCallout) return;
            if (Settings.RequireMasterClient.Value && !Game.IsMaster) return;
            nextCallout = Time.unscaledTime + Settings.CalloutInterval.Value;
            if (Announcer.QueueLength > 2) return;
            if (NeedsMatchingColors)
            {
                Announcer.Say("Players, please put on matching color codes for your team.", "matching_colors");
                return;
            }
            var lines = PositionCallouts(4);
            if (lines.Count == 0) return;
            Announcer.Say("Waiting for all players to get into position.", "waiting_positions");
            foreach (var line in lines) Announcer.Say(line);
        }

        private float nextHostCallout = -1f;

        private void UpdateHostCallout()
        {
            bool need = Game.InRoom && !Game.IsMaster && Settings.RequireMasterClient.Value &&
                        (Round.State == RoundState.Idle || Round.HostLost) && Game.Players.Count > 1;
            if (!need)
            {
                nextHostCallout = -1f;
                return;
            }
            if (nextHostCallout < 0f)
            {
                nextHostCallout = Time.unscaledTime + 4f;
                return;
            }
            if (Time.unscaledTime < nextHostCallout) return;
            nextHostCallout = Time.unscaledTime + 30f;
            AskRejoin();
        }

        public void AskRejoin()
        {
            Announcer.Say("The referee isn't the host. Everyone else, please leave and rejoin the room.", "rejoin_host");
            Log.Add("--", "Asked everyone to leave and rejoin so you become host.", Colors.Info);
        }

        public bool NeedsMatchingColors
        {
            get
            {
                if (TeamManager.PickMode || Teams.TeamsFound || !Game.InRoom || !Settings.FormatChosen || Subs.Locked) return false;
                int others = 0;
                foreach (var p in Game.Players)
                    if (!p.IsLocal) others++;
                return others >= 2;
            }
        }

        public List<string> PositionCallouts(int max)
        {
            var lines = new List<string>();
            foreach (var p in TaggerPlayers)
                if (!p.OnBench && lines.Count < max) lines.Add(CalloutFor(p, Team.Taggers));
            foreach (var p in RunnerPlayers)
                if (!p.UnderGazebo && lines.Count < max) lines.Add(CalloutFor(p, Team.Runners));
            return lines;
        }

        public string CalloutFor(PlayerSnapshot p, Team role)
        {
            string name = SpokenName(p.Name);
            var gazebo = Zones.Gazebo.Center;
            var d = p.Position - gazebo;
            d.y = 0f;
            if (d.magnitude > 15f) return $"{name}, come back to Gazebo.";
            return role == Team.Taggers ? $"{name}, get onto the tagger bench." : $"{name}, move underneath Gazebo.";
        }

        public static string SpokenName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Player";
            var sb = new StringBuilder(name.Length);
            bool start = true;
            foreach (char ch in name)
            {
                if (char.IsLetter(ch))
                {
                    sb.Append(start ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                    start = false;
                }
                else
                {
                    sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
                    start = !char.IsLetterOrDigit(ch);
                }
            }
            var s = sb.ToString().Trim();
            return s.Length > 0 ? s : "Player";
        }

        private void PrewarmVoiceLines()
        {
            if (Time.unscaledTime < nextPrewarm || Round.IsActive) return;
            nextPrewarm = Time.unscaledTime + 3f;
            if (!Settings.AnnouncerEnabled.Value && !Settings.PlayLocally.Value) return;
            foreach (var p in TaggerPlayers) PrewarmPlayer(p);
            foreach (var p in RunnerPlayers) PrewarmPlayer(p);
            int firstTo = Settings.FirstTo.Value;
            foreach (var id in new[] { TeamId.A, TeamId.B })
            {
                Announcer.Prewarm($"{Teams.NameOf(id)} is now Taggers.");
                Announcer.Prewarm($"{Teams.NameOf(id)} is now Runners.");
                Announcer.Prewarm($"{Teams.NameOf(id)} gets the point.");
                if (firstTo <= 0) continue;
                Announcer.Prewarm(Round.MatchPointLine(id));
                var other = id == TeamId.A ? TeamId.B : TeamId.A;
                Announcer.Prewarm(Round.WinLine(id, firstTo, Math.Min(Round.Score.Of(other), firstTo - 1)));
            }
            if (firstTo > 0) Announcer.Prewarm("Both teams are at match point.");
            Announcer.Prewarm("Too close. Both runs are reset.");
        }

        private void PrewarmPlayer(PlayerSnapshot p)
        {
            string name = SpokenName(p.Name);
            Announcer.Prewarm($"{name}, come back to Gazebo.");
            Announcer.Prewarm($"{name}, get onto the tagger bench.");
            Announcer.Prewarm($"{name}, move underneath Gazebo.");
        }
    }

    internal sealed class Readiness
    {
        public bool AllReady;
        public readonly List<string> Issues = new List<string>();
        public readonly Dictionary<int, string> Problems = new Dictionary<int, string>();

        public void Evaluate(RefereeController c)
        {
            Issues.Clear();
            Problems.Clear();
            var g = c.Game;
            if (!g.InRoom)
            {
                Issues.Add("You are not in a room. Join the scrim code first.");
                AllReady = false;
                return;
            }

            var score = c.Round.Score;
            var winner = score.GameWinner;
            if (winner != TeamId.None)
            {
                var loser = winner == TeamId.A ? TeamId.B : TeamId.A;
                Issues.Add($"{c.Teams.NameOf(winner)} won the game {score.Of(winner)}-{score.Of(loser)}. Start a new game to keep playing.");
            }

            if (Settings.RequireMasterClient.Value && !g.IsMaster)
                Issues.Add($"You are not the host (Master Client) - {g.MasterName} is. Create the room yourself, or the host must leave so it passes to you.");

            if (!g.IsInfectionMode)
                Issues.Add($"Game mode is {g.ModeName}, not Infection - tags can't be detected. Make the room Infection.");
            else if (g.ClassicTagMode && !Settings.IsOneVsOneTest)
                Issues.Add("The lobby is in classic Tag mode (Infection needs 4+ players). Use the 1v1 test format to test with fewer.");
            else if (g.WaitingForNextInfectionRound && !c.WillPrepareInfection)
                Issues.Add("The game is between Infection rounds - wait for the next one to start.");

            if (!Settings.FormatChosen)
                Issues.Add("Pick a format (3v3, 4v4 or 1v1 test) on the Teams page.");
            else if (c.NeedsMatchingColors)
                Issues.Add("No teams found yet - each team needs to wear the same color code.");
            else if (TeamManager.PickMode && !c.Teams.TeamsFound && !c.Subs.Locked)
                Issues.Add("Pick the players for each team on the Teams page.");
            c.Subs.AddIssues(Issues);

            if (c.Teams.ColorsConflict)
                Issues.Add("Team A and Team B have the same color code.");

            int t = c.TaggerPlayers.Count, r = c.RunnerPlayers.Count;
            int req = Settings.RequiredTeamSize;
            bool gapOpen = c.Subs.GapOf(TeamId.A) != null || c.Subs.GapOf(TeamId.B) != null;
            bool sizesOk = c.Subs.SizeOk(c.Teams.TaggersTeam, t, req) && c.Subs.SizeOk(c.Teams.RunnersTeam, r, req);
            if (req > 0 && !sizesOk && (!gapOpen || c.Subs.Gaps.All(x => x.Proceed)))
                Issues.Add($"Need {req}v{req}: found {t} tagger{S(t)} and {r} runner{S(r)}.");

            bool checkInfection = !c.WillPrepareInfection;
            string infectedWord = g.ClassicTagMode ? "'it'" : "infected (lava)";
            int badTaggers = 0, badRunners = 0;
            foreach (var p in c.TaggerPlayers)
            {
                string problem = null;
                if (!p.OnBench) problem = "not on the bench";
                else if (checkInfection && Settings.RequireTaggersInfected.Value && !p.Infected) problem = $"not {infectedWord} yet";
                if (problem == null) continue;
                Problems[p.Actor] = problem;
                badTaggers++;
            }
            foreach (var p in c.RunnerPlayers)
            {
                string problem = null;
                if (!p.UnderGazebo) problem = "not under the gazebo";
                else if (checkInfection && Settings.RequireRunnersNotInfected.Value && p.Infected) problem = $"already {infectedWord}";
                if (problem == null) continue;
                Problems[p.Actor] = problem;
                badRunners++;
            }
            if (badTaggers > 0) Issues.Add($"{badTaggers} tagger{S(badTaggers)} not ready.");
            if (badRunners > 0) Issues.Add($"{badRunners} runner{S(badRunners)} not ready.");

            AllReady = Issues.Count == 0;
        }

        private static string S(int n) => n == 1 ? "" : "s";
    }
}
