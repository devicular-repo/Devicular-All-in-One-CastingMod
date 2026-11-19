using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Devicular
{
    /// <summary>
    /// Devicular All-in-One: Devicular Caster, Auto Ref and Replay in one DLL.
    ///
    /// Everything opens from the Caster menu (Tab): the replay recorder and viewer are columns in it, and its Match
    /// column opens the Auto Ref panel (its own key, F6, still works too). Only one of the two is open at a time.
    ///
    /// Each part keeps the settings file it had as a separate DLL (BepInEx\config\&lt;its id&gt;.cfg), so nothing has to be
    /// set up again. Don't install it next to the separate Caster, Auto Ref or Replay DLLs: BepInEx won't load this one
    /// while any of them is there.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    // Soft dependencies only put the separate DLLs (when they're installed) ahead of this one, so BepInEx has seen
    // them when it checks the incompatibilities below and skips this one, instead of running every part twice.
    [BepInDependency(DevicularCaster.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AutoRef.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(DevicularReplay.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(LegacyGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility(DevicularCaster.Plugin.Guid)]
    [BepInIncompatibility(AutoRef.Plugin.Guid)]
    [BepInIncompatibility(DevicularReplay.Plugin.Guid)]
    [BepInIncompatibility(LegacyGuid)]
    public sealed class AllInOnePlugin : BaseUnityPlugin
    {
        public const string Guid = "com.devicular.gorillatag.allinone";
        public const string Name = "Devicular All-in-One";
        public const string Version = "1.1.0";

        /// <summary>The old single Devicular.dll from before the mods were split.</summary>
        private const string LegacyGuid = "com.devicular.gorillatag";

        private void Awake()
        {
            Mod.Init(Logger, Guid, "Devicular");
            ThemeStore.Load();
            ColorAccept.Install();

            // The referee first, so its score and teams are there when the caster's overlays start.
            Run("Auto Ref", () => AutoRef.Plugin.Start(Part(AutoRef.Plugin.Guid, AutoRef.Plugin.Name), Log(AutoRef.Plugin.Name)));
            Run("Caster", () => DevicularCaster.Plugin.Start(Part(DevicularCaster.Plugin.Guid, DevicularCaster.Plugin.Name), Log(DevicularCaster.Plugin.Name)));
            Run("Replay", () => DevicularReplay.Plugin.Start(Part(DevicularReplay.Plugin.Guid, DevicularReplay.Plugin.Name)));

            Logger.LogInfo($"{Name} {Version} loaded. {DevicularCaster.Plugin.MenuKey?.Value}: the menu (camera, overlays, match, replays). " +
                           $"{AutoRef.Settings.ToggleKey?.Value}: the Auto Ref panel, also in the menu's Match column.");
        }

        private void Run(string part, Action start)
        {
            try
            {
                start();
            }
            catch (Exception e)
            {
                Logger.LogError($"Devicular {part} couldn't start: {e}");
            }
        }

        /// <summary>The settings file a part used as its own DLL, so its settings carry over.</summary>
        private ConfigFile Part(string guid, string name)
        {
            var file = new ConfigFile(Path.Combine(Paths.ConfigPath, guid + ".cfg"), false, new BepInPlugin(guid, name, Version));
            Suite.SettingsFiles[name] = file;
            return file;
        }

        private static ManualLogSource Log(string name) => BepInEx.Logging.Logger.CreateLogSource(name);
    }

    /// <summary>
    /// The parts of the All-in-One the Caster menu reaches into: the replay recorder and viewer, and the Auto Ref panel.
    /// A part that failed to start is null.
    /// </summary>
    internal static class Suite
    {
        public static DevicularReplay.ReplayController Replay;
        public static AutoRef.RefereeController Referee => AutoRef.RefereeController.Instance;

        /// <summary>Each part's settings file, by the part's name (Devicular Caster, Auto Ref, Replay), for the menu's Configs.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, ConfigFile> SettingsFiles =
            new System.Collections.Generic.Dictionary<string, ConfigFile>();

        /// <summary>Opens the Auto Ref panel. The Caster menu closes.</summary>
        public static void OpenReferee()
        {
            var r = Referee;
            if (r != null) r.OpenPanel();
        }
    }

    /// <summary>
    /// The sound capture for replay recordings (every game sound and every player's voice in 3D, plus your mic).
    /// </summary>
    internal static class SharedSound
    {
        public static readonly SoundHub Hub = new SoundHub();
        private static SoundCapture capture;
        private static int frame = -1;

        public static SoundCapture Capture => capture ?? (capture = new SoundCapture(Hub, () => true, () => Hub.WantsMic));

        /// <summary>Main thread, from the Replay controller's LateUpdate: runs once a frame.</summary>
        public static void Tick()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            Hub.Tick();
            Capture.Tick();
        }

        public static void Shutdown() => capture?.Shutdown();
    }
}
