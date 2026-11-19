using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AutoRef
{
    internal sealed class Stunner
    {
        private readonly EventLog log;
        private MethodInfo send;
        private object taggedTime, untagged;
        private bool resolved, warnedNotHost;
        private string error = "";

        private readonly HashSet<int> held = new HashSet<int>();
        private readonly Dictionary<int, object> pending = new Dictionary<int, object>();
        private readonly Dictionary<int, double> lastSent = new Dictionary<int, double>();

        private float minGap = 1f;
        private string limiterInfo = "rate limit not read yet";

        public Stunner(EventLog log)
        {
            this.log = log;
        }

        private static double Now => Time.realtimeSinceStartupAsDouble;

        public static float StunSeconds
        {
            get
            {
                try { return GorillaTagger.Instance != null ? GorillaTagger.Instance.tagCooldown : 0f; }
                catch { return 0f; }
            }
        }

        private double RepeatEvery => Math.Max(minGap, StunSeconds - 0.25f);

        public bool Available => Resolve();

        public string Info
        {
            get
            {
                if (!Resolve()) return "Stun unavailable: " + error;
                float s = StunSeconds;
                string gaps = s > 0 && RepeatEvery > s ? $" The game's rate limit is slower than the stun, so held players get short gaps of about {RepeatEvery - s:0.0} s." : "";
                return $"One stun freezes a player for {s:0.##} s; held players get it again every {RepeatEvery:0.##} s ({limiterInfo}).{gaps}";
            }
        }

        public bool IsHeld(int actor) => held.Contains(actor);

        public void StunOnce(int actor)
        {
            if (!Resolve()) return;
            pending[actor] = taggedTime;
        }

        public void Hold(int actor)
        {
            if (!Resolve()) return;
            held.Add(actor);
        }

        public void Release(int actor)
        {
            if (!held.Remove(actor)) return;
            if (lastSent.TryGetValue(actor, out double t) && Now - t < StunSeconds + 0.5) pending[actor] = untagged;
            else pending.Remove(actor);
        }

        public void ReleaseAll()
        {
            double now = Now;
            foreach (int actor in held)
                if (lastSent.TryGetValue(actor, out double t) && now - t < StunSeconds + 0.5)
                    pending[actor] = untagged;
                else
                    pending.Remove(actor);
            held.Clear();
        }

        public void StopAll()
        {
            double now = Now;
            var actors = new HashSet<int>(held);
            actors.UnionWith(pending.Keys);
            actors.UnionWith(lastSent.Keys);
            held.Clear();
            pending.Clear();
            foreach (int actor in actors)
                if (lastSent.TryGetValue(actor, out double t) && now - t < StunSeconds + 0.5)
                    pending[actor] = untagged;
        }

        public void Prepare()
        {
            held.Clear();
            pending.Clear();
            warnedNotHost = false;
            if (Resolve()) ReadLimiter();
        }

        public void Tick()
        {
            if (held.Count == 0 && pending.Count == 0) return;
            double now = Now;
            double repeat = RepeatEvery;
            foreach (int actor in held)
                if (!pending.ContainsKey(actor) && (!lastSent.TryGetValue(actor, out double t) || now - t >= repeat))
                    pending[actor] = taggedTime;

            if (pending.Count == 0) return;
            foreach (var kv in pending.ToList())
            {
                if (lastSent.TryGetValue(kv.Key, out double t) && now - t < minGap) continue;
                pending.Remove(kv.Key);
                Send(kv.Key, kv.Value);
            }
        }

        private void Send(int actor, object effect)
        {
            var ns = NetworkSystem.Instance;
            if (ns == null || !ns.InRoom) return;
            if (!ns.IsMasterClient)
            {
                if (!warnedNotHost)
                {
                    warnedNotHost = true;
                    log.Add("--", "Can't stun players: only the host can, and you aren't the host right now.", Colors.Warn);
                }
                return;
            }
            var player = ns.GetPlayer(actor);
            if (player == null || player.IsLocal) return;
            try
            {
                send.Invoke(null, new[] { effect, (object)player });
                lastSent[actor] = Now;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Stun failed: " + (e.InnerException ?? e).Message);
            }
        }

        private bool Resolve()
        {
            if (resolved) return send != null;
            resolved = true;
            try
            {
                var rs = AccessTools.TypeByName("RoomSystem");
                var effects = rs?.GetNestedType("StatusEffects", BindingFlags.Public | BindingFlags.NonPublic);
                var method = rs == null ? null : AccessTools.Method(rs, "SendStatusEffectToPlayer");
                if (effects == null || method == null)
                {
                    error = "the game's stun function wasn't found (game update?)";
                    return false;
                }
                taggedTime = Enum.Parse(effects, "TaggedTime");
                untagged = Enum.Parse(effects, "UnTagged");
                send = method;
            }
            catch (Exception e)
            {
                error = e.Message;
                send = null;
            }
            if (send == null) Plugin.Log.LogWarning("Stunner: " + error);
            return send != null;
        }

        private void ReadLimiter()
        {
            int history = 0;
            float cooldown = 0f;
            try
            {
                var rs = AccessTools.TypeByName("RoomSystem");
                var instance = AccessTools.Field(rs, "callbackInstance")?.GetValue(null);
                var settings = instance == null ? null : AccessTools.Field(rs, "roomSettings")?.GetValue(instance);
                var limiter = settings == null ? null : AccessTools.Property(settings.GetType(), "StatusEffectLimiter")?.GetValue(settings);
                if (limiter != null)
                {
                    var lt = limiter.GetType();
                    history = Convert.ToInt32(AccessTools.Field(lt, "callHistoryLength")?.GetValue(limiter) ?? 0);
                    cooldown = Convert.ToSingle(AccessTools.Field(lt, "timeCooldown")?.GetValue(limiter) ?? 0f);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Stunner: could not read the rate limit: " + e.Message);
            }

            if (history >= 1 && cooldown > 0f)
            {
                minGap = Mathf.Clamp(cooldown / history * 1.25f + 0.05f, 0.2f, 3f);
                limiterInfo = $"the game allows {history} per {cooldown:0.##} s";
            }
            else
            {
                minGap = 1f;
                limiterInfo = "rate limit unknown, using 1 per second";
            }
        }
    }
}
