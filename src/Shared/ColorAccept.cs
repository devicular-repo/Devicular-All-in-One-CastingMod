using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Devicular
{
    /// <summary>
    /// Makes every player's color change show on your screen.
    ///
    /// A player's new color reaches your game as a network message (VRRigSerializer.RPC_InitializeNoobMaterial),
    /// which hands it to the game's color code (VRRig.InitializeNoobMaterial). That code only takes the color if it
    /// thinks the player is at the computer you last used, or at a wardrobe, and a test showed the messages never
    /// getting through it at all. So this works where the message arrives instead: after the game (and any other
    /// mod) has had its turn, and even if their code failed, it shows the color the player sent for their own
    /// gorilla, with the same ownership check the game makes. It only changes what your game shows; nothing is sent.
    ///
    /// Every Devicular mod has this, so it works with any of them installed; only the first one loaded patches the game.
    ///
    /// The BepInEx log gets a "Color: ..." line for each change, and the error if the game's color code failed:
    /// - "sent": a color message arrived, and what happened to it;
    /// - "seen": a gorilla's color really changed on your screen, however it happened.
    /// </summary>
    internal static class ColorAccept
    {
        public static string Status = "Color change fix not installed.";
        public static int Accepted;

        private const int MaxTrace = 400;
        private static int traced, failuresLogged;
        private static Harmony harmony;
        private static System.Reflection.MethodInfo entry;
        // VRRigSerializer is internal to the game, so it's reached by name.
        private static System.Reflection.FieldInfo serializerRig;
        // The color before the message, per message (messages are handled one at a time on the main thread).
        private static Color before;

        public static void Install()
        {
            string owner = Link.Get<string>("ColorAccept");
            if (owner != null)
            {
                Status = "Color changes are accepted from every player (by " + owner + ").";
                return;
            }
            try
            {
                var serializerType = AccessTools.TypeByName("VRRigSerializer");
                entry = serializerType != null ? AccessTools.Method(serializerType, "RPC_InitializeNoobMaterial") : null;
                serializerRig = serializerType != null ? AccessTools.Field(serializerType, "vrrig") : null;
                if (entry == null || serializerRig == null)
                {
                    Status = "Color change fix unavailable (game update?).";
                    Mod.Log?.LogWarning(Status);
                    return;
                }
                harmony = new Harmony(Mod.Id + ".colors");
                Hook();
                Link.Put("ColorAccept", Mod.Name);
                var go = new GameObject("Devicular Color Watch");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<ColorWatch>();
                Status = "Color changes are accepted from every player, wherever they are.";
                Mod.Log?.LogInfo(Status);
            }
            catch (Exception e)
            {
                Status = "Color change fix failed: " + e.Message;
                Mod.Log?.LogWarning(Status);
            }
        }

        private static void Hook()
        {
            harmony.Patch(entry,
                prefix: new HarmonyMethod(typeof(ColorAccept), nameof(Prefix)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(ColorAccept), nameof(Finalizer)) { priority = Priority.Last });
        }

        /// <summary>Puts the hook back if another mod took it off, and says which mods hook the color message.</summary>
        internal static void CheckHook(bool report)
        {
            if (harmony == null || entry == null) return;
            try
            {
                var info = Harmony.GetPatchInfo(entry);
                bool ours = info != null && info.Finalizers.Any(p => p.owner == harmony.Id);
                if (report || !ours)
                {
                    var owners = info == null ? new List<string>() : info.Owners.ToList();
                    Trace("the color message is hooked by: " + (owners.Count == 0 ? "nobody" : string.Join(", ", owners)) + ".");
                }
                if (!ours)
                {
                    Mod.Log?.LogWarning("Color: another mod took the color fix off the game. Putting it back.");
                    Hook();
                }
            }
            catch (Exception e)
            {
                Trace("couldn't check the hook: " + e.Message);
            }
        }

        internal static void Trace(string text)
        {
            if (traced >= MaxTrace) return;
            traced++;
            Mod.Log?.LogInfo("Color: " + text + (traced == MaxTrace ? " (no more color lines this session)" : ""));
        }

        internal static string Code(Color c) => ColorCode.FromColor(c).ToString();

        internal static string NameOf(VRRig rig, int actor = -1)
        {
            try
            {
                NetPlayer p = rig != null ? (rig.Creator ?? rig.OwningNetPlayer) : null;
                if (p != null && !string.IsNullOrEmpty(p.NickName)) return p.NickName + " (#" + p.ActorNumber + ")";
                if (rig != null && !string.IsNullOrEmpty(rig.playerNameVisible)) return rig.playerNameVisible;
            }
            catch
            {
            }
            return actor >= 0 ? "player #" + actor : "a player";
        }

        private static void Prefix(object __instance)
        {
            try
            {
                var rig = serializerRig.GetValue(__instance) as VRRig;
                before = rig != null ? rig.playerColor : Color.clear;
            }
            catch
            {
                before = Color.clear;
            }
        }

        // Runs after the game and every other mod, even if their code threw. The game's own error is passed on unchanged.
        private static Exception Finalizer(Exception __exception, object __instance, float __0, float __1, float __2, Photon.Pun.PhotonMessageInfo __3)
        {
            try
            {
                if (__exception != null && failuresLogged < 3)
                {
                    failuresLogged++;
                    Mod.Log?.LogWarning("Color: the game's color code failed on a color message: " + __exception.GetType().Name + ": " +
                                        __exception.Message + "\n" + FirstLines(__exception.StackTrace, 8));
                }
                var rig = serializerRig.GetValue(__instance) as VRRig;
                int sender = __3.Sender != null ? __3.Sender.ActorNumber : -1;
                var want = new Color(Clamp(__0), Clamp(__1), Clamp(__2), 1f);
                bool found = false;
                if (rig == null)
                {
                    // The message's handler isn't linked to a gorilla: use the sender's own gorilla.
                    rig = RigOf(sender);
                    found = rig != null;
                }
                if (rig == null)
                {
                    Trace($"sent: player #{sender} sent the color {Code(want)}, but they have no gorilla here; ignored.");
                    return __exception;
                }
                if (found) Trace($"sent: the color message from player #{sender} wasn't linked to a gorilla; using theirs.");
                if (rig.isOfflineVRRig) return __exception;
                string name = NameOf(rig, sender);
                // Only a player's own color, sent by that player: the same check the game makes.
                int owner = -1;
                try { owner = NetworkSystem.Instance.GetOwningPlayerID(((Component)__instance).gameObject); }
                catch { }
                if (owner != sender)
                {
                    Trace($"sent: {name}'s gorilla got the color {Code(want)} from player #{sender}, who doesn't own it; ignored.");
                    return __exception;
                }
                bool gameTook = __exception == null && Same(rig.playerColor, want);
                if (!Same(rig.playerColor, want))
                {
                    rig.InitializeNoobMaterialLocal(want.r, want.g, want.b);
                    Accepted++;
                }
                string what = gameTook ? "the game took it."
                    : !Same(rig.playerColor, want) ? "couldn't be applied."
                    : __exception != null ? "the game's color code failed; shown anyway."
                    : "the game had turned it down; shown anyway.";
                Trace($"sent: {name} changed from {Code(before)} to {Code(want)}: {what}");
            }
            catch (Exception e)
            {
                if (failuresLogged < 6)
                {
                    failuresLogged++;
                    Mod.Log?.LogWarning("Color: couldn't apply a player's new color: " + e.GetType().Name + ": " + e.Message + "\n" + FirstLines(e.StackTrace, 8));
                }
            }
            return __exception;
        }

        private static VRRig RigOf(int actor)
        {
            try
            {
                var rigs = VRRigCache.ActiveRigs;
                if (rigs == null) return null;
                for (int i = 0; i < rigs.Count; i++)
                {
                    var r = rigs[i];
                    if (r == null || r.isOfflineVRRig) continue;
                    var p = r.Creator ?? r.OwningNetPlayer;
                    if (p != null && p.ActorNumber == actor) return r;
                }
            }
            catch
            {
            }
            return null;
        }

        private static string FirstLines(string text, int n)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Split('\n');
            return string.Join("\n", lines.Take(n)).TrimEnd();
        }

        internal static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;

        private static float Clamp(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);
    }

    /// <summary>
    /// Logs when any gorilla's color really changes on your screen ("Color: seen ..."), whatever changed it, and makes
    /// sure the color fix stays hooked into the game.
    /// </summary>
    internal sealed class ColorWatch : MonoBehaviour
    {
        private readonly Dictionary<int, Color> seen = new Dictionary<int, Color>();
        private float nextScan, nextHookCheck = 5f;
        private string room = "";

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextHookCheck)
            {
                nextHookCheck = now + 60f;
                ColorAccept.CheckHook(false);
            }
            if (now < nextScan) return;
            nextScan = now + 0.5f;
            var ns = NetworkSystem.Instance;
            if (ns == null || !ns.InRoom)
            {
                room = "";
                seen.Clear();
                return;
            }
            string name = "";
            try { name = ns.RoomName ?? ""; } catch { }
            bool joined = name != room;
            if (joined)
            {
                room = name;
                seen.Clear();
                ColorAccept.CheckHook(true);
            }
            IReadOnlyList<VRRig> rigs;
            try { rigs = VRRigCache.ActiveRigs; }
            catch { return; }
            if (rigs == null) return;
            var local = VRRig.LocalRig;
            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null || rig == local || rig.isOfflineVRRig) continue;
                NetPlayer p = null;
                try { p = rig.Creator ?? rig.OwningNetPlayer; } catch { }
                if (p == null) continue;
                var c = rig.playerColor;
                if (!seen.TryGetValue(p.ActorNumber, out var old))
                {
                    seen[p.ActorNumber] = c;
                    if (joined) ColorAccept.Trace($"seen: {ColorAccept.NameOf(rig, p.ActorNumber)} is {ColorAccept.Code(c)}.");
                    continue;
                }
                if (ColorAccept.Same(old, c)) continue;
                seen[p.ActorNumber] = c;
                ColorAccept.Trace($"seen: {ColorAccept.NameOf(rig, p.ActorNumber)}'s gorilla changed from {ColorAccept.Code(old)} to {ColorAccept.Code(c)}.");
            }
        }

    }
}
