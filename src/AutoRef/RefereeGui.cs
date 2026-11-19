using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AutoRef
{
    internal sealed class RefereeGui
    {
        private const int WindowId = 0x41524546;
        /// <summary>All-in-One: the panel's id in Menu.Open (see Link), so it and the other Devicular menus open one at a time.</summary>
        private const string MenuId = "autoref";
        private static readonly string[] TabNames = { "Match", "Teams" };

        private readonly RefereeController c;
        public bool Visible;

        private Rect win;
        private bool sized;
        private int tab;
        private float tabY = -1f, openedAt;
        private Vector2 scrollMatch, scrollTeams;
        private string nameAText, nameBText;
        private float endArmUntil, resumeArmUntil, resetArmUntil, offArmUntil;
        private bool confirmReset, prewarmed;

        private readonly RoomLink link = new RoomLink();
        private float leaveArmUntil;
        private string sayText = "";
        private readonly List<string> recentSays = new List<string>();
        private const string SayControl = "devicular-say";
        private const int SayMax = 150;

        public RefereeGui(RefereeController controller)
        {
            c = controller;
        }

        private static string W(string text, Color color) => Dev.W(text, color);

        public void OnGUI()
        {
            var e = Event.current;
            // The Caster menu (Tab) opened: this panel steps aside.
            if (Visible && !string.Equals(Link.Get<string>("Menu.Open"), MenuId, StringComparison.Ordinal)) Toggle();
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && e.keyCode == Settings.ToggleKey.Value)
            {
                c.Defer(Toggle);
                e.Use();
            }

            // All-in-One: the referee puts no text on screen (no labels over players, no banners like "RUNNERS, RUN!",
            // no round clock, no scoreboard). The caster's score bar (Tab > Overlays > Score Bar) shows the referee's
            // score, teams, colors and clock, and everything else is in this panel. Only the zone boxes are drawn,
            // when the zones' own visuals couldn't be made.
            if (Settings.RefereeOn) c.Overlay.Draw();

            Dev.Init();
            var oldSkin = GUI.skin;
            var oldColor = GUI.color;
            GUI.skin = Dev.Skin;
            try
            {
                link.Tick(c.Game.InRoom);

                // All-in-One: no corner button while the panel is closed. It opens from the Caster menu (Tab) or its key.
                if (!Visible) return;

                if (!sized)
                {
                    sized = true;
                    float w = Mathf.Min(1060f, Screen.width - 20f), h = Mathf.Min(720f, Screen.height - 20f);
                    win = new Rect(Mathf.Max(10f, (Screen.width - w) / 2f), Mathf.Max(10f, (Screen.height - h) / 2f), w, h);
                }
                if (!prewarmed && e.type == EventType.Repaint)
                {
                    prewarmed = true;
                    Dev.WindowBackground((int)win.width, (int)win.height);
                }

                // All-in-One: the panel shows in or out of a room. There's no "Join a room" window: the Caster menu's
                // Room module joins rooms.
                var proof = DevicularCaster.Streamproof.Current;
                if (proof != null && proof.Enabled)
                {
                    // Streamproof: the panel goes into the hidden picture with the caster menu, so recordings leave it
                    // out. It's drawn as a plain group here, not a Unity window: windows are drawn after OnGUI is
                    // over, too late to be caught. Dragging it is handled below instead.
                    proof.Layer(drawHidden ?? (drawHidden = DrawPanelHidden));
                    DragHidden(e);
                }
                else
                {
                    GUI.color = new Color(1f, 1f, 1f, OpenFade);
                    Dev.Shadow(win);
                    GUI.color = oldColor;
                    win = GUI.Window(WindowId, win, DrawWindow, GUIContent.none, GUIStyle.none);
                }
                win.x = Mathf.Clamp(win.x, 80f - win.width, Screen.width - 80f);
                win.y = Mathf.Clamp(win.y, 0f, Screen.height - 40f);
            }
            finally
            {
                GUI.skin = oldSkin;
                GUI.color = oldColor;
            }
        }

        private float OpenFade => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.18f));

        // ---------- Streamproof: the panel drawn as a group instead of a window ----------

        private Action drawHidden;
        private bool inGroup, draggingHidden;
        private Vector2 dragOffset;

        /// <summary>The panel, its shadow and its contents, at the panel's place on screen.</summary>
        private void DrawPanelHidden()
        {
            var old = GUI.color;
            GUI.color = new Color(old.r, old.g, old.b, OpenFade);
            Dev.Shadow(win);
            GUI.color = old;
            GUI.BeginGroup(win);
            inGroup = true;
            try
            {
                DrawWindow(WindowId);
            }
            finally
            {
                inGroup = false;
                GUI.EndGroup();
            }
        }

        /// <summary>Drag the panel by any empty part of it (what a Unity window does by itself).</summary>
        private void DragHidden(Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && win.Contains(e.mousePosition))
            {
                draggingHidden = true;
                dragOffset = e.mousePosition - win.position;
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
            else if (draggingHidden && e.type == EventType.MouseDrag)
            {
                win.position = e.mousePosition - dragOffset;
                e.Use();
            }
            else if (draggingHidden && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                draggingHidden = false;
                e.Use();
            }
        }

        private void Leave() => link.Disconnect();

        public void ResetForNewLobby()
        {
            tab = 0;
            tabY = -1f;
            scrollMatch = scrollTeams = Vector2.zero;
            nameAText = nameBText = null;
            confirmReset = false;
            endArmUntil = resumeArmUntil = resetArmUntil = offArmUntil = leaveArmUntil = 0f;
            sayText = "";
            recentSays.Clear();
        }

        public void ToggleVisible() => Toggle();

        private void Toggle()
        {
            Visible = !Visible;
            if (!Visible)
            {
                GUIUtility.keyboardControl = 0;
                if (string.Equals(Link.Get<string>("Menu.Open"), MenuId, StringComparison.Ordinal)) Link.Put("Menu.Open", null);
            }
            if (Visible)
            {
                Link.Put("Menu.Open", MenuId);
                openedAt = Time.unscaledTime;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                nameAText = nameBText = null;
            }
        }

        private void DrawWindow(int id)
        {
            float w = win.width, h = win.height;
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, OpenFade);

            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(0, 0, w, h), Dev.WindowBackground((int)w, (int)h));
            Dev.Snow(w, h);

            DrawSidebar(h);
            if (GUI.Button(new Rect(w - 50f, 18f, 32f, 28f), "×", Dev.Chrome)) c.Defer(Toggle);

            GUILayout.BeginArea(new Rect(Dev.Sidebar + 30f, 22f, w - Dev.Sidebar - 60f, h - 40f));
            if (tab == 0) DrawMatch();
            else DrawTeams();
            GUILayout.EndArea();

            GUI.color = oldColor;
            // As a Unity window it drags itself; drawn as a group (Streamproof), DragHidden does it.
            if (!inGroup) GUI.DragWindow(new Rect(0, 0, w, h));
        }

        private void DrawSidebar(float h)
        {
            GUI.Label(new Rect(26, 26, Dev.Sidebar - 40, 30), "Devicular", Dev.Brand);

            const float y0 = 84f, step = 46f;
            float target = y0 + tab * step;
            if (tabY < 0f) tabY = target;
            if (Event.current.type == EventType.Repaint)
            {
                tabY = Mathf.Lerp(tabY, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
                Dev.DrawIndicator(new Rect(14, tabY, Dev.Sidebar - 28, 42));
            }
            for (int i = 0; i < TabNames.Length; i++)
            {
                var r = new Rect(14, y0 + i * step, Dev.Sidebar - 28, 42);
                int n = i;
                if (GUI.Button(r, TabNames[i], i == tab ? Dev.TabOn : Dev.TabOff) && n != tab) c.Defer(() => tab = n);
            }

            bool armed = Time.unscaledTime < leaveArmUntil;
            GUI.enabled = c.Game.InRoom;
            if (GUI.Button(new Rect(14, h - 58, Dev.Sidebar - 28, 40), armed ? "Click again to leave" : "Disconnect", Dev.Danger))
            {
                if (!c.Round.IsActive || armed)
                {
                    leaveArmUntil = 0f;
                    c.Defer(Leave);
                }
                else leaveArmUntil = Time.unscaledTime + 3f;
            }
            GUI.enabled = true;
        }

        private static void Header(string title, string subtitle, Action right = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label(title, Dev.Title);
            if (!string.IsNullOrEmpty(subtitle)) GUILayout.Label(subtitle, Dev.Sub);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (right != null)
            {
                GUILayout.BeginVertical(GUILayout.Width(200));
                GUILayout.Space(14);
                right();
                GUILayout.EndVertical();
            }
            GUILayout.Space(52);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        private void SwitchSetting(BepInEx.Configuration.ConfigEntry<bool> entry, string label, float width = 0f)
        {
            bool v = Dev.Switch(entry.Value, label, width);
            if (v != entry.Value) c.Defer(() => entry.Value = v);
        }

        private void DrawMatch()
        {
            var r = c.Round;
            var t = c.Teams;
            int roundNo = r.State == RoundState.Idle ? r.RoundNumber + 1 : r.RoundNumber;
            var taggers = r.State == RoundState.Idle ? t.TaggersTeam : r.TaggerTeam;
            var runners = r.State == RoundState.Idle ? t.RunnersTeam : r.RunnerTeam;
            string sub = $"Round {roundNo}  ·  {Clean(t.NameOf(taggers))} tag  ·  {Clean(t.NameOf(runners))} run";
            Header("Match", sub);

            // Off: no card about it (the button on the right says it); on: only the warnings that need you.
            if (Settings.RefereeOn) DrawWarnings();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(Dev.Card, GUILayout.Width(260), GUILayout.Height(172));
            DrawClockCard();
            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.BeginVertical(Dev.Card, GUILayout.Height(172));
            switch (r.State)
            {
                case RoundState.Idle: DrawIdleControls(); break;
                case RoundState.Finished: DrawFinishedControls(); break;
                default: DrawActiveControls(); break;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(Dev.Card, GUILayout.Width(260), GUILayout.Height(150));
            DrawScoreCard();
            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.BeginVertical(Dev.Card, GUILayout.Height(150));
            DrawMatchupCard(taggers, runners);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            scrollMatch = GUILayout.BeginScrollView(scrollMatch);
            DrawSayCard();
            if (r.State != RoundState.Idle) DrawRoundTables();
            // All-in-One: no scoreboard card; the caster's score bar shows the referee's score.
            GUILayout.EndScrollView();
        }

        private void DrawWarnings()
        {
            var g = c.Game;
            if (c.Round.HostLost)
                GUILayout.Label("Host lost: you are no longer the Master Client (" + Clean(g.MasterName) + " is). The round is paused - " +
                                "tags and timing can't be trusted while someone else runs the game. Resume only if you accept that, or End / Abort.", Dev.WarnCard);
            else if (g.InRoom && Settings.RequireMasterClient.Value && !g.IsMaster && !c.Round.IsActive)
            {
                GUILayout.Label($"You are not the host (Master Client) - {Clean(g.MasterName)} is. Rounds can't start until you are. " +
                                "Every 30 seconds the announcer asks everyone else to leave and rejoin; once they have, you're the host.", Dev.WarnCard);
                if (GUILayout.Button("Ask everyone to leave and rejoin now", Dev.SmallGhost, GUILayout.Height(28))) c.Defer(c.AskRejoin);
            }
            if (g.LastError != null) GUILayout.Label("Game read error: " + Clean(g.LastError), Dev.WarnCard);
            if (!c.Round.IsActive) DrawSubs();
        }

        private void DrawSubs()
        {
            foreach (var gap in c.Subs.Gaps.ToList())
            {
                var id = gap.Team;
                string name = Clean(c.Teams.NameOf(id));
                string players = gap.Missing == 1 ? "a player" : gap.Missing + " players";
                GUILayout.BeginVertical(Dev.WarnCard);
                if (gap.Proceed)
                {
                    GUILayout.Label($"{name} is playing {gap.ProceedMissing} short. If a sub shows up, they're added back automatically" +
                                    (TeamManager.PickMode ? " once you pick them on the Teams page." : " when they wear the team's color code."), Dev.Body);
                }
                else if (gap.Expired)
                {
                    GUILayout.Label($"{name} is still missing {players} and the 3 minutes are up. Play on without them, or wait longer?", Dev.Bold);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button($"Play {gap.Missing} short", Dev.Primary, GUILayout.Height(32))) c.Defer(() => c.Subs.PlayShort(id));
                    if (GUILayout.Button("Keep waiting (3 more minutes)", Dev.Ghost, GUILayout.Height(32))) c.Defer(() => c.Subs.KeepWaiting(id));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label($"{name} is missing {players} - {SubManager.Minutes(gap.Left)} left to find a sub. " +
                                    (TeamManager.PickMode ? "Pick the sub on the Teams page when they join." : "A sub joins the team as soon as they wear its color code."), Dev.Body);
                }
                GUILayout.EndVertical();
            }
            if (c.Subs.Newcomers.Count > 0)
            {
                GUILayout.BeginVertical(Dev.WarnCard);
                GUILayout.Label($"Not on a team: {Clean(string.Join(", ", c.Subs.Newcomers))}. Pick the sub for the short team.", Dev.Bold);
                if (GUILayout.Button("Open the Teams page", Dev.SmallGhost, GUILayout.Height(28))) c.Defer(() => tab = 1);
                GUILayout.EndVertical();
            }
        }

        private void DrawClockCard()
        {
            var r = c.Round;
            GUILayout.Label(StateText(), Dev.Caps);
            string clockText;
            if (r.State == RoundState.Starting) clockText = r.LeadInNumber.ToString(CultureInfo.InvariantCulture);
            else
            {
                double clock = r.State == RoundState.Idle ? -Settings.HeadStartSeconds.Value
                    : r.State == RoundState.Running ? Math.Min(r.Clock, r.Cap) : r.Clock;
                clockText = TimeFmt.Clock(clock);
            }
            Color clockColor = r.Paused ? Colors.Bad
                : r.State == RoundState.Countdown ? c.Teams.DisplayColor(Team.Runners)
                : r.State == RoundState.Running ? c.Teams.DisplayColor(Team.Taggers) : Color.white;
            GUILayout.Label(W(clockText, clockColor), Dev.Clock, GUILayout.Height(58));
            if (r.IsActive || r.State == RoundState.Finished)
                GUILayout.Label($"{r.AliveCount}/{r.Runners.Count} runners alive", Dev.Hint);
        }

        private string StateText()
        {
            var r = c.Round;
            if (r.Paused) return W("PAUSED", Colors.Bad);
            switch (r.State)
            {
                case RoundState.Starting: return "GET READY";
                case RoundState.Countdown: return W("RUNNERS RUNNING", c.Teams.DisplayColor(Team.Runners));
                case RoundState.Running: return W("TAGGERS RELEASED", c.Teams.DisplayColor(Team.Taggers));
                case RoundState.Finished: return W($"ROUND {r.RoundNumber} OVER", Colors.Good);
                default: return r.Score.GameOver ? W("GAME OVER", Colors.Good) : c.Ready.AllReady ? W("READY", Colors.Good) : W("SETTING UP", Colors.Warn);
            }
        }

        private string GameWonText()
        {
            var s = c.Round.Score;
            var w = s.GameWinner;
            var l = w == TeamId.A ? TeamId.B : TeamId.A;
            return W($"{Clean(c.Teams.NameOf(w))} won the game {s.Of(w)}-{s.Of(l)}", c.Teams.DisplayColor(w));
        }

        private void DrawIdleControls()
        {
            var rd = c.Ready;
            if (c.Round.Score.GameOver)
            {
                GUILayout.Label(GameWonText(), Dev.Bold);
                GUILayout.FlexibleSpace();
                confirmReset = false;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("New game", Dev.Primary, GUILayout.Height(40))) c.Defer(c.NewGame);
                if (Settings.AutoReferee.Value) TurnOffButton(false);
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.Label(rd.AllReady ? W("Everyone is in position", Colors.Good) : W("Not ready yet", Colors.Warn), Dev.Bold);
            if (rd.AllReady && Settings.AutoReferee.Value && c.AutoStartIn >= 0f)
                GUILayout.Label($"Starting in {c.AutoStartIn:0.0}s", Dev.Hint);
            foreach (var issue in rd.Issues.Take(2)) GUILayout.Label(Clean(issue), Dev.Hint);
            if (rd.Issues.Count > 2) GUILayout.Label($"+{rd.Issues.Count - 2} more", Dev.Hint);
            GUILayout.FlexibleSpace();

            confirmReset = false;
            bool on = Settings.AutoReferee.Value;
            if (GUILayout.Button(on ? "Turn off auto referee" : "Turn on auto referee", on ? Dev.Ghost : Dev.Primary, GUILayout.Height(40)))
                c.Defer(() => c.SetAutoReferee(!on));
        }

        private void TurnOffButton(bool confirm)
        {
            bool armed = Time.unscaledTime < offArmUntil;
            if (GUILayout.Button(armed ? "Click again to turn off" : "Turn off auto referee", Dev.Ghost, GUILayout.Height(40), GUILayout.Width(190)))
            {
                if (!confirm || armed)
                {
                    offArmUntil = 0f;
                    c.Defer(() => c.SetAutoReferee(false));
                }
                else offArmUntil = Time.unscaledTime + 3f;
            }
        }

        private void DrawActiveControls()
        {
            var r = c.Round;
            if (r.Paused)
                GUILayout.Label(W("Paused: " + Clean(r.PauseReason), Colors.Bad), Dev.Bold);
            else if (r.State == RoundState.Starting)
                GUILayout.Label("Counting down 3, 2, 1...", Dev.Bold);
            else if (r.State == RoundState.Countdown)
                GUILayout.Label(W($"Head start - taggers released in {Math.Max(0, -r.Clock):0.0}s", c.Teams.DisplayColor(Team.Runners)), Dev.Bold);
            else
                GUILayout.Label(W("Taggers released - round in progress", c.Teams.DisplayColor(Team.Taggers)), Dev.Bold);
            GUILayout.Label($"Time left {TimeFmt.Clock(Math.Max(0, r.Cap - Math.Max(0, r.Clock)))}" +
                            (c.Authority.Active ? $"  ·  tags blocked {c.Authority.BlockedTags}" : ""), Dev.Hint);
            GUILayout.FlexibleSpace();

            if (confirmReset)
            {
                GUILayout.Label(W("Are you sure? This round is thrown out and everyone goes back to the gazebo to replay it.", Colors.Warn), Dev.Small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Yes, reset round", Dev.Danger, GUILayout.Height(40)))
                    c.Defer(() =>
                    {
                        confirmReset = false;
                        r.ResetRound();
                    });
                if (GUILayout.Button("Cancel", Dev.Ghost, GUILayout.Height(40), GUILayout.Width(130))) c.Defer(() => confirmReset = false);
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal();
            if (!r.Paused)
            {
                if (GUILayout.Button("Pause", Dev.Primary, GUILayout.Height(40), GUILayout.Width(120))) c.Defer(() => r.Pause("paused by the referee"));
            }
            else
            {
                bool needConfirm = r.HostLost;
                bool armed = Time.unscaledTime < resumeArmUntil;
                if (GUILayout.Button(needConfirm ? (armed ? "Resume anyway" : "Resume (not host)") : "Resume", Dev.Primary, GUILayout.Height(40), GUILayout.Width(150)))
                {
                    if (!needConfirm || armed)
                    {
                        resumeArmUntil = 0f;
                        c.Defer(r.Resume);
                    }
                    else resumeArmUntil = Time.unscaledTime + 3f;
                }
            }
            bool endArmed = Time.unscaledTime < endArmUntil;
            if (GUILayout.Button(endArmed ? "Click again to end" : "End round", Dev.Ghost, GUILayout.Height(40)))
            {
                if (endArmed)
                {
                    endArmUntil = 0f;
                    c.Defer(r.EndNow);
                }
                else endArmUntil = Time.unscaledTime + 3f;
            }
            if (GUILayout.Button("Reset round", Dev.Danger, GUILayout.Height(40), GUILayout.Width(150))) c.Defer(() => confirmReset = true);
            TurnOffButton(true);
            GUILayout.EndHorizontal();
        }

        private void DrawFinishedControls()
        {
            confirmReset = false;
            var res = c.Round.LastResult;
            if (res != null)
            {
                GUILayout.Label(W($"{Clean(res.RunnerTeamName)} lasted {TimeFmt.Clock(res.Duration)}", Colors.Good), Dev.Bold);
                GUILayout.Label($"{Clean(res.EndReason)}  ·  {Clean(res.WinnerLine)}", Dev.Hint);
                GUILayout.Label(PointText(res), Dev.Small);
            }
            if (c.Round.Score.GameOver) GUILayout.Label(GameWonText(), Dev.Bold);
            else if (Settings.AutoRotate.Value)
                GUILayout.Label($"Roles switched: {Clean(c.Teams.NameOf(c.Teams.TaggersTeam))} tag next.", Dev.Hint);
            if (c.NextRoundIn >= 0f)
                GUILayout.Label($"Back to setup in {c.NextRoundIn:0}s.", Dev.Hint);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Next round", Dev.Primary, GUILayout.Height(40))) c.Defer(() => c.Round.NextRound());
            if (res != null && GUILayout.Button("Copy results", Dev.Ghost, GUILayout.Height(40), GUILayout.Width(140)))
                GUIUtility.systemCopyBuffer = res.Summary;
            TurnOffButton(false);
            GUILayout.EndHorizontal();
        }

        private string PointText(RoundResult res)
        {
            var t = c.Teams;
            var p = res.Point;
            if (p == null)
            {
                var other = res.RunnerTeam == TeamId.A ? TeamId.B : TeamId.A;
                return $"{Clean(t.NameOf(other))} runs next for the point.";
            }
            if (p.Replay)
                return W($"Too close ({TimeFmt.Clock(p.TimeA)} vs {TimeFmt.Clock(p.TimeB)}, within {p.Margin:0.##}s) - both runs reset, score stays.", Colors.Warn);
            return W($"Point to {Clean(t.NameOf(p.Winner))}", t.DisplayColor(p.Winner)) +
                   $"  ({TimeFmt.Clock(p.TimeA)} vs {TimeFmt.Clock(p.TimeB)})";
        }

        private void DrawScoreCard()
        {
            var s = c.Round.Score;
            var t = c.Teams;
            GUILayout.BeginHorizontal();
            GUILayout.Label("SCORE", Dev.Caps);
            GUILayout.FlexibleSpace();
            bool armed = Time.unscaledTime < resetArmUntil;
            if (GUILayout.Button(armed ? "Sure?" : "Reset", Dev.SmallGhost, GUILayout.Height(22)))
            {
                if (armed)
                {
                    resetArmUntil = 0f;
                    c.Defer(s.Reset);
                }
                else resetArmUntil = Time.unscaledTime + 3f;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ScoreColumn(TeamId.A, s.A);
            GUILayout.Label(W(":", Dev.Faint), Dev.BigNumber, GUILayout.Width(16), GUILayout.Height(40));
            ScoreColumn(TeamId.B, s.B);
            GUILayout.EndHorizontal();

            string note = null;
            if (s.PendingTeam != TeamId.None)
                note = $"{Clean(t.NameOf(s.PendingTeam == TeamId.A ? TeamId.B : TeamId.A))} to beat {TimeFmt.Clock(s.PendingTime)}";
            else if (s.Points.Count > 0 && s.Points[s.Points.Count - 1].Replay)
                note = "Last pair was too close - replayed";
            if (MatchScore.FirstTo > 0) note = $"First to {MatchScore.FirstTo}" + (note != null ? "  ·  " + note : "");
            if (note != null) GUILayout.Label(note, Dev.Hint);
        }

        private void ScoreColumn(TeamId id, int points)
        {
            var t = c.Teams;
            var s = c.Round.Score;
            GUILayout.BeginVertical(GUILayout.Width(98));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var code = t.ColorOf(id);
            if (code.HasValue) Dev.Swatch(code.Value.ToColor(), 10);
            GUILayout.Label(Clean(t.NameOf(id)), Dev.Small);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(points.ToString(CultureInfo.InvariantCulture), Dev.BigNumber, GUILayout.Height(40));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("-", Dev.SmallGhost, GUILayout.Width(30), GUILayout.Height(22))) c.Defer(() => c.AdjustScore(id, -1));
            if (GUILayout.Button("+", Dev.SmallGhost, GUILayout.Width(30), GUILayout.Height(22))) c.Defer(() => c.AdjustScore(id, 1));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawSayCard()
        {
            var e = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == SayControl;
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n'))
            {
                if (e.keyCode != KeyCode.None) SayNow(sayText);
                e.Use();
            }

            GUILayout.BeginVertical(Dev.Card);
            GUILayout.Label("TEXT TO SPEECH", Dev.Caps);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(SayControl);
            sayText = GUILayout.TextField(sayText ?? "", SayMax, GUILayout.Height(32), GUILayout.ExpandWidth(true));
            GUILayout.Space(8);
            if (GUILayout.Button("Say", Dev.Primary, GUILayout.Width(90), GUILayout.Height(32))) SayNow(sayText);
            if (GUILayout.Button("Stop", Dev.SmallGhost, GUILayout.Width(70), GUILayout.Height(32))) c.Defer(c.Announcer.StopAll);
            GUILayout.EndHorizontal();

            if (recentSays.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                foreach (var line in recentSays)
                {
                    string label = line.Length > 28 ? line.Substring(0, 26) + "..." : line;
                    if (GUILayout.Button(label, Dev.SmallGhost, GUILayout.Height(26))) SayNow(line);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Only a line when something's wrong; no instructions.
            string problem = !Settings.RefereeOn ? "The auto referee is off, so nothing is said."
                : !c.Announcer.Tts.Available ? "Text-to-speech isn't working on this PC: " + c.Announcer.Tts.Status
                : null;
            if (problem != null) GUILayout.Label(problem, Dev.Hint);
            GUILayout.EndVertical();
        }

        private void SayNow(string text)
        {
            string line = Regex.Replace(TtsService.Clean(text), @"\s+", " ").Trim();
            if (line.Length == 0) return;
            if (line.Length > SayMax) line = line.Substring(0, SayMax);
            c.Defer(() =>
            {
                c.Announcer.Say(line);
                recentSays.RemoveAll(x => string.Equals(x, line, StringComparison.OrdinalIgnoreCase));
                recentSays.Insert(0, line);
                if (recentSays.Count > 4) recentSays.RemoveAt(recentSays.Count - 1);
                if (string.Equals((sayText ?? "").Trim(), line, StringComparison.Ordinal)) sayText = "";
            });
        }

        private void DrawMatchupCard(TeamId taggers, TeamId runners)
        {
            var t = c.Teams;
            var r = c.Round;
            GUILayout.Label(r.State == RoundState.Idle ? "NEXT ROUND" : "THIS ROUND", Dev.Caps);
            RoleLine("Taggers", taggers, Team.Taggers);
            RoleLine("Runners", runners, Team.Runners);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = !r.IsActive;
            if (GUILayout.Button("Swap roles now", Dev.SmallGhost, GUILayout.Height(26))) c.Defer(() => t.SwapRoles());
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void RoleLine(string role, TeamId id, Team kind)
        {
            var t = c.Teams;
            var code = t.ColorOf(id);
            GUILayout.BeginHorizontal();
            Dev.Swatch(code.HasValue ? code.Value.ToColor() : Dev.Faint);
            GUILayout.Label(W(role, t.DisplayColor(id, kind)), Dev.Bold, GUILayout.Width(70));
            GUILayout.Label($"{Clean(t.NameOf(id))}  <color=#{ColorUtility.ToHtmlStringRGB(Dev.Muted)}>{TeamManager.Describe(code)}</color>", Dev.Body);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawRoundTables()
        {
            var r = c.Round;
            var t = c.Teams;
            GUILayout.BeginVertical(Dev.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label(W("RUNNERS", t.DisplayColor(r.RunnerTeam, Team.Runners)), Dev.Caps);
            GUILayout.Label(Clean(t.NameOf(r.RunnerTeam)), Dev.Hint);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            var ordered = r.State == RoundState.Finished
                ? r.Runners.OrderBy(x => x.Rank).ToList()
                : r.Runners.OrderByDescending(x => x.IsAlive).ThenByDescending(x => x.OutTime).ToList();
            foreach (var e in ordered)
            {
                GUILayout.BeginVertical(Dev.Row);
                GUILayout.BeginHorizontal();
                Dev.Swatch(e.Color.ToColor());
                if (r.State == RoundState.Finished) GUILayout.Label($"#{e.Rank}", Dev.Bold, GUILayout.Width(30));
                GUILayout.Label(Clean(e.Name), Dev.Bold, GUILayout.Width(180));
                Color sc = e.IsAlive || e.Status == RunnerStatus.Survived ? Colors.Good : Colors.Bad;
                GUILayout.Label(W(e.StatusText, sc), Dev.Body, GUILayout.Width(140));
                GUILayout.Label(TimeFmt.Clock(e.SurvivalAt(r.Clock, r.Cap)), Dev.Bold, GUILayout.Width(80));
                GUILayout.Label(string.IsNullOrEmpty(e.TaggedBy) ? "" : "by " + Clean(e.TaggedBy), Dev.Hint, GUILayout.Width(170));
                GUILayout.FlexibleSpace();
                if (r.IsActive)
                {
                    if (e.IsAlive)
                    {
                        if (GUILayout.Button("Eliminate", Dev.SmallGhost, GUILayout.Height(26))) c.Defer(() => r.EliminateManually(e));
                    }
                    else if (e.Status == RunnerStatus.Tagged || e.Status == RunnerStatus.EliminatedByReferee)
                    {
                        if (GUILayout.Button("Revive", Dev.SmallGhost, GUILayout.Height(26))) c.Defer(() => r.Revive(e));
                    }
                }
                GUILayout.EndHorizontal();
                foreach (var f in e.Flags)
                    GUILayout.Label(W("!  " + Clean(f), RoundManager.IsSeriousFlag(f) ? Colors.Bad : Colors.Warn), Dev.Small);
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Dev.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label(W("TAGGERS", t.DisplayColor(r.TaggerTeam, Team.Taggers)), Dev.Caps);
            GUILayout.Label(Clean(t.NameOf(r.TaggerTeam)), Dev.Hint);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            foreach (var tg in r.Taggers)
            {
                var p = c.Game.Get(tg.Actor);
                bool present = p != null && p.Key == tg.Key;
                GUILayout.BeginHorizontal(Dev.Row);
                Dev.Swatch(tg.Color.ToColor());
                GUILayout.Label(Clean(tg.Name), Dev.Bold, GUILayout.Width(200));
                GUILayout.Label($"{tg.Tags} tag{(tg.Tags == 1 ? "" : "s")}", Dev.Body, GUILayout.Width(70));
                if (tg.FalseStarts > 0) GUILayout.Label(W($"{tg.FalseStarts} false start{(tg.FalseStarts == 1 ? "" : "s")}", Colors.Bad), Dev.Body, GUILayout.Width(110));
                if (!present) GUILayout.Label(W("left the room", Colors.Bad), Dev.Body);
                else if (r.State == RoundState.Countdown || r.State == RoundState.Starting)
                    GUILayout.Label(r.IsStunHeld(tg) ? W("left the bench - frozen", Colors.Bad)
                        : p.OnBench ? W("on bench", Colors.Good) : W("off bench", Colors.Bad), Dev.Body);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }


        private void DrawTeams()
        {
            var teams = c.Teams;
            bool pick = TeamManager.PickMode;
            Header("Teams", null);
            scrollTeams = GUILayout.BeginScrollView(scrollTeams);

            GUILayout.BeginVertical(Dev.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Teams", Dev.Body, GUILayout.Width(110), GUILayout.Height(30));
            int mode = pick ? 1 : 0;
            int newMode = Dev.Segmented(mode, new[] { "Auto-detect by color", "Pick players" }, 170);
            if (newMode != mode) c.Defer(() => SetPickMode(newMode == 1));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Format", Dev.Body, GUILayout.Width(110), GUILayout.Height(30));
            int size = (int)Settings.TeamSize.Value - 1;
            int newSize = Dev.Segmented(size, new[] { "3v3", "4v4", "1v1 test" }, 84);
            if (newSize != size) c.Defer(() => Settings.TeamSize.Value = (TeamSizeMode)(newSize + 1));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            // Only when it needs you: no format picked yet.
            if (!Settings.FormatChosen)
                GUILayout.Label(W(pick ? "Choose 3v3, 4v4 or 1v1 test. Rounds don't start until you do." : "Choose 3v3, 4v4 or 1v1 test. Teams aren't detected until you do.", Colors.Warn), Dev.Hint);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Game length", Dev.Body, GUILayout.Width(110), GUILayout.Height(30));
            int length = Settings.FirstTo.Value == 3 ? 1 : Settings.FirstTo.Value == 5 ? 2 : 0;
            int newLength = Dev.Segmented(length, new[] { "No limit", "First to 3", "First to 5" }, 96);
            if (newLength != length) c.Defer(() => c.SetFirstTo(newLength == 1 ? 3 : newLength == 2 ? 5 : 0));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Dev.Card);
            GUILayout.Label("NAMES AND ROLES", Dev.Caps);
            TeamRow(TeamId.A, ref nameAText, Settings.TeamAName);
            TeamRow(TeamId.B, ref nameBText, Settings.TeamBName);
            GUILayout.EndVertical();

            DrawBoard(pick);
            GUILayout.EndScrollView();
        }

        private void SetPickMode(bool on)
        {
            if (on) c.Teams.FreezeIntoPicks(c.Game.Players);
            else c.Teams.Overrides.Clear();
            Settings.ColorMode.Value = on ? ColorAssignMode.Manual : ColorAssignMode.AutoDetect;
        }

        private void DrawBoard(bool pick)
        {
            var teams = c.Teams;
            var players = c.Game.Players.Where(p => !p.IsLocal).ToList();
            float colW = Mathf.Max(200f, (win.width - Dev.Sidebar - 104f) / 3f);

            if (!c.Game.InRoom)
            {
                GUILayout.Label("Join the scrim room to see players.", Dev.Hint);
                return;
            }
            if (!pick && !Settings.FormatChosen)
                GUILayout.Label(W("Pick a format above (3v3, 4v4 or 1v1 test) - teams are only detected once you choose one.", Colors.Warn), Dev.Small);
            else if (c.NeedsMatchingColors)
                GUILayout.Label(W("No teams found yet - each team needs to wear the same color code (the announcer asks them to).", Colors.Warn), Dev.Small);
            if (teams.ColorsConflict) GUILayout.Label(W("Both teams have the same color code!", Colors.Bad), Dev.Bold);

            GUILayout.BeginHorizontal();
            BoardColumn(TeamId.A, players, pick, colW);
            GUILayout.Space(10);
            BoardColumn(TeamId.None, players, pick, colW);
            GUILayout.Space(10);
            BoardColumn(TeamId.B, players, pick, colW);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = Settings.FormatChosen;
            if (!pick && GUILayout.Button("Detect teams now", Dev.Primary, GUILayout.Height(32)))
                c.Defer(() => teams.Detect(c.Game.Players, Settings.RequiredTeamSize, userInitiated: true, roundsPlayed: c.Round.History.Count > 0));
            GUI.enabled = true;
            if (pick)
            {
                GUI.enabled = teams.Overrides.Count > 0;
                if (GUILayout.Button($"Clear all picks ({teams.Overrides.Count})", Dev.Ghost, GUILayout.Height(32)))
                    c.Defer(teams.Overrides.Clear);
                GUI.enabled = true;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!pick && !string.IsNullOrEmpty(teams.DetectMessage)) GUILayout.Label(Clean(teams.DetectMessage), Dev.Hint);
        }

        private void BoardColumn(TeamId id, List<PlayerSnapshot> players, bool pick, float width)
        {
            var teams = c.Teams;
            var list = players.Where(p => teams.TeamIdOf(p) == id).ToList();
            GUILayout.BeginVertical(Dev.Card, GUILayout.Width(width), GUILayout.MinHeight(150));
            GUILayout.BeginHorizontal();
            if (id == TeamId.None) GUILayout.Label("NOT ON A TEAM", Dev.Caps);
            else GUILayout.Label(W(Clean(teams.NameOf(id)).ToUpperInvariant(), teams.DisplayColor(id)), Dev.Caps);
            GUILayout.FlexibleSpace();
            GUILayout.Label(id == TeamId.None ? list.Count.ToString(CultureInfo.InvariantCulture)
                : list.Count.ToString(CultureInfo.InvariantCulture), Dev.Hint);
            GUILayout.EndHorizontal();

            foreach (var p in list)
            {
                GUILayout.BeginHorizontal(Dev.Row);
                if (id == TeamId.None && GUILayout.Button("« A", Dev.SmallGhost, GUILayout.Width(40), GUILayout.Height(24)))
                    c.Defer(() => teams.SetOverride(p, TeamId.A));
                Dev.Swatch(p.ColorKnown ? p.RawColor : Dev.Faint);
                GUILayout.Label(Clean(p.Name), Dev.Bold, GUILayout.Height(24), GUILayout.MaxWidth(width - 110f));
                GUILayout.FlexibleSpace();
                if (id == TeamId.None)
                {
                    if (GUILayout.Button("B »", Dev.SmallGhost, GUILayout.Width(40), GUILayout.Height(24)))
                        c.Defer(() => teams.SetOverride(p, TeamId.B));
                }
                else if (GUILayout.Button("\u00d7", Dev.SmallGhost, GUILayout.Width(30), GUILayout.Height(24)))
                    c.Defer(() => teams.SetOverride(p, pick ? (TeamId?)null : TeamId.None));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void TeamRow(TeamId id, ref string nameText, BepInEx.Configuration.ConfigEntry<string> nameEntry)
        {
            var code = c.Teams.ColorOf(id);
            if (nameText == null) nameText = c.Teams.HasCustomName(id) ? nameEntry.Value.Trim() : "";
            var role = c.Teams.RoleOf(id);
            GUILayout.BeginHorizontal(Dev.Row);
            Dev.Swatch(code.HasValue ? code.Value.ToColor() : Dev.Faint);
            GUILayout.Label(W(Clean(c.Teams.NameOf(id)), c.Teams.DisplayColor(id)), Dev.Bold, GUILayout.Width(150), GUILayout.Height(28));
            nameText = GUILayout.TextField(nameText, 24, GUILayout.Width(170), GUILayout.Height(28));
            if (GUILayout.Button("Save name", Dev.SmallGhost, GUILayout.Height(28)))
            {
                string n = nameText.Trim();
                string value = n.Length > 0 ? n : (id == TeamId.A ? "Team A" : "Team B");
                c.Defer(() => nameEntry.Value = value);
                nameText = null;
            }
            GUILayout.Space(12);
            GUILayout.Label(code.HasValue ? $"color {code.Value}" : "no color yet", Dev.Hint, GUILayout.Height(28));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");
    }
}
