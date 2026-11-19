using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AutoRef
{
    internal sealed class HostAuthority
    {
        public static HostAuthority Current;
        public static bool PatchInstalled;
        public static string PatchStatus = "Tag filter not installed.";

        private readonly RefereeController c;
        private readonly Dictionary<long, float> lastBlockLog = new Dictionary<long, float>();
        private float nextReconcile;
        private bool touched;
        public int BlockedTags;

        public void ResetForNewLobby()
        {
            BlockedTags = 0;
            lastBlockLog.Clear();
            touched = false;
        }

        /// <summary>
        /// Gives the lobby back to the game if the referee had set it up (who's lava, who's it). Only the host can;
        /// if you aren't host yet it's tried again later (it's called every frame while the referee is off).
        /// </summary>
        public void HandBack()
        {
            if (!touched) return;
            var tm = TagManager;
            var ns = NetworkSystem.Instance;
            if (tm == null || ns == null || !ns.InRoom || !ns.IsMasterClient) return;
            touched = false;
            try
            {
                tm.lastTaggedActorNr.Clear();
                if (tm.isCurrentlyTag)
                {
                    var others = new List<NetPlayer>();
                    foreach (var p in ns.AllNetPlayers)
                        if (p != null && !p.IsNull && p.InRoom && !p.IsLocal) others.Add(p);
                    if (others.Count > 0) tm.currentIt = others[UnityEngine.Random.Range(0, others.Count)];
                    tm.lastTag = Time.time;
                    c.Log.Add("--", "Lobby handed back to the game: a random player is it.", Colors.Info);
                }
                else
                {
                    if (InfectionRoundStart != null) InfectionRoundStart.Invoke(tm, null);
                    else
                    {
                        var all = new List<NetPlayer>();
                        foreach (var p in ns.AllNetPlayers)
                            if (p != null && !p.IsNull && p.InRoom) all.Add(p);
                        tm.currentInfected.Clear();
                        tm.waitingToStartNextInfectionGame = false;
                        if (all.Count > 0) tm.AddInfectedPlayer(all[UnityEngine.Random.Range(0, all.Count)]);
                        tm.lastTag = Time.time;
                    }
                    c.Log.Add("--", "Lobby handed back to the game: a new Infection round with a random player infected.", Colors.Info);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hand the lobby back to the game: " + e.Message);
            }
        }

        public HostAuthority(RefereeController controller)
        {
            c = controller;
            Current = this;
        }

        public bool Active => Settings.RefereeOn && c.Game.InRoom && c.Game.IsMaster && Settings.HostAuthority.Value;

        public static void Install(Harmony harmony)
        {
            int ok = 0;
            try
            {
                var report = AccessTools.Method(typeof(GorillaTagManager), nameof(GorillaTagManager.ReportTag), new[] { typeof(NetPlayer), typeof(NetPlayer) });
                if (report != null)
                {
                    harmony.Patch(report, prefix: new HarmonyMethod(typeof(HostAuthority), nameof(ReportTagPrefix)));
                    ok++;
                }
                var canTag = AccessTools.Method(typeof(GorillaTagManager), nameof(GorillaTagManager.LocalCanTag), new[] { typeof(NetPlayer), typeof(NetPlayer) });
                if (canTag != null)
                {
                    harmony.Patch(canTag, prefix: new HarmonyMethod(typeof(HostAuthority), nameof(LocalCanTagPrefix)));
                    ok++;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Host authority patches failed: " + e);
            }
            PatchInstalled = ok == 2;
            PatchStatus = ok == 2 ? "Tag filter ready (works while you are host)." : ok == 1 ? "Tag filter partly installed." : "Tag filter unavailable (game update?).";
            Plugin.Log.LogInfo(PatchStatus);
        }

        private static bool ReportTagPrefix(GorillaTagManager __instance, NetPlayer __0, NetPlayer __1)
        {
            try
            {
                var a = Current;
                if (a == null) return true;
                if (!a.AllowTag(__0, __1)) return false;
                return !a.ApplyInfectionTag(__instance, __0, __1);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Tag check failed, leaving it to the game: " + e.Message);
                return true;
            }
        }

        private static readonly System.Reflection.MethodInfo SetTaggedBy = AccessTools.Method(typeof(VRRig), "SetTaggedBy", new[] { typeof(VRRig) });
        private static readonly System.Reflection.MethodInfo InfectionRoundStart = AccessTools.Method(typeof(GorillaTagManager), "InfectionRoundStart", Type.EmptyTypes);

        private bool ApplyInfectionTag(GorillaTagManager tm, NetPlayer tagged, NetPlayer tagging)
        {
            var r = c.Round;
            if (!Active || tm == null || tm.isCurrentlyTag || tagged == null || tagging == null) return false;
            if (r.State != RoundState.Running || r.Paused || !r.IsRoundTagger(tagging) || !r.IsAliveRunner(tagged)) return false;
            var tagger = Canon(tagging.ActorNumber) ?? tagging;
            var target = Canon(tagged.ActorNumber) ?? tagged;
            if (tm.currentInfected.Exists(x => x != null && x.ActorNumber == target.ActorNumber)) return false;
            touched = true;
            var taggerRig = tm.FindPlayerVRRig(tagger);
            var targetRig = tm.FindPlayerVRRig(target);
            for (int i = tm.currentInfected.Count - 1; i >= 0; i--)
            {
                var p = tm.currentInfected[i];
                if (p != null && p.ActorNumber == tagger.ActorNumber && !ReferenceEquals(p, tagger)) tm.currentInfected[i] = tagger;
            }
            if (!tm.currentInfected.Exists(x => ReferenceEquals(x, tagger))) tm.currentInfected.Add(tagger);
            if (taggerRig != null && targetRig != null)
            {
                try { SetTaggedBy?.Invoke(targetRig, new object[] { taggerRig }); } catch { }
            }
            tm.HandleTagBroadcast(target, tagger);
            GorillaGameModes.GameMode.BroadcastTag(target, tagger);
            tm.AddLastTagged(target, tagger);
            tm.AddInfectedPlayer(target);
            return true;
        }

        private static bool LocalCanTagPrefix(NetPlayer __0, ref bool __result)
        {
            try
            {
                if (Plugin.Enabled && Settings.RefereeOn && __0 != null && __0.IsLocal && Settings.RefereeNeverTags.Value)
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }

        private readonly Dictionary<long, float> lastReportLog = new Dictionary<long, float>();
        private float nextRunnerFixLog;

        private void LogReport(NetPlayer tagged, NetPlayer tagging)
        {
            long key = ((long)tagging.ActorNumber << 32) | (uint)tagged.ActorNumber;
            if (lastReportLog.TryGetValue(key, out var last) && Time.unscaledTime - last < 2f) return;
            lastReportLog[key] = Time.unscaledTime;
            var tm = TagManager;
            if (tm == null) return;
            bool aInf = tm.currentInfected.Exists(x => x != null && x.ActorNumber == tagging.ActorNumber);
            bool bInf = tm.currentInfected.Exists(x => x != null && x.ActorNumber == tagged.ActorNumber);
            double cooldown = tm.lastTag + tm.tagCoolDown - Time.time;
            bool exact = tm.currentInfected.Contains(tagging);
            string clock = c.Round.IsActive ? TimeFmt.Clock(c.Round.Clock) : "--";
            c.Log.Add(clock, $"Tag report {c.Game.NameOf(tagging.ActorNumber)} -> {c.Game.NameOf(tagged.ActorNumber)}: tagger lava {aInf}, target lava {bInf}" +
                             (aInf && !exact ? " (the game's own list didn't match the tagger)" : "") +
                             (tm.isCurrentlyTag ? ", game in tag mode" : "") +
                             (cooldown > 0 ? $", game cooldown {cooldown:0.0}s" : "") + ".", Colors.Dim);
        }

        private bool AllowTag(NetPlayer tagged, NetPlayer tagging)
        {
            if (!Active || tagged == null || tagging == null) return true;
            var r = c.Round;
            if (r.State == RoundState.Running) LogReport(tagged, tagging);
            int referee = c.Game.LocalActor;
            if (tagging.ActorNumber == referee) return Block(tagged, tagging, "the referee can't tag");
            if (tagged.ActorNumber == referee) return Block(tagged, tagging, "the referee can't be tagged");

            if (!r.IsActive)
            {
                if (c.TaggerPlayers.Count > 0 && c.RunnerPlayers.Count > 0)
                    return Block(tagged, tagging, "the round hasn't started", quiet: true);
                return true;
            }
            if (r.Paused) return Block(tagged, tagging, "the round is paused");
            if (r.State != RoundState.Running) return Block(tagged, tagging, "taggers aren't released yet");
            if (!r.IsRoundTagger(tagging))
            {
                if (!Settings.BlockInterference.Value) return true;
                return Block(tagged, tagging, $"{c.Game.NameOf(tagging.ActorNumber)} is not a tagger this round");
            }
            if (!r.IsAliveRunner(tagged)) return Block(tagged, tagging, $"{c.Game.NameOf(tagged.ActorNumber)} is not an alive runner");
            return true;
        }

        private bool Block(NetPlayer tagged, NetPlayer tagging, string reason, bool quiet = false)
        {
            BlockedTags++;
            long key = ((long)tagging.ActorNumber << 32) | (uint)tagged.ActorNumber;
            if (!lastBlockLog.TryGetValue(key, out var last) || Time.unscaledTime - last > (quiet ? 10f : 3f))
            {
                lastBlockLog[key] = Time.unscaledTime;
                string clock = c.Round.IsActive ? TimeFmt.Clock(c.Round.Clock) : "--";
                c.Log.Add(clock, $"Blocked tag {c.Game.NameOf(tagging.ActorNumber)} -> {c.Game.NameOf(tagged.ActorNumber)}: {reason}.", quiet ? Colors.Dim : Colors.Warn);
            }
            return false;
        }

        private static GorillaTagManager TagManager => GorillaGameManager.instance as GorillaTagManager;
        private static NetPlayer Local => NetworkSystem.Instance != null ? NetworkSystem.Instance.LocalPlayer : null;

        public void Tick()
        {
            if (!Active || Time.unscaledTime < nextReconcile) return;
            nextReconcile = Time.unscaledTime + 0.5f;
            if (c.Round.IsActive) ApplyRoundInfection();
            else if (c.TaggerPlayers.Count > 0 && c.RunnerPlayers.Count > 0)
            {
                touched = true;
                ApplySetupInfection();
            }
            else KeepRefereeInfected();
        }

        private static NetPlayer Canon(int actor)
        {
            var ns = NetworkSystem.Instance;
            if (ns == null) return null;
            var p = ns.GetPlayer(actor);
            return p != null && !p.IsNull ? p : null;
        }

        private void ApplySetupInfection()
        {
            var tm = TagManager;
            var local = Local;
            if (tm == null || local == null) return;
            try
            {
                var taggers = new List<int>();
                foreach (var p in c.TaggerPlayers) taggers.Add(p.Actor);
                if (tm.isCurrentlyTag)
                {
                    var it = taggers.Count > 0 ? Canon(taggers[0]) ?? local : local;
                    if (tm.currentIt != it) tm.currentIt = it;
                    return;
                }
                if (!c.Game.InfectionActive) return;
                SetInfected(tm, local, taggers);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not set the setup infection state: " + e.Message);
            }
        }

        private static void SetInfected(GorillaTagManager tm, NetPlayer local, List<int> actors)
        {
            var want = new HashSet<int>(actors) { local.ActorNumber };
            var have = new HashSet<int>();
            for (int i = tm.currentInfected.Count - 1; i >= 0; i--)
            {
                var p = tm.currentInfected[i];
                if (p == null || !want.Contains(p.ActorNumber) || !have.Add(p.ActorNumber))
                {
                    tm.currentInfected.RemoveAt(i);
                    continue;
                }
                var canon = Canon(p.ActorNumber);
                if (canon != null && !ReferenceEquals(canon, p)) tm.currentInfected[i] = canon;
            }
            foreach (int actor in want)
            {
                if (have.Contains(actor)) continue;
                var canon = actor == local.ActorNumber ? local : Canon(actor);
                if (canon != null) tm.currentInfected.Add(canon);
            }
        }

        private List<int> PresentRoundTaggers()
        {
            var list = new List<int>();
            foreach (var t in c.Round.Taggers)
            {
                var p = c.Game.Get(t.Actor);
                if (p != null && p.Key == t.Key) list.Add(t.Actor);
            }
            return list;
        }

        public void OnRelease()
        {
            if (!Active) return;
            var tm = TagManager;
            if (tm == null) return;
            touched = true;
            try
            {
                tm.lastTag = Time.time - tm.tagCoolDown - 1f;
                ApplyRoundInfection();
                var names = new List<string>();
                foreach (var p in tm.currentInfected)
                    if (p != null) names.Add(c.Game.NameOf(p.ActorNumber));
                c.Log.Add(TimeFmt.Clock(c.Round.Clock), "Released. Lava now: " + string.Join(", ", names) + ".", Colors.Dim);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Release setup failed: " + e.Message);
            }
        }

        private void KeepRefereeInfected()
        {
            var tm = TagManager;
            var local = Local;
            if (tm == null || local == null || tm.isCurrentlyTag || !c.Game.InfectionActive) return;
            if (tm.currentInfected.Contains(local)) return;
            touched = true;
            tm.currentInfected.Add(local);
        }

        public bool PrepareRound()
        {
            if (!Active) return false;
            var tm = TagManager;
            if (tm == null) return false;
            touched = true;
            try
            {
                tm.waitingToStartNextInfectionGame = false;
                tm.lastTaggedActorNr.Clear();
                ApplyRoundInfection(forcePreRelease: true);
                c.Log.Add("--", "Host: taggers are lava, runners are clear - tags are refused until 'Taggers, TAG!'.", Colors.Info);
                return true;
            }
            catch (Exception e)
            {
                c.Log.Add("--", "Could not set the infection state: " + e.Message, Colors.Bad);
                return false;
            }
        }

        public void ApplyRoundInfection(bool forcePreRelease = false)
        {
            if (!Active) return;
            var tm = TagManager;
            var local = Local;
            var ns = NetworkSystem.Instance;
            if (tm == null || local == null || ns == null) return;
            touched = true;
            var r = c.Round;
            bool released = !forcePreRelease && r.State == RoundState.Running && !r.Paused;

            try
            {
                if (tm.isCurrentlyTag)
                {
                    NetPlayer it = local;
                    if (!r.Paused && r.AliveCount > 0)
                    {
                        var tagger = FirstPresentTagger();
                        if (tagger != null) it = Canon(tagger.ActorNumber) ?? tagger;
                    }
                    if (tm.currentIt != it) tm.currentIt = it;
                    return;
                }

                if (!released)
                {
                    SetInfected(tm, local, r.Paused && !forcePreRelease ? new List<int>() : PresentRoundTaggers());
                    return;
                }

                var lava = new List<int>();
                foreach (var p in ns.AllNetPlayers)
                {
                    if (p == null || p.IsNull || !p.InRoom) continue;
                    if (r.IsAliveRunner(p))
                    {
                        if (tm.currentInfected.Exists(x => x != null && x.ActorNumber == p.ActorNumber) && Time.unscaledTime >= nextRunnerFixLog)
                        {
                            nextRunnerFixLog = Time.unscaledTime + 3f;
                            c.Log.Add(TimeFmt.Clock(r.Clock), $"{c.Game.NameOf(p.ActorNumber)} is an alive runner but was lava - cleared.", Colors.Warn);
                        }
                        continue;
                    }
                    lava.Add(p.ActorNumber);
                }
                SetInfected(tm, local, lava);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not apply the round's infection state: " + e.Message);
            }
        }

        private NetPlayer FirstPresentTagger()
        {
            foreach (var t in c.Round.Taggers)
            {
                var p = c.Game.Get(t.Actor);
                if (p != null && p.Key == t.Key && p.Net != null) return p.Net;
            }
            return null;
        }

        public void ReviveRunner(int actor)
        {
            if (!Active) return;
            var tm = TagManager;
            if (tm == null) return;
            touched = true;
            try
            {
                if (tm.isCurrentlyTag)
                {
                    var tagger = FirstPresentTagger();
                    if (tagger != null) tm.currentIt = tagger;
                }
                else
                {
                    tm.currentInfected.RemoveAll(p => p == null || p.ActorNumber == actor);
                    var local = Local;
                    if (local != null && !tm.currentInfected.Contains(local)) tm.currentInfected.Add(local);
                }
                tm.lastTaggedActorNr.Remove(actor);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not un-infect the revived runner: " + e.Message);
            }
        }
    }
}
