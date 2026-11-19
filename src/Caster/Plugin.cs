using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GorillaNetworking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DevicularCaster
{
    /// <summary>
    /// Devicular Caster: the caster camera, the overlays (score bar, player list), name tags and the match clock.
    /// Menu: Tab. In the All-in-One DLL it's the only menu: it has the replay recorder and viewer in a Replay column,
    /// and its Match column opens the Auto Ref panel. The score bar and teams follow the referee, and while a replay
    /// plays in the game the camera can follow the replay's gorillas.
    /// </summary>
    internal static class Plugin
    {
        // The same id as the separate DLL, so the caster keeps its settings file.
        public const string Guid = "com.devicular.gorillatag.caster";
        public const string Name = "Devicular Caster";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<Key> MenuKey;
        internal static ConfigEntry<string> ScoreBarStyle;

        public static void Start(ConfigFile config, ManualLogSource log)
        {
            Log = log;
            bool fresh = !File.Exists(config.ConfigFilePath);
            MenuKey = config.Bind("Keys", "Open menu", Key.Tab, "Opens and closes the Devicular Caster menu.");
            ScoreBarStyle = config.Bind("Overlays", "Score bar style", Overlays.ScoreBarStyles[0],
                new ConfigDescription("Classic: score tiles and team names. Band: a dark band with a diamond in each team's color, games won and points.",
                    new AcceptableValueList<string>(Overlays.ScoreBarStyles)));
            if (fresh) Legacy.Copy(MenuKey, "Keys", "Open menu");
            var go = new GameObject("DevicularCaster");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<CasterController>();
            Log.LogInfo($"{Name} {Version} loaded. Press {MenuKey.Value} for the menu.");
        }
    }

    internal sealed class CasterController : MonoBehaviour
    {
        public readonly CasterCamera Camera = new CasterCamera();
        public readonly MatchClock Clock = new MatchClock();
        public readonly Overlays Overlays = new Overlays();
        public readonly ClickGui Gui = new ClickGui("caster", "Casting Mod");
        public readonly RoomLink Room = new RoomLink();
        public readonly ReplayLink Replay = new ReplayLink();
        public readonly Nametags Nametags = new Nametags();
        public readonly CasterConfigs Configs = new CasterConfigs();
        public string RoomCode = "";
        public ScoreView View;

        /// <summary>The score bar shows the Devicular Auto Ref's score.</summary>
        private bool linked;
        public readonly Minimap Minimap = new Minimap();
        public readonly PcControls Pc = new PcControls();
        public readonly Streamproof Proof = new Streamproof();
        private bool menuBuilt;

        private void Awake()
        {
            // The caster's teams are the ones you pick in Players > Team Filter, never the referee's.
            Roster.PickTeams = true;
            Roster.TeamLinks = new string[0];
            // What the other Devicular mods can read from the caster, when they're installed.
            Link.Put("Caster.Teams", (Func<object[]>)Roster.PackTeams);
            Link.Put("Caster.ShowsRefereeScore", (Func<bool>)(() => linked && Overlays.ScoreBar && !Overlays.HideAll));
            Link.Put("Caster.NameTags", (Func<bool>)(() => Nametags.Enabled && !Overlays.HideAll));
            Link.Put("Caster.HideOverlays", (Func<bool>)(() => Overlays.HideAll));
            // The player list shows each player's platform logo, worked out by the name tags.
            Overlays.Platform = Nametags.PlatformOf;
            // Streamproof is shared with the Auto Ref panel, and its tip lives in this menu's tooltips.
            Streamproof.Current = Proof;
            Proof.Tips = Gui.Tips;
        }

        private void Start() => StartCoroutine(EndOfFrames());

        /// <summary>After every frame is drawn (all the menus included), Streamproof copies the hidden menus.</summary>
        private System.Collections.IEnumerator EndOfFrames()
        {
            var wait = new WaitForEndOfFrame();
            bool warned = false;
            while (true)
            {
                yield return wait;
                try
                {
                    Proof.EndFrame();
                }
                catch (Exception e)
                {
                    if (!warned) Plugin.Log?.LogWarning("Streamproof: " + e.Message);
                    warned = true;
                }
            }
        }

        private void Update()
        {
            Ui.Flush();
            ThemeStore.Poll();
            Replay.Sync();
            Roster.Refresh();
            Camera.TryInit();
            bool inRoom = NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom;
            Room.Tick(inRoom);
            if (PhotonNetworkController.Instance != null) PhotonNetworkController.Instance.disableAFKKick = true;

            bool typing = Ui.Typing;
            Hotkeys(typing);
            Clock.Tick(Time.unscaledDeltaTime);

            ScoreView refView = default;
            linked = Clock.UseReferee && ScoreView.Unpack(Link.Call("Referee.Score"), out refView);
            View = linked ? refView : Clock.Manual();
            if (Replay.Active)
            {
                View.Time = Replay.Time;
                View.Lap = double.NaN;
                View.Running = Replay.Playing;
                View.Paused = false;
                View.HeadStart = false;
                View.Finished = false;
                View.Note = "REPLAY  " + Replay.Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
            }

            Vector2 pointer = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var mp = mouse.position.ReadValue();
                pointer = new Vector2(mp.x, Screen.height - mp.y);
            }
            bool overMenu = Gui.Contains(pointer);
            Camera.Update(typing, overMenu);
            Pc.Update(Camera, typing, overMenu);
            RigLerp.Tick();

            if (Gui.Visible)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void LateUpdate()
        {
            Camera.LateUpdate();
            Minimap.LateUpdate(Camera, Overlays.HideAll);
            Nametags.Update(Camera, Overlays.HideAll);
        }

        private void OnDestroy()
        {
            Camera.Shutdown();
            Minimap.Shutdown();
            Pc.Shutdown();
            Proof.Shutdown();
            RigLerp.Restore();
            Nametags.Shutdown();
        }

        private void Hotkeys(bool typing)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            var menuKey = kb[Plugin.MenuKey.Value];
            if (menuKey != null && menuKey.wasPressedThisFrame && (!typing || Gui.Visible)) Gui.Toggle();
            if (typing) return;

            if (kb.leftCtrlKey.wasPressedThisFrame) Camera.CycleMode(-1);
            if (kb.rightCtrlKey.wasPressedThisFrame) Camera.CycleMode(1);
            if (kb.f2Key.wasPressedThisFrame) Camera.FollowFirstPerson = !Camera.FollowFirstPerson;
            if (kb.f3Key.wasPressedThisFrame) Nametags.Enabled = !Nametags.Enabled;
            if (kb.f4Key.wasPressedThisFrame) Overlays.HideAll = !Overlays.HideAll;

            Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            Key[] numpad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };
            for (int i = 0; i < digits.Length; i++)
                if (kb[digits[i]].wasPressedThisFrame || kb[numpad[i]].wasPressedThisFrame) Camera.FollowIndex(i);

            // The clock and points keys only work while the score isn't coming from the referee.
            if (linked) return;
            if (kb.fKey.wasPressedThisFrame) Clock.PointsA++;
            if (kb.gKey.wasPressedThisFrame) Clock.PointsA = Mathf.Max(0, Clock.PointsA - 1);
            if (kb.hKey.wasPressedThisFrame) Clock.PointsB++;
            if (kb.jKey.wasPressedThisFrame) Clock.PointsB = Mathf.Max(0, Clock.PointsB - 1);
            if (kb.vKey.wasPressedThisFrame) Clock.Start();
            if (kb.bKey.wasPressedThisFrame) Clock.Toggle();
            if (kb.nKey.wasPressedThisFrame) Clock.Reset();
            if (kb.mKey.wasPressedThisFrame) Clock.SetLap();
        }

        private void OnGUI()
        {
            Ui.Init();
            if (!menuBuilt)
            {
                menuBuilt = true;
                Menu.Build(this);
            }
            var oldSkin = GUI.skin;
            GUI.skin = Dev.Skin;
            try
            {
                Overlays.Draw(View, Camera);
                Minimap.Draw(Camera);
                // With Streamproof on, the open menu is drawn into the hidden picture instead of the screen.
                if (Gui.Visible) Proof.Layer(Gui.Draw);
            }
            finally
            {
                GUI.skin = oldSkin;
            }
        }
    }

    /// <summary>
    /// A replay playing in the game through the Replay part, as the caster sees it: its time, speed and whether it's
    /// playing, for the score bar. Its gorillas are already in the shared Roster (the Replay part puts them there), so
    /// the camera, name tags and overlays work on them like on live players.
    /// </summary>
    internal sealed class ReplayLink
    {
        public bool Active { get; private set; }
        public float Time { get; private set; }
        public bool Playing { get; private set; }
        public float Speed { get; private set; } = 1f;

        public void Sync()
        {
            var state = Link.Call("Replay.State");
            Active = state != null;
            if (!Active) return;
            Time = Link.At(state, 0, 0f);
            Playing = Link.At(state, 1, false);
            Speed = Link.At(state, 2, 1f);
        }
    }
}
