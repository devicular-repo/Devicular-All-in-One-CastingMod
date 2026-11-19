using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AutoRef
{
    internal enum RoundState { Idle, Starting, Countdown, Running, Finished }

    internal enum RunnerStatus { Alive, Tagged, Survived, Disconnected, StoppedByReferee, EliminatedByReferee }

    internal sealed class RunnerEntry
    {
        public string Key, Name, UserId;
        public int Actor;
        public ColorCode Color;
        public RunnerStatus Status = RunnerStatus.Alive;
        public double OutTime;
        public string TaggedBy = "";
        public string TaggedByKey = "";
        public readonly List<string> Flags = new List<string>();
        public bool WasInfected;
        internal bool TimedFromList;
        internal bool PauseFlagged;
        public bool FalseStart;
        internal bool InPositionSeen;
        internal double OutSince = -1;
        public int Rank;

        public bool IsAlive => Status == RunnerStatus.Alive;

        public double SurvivalAt(double clock, double cap) =>
            IsAlive ? Math.Max(0.0, Math.Min(clock, cap)) : OutTime;

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case RunnerStatus.Alive: return "ALIVE";
                    case RunnerStatus.Tagged: return "TAGGED";
                    case RunnerStatus.Survived: return "SURVIVED";
                    case RunnerStatus.Disconnected: return "LEFT";
                    case RunnerStatus.StoppedByReferee: return "NOT TAGGED (ended)";
                    case RunnerStatus.EliminatedByReferee: return "OUT (referee)";
                    default: return Status.ToString();
                }
            }
        }
    }

    internal sealed class TaggerEntry
    {
        public string Key, Name;
        public int Actor;
        public ColorCode Color;
        public int Tags;
        public int FalseStarts;
        public bool Left;
        internal double OffBenchSince = -1;
        internal bool OffBenchFlagged;
        internal bool OnBenchSeen;
    }

    internal sealed class RoundResult
    {
        public int Number;
        public DateTime StartedAt;
        public string Room;
        public string Format;
        public double Cap;
        public double HeadStart;
        public double Duration;
        public string EndReason;
        public TeamId TaggerTeam, RunnerTeam;
        public string TaggerTeamName, RunnerTeamName;
        public List<RunnerEntry> Ranked;
        public List<TaggerEntry> Taggers;
        public List<RunnerEntry> Winners;
        public PointResult Point;
        public string Summary;

        public string WinnerLine
        {
            get
            {
                if (Winners == null || Winners.Count == 0) return "No winner";
                string names = string.Join(" & ", Winners.Select(w => w.Name));
                var w0 = Winners[0];
                string time = TimeFmt.Clock(w0.Status == RunnerStatus.Survived ? Cap : w0.OutTime);
                string how = w0.Status == RunnerStatus.Survived ? "survived the full time" : "longest survival";
                return (Winners.Count > 1 ? "WINNERS (tie): " : "WINNER: ") + $"{names}  {time}  ({how})";
            }
        }
    }

    internal sealed class RoundManager
    {
        private const double LeadIn = 3.0;

        public RoundState State { get; private set; } = RoundState.Idle;
        public readonly List<RunnerEntry> Runners = new List<RunnerEntry>();
        public readonly List<TaggerEntry> Taggers = new List<TaggerEntry>();
        public readonly List<RoundResult> History = new List<RoundResult>();
        public readonly MatchScore Score = new MatchScore();
        public RoundResult LastResult => History.Count > 0 ? History[History.Count - 1] : null;
        public double Cap { get; private set; } = 180;
        public double HeadStart { get; private set; } = 10;
        public int RoundNumber { get; private set; }
        public bool Paused { get; private set; }
        public string PauseReason { get; private set; } = "";
        public bool HostLost { get; private set; }
        public TeamId TaggerTeam { get; private set; }
        public TeamId RunnerTeam { get; private set; }

        private readonly GameReader game;
        private readonly Announcer announcer;
        private readonly EventLog log;
        private readonly TeamManager teams;
        private readonly HostAuthority authority;
        private readonly Stunner stunner;
        private double releaseAt;
        private double pausedAt;
        private double finishedClock;
        private int lastLeadInNumber;
        private int lastTagCountdown;
        private bool warnedNoInfection;
        private bool startedAsHost;
        private bool allowClassicTag;
        private int lastInfectedCount;
        private double allOutAt = -1;
        private DateTime startedAt;

        public RoundManager(GameReader game, Announcer announcer, EventLog log, TeamManager teams, HostAuthority authority, Stunner stunner)
        {
            this.game = game;
            this.announcer = announcer;
            this.log = log;
            this.teams = teams;
            this.authority = authority;
            this.stunner = stunner;
        }

        public bool IsStunHeld(TaggerEntry t) => t != null && stunner.IsHeld(t.Actor);

        private static double Now => Time.realtimeSinceStartupAsDouble;

        public bool IsActive => State == RoundState.Starting || State == RoundState.Countdown || State == RoundState.Running;

        public double Clock
        {
            get
            {
                switch (State)
                {
                    case RoundState.Starting:
                    case RoundState.Countdown:
                    case RoundState.Running:
                        return (Paused ? pausedAt : Now) - releaseAt;
                    case RoundState.Finished:
                        return finishedClock;
                    default:
                        return -Settings.HeadStartSeconds.Value;
                }
            }
        }

        public int LeadInNumber => State == RoundState.Starting ? Math.Max(1, (int)Math.Ceiling(-(Clock + HeadStart))) : 0;

        public int AliveCount => Runners.Count(r => r.IsAlive);

        private void Log(string text, Color color) =>
            log.Add(State == RoundState.Idle ? "--" : TimeFmt.Clock(Clock), text, color);

        private bool Present(string key, int actor)
        {
            var p = game.Get(actor);
            return p != null && p.Key == key;
        }

        private static string KeyOf(NetPlayer p) => string.IsNullOrEmpty(p.UserId) ? "actor:" + p.ActorNumber : p.UserId;

        public bool IsRoundTagger(NetPlayer p)
        {
            if (p == null) return false;
            string key = KeyOf(p);
            return Taggers.Any(t => t.Key == key);
        }

        public bool IsAliveRunner(NetPlayer p)
        {
            if (p == null) return false;
            string key = KeyOf(p);
            return Runners.Any(r => r.IsAlive && r.Actor == p.ActorNumber && r.Key == key);
        }

        public void Start(IList<PlayerSnapshot> taggers, IList<PlayerSnapshot> runners, string forcedReason)
        {
            if (IsActive || runners.Count == 0) return;
            Runners.Clear();
            Taggers.Clear();
            TagHook.Events.Clear();
            Cap = Math.Max(1.0, Settings.TimeCapSeconds.Value);
            HeadStart = Math.Max(0.0, Settings.HeadStartSeconds.Value);
            warnedNoInfection = false;
            allowClassicTag = Settings.IsOneVsOneTest;
            allOutAt = -1;
            Paused = false;
            HostLost = false;
            PauseReason = "";
            startedAsHost = game.IsMaster;
            TaggerTeam = teams.TaggersTeam;
            RunnerTeam = teams.RunnersTeam;
            stunner.Prepare();

            foreach (var t in taggers)
                Taggers.Add(new TaggerEntry { Key = t.Key, Name = t.Name, Actor = t.Actor, Color = t.Color });

            bool prepared = authority.PrepareRound();
            if (prepared) game.Refresh();

            foreach (var r in runners)
            {
                bool infected = r.Infected;
                var e = new RunnerEntry
                {
                    Key = r.Key, Name = r.Name, UserId = r.UserId, Actor = r.Actor, Color = r.Color,
                    WasInfected = infected
                };
                if (infected)
                    e.Flags.Add("was already infected at the start - the game can't tag them again, use Eliminate if they get caught");
                Runners.Add(e);
            }
            lastInfectedCount = game.Infected.Count;

            RoundNumber++;
            startedAt = DateTime.Now;
            releaseAt = Now + LeadIn + HeadStart;
            lastLeadInNumber = 3;
            State = RoundState.Starting;

            Log($"ROUND {RoundNumber} - {teams.NameOf(TaggerTeam)} tagging ({Taggers.Count}) vs {teams.NameOf(RunnerTeam)} running ({Runners.Count}), " +
                $"time cap {TimeFmt.Clock(Cap)}" + (forcedReason != null ? $"  [FORCED START: {forcedReason}]" : ""),
                forcedReason != null ? Colors.Warn : Colors.Info);
            if (!startedAsHost && Settings.RequireMasterClient.Value)
                Log("Note: you are not the host for this round.", Colors.Warn);

            announcer.StopAll();
            announcer.Say("3.", "3", Priority.Critical);
        }

        public void Pause(string reason)
        {
            if (!IsActive || Paused) return;
            Paused = true;
            pausedAt = Now;
            PauseReason = reason;
            authority.ApplyRoundInfection();
            Log("ROUND PAUSED - " + reason, Colors.Warn);
            announcer.Alert();
        }

        public void Resume()
        {
            if (!IsActive || !Paused) return;
            double paused = Now - pausedAt;
            releaseAt += paused;
            foreach (var t in Taggers)
                if (t.OffBenchSince >= 0) t.OffBenchSince += paused;
            Paused = false;
            PauseReason = "";
            authority.ApplyRoundInfection();
            Log($"Round resumed (paused {paused:0.0}s).", Colors.Info);
        }

        public void Tick()
        {
            if (!IsActive) return;

            if (!game.InRoom)
            {
                Log("You left the room - round aborted, no result saved.", Colors.Bad);
                stunner.ReleaseAll();
                State = RoundState.Idle;
                Paused = false;
                return;
            }

            if (startedAsHost && !game.IsMaster && !HostLost)
            {
                HostLost = true;
                Pause($"you are no longer the host (Master Client) - {game.MasterName} is. Results may be wrong from here");
            }
            else if (HostLost && game.IsMaster)
            {
                HostLost = false;
                Log("You are the host again.", Colors.Good);
            }

            if (Paused)
            {
                IgnoreTagsWhilePaused();
                return;
            }

            if (State == RoundState.Starting)
            {
                int n = LeadInNumber;
                if (n < lastLeadInNumber && n >= 1)
                {
                    lastLeadInNumber = n;
                    announcer.Say(n + ".", n.ToString(CultureInfo.InvariantCulture), Priority.Critical);
                }
                CheckTaggerFalseStarts(atRelease: false);
                CheckRunnerFalseStarts();
                if (Clock >= -HeadStart)
                {
                    State = RoundState.Countdown;
                    lastTagCountdown = 4;
                    Log("RUNNERS, RUN!", teams.DisplayColor(Team.Runners));
                    announcer.Say("Runners, run!", "runners_run", Priority.Critical);
                }
            }

            if (State == RoundState.Countdown)
            {
                CheckTaggerFalseStarts(atRelease: false);
                int n = (int)Math.Ceiling(-Clock);
                if (n >= 1 && n <= 3 && n < lastTagCountdown)
                {
                    lastTagCountdown = n;
                    announcer.Say(n + ".", n.ToString(CultureInfo.InvariantCulture), Priority.Critical);
                }
                if (Clock >= 0)
                {
                    CheckTaggerFalseStarts(atRelease: true);
                    if (Taggers.Any(IsStunHeld)) Log("Taggers unfrozen.", Colors.Dim);
                    stunner.ReleaseAll();
                    State = RoundState.Running;
                    authority.OnRelease();
                    Log("TAGGERS, TAG!", teams.DisplayColor(Team.Taggers));
                    announcer.Say("Taggers, tag!", "taggers_tag", Priority.Critical);
                }
            }

            ProcessTagBroadcasts();

            if (State == RoundState.Running && Clock >= Cap)
            {
                if (AliveCount == 0) Finish(AllOutReason(), byTimeCap: false, byReferee: false);
                else Finish("Time cap reached", byTimeCap: true, byReferee: false);
                return;
            }

            if (game.LastError == null)
            {
                CheckTaggersPresent();
                DetectFromInfectionList(Clock);
            }

            if (AliveCount == 0)
            {
                if (allOutAt < 0) allOutAt = Now;
                else if (Now - allOutAt >= 0.6) Finish(AllOutReason(), byTimeCap: false, byReferee: false);
            }
            else allOutAt = -1;
        }

        private string AllOutReason()
        {
            int left = Runners.Count(r => r.Status == RunnerStatus.Disconnected);
            if (left == 0) return "All runners tagged";
            if (left == Runners.Count) return "All runners left the room";
            return "No runners left in play";
        }

        private void CheckTaggerFalseStarts(bool atRelease)
        {
            double now = Now;
            bool canStun = Settings.FreezeTaggers.Value && stunner.Available;
            if (!canStun && Taggers.Any(IsStunHeld))
            {
                stunner.ReleaseAll();
                Log("Taggers unfrozen (freeze switched off).", Colors.Dim);
            }
            foreach (var t in Taggers)
            {
                var p = game.Get(t.Actor);
                if (p == null || p.Key != t.Key) continue;
                if (!atRelease)
                {
                    if (p.OnBench)
                    {
                        if (stunner.IsHeld(t.Actor))
                        {
                            stunner.Release(t.Actor);
                            Log($"Tagger {t.Name} is back on the bench - unfrozen.", Colors.Dim);
                        }
                        t.OnBenchSeen = true;
                        t.OffBenchSince = -1;
                        t.OffBenchFlagged = false;
                        continue;
                    }
                    if (t.OffBenchSince < 0) t.OffBenchSince = now;
                }
                bool due = t.OffBenchSince >= 0 && !t.OffBenchFlagged && (atRelease || now - t.OffBenchSince >= 0.3);
                if (!due) continue;
                t.OffBenchFlagged = true;
                t.FalseStarts++;
                double early = Math.Max(0.0, releaseAt - t.OffBenchSince);
                bool stun = !atRelease && t.OnBenchSeen && canStun;
                if (stun && !stunner.IsHeld(t.Actor)) stunner.Hold(t.Actor);
                Log($"FALSE START: tagger {t.Name} left the bench {TimeFmt.Seconds(early)}s before release." +
                    (stun ? " Frozen until 'Taggers, TAG!' or until they're back on the bench." : ""), Colors.Bad);
                announcer.Alert();
            }
        }

        private void CheckRunnerFalseStarts()
        {
            double now = Now;
            foreach (var r in Runners)
            {
                if (!r.IsAlive || r.FalseStart) continue;
                var p = game.Get(r.Actor);
                if (p == null || p.Key != r.Key) continue;
                if (p.UnderGazebo)
                {
                    r.InPositionSeen = true;
                    r.OutSince = -1;
                    continue;
                }
                if (!r.InPositionSeen) continue;
                if (r.OutSince < 0) r.OutSince = now;
                if (now - r.OutSince < 0.3) continue;

                r.FalseStart = true;
                bool stun = Settings.StunEarlyRunners.Value && stunner.Available;
                if (stun) stunner.StunOnce(r.Actor);
                double early = Math.Max(0.0, releaseAt - HeadStart - r.OutSince);
                r.Flags.Add($"false start: left the gazebo {TimeFmt.Seconds(early)}s before 'Runners, RUN!'" + (stun ? " (stunned)" : ""));
                Log($"FALSE START: runner {r.Name} left the gazebo {TimeFmt.Seconds(early)}s before 'Runners, RUN!'." +
                    (stun ? " Stunned." : ""), Colors.Bad);
                announcer.Alert();
            }
        }

        private void CheckTaggersPresent()
        {
            foreach (var t in Taggers)
            {
                if (t.Left || Present(t.Key, t.Actor)) continue;
                t.Left = true;
                Log($"Tagger {t.Name} left the room.", Colors.Warn);
            }
        }

        private void IgnoreTagsWhilePaused()
        {
            while (TagHook.Events.Count > 0)
            {
                var ev = TagHook.Events.Dequeue();
                var r = Runners.FirstOrDefault(x => x.Actor == ev.Tagged && x.IsAlive);
                if (r != null) Log($"{r.Name} was tagged by {game.NameOf(ev.Tagger)} while paused - NOT counted. Use Eliminate if it should count.", Colors.Warn);
            }
            foreach (var r in Runners)
            {
                if (!Present(r.Key, r.Actor)) continue;
                bool infected = game.Infected.Contains(r.Actor);
                if (r.IsAlive && infected && !r.WasInfected && !r.PauseFlagged)
                {
                    r.PauseFlagged = true;
                    r.Flags.Add("infected while the round was paused - not counted; the game now shows them infected, use Eliminate if they get caught");
                }
                r.WasInfected = infected;
            }
        }

        private void ProcessTagBroadcasts()
        {
            while (TagHook.Events.Count > 0)
            {
                var ev = TagHook.Events.Dequeue();
                double tagClock = ev.TagRealtime - releaseAt;

                var early = Runners.FirstOrDefault(x => x.Actor == ev.Tagged && x.TimedFromList && x.Status == RunnerStatus.Tagged);
                if (early != null)
                {
                    early.TimedFromList = false;
                    double exact = Math.Max(0.0, Math.Min(tagClock, Cap));
                    if (exact < early.OutTime && early.OutTime - exact < 1.5) early.OutTime = exact;
                    continue;
                }

                var r = Runners.FirstOrDefault(x => x.Actor == ev.Tagged && x.IsAlive && Present(x.Key, x.Actor));
                if (r == null) continue;
                if (tagClock >= Cap) continue;

                var flags = new List<string>();
                string by = ClassifyTagger(r, ev.Tagger, tagClock, flags, out string byKey);
                if (tagClock < 0) flags.Add("tagged during the head start (counts as 0:00.00)");
                MarkOut(r, RunnerStatus.Tagged, tagClock, by, flags.ToArray());
                r.TaggedByKey = byKey;
                r.WasInfected = true;
            }
        }

        private void DetectFromInfectionList(double clock)
        {
            foreach (var r in Runners)
                if (r.IsAlive && !Present(r.Key, r.Actor))
                    MarkOut(r, RunnerStatus.Disconnected, clock, "", "left the room");

            if (!game.TagsDetectable(allowClassicTag))
            {
                if (!warnedNoInfection)
                {
                    warnedNoInfection = true;
                    Log($"WARNING: Infection is not running ({game.ModeName}) - tags can't be detected until it is. Use Eliminate if needed.", Colors.Warn);
                }
                return;
            }
            warnedNoInfection = false;

            int infectedCount = game.Infected.Count;
            if (lastInfectedCount >= 2 && infectedCount == 0)
                Log("The game reset its Infection round (everyone un-infected). Already-eliminated runners stay out.", Colors.Warn);
            lastInfectedCount = infectedCount;

            foreach (var r in Runners)
            {
                if (!Present(r.Key, r.Actor)) continue;
                bool infected = game.Infected.Contains(r.Actor);
                if (r.IsAlive && infected && !r.WasInfected)
                {
                    var flags = new List<string>();
                    string by, byKey = "";
                    if (game.TryGetTagger(r.Actor, out int taggerActor))
                    {
                        by = ClassifyTagger(r, taggerActor, clock, flags, out byKey);
                    }
                    else
                    {
                        by = "?";
                        flags.Add(TagHook.Installed
                            ? "infected by the game without a tag (Infection reset?) - Revive if this wasn't a real tag"
                            : "tagger unknown");
                    }
                    if (clock < 0) flags.Add("tagged during the head start (counts as 0:00.00)");
                    MarkOut(r, RunnerStatus.Tagged, clock, by, flags.ToArray());
                    r.TaggedByKey = byKey;
                    r.TimedFromList = TagHook.Installed;
                }
                r.WasInfected = infected;
            }
        }

        private string ClassifyTagger(RunnerEntry r, int taggerActor, double clock, List<string> flags, out string taggerKeyOut)
        {
            taggerKeyOut = "";
            if (taggerActor == r.Actor)
            {
                flags.Add("infected by the environment, not by a player");
                return "(environment)";
            }
            string taggerKey = game.Get(taggerActor)?.Key;
            var t = Taggers.FirstOrDefault(x => x.Actor == taggerActor || (taggerKey != null && x.Key == taggerKey));
            if (t != null)
            {
                t.Tags++;
                taggerKeyOut = t.Key;
                if (clock < 0) flags.Add($"tagger {t.Name} tagged before being released");
                return t.Name;
            }
            var other = Runners.FirstOrDefault(x => x.Actor == taggerActor || (taggerKey != null && x.Key == taggerKey));
            if (other != null)
            {
                flags.Add($"INTERFERENCE: tagged by eliminated runner {other.Name}");
                return other.Name;
            }
            string name = game.NameOf(taggerActor);
            if (taggerActor == game.LocalActor)
            {
                flags.Add("tagged by the referee");
                return name + " (referee)";
            }
            flags.Add($"tagged by {name}, who is not on the Taggers team");
            return name;
        }

        public static bool IsSeriousFlag(string f) =>
            f.StartsWith("INTERFERENCE") || f.Contains("not on the Taggers") || f.Contains("before being released") ||
            f.Contains("without a tag") || f.Contains("by the referee") || f.Contains("while the round was paused");

        private void MarkOut(RunnerEntry r, RunnerStatus status, double clock, string by, params string[] flags)
        {
            r.Status = status;
            r.OutTime = Math.Max(0.0, Math.Min(clock, Cap));
            r.TaggedBy = by ?? "";
            r.Flags.AddRange(flags);

            bool serious = flags.Any(IsSeriousFlag);
            string what = status == RunnerStatus.Tagged ? $"TAGGED by {r.TaggedBy}" :
                          status == RunnerStatus.Disconnected ? "LEFT THE ROOM" : "ELIMINATED by referee";
            string extra = flags.Length > 0 ? "  (" + string.Join("; ", flags) + ")" : "";
            Log($"{r.Name} {what} - survived {TimeFmt.Clock(r.OutTime)}{extra}", serious ? Colors.Bad : Colors.Warn);
            announcer.TagSound();
        }

        public void EliminateManually(RunnerEntry r)
        {
            if (!IsActive || r == null || !r.IsAlive) return;
            MarkOut(r, RunnerStatus.EliminatedByReferee, Clock, "(referee)", "eliminated manually by the referee");
        }

        public void Revive(RunnerEntry r)
        {
            if (!IsActive || r == null) return;
            if (r.Status != RunnerStatus.Tagged && r.Status != RunnerStatus.EliminatedByReferee) return;
            bool infectedNow;
            if (authority.Active)
            {
                authority.ReviveRunner(r.Actor);
                infectedNow = false;
            }
            else infectedNow = game.Infected.Contains(r.Actor);
            r.WasInfected = infectedNow;
            r.Flags.Add($"revived by the referee (was out at {TimeFmt.Clock(r.OutTime)})" +
                        (infectedNow ? " - the game still shows them as infected, use Eliminate if they get caught" : ""));
            r.Status = RunnerStatus.Alive;
            r.TaggedBy = r.TaggedByKey = "";
            r.TimedFromList = false;
            Log($"{r.Name} revived by the referee - timer continues.", Colors.Info);
        }

        public void EndNow()
        {
            if (!IsActive) return;
            if (Paused) Resume();
            Finish("Ended early by the referee", byTimeCap: false, byReferee: true);
        }

        public void Abort(bool quiet = false)
        {
            if (!IsActive) return;
            Log($"Round {RoundNumber} aborted by the referee - no result saved.", Colors.Warn);
            stunner.ReleaseAll();
            State = RoundState.Idle;
            Paused = false;
            RoundNumber--;
            announcer.StopAll();
            if (!quiet) announcer.Alert();
        }

        public void ClearForNewLobby()
        {
            stunner.Prepare();
            TagHook.Events.Clear();
            Runners.Clear();
            Taggers.Clear();
            History.Clear();
            Score.Reset();
            State = RoundState.Idle;
            RoundNumber = 0;
            Paused = false;
            HostLost = false;
            PauseReason = "";
            TaggerTeam = TeamId.None;
            RunnerTeam = TeamId.None;
            finishedClock = 0;
            allOutAt = -1;
            warnedNoInfection = false;
            startedAsHost = false;
            lastInfectedCount = 0;
        }

        public void ResetRound()
        {
            if (!IsActive) return;
            Log($"ROUND {RoundNumber} RESET by the referee - no result saved. Same teams and roles replay it from the gazebo.", Colors.Warn);
            stunner.ReleaseAll();
            State = RoundState.Idle;
            Paused = false;
            HostLost = false;
            PauseReason = "";
            RoundNumber--;
            announcer.StopAll();
            announcer.Say("Round reset. Everyone, return to Gazebo.", "round_reset", Priority.Critical);
        }

        public void NextRound(bool quiet = false)
        {
            if (State != RoundState.Finished) return;
            State = RoundState.Idle;
            if (!quiet && !Score.GameOver) announcer.Say("All players, return to Gazebo.", "return_gazebo");
        }

        private void Finish(string reason, bool byTimeCap, bool byReferee)
        {
            stunner.ReleaseAll();
            double clock = Math.Max(0.0, Math.Min(Clock, Cap));
            foreach (var r in Runners.Where(r => r.IsAlive))
            {
                if (byTimeCap)
                {
                    r.Status = RunnerStatus.Survived;
                    r.OutTime = Cap;
                }
                else if (byReferee)
                {
                    r.Status = RunnerStatus.StoppedByReferee;
                    r.OutTime = clock;
                }
            }
            finishedClock = byTimeCap ? Cap : byReferee || Runners.Count == 0 ? clock : Runners.Max(r => r.OutTime);
            State = RoundState.Finished;
            Paused = false;

            var result = BuildResult(reason);
            History.Add(result);
            Log($"ROUND {result.Number} OVER - {reason}. Runners lasted {TimeFmt.Clock(result.Duration)}. {result.WinnerLine}", Colors.Good);

            result.Point = Score.Record(result, out bool replaced);
            string pointLine = PointLine(result, replaced);
            Log(pointLine, result.Point != null ? Colors.Good : Colors.Info);
            result.Summary += Environment.NewLine + pointLine;

            announcer.StopAll();
            if (byTimeCap) announcer.Say("Time is up!", "time_up");
            else if (reason == "All runners tagged") announcer.Say("All runners have been tagged!", "all_tagged");
            announcer.Say("Round complete. " + SpokenTime(result.Duration), fallbackKey: "round_complete");
            if (result.Point != null)
                announcer.Say(result.Point.Replay ? "Too close. Both runs are reset." : $"{teams.NameOf(result.Point.Winner)} gets the point.");
            bool gameWon = result.Point != null && !result.Point.Replay && SayGameState(true);

            if (Settings.AutoRotate.Value && !gameWon)
            {
                teams.SwapRoles();
                announcer.Say("Teams are switching.", "teams_switching");
                announcer.Say(RoleLine(teams.TaggersTeam, "Taggers"));
                announcer.Say(RoleLine(teams.RunnersTeam, "Runners"));
                announcer.Say("Prepare for the next round.", "prepare_next");
            }
        }

        public bool SayGameState(bool matchPoint)
        {
            var w = Score.GameWinner;
            if (w != TeamId.None)
            {
                var l = w == TeamId.A ? TeamId.B : TeamId.A;
                Log($"GAME OVER - {teams.NameOf(w)} wins the game {Score.Of(w)}-{Score.Of(l)}.", Colors.Good);
                announcer.Say(WinLine(w, Score.Of(w), Score.Of(l)));
                return true;
            }
            if (!matchPoint) return false;
            bool a = Score.AtMatchPoint(TeamId.A), b = Score.AtMatchPoint(TeamId.B);
            if (a && b) announcer.Say("Both teams are at match point.");
            else if (a || b) announcer.Say(MatchPointLine(a ? TeamId.A : TeamId.B));
            return false;
        }

        public string WinLine(TeamId w, int won, int lost) => $"{teams.NameOf(w)} wins the game, {won} to {lost}!";

        public string MatchPointLine(TeamId t) => $"{teams.NameOf(t)} is at match point.";

        private string PointLine(RoundResult r, bool replaced)
        {
            string score = $"Score: {teams.NameOf(TeamId.A)} {Score.A} - {Score.B} {teams.NameOf(TeamId.B)}.";
            var p = r.Point;
            if (p == null)
            {
                var other = r.RunnerTeam == TeamId.A ? TeamId.B : TeamId.A;
                return $"{teams.NameOf(r.RunnerTeam)} ran {TimeFmt.Clock(r.Duration)}" +
                       (replaced ? " (replaces their earlier run)" : "") +
                       $" - {teams.NameOf(other)} runs next for the point. {score}";
            }
            string times = $"{teams.NameOf(TeamId.A)} {TimeFmt.Clock(p.TimeA)} vs {teams.NameOf(TeamId.B)} {TimeFmt.Clock(p.TimeB)}";
            return p.Replay
                ? $"TOO CLOSE ({times}, within {p.Margin:0.##} s) - both runs reset, the score stays. {score}"
                : $"POINT to {teams.NameOf(p.Winner)} ({times}). {score}";
        }

        private string RoleLine(TeamId id, string role) => $"{teams.NameOf(id)} is now {role}.";

        public static string SpokenTime(double seconds)
        {
            int total = (int)Math.Round(Math.Max(0, seconds), MidpointRounding.AwayFromZero);
            int minutes = total / 60, rest = total % 60;
            string s = rest + (rest == 1 ? " second" : " seconds");
            if (minutes == 0) return s + ".";
            string m = minutes + (minutes == 1 ? " minute" : " minutes");
            return rest == 0 ? m + "." : m + " " + s + ".";
        }

        private RoundResult BuildResult(string reason)
        {
            var ranked = Runners
                .OrderBy(r => r.Status == RunnerStatus.Disconnected ? 1 : 0)
                .ThenByDescending(r => r.OutTime)
                .ThenBy(r => r.Status == RunnerStatus.Survived ? 0 : 1)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int rank = 0;
            for (int i = 0; i < ranked.Count; i++)
            {
                var cur = ranked[i];
                bool tie = i > 0 &&
                           Math.Abs(cur.OutTime - ranked[i - 1].OutTime) <= 0.0005 &&
                           (cur.Status == RunnerStatus.Disconnected) == (ranked[i - 1].Status == RunnerStatus.Disconnected);
                if (!tie) rank = i + 1;
                cur.Rank = rank;
            }

            var result = new RoundResult
            {
                Number = RoundNumber,
                StartedAt = startedAt,
                Room = game.RoomName,
                Format = $"{Taggers.Count}v{Runners.Count}",
                Cap = Cap,
                HeadStart = HeadStart,
                Duration = finishedClock,
                EndReason = reason,
                TaggerTeam = TaggerTeam,
                RunnerTeam = RunnerTeam,
                TaggerTeamName = teams.NameOf(TaggerTeam),
                RunnerTeamName = teams.NameOf(RunnerTeam),
                Ranked = ranked,
                Taggers = Taggers.ToList(),
                Winners = ranked.Where(r => r.Rank == 1 && r.Status != RunnerStatus.Disconnected).ToList()
            };
            result.Summary = BuildSummary(result);
            return result;
        }

        private static string BuildSummary(RoundResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Gorilla Tag scrim - Round {r.Number} ({r.Format}) - {r.StartedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Taggers: {r.TaggerTeamName} | Runners: {r.RunnerTeamName}");
            sb.AppendLine($"Room {r.Room} | time cap {TimeFmt.Clock(r.Cap)} | head start {r.HeadStart:0.#}s | {r.EndReason} | runners lasted {TimeFmt.Clock(r.Duration)}");
            sb.AppendLine(r.WinnerLine);
            foreach (var e in r.Ranked)
            {
                string how = e.Status == RunnerStatus.Survived ? "SURVIVED" :
                             e.Status == RunnerStatus.Tagged ? $"tagged by {e.TaggedBy}" : e.StatusText.ToLowerInvariant();
                sb.AppendLine($"{e.Rank}. {e.Name} - {TimeFmt.Clock(e.OutTime)} - {how}");
                foreach (var f in e.Flags) sb.AppendLine($"     ! {f}");
            }
            sb.Append("Taggers: ").AppendLine(string.Join(", ", r.Taggers.Select(t =>
                $"{t.Name} ({t.Tags} tag{(t.Tags == 1 ? "" : "s")}{(t.FalseStarts > 0 ? $", {t.FalseStarts} false start" : "")})")));
            return sb.ToString().TrimEnd();
        }
    }
}
