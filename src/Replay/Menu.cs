using System.Collections.Generic;
using UnityEngine;

namespace DevicularReplay
{
    /// <summary>
    /// All-in-One: the replay column of the Caster menu (Tab). There's no separate Replay menu.
    /// </summary>
    internal static class Menu
    {
        /// <summary>The replay column's tooltips, added to the Caster menu's.</summary>
        public static void AddTips()
        {
            Ui.Tips["Recorder"] = "Records every player's movement so you can watch it again later. Open it to start or stop a replay.";
            Ui.Tips["Recorder/Start replay"] = "Starts recording a replay. You need to be in a room.";
            Ui.Tips["Recorder/Stop replay"] = "Stops recording and saves the replay.";
            Ui.Tips["Replay Viewer"] = "Plays back saved recordings, with the players shown as gorillas.";
            Ui.Tips["Recorder/Samples per second"] = "How smooth replays are. Higher makes bigger files.";
            Ui.Tips["Recorder/Record sound"] = "Saves sound in replays: every game sound and player's voice in 3D, so the browser viewer plays them around your camera. Starts with the next recording.";
            Ui.Tips["Recorder/Record my mic"] = "Also saves your microphone as commentary. It's recorded even when players in the room can't hear you.";
            Ui.Tips["Recorder/Open replays folder"] = "Opens the folder your replays are saved in.";
            Ui.Tips["Replay Viewer/Play"] = "Plays the replay (P).";
            Ui.Tips["Replay Viewer/Pause"] = "Pauses the replay (P).";
            Ui.Tips["Replay Viewer/-5s"] = "Jumps back 5 seconds ([).";
            Ui.Tips["Replay Viewer/+5s"] = "Jumps forward 5 seconds (]).";
            Ui.Tips["Replay Viewer/Speed"] = "Playback speed (- and =).";
            Ui.Tips["Replay Viewer/Hide live players"] = "Hides the real players while you watch the replay.";
            Ui.Tips["Replay Viewer/Close replay"] = "Stops the replay and goes back to the live game.";
            Ui.Tips["Replay Viewer/*"] = "Plays this recording. Right-click to delete it.";
            Ui.Tips["Replay Viewer/Play in"] = "Game: plays the replay here with gorilla ghosts; the camera modes and Players column work on them. Browser: opens scrimreplay.vercel.app and shows the replay file in a folder, so you can drag it onto the page.";
            Ui.Tips["Replay Viewer/Open replay site"] = "Opens scrimreplay.vercel.app. Drop any .dvr replay on the page.";
        }

        private static List<System.IO.FileInfo> replayFiles = new List<System.IO.FileInfo>();
        private static float nextReplayScan;
        private static string armedReplay;
        private static float armedUntil;

        /// <summary>Replay Viewer > Play in: Browser (true) or Game. Saved in the menu's configs.</summary>
        public static bool PlayInBrowser;

        public static Column ReplayColumn(ReplayController c)
        {
            var rec = c.Recorder;
            var player = c.Player;
            var col = new Column { Title = "Replay" };
            col.Modules.Add(new Module
            {
                Name = "Recorder",
                Keywords = "record capture save sound",
                IsOn = () => rec.Recording,
                // No click-to-record on the row: clicking it opens the settings, and recording starts and stops
                // only with the button below, so a stray click can't start or end a replay.
                DrawSettings = () =>
                {
                    if (rec.Recording) Ui.Button("Stop replay", rec.Stop, danger: true);
                    else Ui.Button("Start replay", rec.Start);
                    Ui.Slider("Samples per second", rec.Rate, 10f, 60f, v => rec.Rate = Mathf.Round(v));
                    Ui.Check("Record sound", Plugin.ReplaySound.Value, v => Plugin.ReplaySound.Value = v);
                    if (Plugin.ReplaySound.Value)
                        Ui.Check("Record my mic", Plugin.ReplayMic.Value, v => Plugin.ReplayMic.Value = v);
                    Ui.Info(rec.Recording ? "Recording " + TimeFmt.Clock(rec.Elapsed) : rec.Status);
                    if (rec.Recording)
                    {
                        string s = rec.SoundStatus;
                        if (s.Length > 0) Ui.Info(s);
                        if (Plugin.ReplaySound.Value) Ui.Info(c.Audio.Describe());
                    }
                    Ui.Button("Open replays folder", () => Application.OpenURL("file:///" + ReplayFiles.Folder.Replace('\\', '/')));
                }
            });
            col.Modules.Add(new Module
            {
                Name = "Replay Viewer",
                Keywords = "playback watch ghosts timeline browser",
                IsOn = () => player.Active,
                DrawSettings = () =>
                {
                    if (player.Active)
                    {
                        Ui.Info(player.FileName + "   " + TimeFmt.Clock(player.Time) + " / " + TimeFmt.Clock(player.Duration));
                        Ui.ButtonRow((player.Playing ? "Pause" : "Play", player.TogglePlay),
                            ("-5s", () => player.Seek(player.Time - 5f)), ("+5s", () => player.Seek(player.Time + 5f)));
                        float[] speeds = { 0.25f, 0.5f, 1f, 2f };
                        Ui.Options("Speed", System.Array.IndexOf(speeds, player.Speed), new[] { "0.25x", "0.5x", "1x", "2x" }, i => player.Speed = speeds[i]);
                        Ui.Check("Hide live players", player.HideLive, v => player.HideLive = v);
                        Ui.Button("Close replay", player.Close, danger: true);
                    }
                    else if (!PlayInBrowser && !string.IsNullOrEmpty(player.Status)) Ui.Info(player.Status);
                    Ui.Options("Play in", PlayInBrowser ? 1 : 0, new[] { "Game", "Browser" }, i => PlayInBrowser = i == 1);
                    if (PlayInBrowser)
                    {
                        if (!string.IsNullOrEmpty(ReplaySite.Status)) Ui.Info(ReplaySite.Status);
                        Ui.Button("Open replay site", () => ReplaySite.Open(null));
                    }
                    if (Time.unscaledTime >= nextReplayScan)
                    {
                        nextReplayScan = Time.unscaledTime + 3f;
                        replayFiles = ReplayFiles.List();
                    }
                    Ui.Section("Saved replays");
                    if (replayFiles.Count == 0) Ui.Info("Nothing recorded yet.");
                    if (armedReplay != null && Time.unscaledTime > armedUntil) armedReplay = null;
                    for (int i = 0; i < replayFiles.Count && i < 8; i++)
                    {
                        string path = replayFiles[i].FullName;
                        bool armed = path == armedReplay;
                        string label = armed ? "Right-click again to delete" : replayFiles[i].LastWriteTime.ToString("MMM d  HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                        Ui.Button(label, () =>
                        {
                            armedReplay = null;
                            if (PlayInBrowser) ReplaySite.Open(path);
                            else player.Load(path);
                        }, armed, () =>
                        {
                            if (path != armedReplay)
                            {
                                armedReplay = path;
                                armedUntil = Time.unscaledTime + 3f;
                                return;
                            }
                            armedReplay = null;
                            string name = System.IO.Path.GetFileNameWithoutExtension(path);
                            string result = ReplayFiles.Delete(path, out string error)
                                ? $"Deleted {name}. It's in the Recycle Bin."
                                : $"Couldn't delete {name}: {error}";
                            player.Status = result;
                            ReplaySite.Status = result;
                            nextReplayScan = 0f;
                        }, armed ? "Right-click again to delete this replay. It goes to the Recycle Bin." : null);
                    }
                }
            });
            return col;
        }
    }
}
