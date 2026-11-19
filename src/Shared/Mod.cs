using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Devicular
{
    /// <summary>
    /// Which Devicular mod this copy of the shared code is compiled into. Each mod's plugin fills it in first thing,
    /// so shared code can log and name things as that mod.
    /// </summary>
    internal static class Mod
    {
        public static ManualLogSource Log;
        public static string Id = "com.devicular.gorillatag";
        public static string Name = "Devicular";

        public static void Init(ManualLogSource log, string id, string name)
        {
            Log = log;
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// How the Devicular mods find each other while the game runs, without any of them referencing another's DLL.
    /// A mod puts a value (usually a Func that reads its state) under a name; another mod reads it by that name.
    /// Values are plain .NET or Unity types, which every mod shares, so nothing breaks when a mod is missing: the
    /// name just isn't there.
    ///
    /// Names in use:
    /// - Referee.Score (Func&lt;object[]&gt;): the referee's score bar, <see cref="ScoreView.Pack"/>. Null when it has none.
    /// - Referee.Teams (Func&lt;object[]&gt;): the referee's teams, <see cref="Roster.PackTeams"/>. Null when it has none.
    /// - Caster.Teams (Func&lt;object[]&gt;): the teams the caster's player list uses.
    /// - Caster.ShowsRefereeScore (Func&lt;bool&gt;): the caster's score bar is showing the referee's score, so the
    ///   referee doesn't draw its own on top.
    /// - Caster.NameTags (Func&lt;bool&gt;): the caster's name tags are on, so the referee hides its player labels.
    /// - Caster.HideOverlays (Func&lt;bool&gt;): the caster hid every overlay (F4).
    /// - Replay.Active (Func&lt;bool&gt;): a replay is playing in the game.
    /// - Replay.State (Func&lt;object[]&gt;): time, playing, speed and length of that replay.
    /// - Replay.Ghosts (Func&lt;object[]&gt;): the replay's gorillas, each <see cref="CastPlayer.Pack"/>.
    /// - Menu.Open (string): which mod's menu is open, so only one is open at a time.
    /// - Theme (string): "Palette|Accent", the theme every mod uses.
    /// </summary>
    internal static class Link
    {
        private const string Prefix = "Devicular.Link.";

        public static void Put(string name, object value)
        {
            try { AppDomain.CurrentDomain.SetData(Prefix + name, value); }
            catch { }
        }

        public static T Get<T>(string name) where T : class
        {
            try { return AppDomain.CurrentDomain.GetData(Prefix + name) as T; }
            catch { return null; }
        }

        /// <summary>True when some mod has put something under this name.</summary>
        public static bool Has(string name) => Get<object>(name) != null;

        /// <summary>Calls a Func&lt;bool&gt;. False when it isn't there or fails.</summary>
        public static bool Flag(string name)
        {
            var f = Get<Func<bool>>(name);
            if (f == null) return false;
            try { return f(); }
            catch { return false; }
        }

        /// <summary>Calls a Func&lt;object[]&gt;. Null when it isn't there or fails.</summary>
        public static object[] Call(string name)
        {
            var f = Get<Func<object[]>>(name);
            if (f == null) return null;
            try { return f(); }
            catch { return null; }
        }

        internal static T At<T>(object[] a, int i, T fallback = default)
        {
            if (a == null || i < 0 || i >= a.Length) return fallback;
            return a[i] is T v ? v : fallback;
        }
    }

    /// <summary>
    /// Reads settings from the single Devicular.dll these mods were split from (BepInEx\config\com.devicular.gorillatag.cfg),
    /// so a mod's first settings file starts with the values that were already chosen.
    /// </summary>
    internal static class Legacy
    {
        public const string FileName = "com.devicular.gorillatag.cfg";
        private static Dictionary<string, string> values;

        public static string Get(string section, string key)
        {
            if (values == null)
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = Path.Combine(Paths.ConfigPath, FileName);
                    if (File.Exists(path))
                    {
                        string current = "";
                        foreach (var raw in File.ReadAllLines(path))
                        {
                            string line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith("#")) continue;
                            if (line.StartsWith("[") && line.EndsWith("]"))
                            {
                                current = line.Substring(1, line.Length - 2).Trim();
                                continue;
                            }
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            values[current + "/" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                        }
                    }
                }
                catch (Exception e)
                {
                    Mod.Log?.LogWarning("Couldn't read the old Devicular settings: " + e.Message);
                }
            }
            return values.TryGetValue(section + "/" + key, out var v) ? v : null;
        }

        /// <summary>Gives an entry the old file's value for (section, key), if there is one.</summary>
        public static void Copy(ConfigEntryBase entry, string section, string key)
        {
            string v = Get(section, key);
            if (v == null || entry == null) return;
            try { entry.SetSerializedValue(v); }
            catch { }
        }
    }
}
