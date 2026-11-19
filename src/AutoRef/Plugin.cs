using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace AutoRef
{
    /// <summary>
    /// Devicular Auto Ref: runs scrim rounds (teams, countdowns, tags, scoring and the announcer). Its panel opens
    /// with F6, or from the Caster menu's Match column (Tab). In the All-in-One DLL it's started by
    /// <see cref="Devicular.AllInOnePlugin"/> and shares its score and teams with the caster (see <see cref="ScoreFeed"/>).
    ///
    /// Off means fully off: its hooks into the game (tags, the tag filter, the voice recorder) are only in while it's
    /// on (see <see cref="SetHooks"/>), and the lobby is left entirely to the game.
    /// </summary>
    internal static class Plugin
    {
        // The same id as the separate DLL, so the referee keeps its settings file.
        public const string Guid = "com.autoref.gorillatag.scrimreferee";
        public const string Name = "Devicular Auto Ref";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        private static GameObject host;
        private static HarmonyLib.Harmony harmony;

        /// <summary>True once the referee is running.</summary>
        public static bool Enabled => host != null;

        /// <summary>True while the referee's hooks are in the game (only while it's on).</summary>
        public static bool Hooked { get; private set; }

        public static void Start(ConfigFile config, ManualLogSource log)
        {
            Log = log;
            Settings.Bind(config);
            harmony = new HarmonyLib.Harmony(Guid);
            ScoreFeed.Publish();
            host = new GameObject("AutoRef");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<RefereeController>();
            SetHooks(Settings.RefereeOn);
            Log.LogInfo($"{Name} {Version} loaded ({(Settings.RefereeOn ? "on" : "off")}). Press {Settings.ToggleKey.Value} to open the referee panel.");
        }

        /// <summary>
        /// Puts the referee's hooks into the game while it's on, and takes every one of them out while it's off, so an
        /// off referee can't refuse tags, change who's lava, or touch the voice recorder.
        /// </summary>
        public static void SetHooks(bool on)
        {
            if (harmony == null || on == Hooked) return;
            try
            {
                if (on)
                {
                    TagHook.Install(harmony);
                    HostAuthority.Install(harmony);
                    VoiceGuard.Install(harmony);
                }
                else
                {
                    // Give the mic back before the voice hooks come out.
                    try { RefereeController.Instance?.Announcer?.Voice?.Restore(); } catch { }
                    harmony.UnpatchSelf();
                    TagHook.Installed = false;
                    TagHook.Status = "Off: the referee isn't hooked into the game.";
                    HostAuthority.PatchInstalled = false;
                    HostAuthority.PatchStatus = "Off: tags are entirely up to the game.";
                }
                Hooked = on;
                Log.LogInfo(on ? "Auto Ref on: hooked into tags and voice." : "Auto Ref off: all its hooks are out of the game; the lobby is the game's own.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't {(on ? "hook the referee into" : "unhook the referee from")} the game: {e.Message}");
            }
        }
    }
}
