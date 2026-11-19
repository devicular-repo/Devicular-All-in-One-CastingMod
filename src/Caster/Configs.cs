using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DevicularCaster
{
    /// <summary>One setting from a part's settings file (Caster, Auto Ref or Replay), as it's written in that file.</summary>
    [Serializable]
    internal sealed class SavedSetting
    {
        public string part = "";
        public string section = "";
        public string key = "";
        public string value = "";
    }

    /// <summary>
    /// Everything in a saved config (Tab > Client > Configs): the camera, the overlays, name tags and minimap, the
    /// match clock and team names, the client toggles, the theme, the replay options, whether the Auto Ref is on, and
    /// every setting in the Caster, Auto Ref and Replay settings files (keys, announcer, rounds, recording sound...).
    ///
    /// The camera fields keep their old names, so configs saved before (camera only) still load; whatever a config
    /// doesn't have stays as it is.
    /// </summary>
    [Serializable]
    internal sealed class CasterConfig
    {
        public int version = 2;

        // Camera
        public string mode = "";
        public float fieldOfView;
        public float nearClip;
        public float moveSmoothing;
        public float turnSmoothing;
        public float freecamSpeed;
        public float mouseSensitivity;
        public bool trackHead;
        public bool firstPerson;
        public Vector3 followOffset;
        public bool autoDirector;
        public float directorSeconds;
        public bool cameraAudio;
        public float rigLerp;

        // Players
        public bool teamFilter;

        // Overlays
        public bool scoreBar;
        public bool scoreBarBottom;
        public float scoreBarSize;
        public bool playerList;
        public bool playerListRight;
        public float playerListSize;
        public bool showPing;
        public bool nameTags;
        public bool nameTagUsername;
        public bool nameTagFps;
        public bool nameTagPlatform;
        public float nameTagSize;
        public bool nameTagThroughWalls;
        public bool minimap;
        public int minimapCorner;
        public float minimapSize;
        public float minimapZoom;
        public bool minimapTurnWithView;
        public bool hideOverlays;

        // Match
        public float timerHeadStart;
        public float timerCap;
        public float timerCloseCall;
        public string teamNameA = "";
        public string teamNameB = "";
        public bool refereeOn;

        // Replay
        public float replaySamplesPerSecond;
        public bool replayHideLivePlayers;
        public bool replayPlayInBrowser;

        // Client
        public bool wasdMove;
        public float wasdSpeed;
        public bool pcClicker;
        public bool streamproof;
        public string themePalette = "";
        public string themeAccent = "";
        public string themeFont = "";
        public bool themeParticles;
        public bool themeGlow;

        // The Caster, Auto Ref and Replay settings files.
        public List<SavedSetting> settings = new List<SavedSetting>();

        public static CasterConfig From(CasterController c)
        {
            var cam = c.Camera;
            var o = c.Overlays;
            var tags = c.Nametags;
            var map = c.Minimap;
            var clock = c.Clock;
            var replay = Suite.Replay;
            var cfg = new CasterConfig
            {
                mode = cam.Mode.ToString(),
                fieldOfView = cam.Fov,
                nearClip = cam.NearClip,
                moveSmoothing = cam.MoveSmooth,
                turnSmoothing = cam.TurnSmooth,
                freecamSpeed = cam.FreeSpeed,
                mouseSensitivity = cam.MouseSensitivity,
                trackHead = cam.TrackHead,
                firstPerson = cam.FollowFirstPerson,
                followOffset = cam.FollowNudge,
                autoDirector = cam.AutoDirector,
                directorSeconds = cam.DirectorSeconds,
                cameraAudio = cam.ListenFromCamera,
                rigLerp = RigLerp.Custom ? RigLerp.Value : 0f,

                teamFilter = Roster.TeamFilter,

                scoreBar = o.ScoreBar,
                scoreBarBottom = o.ScoreBarBottom,
                scoreBarSize = o.ScoreBarSize,
                playerList = o.PlayerList,
                playerListRight = o.PlayerListRight,
                playerListSize = o.PlayerListSize,
                showPing = o.ShowPing,
                nameTags = tags.Enabled,
                nameTagUsername = tags.ShowName,
                nameTagFps = tags.ShowFps,
                nameTagPlatform = tags.ShowPlatform,
                nameTagSize = tags.Size,
                nameTagThroughWalls = tags.ThroughWalls,
                minimap = map.Enabled,
                minimapCorner = map.Corner,
                minimapSize = map.Size,
                minimapZoom = map.Zoom,
                minimapTurnWithView = map.TurnWithView,
                hideOverlays = o.HideAll,

                timerHeadStart = clock.HeadStart,
                timerCap = clock.Cap,
                timerCloseCall = clock.Margin,
                teamNameA = clock.NameA,
                teamNameB = clock.NameB,
                refereeOn = AutoRef.Settings.AutoReferee != null && AutoRef.Settings.RefereeOn,

                replaySamplesPerSecond = replay != null ? replay.Recorder.Rate : 30f,
                replayHideLivePlayers = replay == null || replay.Player.HideLive,
                replayPlayInBrowser = DevicularReplay.Menu.PlayInBrowser,

                wasdMove = c.Pc.Move,
                wasdSpeed = c.Pc.Speed,
                pcClicker = c.Pc.Clicker,
                streamproof = c.Proof.Enabled,
                themePalette = Dev.Theme.Name,
                themeAccent = Dev.AccentChoice.Name,
                themeFont = Dev.Fonts[Mathf.Clamp(Dev.FontChoice, 0, Dev.Fonts.Length - 1)],
                themeParticles = Dev.ParticlesOn,
                themeGlow = Dev.GlowOn
            };
            foreach (var part in Suite.SettingsFiles)
                foreach (var kv in part.Value)
                    cfg.settings.Add(new SavedSetting
                    {
                        part = part.Key,
                        section = kv.Key.Section,
                        key = kv.Key.Key,
                        value = kv.Value.GetSerializedValue()
                    });
            return cfg;
        }

        /// <summary>Puts everything in this config back. Returns how many settings-file values couldn't be applied.</summary>
        public int ApplyTo(CasterController c)
        {
            // The settings files first, so the menu key, score bar style, Auto Ref rules and recording sound are in
            // place before anything below reads them.
            int failed = 0;
            foreach (var s in settings ?? new List<SavedSetting>())
            {
                if (s == null || !Suite.SettingsFiles.TryGetValue(s.part ?? "", out var file)) continue;
                var def = new ConfigDefinition(s.section ?? "", s.key ?? "");
                if (!file.ContainsKey(def)) continue;
                var entry = file[def];
                try
                {
                    if (entry.GetSerializedValue() != s.value) entry.SetSerializedValue(s.value ?? "");
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log?.LogWarning($"Configs: couldn't set {s.part} [{s.section}] {s.key}: {e.Message}");
                }
            }

            var cam = c.Camera;
            cam.Fov = Mathf.Clamp(fieldOfView, 30f, 160f);
            cam.NearClip = Mathf.Clamp(nearClip, 0.01f, 0.5f);
            cam.MoveSmooth = Mathf.Clamp01(moveSmoothing);
            cam.TurnSmooth = Mathf.Clamp01(turnSmoothing);
            cam.FreeSpeed = Mathf.Clamp(freecamSpeed, 1f, 20f);
            cam.MouseSensitivity = Mathf.Clamp(mouseSensitivity, 0.03f, 0.6f);
            cam.TrackHead = trackHead;
            cam.FollowFirstPerson = firstPerson;
            cam.FollowNudge = new Vector3(
                Mathf.Clamp(followOffset.x, -2.5f, 2.5f),
                Mathf.Clamp(followOffset.y, -1.2f, 2.5f),
                Mathf.Clamp(followOffset.z, -3f, 1.4f));
            cam.AutoDirector = autoDirector;
            cam.DirectorSeconds = Mathf.Clamp(directorSeconds, 3f, 20f);
            cam.ListenFromCamera = cameraAudio;
            if (rigLerp > 0f) RigLerp.Set(rigLerp);
            else if (RigLerp.Custom) RigLerp.ResetToGame();
            if (!string.IsNullOrEmpty(mode) && Enum.TryParse(mode, true, out CamMode m) && m != cam.Mode) cam.SetMode(m);

            Roster.TeamFilter = teamFilter;

            var o = c.Overlays;
            o.ScoreBar = scoreBar;
            o.ScoreBarBottom = scoreBarBottom;
            o.ScoreBarSize = Mathf.Clamp(scoreBarSize, Overlays.ScoreBarMin, Overlays.ScoreBarMax);
            o.PlayerList = playerList;
            o.PlayerListRight = playerListRight;
            o.PlayerListSize = Mathf.Clamp(playerListSize, 0.7f, 1.4f);
            o.ShowPing = showPing;
            o.HideAll = hideOverlays;
            var tags = c.Nametags;
            tags.Enabled = nameTags;
            tags.ShowName = nameTagUsername;
            tags.ShowFps = nameTagFps;
            tags.ShowPlatform = nameTagPlatform;
            tags.Size = Mathf.Clamp(nameTagSize, 0.5f, 2.5f);
            tags.ThroughWalls = nameTagThroughWalls;
            var map = c.Minimap;
            map.Enabled = minimap;
            map.Corner = Mathf.Clamp(minimapCorner, 0, 3);
            map.Size = Mathf.Clamp(minimapSize, 0.6f, 2f);
            map.Zoom = Mathf.Clamp(minimapZoom, 5f, 80f);
            map.TurnWithView = minimapTurnWithView;

            var clock = c.Clock;
            clock.HeadStart = Mathf.Clamp(timerHeadStart, 0f, 30f);
            clock.Cap = Mathf.Clamp(timerCap, 30f, 600f);
            clock.Margin = Mathf.Clamp(timerCloseCall, 0f, 5f);
            if (!string.IsNullOrEmpty(teamNameA)) clock.NameA = teamNameA;
            if (!string.IsNullOrEmpty(teamNameB)) clock.NameB = teamNameB;
            // Through the referee's own switch, so turning it off ends a round properly and unhooks it.
            var referee = Suite.Referee;
            if (referee != null && AutoRef.Settings.AutoReferee != null && refereeOn != AutoRef.Settings.RefereeOn)
                referee.SetAutoReferee(refereeOn);

            var replay = Suite.Replay;
            if (replay != null)
            {
                if (replaySamplesPerSecond > 0f) replay.Recorder.Rate = Mathf.Clamp(Mathf.Round(replaySamplesPerSecond), 10f, 60f);
                replay.Player.HideLive = replayHideLivePlayers;
            }
            DevicularReplay.Menu.PlayInBrowser = replayPlayInBrowser;

            // After the camera mode, so turning WASD move on can take the camera out of Freecam or Spectator itself.
            c.Pc.SetMove(wasdMove, cam);
            if (wasdSpeed > 0f) c.Pc.Speed = Mathf.Clamp(wasdSpeed, 1f, 20f);
            c.Pc.Clicker = pcClicker;
            if (streamproof != c.Proof.Enabled) c.Proof.Toggle();
            if (!string.IsNullOrEmpty(themePalette))
                ThemeStore.SetByName(themePalette, themeAccent, themeFont, themeParticles, themeGlow);
            return failed;
        }
    }

    /// <summary>The saved configs (BepInEx\Devicular\configs\*.json): saving, listing and loading them.</summary>
    internal sealed class CasterConfigs
    {
        public const int MaxName = 32;
        public static string Folder => Path.Combine(Paths.BepInExRootPath, "Devicular", "configs");

        public string Name = "";
        public string Status = "";

        private readonly List<string> files = new List<string>();
        private float nextScan;

        public IReadOnlyList<string> Files
        {
            get
            {
                if (Time.unscaledTime >= nextScan) Scan();
                return files;
            }
        }

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_') sb.Append(ch);
            string s = sb.ToString().TrimStart();
            return s.Length > MaxName ? s.Substring(0, MaxName) : s;
        }

        private static string PathOf(string name) => Path.Combine(Folder, name + ".json");

        public bool Exists(string name)
        {
            name = Clean(name).Trim();
            return name.Length > 0 && files.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        }

        private void Scan()
        {
            nextScan = Time.unscaledTime + 1.5f;
            files.Clear();
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (var f in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc))
                    files.Add(Path.GetFileNameWithoutExtension(f.Name));
            }
            catch
            {
            }
        }

        public void Save(CasterController c)
        {
            string name = Clean(Name).Trim();
            if (name.Length == 0)
            {
                Status = "Type a name for the config first.";
                return;
            }
            try
            {
                Directory.CreateDirectory(Folder);
                var cfg = CasterConfig.From(c);
                File.WriteAllText(PathOf(name), JsonUtility.ToJson(cfg, true));
                Name = name;
                Status = $"Saved \"{name}\": every menu setting, the theme, and {cfg.settings.Count} Caster, Auto Ref and Replay settings.";
            }
            catch (Exception e)
            {
                Status = "Couldn't save: " + e.Message;
            }
            Scan();
        }

        public void Load(CasterController c, string name)
        {
            try
            {
                // Start from what's set now, so anything the file doesn't have (like everything but the camera in an
                // older config) stays as it is.
                var cfg = CasterConfig.From(c);
                cfg.settings.Clear();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(PathOf(name)), cfg);
                int failed = cfg.ApplyTo(c);
                Name = name;
                Status = failed == 0 ? $"Loaded \"{name}\"." : $"Loaded \"{name}\", but {failed} setting(s) couldn't be set (see BepInEx's log).";
            }
            catch (Exception e)
            {
                Status = $"Couldn't load \"{name}\": {e.Message}";
            }
        }

        public void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Status = "Couldn't open the folder: " + e.Message;
            }
        }
    }
}
