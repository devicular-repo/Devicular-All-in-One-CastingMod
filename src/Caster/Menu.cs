using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DevicularCaster
{
    internal static class Menu
    {

        private static void AddTips()
        {
            Ui.Tips.Clear();
            Ui.Tips["Regular"] = "The game's own camera, the same view as normal play.";
            Ui.Tips["First Person"] = "Shows exactly what your headset sees.";
            Ui.Tips["Freecam"] = "Fly the camera anywhere. W A S D to move, Q / E up and down, Shift is faster, hold the right mouse button or use the arrow keys to look.";
            Ui.Tips["Spectator"] = "Follows the player you pick with 0-9 or in the Players panel. W / S, A / D and Q / E move the camera around them, R puts it back.";
            Ui.Tips["Settings"] = "Lens, smoothing and sound for the caster camera.";
            Ui.Tips["Team Filter"] = "Only show the two teams in the player list and the Players panel, numbered in team order.";
            Ui.Tips["Score Bar"] = "The scoreboard on screen: points, team names, the clock and the time to beat.";
            Ui.Tips["Player List"] = "The numbered list of players in the corner of the screen.";
            Ui.Tips["Name Tags"] = "Names above players' heads, in their color or lava orange when tagged. F3 turns them on or off.";
            Ui.Tips["Hide Overlays"] = "Hides every overlay for a clean feed. F4 does the same.";
            Ui.Tips["Minimap"] = "A map seen from above in a corner of the screen, centered on the player you follow (or on the camera).";
            Ui.Tips["Minimap/Corner"] = "Which corner of the screen the minimap sits in.";
            Ui.Tips["Minimap/Size"] = "Makes the minimap bigger or smaller.";
            Ui.Tips["Minimap/Zoom (m)"] = "How many meters the map shows from its center to its edge. Lower is closer in.";
            Ui.Tips["Minimap/Turn with view"] = "On: the map turns so the way you're looking is always up. Off: north is always up.";
            Ui.Tips["WASD Move"] = "Fly your own gorilla with the keyboard: W A S D to move, Q / E down and up, Shift is faster, hold the right mouse button to look. Only works in private rooms.";
            Ui.Tips["WASD Move/Speed"] = "How fast your gorilla flies, in meters per second. Shift doubles it.";
            Ui.Tips["PC Clicker"] = "Hold left click on a button in the world (the computer, color buttons, room buttons) to press it with your right hand. It has to be within 6 m of your gorilla. Only works in private rooms.";
            Ui.Tips["Streamproof"] = Streamproof.Tip;
            Ui.Tips["Timer"] = "The clock on the score bar. Open it to start, pause or reset it. It follows the Auto Ref while that's on.";
            Ui.Tips["Score"] = "Team names and points on the score bar. They follow the Devicular Auto Ref when that's installed and on.";
            Ui.Tips["Room"] = "Join a room by its code, or leave the one you're in.";
            Ui.Tips["Configs"] = "Save every setting to a file and load them back: camera, overlays, match, replay, client, theme, keys, and the Auto Ref's settings.";
            Ui.Tips["Themes"] = "Palettes, accents, font, particles and glow for the menus, overlays and referee panel.";
            Ui.Tips["Themes/Font"] = "Classic: the game's own font. Modern: Segoe UI. Sport: Bahnschrift, a sporty DIN-style font.";
            Ui.Tips["Themes/Particles"] = "Each palette has its own particles drifting behind the menus: snow, stars, sparks, bubbles, petals or fireflies.";
            Ui.Tips["Themes/Glow & shine"] = "A soft glow in the accent color around the panels, the score bar and whatever is on.";
            Ui.Tips["Themes/Surprise me"] = "Picks a random palette and accent.";
            Ui.Tips["Themes/Default"] = "Back to Snow and Grey, with the Modern font, particles and glow.";
            Ui.Tips["Keybinds"] = "Every keyboard shortcut in one list.";
            Ui.Tips["Auto Ref"] = "Opens the Auto Ref panel: teams, countdowns, tags, scoring and the announcer (this menu closes). Its key does the same.";
            Ui.Tips["Auto Ref/Open referee panel"] = "Opens the Auto Ref panel (this menu closes).";
            Ui.Tips["Auto Ref/Referee on"] = "On: the auto referee runs rounds, controls tags and who's lava, stuns and talks. Off: it's fully off and unhooked from the game; the lobby plays exactly as normal.";
            // The replay column (Recorder, Replay Viewer).
            DevicularReplay.Menu.AddTips();
            Ui.Tips["Freecam/Speed"] = "How fast the free camera flies.";
            Ui.Tips["Freecam/Mouse sensitivity"] = "How fast the camera turns while you hold the right mouse button.";
            Ui.Tips["Spectator/First person  [F2]"] = "Look through the player's eyes instead of from behind them.";
            Ui.Tips["Spectator/Tracking"] = "Head: the camera sits behind their head and turns with it. Body: steadier, it stays behind their body and only turns left and right with it, never up or down.";
            Ui.Tips["Spectator/Auto director"] = "Switches by itself to whoever is in the closest chase.";
            Ui.Tips["Spectator/Switch every (s)"] = "How often the auto director is allowed to switch players.";
            Ui.Tips["Spectator/Reset camera position"] = "Puts the follow camera back in its normal spot, the same as pressing R.";
            Ui.Tips["Settings/Field of view"] = "How wide the camera sees. Higher shows more of the map.";
            Ui.Tips["Settings/Near clipping"] = "How close to the camera things still get drawn. Raise it if you can see inside heads.";
            Ui.Tips["Settings/Move smoothing"] = "Higher makes the camera glide more smoothly but follow a little later.";
            Ui.Tips["Settings/Turn smoothing"] = "Higher makes the camera turn more smoothly but follow a little later.";
            Ui.Tips["Settings/Camera audio"] = "On: you hear the game from where the camera is. Off: from your own gorilla.";
            Ui.Tips["Settings/Rig lerp"] = "How smoothly other players move on your screen. 50 is the game's own value. Lower: they glide more but trail a little behind. Higher: snappier but jittery. 100 is no smoothing. Only your view changes.";
            Ui.Tips["Team Filter/Only show the two teams"] = "Only list the two teams, Team A first and Team B second.";
            Ui.Tips["Team Filter/Clear picks"] = "Takes everyone off the teams you picked.";
            Ui.Tips["Score Bar/Position"] = "Put the score bar at the top or the bottom of the screen.";
            Ui.Tips["Score Bar/Size"] = "Makes the score bar bigger or smaller.";
            Ui.Tips["Score Bar/Style"] = "Classic: score tiles with the team names. Band: a dark band with a diamond in each team's color, the team names (or the diamond's color when a team has no name), the points in the team color, and the clock with the time to beat under it.";
            Ui.Tips["Player List/Team filter (teams only, grouped)"] = "Only list the two teams, Team A first and Team B second.";
            Ui.Tips["Player List/Side"] = "Which side of the screen the list sits on.";
            Ui.Tips["Player List/Size"] = "Makes the player list bigger or smaller.";
            Ui.Tips["Player List/Show ping"] = "Shows your ping above the list.";
            Ui.Tips["Name Tags/Username"] = "Show each player's name.";
            Ui.Tips["Name Tags/FPS"] = "Show the frame rate each player's game reports.";
            Ui.Tips["Name Tags/Platform"] = "Show each player's platform, like STEAM or QUEST. It's a guess when their game doesn't report one.";
            Ui.Tips["Name Tags/Size"] = "Makes the name tags bigger or smaller.";
            Ui.Tips["Name Tags/Show through walls"] = "Keep name tags visible behind walls and trees.";
            Ui.Tips["Timer/Start"] = "Starts the clock (V).";
            Ui.Tips["Timer/Pause"] = "Pauses the clock (B).";
            Ui.Tips["Timer/Reset"] = "Puts the clock back to the head start (N) and keeps the time as the last round's time.";
            Ui.Tips["Timer/Set time to beat"] = "Saves the current time as the time to beat (M).";
            Ui.Tips["Timer/Clear"] = "Removes the time to beat.";
            Ui.Tips["Timer/Head start (s)"] = "How long the clock counts down before it starts timing the runners.";
            Ui.Tips["Timer/Time cap (s)"] = "The clock stops when it reaches this time.";
            Ui.Tips["Timer/Close call (s)"] = "When the clock is within this many seconds of the time to beat, the time to beat turns yellow.";
            Ui.Tips["caster-team-a"] = "The left team's name on the score bar.";
            Ui.Tips["caster-team-b"] = "The right team's name on the score bar.";
            Ui.Tips["Score/+1"] = "Adds a point to this team (F or H).";
            Ui.Tips["Score/-1"] = "Takes a point away from this team (G or J).";
            Ui.Tips["Score/Reset points"] = "Sets both teams back to 0.";
            Ui.Tips["caster-room-code"] = "The room code: capital letters and numbers, up to 10.";
            Ui.Tips["Room/Join"] = "Joins the room with this code.";
            Ui.Tips["Room/Joining..."] = "Joining the room...";
            Ui.Tips["Room/Paste"] = "Pastes a room code from your clipboard.";
            Ui.Tips["Room/Disconnect"] = "Leaves the room you're in.";
            Ui.Tips["caster-config-name"] = "The name to save the camera settings under.";
            Ui.Tips["Configs/Save"] = "Saves all your current settings under this name.";
            Ui.Tips["Configs/Overwrite"] = "Replaces the saved config that has this name.";
            Ui.Tips["Configs/Open folder"] = "Opens the folder your configs are saved in.";
            Ui.Tips["Configs/*"] = "Loads this config.";
        }

        public static void Build(CasterController c)
        {
            var gui = c.Gui;
            gui.WithTips(AddTips);
            gui.Columns.Clear();
            gui.Columns.Add(CameraColumn(c));
            gui.Columns.Add(PlayersColumn(c));
            gui.Columns.Add(OverlaysColumn(c));
            gui.Columns.Add(MatchColumn(c));
            // All-in-One: the replay recorder and viewer live in this menu.
            if (Suite.Replay != null) gui.Columns.Add(DevicularReplay.Menu.ReplayColumn(Suite.Replay));
            gui.Columns.Add(ClientColumn(c));
            gui.Layout();
        }

        private static Module ModeModule(CasterController c, CamMode mode, string keywords, System.Action settings = null) => new Module
        {
            Name = CasterCamera.ModeNames[(int)mode],
            Keywords = "camera mode " + keywords,
            IsOn = () => c.Camera.Mode == mode,
            OnClick = () => c.Camera.SetMode(mode),
            DrawSettings = settings
        };

        private static Column CameraColumn(CasterController c)
        {
            var cam = c.Camera;
            var col = new Column { Title = "Camera" };
            col.Modules.Add(ModeModule(c, CamMode.Regular, "default game"));
            col.Modules.Add(ModeModule(c, CamMode.FirstPerson, "vr headset"));
            col.Modules.Add(ModeModule(c, CamMode.Freecam, "fly spectator free", () =>
            {
                Ui.Slider("Speed", cam.FreeSpeed, 1f, 20f, v => cam.FreeSpeed = v);
                Ui.Slider("Mouse sensitivity", cam.MouseSensitivity, 0.03f, 0.6f, v => cam.MouseSensitivity = v, 100f);
            }));
            col.Modules.Add(ModeModule(c, CamMode.Follow, "spectate player focus director", () =>
            {
                Ui.Check("First person  [F2]", cam.FollowFirstPerson, v => cam.FollowFirstPerson = v);
                Ui.Options("Tracking", cam.TrackHead ? 0 : 1, new[] { "Head", "Body" }, i => cam.TrackHead = i == 0);
                Ui.Check("Auto director", cam.AutoDirector, v => cam.AutoDirector = v);
                if (cam.AutoDirector)
                    Ui.Slider("Switch every (s)", cam.DirectorSeconds, 3f, 20f, v => cam.DirectorSeconds = v);
                Ui.Button("Reset camera position", cam.ResetNudge);
            }));
            col.Modules.Add(new Module
            {
                Name = "Settings",
                Keywords = "lens fov clipping smoothing camera audio listener sound hear rig lerp players smooth jitter",
                DrawSettings = () =>
                {
                    Ui.Slider("Field of view", cam.Fov, 30f, 160f, v => cam.Fov = v);
                    Ui.Slider("Near clipping", cam.NearClip, 0.01f, 0.5f, v => cam.NearClip = v, 100f);
                    Ui.Slider("Move smoothing", cam.MoveSmooth, 0f, 1f, v => cam.MoveSmooth = v, 100f);
                    Ui.Slider("Turn smoothing", cam.TurnSmooth, 0f, 1f, v => cam.TurnSmooth = v, 100f);
                    Ui.Slider("Rig lerp", RigLerp.Shown, 0f, 100f, RigLerp.SetShown);
                    Ui.Check("Camera audio", cam.ListenFromCamera, v => cam.ListenFromCamera = v);
                }
            });
            return col;
        }

        private static Column PlayersColumn(CasterController c)
        {
            var col = new Column { Title = "Players", Empty = "Join a room to see players." };
            var cache = new Dictionary<int, Module>();
            var filter = new Module
            {
                Name = "Team Filter",
                Keywords = "teams only grouped organize",
                IsOn = () => Roster.TeamFilter,
                OnClick = () => Roster.TeamFilter = !Roster.TeamFilter,
                DrawSettings = () =>
                {
                    Ui.Check("Only show the two teams", Roster.TeamFilter, v => Roster.TeamFilter = v);
                    if (Roster.Replaying)
                    {
                        // A replay's teams are the ones it recorded.
                        Ui.Info(Roster.TeamSource);
                        return;
                    }
                    Ui.Section("Teams");
                    if (Roster.Players.Count == 0) Ui.Info("Join a room to pick players.");
                    foreach (var p in Roster.Players.OrderBy(x => x.Name, System.StringComparer.OrdinalIgnoreCase).ToList())
                    {
                        int actor = p.Actor;
                        Ui.TeamPick(Str.Clean(p.Name), p.Color, Roster.PickOf(actor), t => Roster.SetPick(actor, t));
                    }
                    if (Roster.Picks.Count > 0) Ui.Button("Clear picks", Roster.Picks.Clear);
                    else if (Roster.Players.Count > 0) Ui.Info(Roster.TeamSource);
                }
            };
            col.Dynamic = () =>
            {
                var list = new List<Module> { filter };
                foreach (var p in Roster.Ordered)
                {
                    int actor = p.Actor;
                    if (!cache.TryGetValue(actor, out var m))
                    {
                        m = new Module
                        {
                            IsOn = () => c.Camera.IsTarget(Roster.ByActor(actor)),
                            OnClick = () =>
                            {
                                var target = Roster.ByActor(actor);
                                if (target != null) c.Camera.Follow(target);
                            },
                            Swatch = () =>
                            {
                                var who = Roster.ByActor(actor);
                                if (who == null) return null;
                                return who.Team > 0 ? who.TeamColor : who.Color;
                            },
                            Badge = () => Roster.ByActor(actor)?.Tagged == true ? "TAGGER" : ""
                        };
                        cache[actor] = m;
                    }
                    m.Name = (p.Index >= 0 ? p.Index.ToString() : "-") + "  " + Str.Clean(p.Name) + (p.IsLocal ? "  (you)" : "");
                    m.Tip = (p.IsLocal ? "Follow your own gorilla" : "Spectate " + Str.Clean(p.Name)) +
                            (p.Index >= 0 ? $". Press {p.Index} on the keyboard to do the same." : ".") +
                            (p.Team > 0 ? $" Team: {Str.Clean(p.TeamName)}." : "") + (p.Tagged ? " Tagged." : "");
                    m.Keywords = "player follow " + p.Name;
                    list.Add(m);
                }
                return list;
            };
            return col;
        }

        private static Column OverlaysColumn(CasterController c)
        {
            var o = c.Overlays;
            var col = new Column { Title = "Overlays" };
            col.Modules.Add(new Module
            {
                Name = "Score Bar",
                Keywords = "scoreboard timer points",
                IsOn = () => o.ScoreBar,
                OnClick = () => o.ScoreBar = !o.ScoreBar,
                DrawSettings = () =>
                {
                    Ui.Options("Style", o.ScoreBarStyle, Overlays.ScoreBarStyles, i => o.ScoreBarStyle = i);
                    Ui.Options("Position", o.ScoreBarBottom ? 1 : 0, new[] { "Top", "Bottom" }, i => o.ScoreBarBottom = i == 1);
                    Ui.Slider("Size", o.ScoreBarSize, Overlays.ScoreBarMin, Overlays.ScoreBarMax, v => o.ScoreBarSize = v, 100f, "%");
                    if (o.ScoreBarStyle == 1)
                        Ui.Info("Team names come from Match > Score, or the Auto Ref. A team with no name shows its diamond's color, like BLUE.");
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Player List",
                Keywords = "scoreboard players ping",
                IsOn = () => o.PlayerList,
                OnClick = () => o.PlayerList = !o.PlayerList,
                DrawSettings = () =>
                {
                    Ui.Check("Team filter (teams only, grouped)", Roster.TeamFilter, v => Roster.TeamFilter = v);
                    Ui.Options("Side", o.PlayerListRight ? 1 : 0, new[] { "Left", "Right" }, i => o.PlayerListRight = i == 1);
                    Ui.Slider("Size", o.PlayerListSize, 0.7f, 1.4f, v => o.PlayerListSize = v, 100f, "%");
                    Ui.Check("Show ping", o.ShowPing, v => o.ShowPing = v);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Name Tags",
                Keywords = "nametags labels f3",
                IsOn = () => c.Nametags.Enabled,
                OnClick = () => c.Nametags.Enabled = !c.Nametags.Enabled,
                DrawSettings = () =>
                {
                    Ui.Check("Username", c.Nametags.ShowName, v => c.Nametags.ShowName = v);
                    Ui.Check("FPS", c.Nametags.ShowFps, v => c.Nametags.ShowFps = v);
                    Ui.Check("Platform", c.Nametags.ShowPlatform, v => c.Nametags.ShowPlatform = v);
                    Ui.Slider("Size", c.Nametags.Size, 0.5f, 2.5f, v => c.Nametags.Size = v, 100f, "%");
                    Ui.Check("Show through walls", c.Nametags.ThroughWalls, v => c.Nametags.ThroughWalls = v);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Minimap",
                Keywords = "map radar top down overview",
                IsOn = () => c.Minimap.Enabled,
                OnClick = () => c.Minimap.Enabled = !c.Minimap.Enabled,
                DrawSettings = () =>
                {
                    var m = c.Minimap;
                    Ui.Options("Corner", m.Corner >= 2 ? 1 : 0, new[] { "Top", "Bottom" }, i => m.Corner = i * 2 + m.Corner % 2);
                    Ui.Options("", m.Corner % 2, new[] { "Left", "Right" }, i => m.Corner = (m.Corner >= 2 ? 2 : 0) + i);
                    Ui.Slider("Size", m.Size, 0.6f, 2f, v => m.Size = v, 100f, "%");
                    Ui.Slider("Zoom (m)", m.Zoom, 5f, 80f, v => m.Zoom = v);
                    Ui.Check("Turn with view", m.TurnWithView, v => m.TurnWithView = v);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Hide Overlays",
                Keywords = "clean feed f4",
                IsOn = () => o.HideAll,
                OnClick = () => o.HideAll = !o.HideAll
            });
            return col;
        }

        private static Column MatchColumn(CasterController c)
        {
            var clock = c.Clock;
            var col = new Column { Title = "Match" };
            col.Modules.Add(new Module
            {
                Name = "Timer",
                Keywords = "clock time lap start reset",
                IsOn = () => c.View.Running,
                // No click-to-start on the row: clicking it opens the buttons below, so a stray click can't start or
                // stop the clock.
                DrawSettings = () =>
                {
                    if (c.View.Linked)
                    {
                        Ui.Info("Following the auto referee.");
                        return;
                    }
                    Ui.Info("Time " + TimeFmt.Clock(clock.Time) + (double.IsNaN(clock.Lap) ? "" : "   to beat " + TimeFmt.Clock(clock.Lap)));
                    Ui.ButtonRow(("Start", clock.Start), ("Pause", () => clock.Running = false), ("Reset", clock.Reset));
                    Ui.ButtonRow(("Set time to beat", clock.SetLap), ("Clear", clock.ClearLap));
                    Ui.Slider("Head start (s)", clock.HeadStart, 0f, 30f, v => clock.HeadStart = v);
                    Ui.Slider("Time cap (s)", clock.Cap, 30f, 600f, v => clock.Cap = Mathf.Round(v / 5f) * 5f);
                    Ui.Slider("Close call (s)", clock.Margin, 0f, 5f, v => clock.Margin = v);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Score",
                Keywords = "points teams names",
                DrawSettings = () =>
                {
                    if (c.View.Linked)
                    {
                        Ui.Info($"Following the referee: {Str.Clean(c.View.NameA)} {c.View.PointsA} - {c.View.PointsB} {Str.Clean(c.View.NameB)}.");
                        return;
                    }
                    Ui.Section("Left team");
                    clock.NameA = Ui.Field("caster-team-a", clock.NameA, 12).ToUpperInvariant();
                    Ui.ButtonRow(("+1", () => clock.PointsA++), ("-1", () => clock.PointsA = Mathf.Max(0, clock.PointsA - 1)));
                    Ui.Section("Right team");
                    clock.NameB = Ui.Field("caster-team-b", clock.NameB, 12).ToUpperInvariant();
                    Ui.ButtonRow(("+1", () => clock.PointsB++), ("-1", () => clock.PointsB = Mathf.Max(0, clock.PointsB - 1)));
                    GUILayout.Space(4);
                    Ui.Button("Reset points", clock.ResetPoints);
                }
            });
            // All-in-One: the Auto Ref panel opens from here.
            col.Modules.Add(new Module
            {
                Name = "Auto Ref",
                Keywords = "auto ref referee rounds teams score countdown announcer panel",
                OnClick = () =>
                {
                    c.Gui.Visible = false;
                    Suite.OpenReferee();
                },
                Badge = () => Suite.Referee == null ? "UNAVAILABLE" : AutoRef.Settings.RefereeOn ? "ON" : "OFF",
                // Green while it's on; red (like every other badge) while off or when it couldn't start.
                BadgeColor = () => Suite.Referee != null && AutoRef.Settings.RefereeOn ? Dev.Good : Dev.Bad,
                DrawSettings = () =>
                {
                    if (Suite.Referee == null)
                    {
                        Ui.Info("The Auto Ref didn't start. BepInEx's log says why.");
                        return;
                    }
                    var referee = Suite.Referee;
                    Ui.Check("Referee on", AutoRef.Settings.RefereeOn, v => referee.SetAutoReferee(v));
                    Ui.Info(AutoRef.Settings.RefereeOn
                        ? "On: it runs rounds, controls tags and who's lava, stuns and talks, and the score bar follows it."
                        : "Off: fully off. It doesn't touch the lobby at all; tags, lava and voice are all the game's own.");
                    Ui.Button("Open referee panel", () =>
                    {
                        c.Gui.Visible = false;
                        Suite.OpenReferee();
                    });
                }
            });
            return col;
        }

        private static Column ClientColumn(CasterController c)
        {
            var cam = c.Camera;
            var col = new Column { Title = "Client" };
            col.Modules.Add(new Module
            {
                Name = "Room",
                Keywords = "join code disconnect lobby",
                IsOn = () => NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom,
                DrawSettings = () =>
                {
                    bool inRoom = NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom;
                    if (inRoom)
                    {
                        Ui.Button("Disconnect", () => c.Room.Disconnect(), danger: true);
                        return;
                    }
                    c.RoomCode = RoomLink.Sanitize(Ui.Field("caster-room-code", c.RoomCode, RoomLink.MaxLength));
                    Ui.ButtonRow(
                        (c.Room.Busy ? "Joining..." : "Join", () => c.Room.Join(c.RoomCode)),
                        ("Paste", () => c.RoomCode = RoomLink.Sanitize(GUIUtility.systemCopyBuffer)));
                    if (!string.IsNullOrEmpty(c.Room.Status)) Ui.Info(c.Room.Status);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "WASD Move",
                Keywords = "wasd move fly walk keyboard pc movement",
                IsOn = () => c.Pc.Move,
                OnClick = () => c.Pc.SetMove(!c.Pc.Move, cam),
                DrawSettings = () =>
                {
                    Ui.Slider("Speed", c.Pc.Speed, 1f, 20f, v => c.Pc.Speed = v);
                    if (!PcControls.Allowed) Ui.Info("Only works in private rooms.");
                    else if (c.Pc.Move && cam.Mode != CamMode.Regular && cam.Mode != CamMode.FirstPerson) Ui.Info("Paused while the camera is in Freecam or Spectator.");
                }
            });
            col.Modules.Add(new Module
            {
                Name = "PC Clicker",
                Keywords = "click mouse buttons press computer pc",
                IsOn = () => c.Pc.Clicker,
                OnClick = () => c.Pc.Clicker = !c.Pc.Clicker,
                DrawSettings = () =>
                {
                    if (!PcControls.Allowed) Ui.Info("Only works in private rooms.");
                    else if (!string.IsNullOrEmpty(c.Pc.ClickStatus)) Ui.Info(c.Pc.ClickStatus);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Streamproof",
                Keywords = "stream proof obs discord record capture screenshare hide menu",
                IsOn = () => c.Proof.Enabled,
                OnClick = c.Proof.Toggle,
                Badge = () => c.Proof.Error.Length > 0 ? "UNAVAILABLE" : ""
            });
            col.Modules.Add(new Module
            {
                Name = "Configs",
                Keywords = "config configs save load json camera overlays theme settings preset everything",
                DrawSettings = () =>
                {
                    var cf = c.Configs;
                    cf.Name = CasterConfigs.Clean(Ui.Field("caster-config-name", cf.Name, CasterConfigs.MaxName));
                    Ui.ButtonRow((cf.Exists(cf.Name) ? "Overwrite" : "Save", () => cf.Save(c)), ("Open folder", cf.OpenFolder));
                    var files = cf.Files;
                    if (files.Count == 0) Ui.Info("No saved configs yet.");
                    else
                    {
                        Ui.Section("Load");
                        foreach (var f in files.Take(12).ToList())
                        {
                            string n = f;
                            Ui.Button(n, () => cf.Load(c, n));
                        }
                    }
                    if (!string.IsNullOrEmpty(cf.Status)) Ui.Info(cf.Status);
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Themes",
                Keywords = "theme palette color accent dark light",
                DrawSettings = Ui.ThemePicker
            });
            col.Modules.Add(new Module
            {
                Name = "Keybinds",
                Keywords = "hotkeys keys controls",
                DrawSettings = () =>
                {
                    Ui.Key("Menu", Plugin.MenuKey.Value.ToString());
                    Ui.Key("Camera mode", "L / R Ctrl");
                    Ui.Key("First person", "F2");
                    Ui.Key("Name tags", "F3");
                    Ui.Key("Hide overlays", "F4");
                    Ui.Key("Spectate player", "0 - 9");
                    Ui.Key("Points left", "F / G");
                    Ui.Key("Points right", "H / J");
                    Ui.Key("Freecam move", "W A S D Q E");
                    Ui.Key("Freecam look", "RMB / Arrows");
                    Ui.Key("Spectator camera move", "W A S D Q E");
                    Ui.Key("Spectator camera reset", "R");
                    Ui.Key("Timer", "V B N M");
                    Ui.Key("WASD move", "W A S D Q E");
                    Ui.Key("WASD move look", "RMB");
                    Ui.Key("PC clicker", "Left mouse");
                    if (AutoRef.Settings.ToggleKey != null) Ui.Key("Auto Ref panel", AutoRef.Settings.ToggleKey.Value.ToString());
                    if (Suite.Replay != null)
                    {
                        Ui.Key("Replay play / pause", "P");
                        Ui.Key("Replay seek / speed", "[  ]  -  =");
                    }
                }
            });
            return col;
        }
    }
}
