using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DevicularReplay
{
    /// <summary>
    /// Devicular Replay: records replays (every player's movement, and with Record sound every game sound and player's
    /// voice in 3D), plays them back in the game with gorilla ghosts, and sends them to scrimreplay.vercel.app.
    /// All-in-One: it has no menu of its own. Its Recorder and Replay Viewer are a column in the Caster menu (Tab), and
    /// the caster camera can follow the replay's gorillas.
    /// It writes nothing to BepInEx's log of its own; problems show in the Recorder's and Replay Viewer's status lines.
    /// </summary>
    internal static class Plugin
    {
        // The same id as the separate DLL, so replays keep their settings file.
        public const string Guid = "com.devicular.gorillatag.replay";
        public const string Name = "Devicular Replay";
        public const string Version = "1.0.0";

        internal static ConfigEntry<bool> ReplaySound;
        internal static ConfigEntry<bool> ReplayMic;

        public static void Start(ConfigFile config)
        {
            bool fresh = !File.Exists(config.ConfigFilePath);
            ReplaySound = config.Bind("Sound", "Record sound", true,
                "Save sound in replays: every game sound and every player's voice with its position. " +
                "Adds roughly 0.5-2 MB a minute, depending on how much is going on.");
            ReplayMic = config.Bind("Sound", "Record my mic", true,
                "Also save your microphone in replays as commentary. It's recorded even when players in the room can't hear you.");
            if (fresh)
            {
                // Settings chosen before the mods were split up.
                Legacy.Copy(ReplaySound, "Replay", "Record sound");
                Legacy.Copy(ReplayMic, "Replay", "Record my mic");
            }
            var go = new GameObject("DevicularReplay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ReplayController>();
        }
    }

    internal sealed class ReplayController : MonoBehaviour
    {
        public readonly ReplayRecorder Recorder = new ReplayRecorder();
        public readonly ReplayPlayer Player = new ReplayPlayer();
        public readonly Timeline Timeline = new Timeline();

        /// <summary>Sound capture for recordings.</summary>
        public SoundHub Sound => SharedSound.Hub;
        public SoundCapture Audio => SharedSound.Capture;

        private void Awake()
        {
            Suite.Replay = this;
            SharedSound.Hub.Add(Recorder);
            // What the caster reads: its score bar shows the replay's time and speed. (Replay.Active also pauses a
            // separately installed Devicular Live Replay's stream while a replay plays.)
            Link.Put("Replay.Active", (Func<bool>)(() => Player.Active));
            Link.Put("Replay.State", (Func<object[]>)Player.PackState);
            Link.Put("Replay.Ghosts", (Func<object[]>)Player.PackGhosts);
        }

        private void Update()
        {
            Ui.Flush();
            ThemeStore.Poll();
            PlayerColors.UpdateColor();
            Roster.Refresh();
            bool inRoom = NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom;
            Hotkeys(Ui.Typing);
            Recorder.Tick(inRoom, Player.Active);
            Player.Tick(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            SharedSound.Tick();
        }

        private void OnDestroy()
        {
            SharedSound.Shutdown();
            Player.Close();
        }

        private void Hotkeys(bool typing)
        {
            // Playback keys only, while a replay plays. The replay controls themselves are in the Caster menu (Tab).
            var kb = Keyboard.current;
            if (kb == null || typing || !Player.Active) return;

            if (kb.pKey.wasPressedThisFrame) Player.TogglePlay();
            if (kb.leftBracketKey.wasPressedThisFrame) Player.Seek(Player.Time - 5f);
            if (kb.rightBracketKey.wasPressedThisFrame) Player.Seek(Player.Time + 5f);
            float[] speeds = { 0.25f, 0.5f, 1f, 2f };
            int si = Array.IndexOf(speeds, Player.Speed);
            if (kb.minusKey.wasPressedThisFrame) Player.Speed = speeds[Mathf.Max(0, (si < 0 ? 2 : si) - 1)];
            if (kb.equalsKey.wasPressedThisFrame) Player.Speed = speeds[Mathf.Min(speeds.Length - 1, (si < 0 ? 2 : si) + 1)];
        }

        private void OnGUI()
        {
            Ui.Init();
            var oldSkin = GUI.skin;
            GUI.skin = Dev.Skin;
            try
            {
                // The caster's Hide Overlays (F4) hides the timeline too.
                if (!Link.Flag("Caster.HideOverlays")) Timeline.Draw(Player);
            }
            finally
            {
                GUI.skin = oldSkin;
            }
        }
    }
}
