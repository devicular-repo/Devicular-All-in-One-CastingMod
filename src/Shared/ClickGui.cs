using System;
using System.Collections.Generic;
using UnityEngine;

namespace Devicular
{
    internal sealed class Module
    {
        public string Name;
        public string Keywords = "";
        public Func<bool> IsOn;
        public Action OnClick;
        public Action DrawSettings;
        public Func<Color?> Swatch;
        public Func<string> Badge;
        /// <summary>The badge's color (its text, and a light pill behind it). Red when not set.</summary>
        public Func<Color> BadgeColor;
        public string Tip;
        public bool Expanded;
    }

    internal sealed class Column
    {
        public string Title;
        public Vector2 Pos;
        public Vector2 Home;
        public readonly List<Module> Modules = new List<Module>();
        public Func<List<Module>> Dynamic;
        public string Empty = "";
        public Rect Header;
        public Rect Body;
        /// <summary>The column's position when <see cref="Body"/> was measured, so it can follow a drag straight away.</summary>
        public Vector2 BodyAt;
        /// <summary>Which particles drift in this column (made once, not every frame).</summary>
        public string ParticleKey;
    }

    /// <summary>
    /// The controls the Devicular menus are made of. Everything takes its colors from the shared theme (<see cref="Dev"/>):
    /// what's on, being dragged or typed in shows in the accent color; everything else stays neutral.
    /// </summary>
    internal static class Ui
    {
        public const float ColumnWidth = 226f;
        private const float RowHeight = 34f;

        public static GUIStyle HintCentered;
        public static GUIStyle ColumnBox, HeaderText, RowText, RowTextOn, SettingText, SettingHint, SectionText,
            OptionOn, OptionOff, SearchText, SearchHint, BrandText, BrandAccent, BadgeText, KeyChip, SettingsArea;

        private static GUIStyle cardRing, cardHover;
        private static GUIStyle rowHover, rowOn, headerLine, searchBox, searchBoxFocused, trackStyle, fillStyle, valueChip,
            switchTrack, switchTrackOn, checkText, checkHover, swatchOn, badgePill, pickName, pickOn, pickOff, optBg, button, danger, field;
        private static readonly Dictionary<string, float> switchAnim = new Dictionary<string, float>();
        /// <summary>The tooltips of the menu being built or drawn. All-in-One: each <see cref="ClickGui"/> has its own.</summary>
        public static Dictionary<string, string> Tips = new Dictionary<string, string>();
        public static string Context = "";
        private static string frameTip, shownTip;
        private static float tipSince;
        private static GUIStyle tipStyle;

        private static string TipFor(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            if (Tips.TryGetValue(Context + "/" + label, out var t)) return t;
            if (Tips.TryGetValue(label, out t)) return t;
            return Tips.TryGetValue(Context + "/*", out t) ? t : null;
        }

        public static void Tip(Rect r, string tip)
        {
            if (string.IsNullOrEmpty(tip) || Event.current.type != EventType.Repaint) return;
            if (r.Contains(Event.current.mousePosition)) frameTip = tip;
        }

        public static void BeginTips()
        {
            if (Event.current.type == EventType.Repaint) frameTip = null;
        }

        public static void DrawTip(float width, float height)
        {
            var e = Event.current;
            if (e.type != EventType.Repaint) return;
            if (frameTip != shownTip)
            {
                shownTip = frameTip;
                tipSince = Time.unscaledTime;
            }
            if (shownTip == null || Time.unscaledTime - tipSince < 0.55f) return;
            var content = new GUIContent(shownTip);
            float w = Mathf.Min(270f, tipStyle.CalcSize(content).x + 1f);
            float h = tipStyle.CalcHeight(content, w);
            var m = e.mousePosition;
            float x = m.x + 14f, y = m.y + 20f;
            if (x + w > width - 6f) x = Mathf.Max(6f, m.x - w - 8f);
            if (y + h > height - 6f) y = Mathf.Max(6f, m.y - h - 10f);
            tipStyle.Draw(new Rect(x, y, w, h), content, false, false, false, false);
        }

        private static Texture2D chevronRight, chevronDown, check, cross, magnifier, line;
        private static readonly Queue<Action> later = new Queue<Action>();
        private static bool ready;
        private static int builtVersion = -1;

        public static bool Typing => GUIUtility.keyboardControl != 0;

        public static void Later(Action action) => later.Enqueue(action);

        public static void Flush()
        {
            while (later.Count > 0)
            {
                var a = later.Dequeue();
                try { a(); }
                catch (Exception e) { Mod.Log?.LogWarning("Menu action failed: " + e.Message); }
            }
        }

        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Text on a solid accent background.</summary>
        private static Color OnAccent => Dev.OnAccent;

        private static GUIStyle Box(int radius, Texture2D normal, RectOffset padding = null)
        {
            var s = new GUIStyle(GUIStyle.none) { border = new RectOffset(radius, radius, radius, radius), padding = padding ?? new RectOffset(0, 0, 0, 0) };
            s.normal.background = normal;
            return s;
        }

        public static void Init()
        {
            Dev.Init();
            if (ready && builtVersion == Dev.Version) return;
            ready = true;
            builtVersion = Dev.Version;
            var accent = Dev.Info;

            // Panels and their headers.
            // Glass panels with the palette's light in them; a plain line under each title.
            // (A panel is always taller than 96 px, so the shading can spread over all of it.)
            ColumnBox = Box(14, Dev.GlassTex(14, Dev.Dark ? 0.94f : 0.965f, default, 96), new RectOffset(10, 10, 4, 10));
            HeaderText = Label(15, Dev.Ink, true, TextAnchor.MiddleLeft);
            headerLine = Box(1, Dev.RoundTex(1, Dev.Shade(0.08f), Color.clear));

            // Module rows: quiet until hovered; an accent tint and side bar when on.
            RowText = Label(13, Dev.Ink, false, TextAnchor.MiddleLeft);
            RowTextOn = Label(13, accent, true, TextAnchor.MiddleLeft);
            RowText.clipping = RowTextOn.clipping = TextClipping.Clip;
            rowHover = Box(9, Dev.RoundTex(9, Dev.Shade(0.045f), Color.clear));
            rowOn = Box(9, Dev.HGradientTex(9, A(accent, Dev.Dark ? 0.27f : 0.17f), A(accent, 0.03f), 34, 96, A(accent, Dev.Dark ? 0.26f : 0.17f)));

            // A module's settings sit in their own inset box under it.
            SettingsArea = Box(10, Dev.RoundTex(10, Dev.Shade(0.03f), Dev.Shade(0.05f)), new RectOffset(9, 9, 7, 5));
            SettingsArea.margin = new RectOffset(0, 0, 0, 0);
            SettingText = Label(12, Dev.Ink, false, TextAnchor.MiddleLeft);
            SettingText.clipping = TextClipping.Clip;
            SettingHint = Label(11, Dev.Muted, false, TextAnchor.MiddleLeft);
            SettingHint.wordWrap = true;
            SectionText = Label(10, Dev.Muted, true, TextAnchor.LowerLeft);
            HintCentered = Label(11, Dev.Muted, false, TextAnchor.MiddleCenter);

            // Switches.
            checkText = Label(12, Dev.Ink, false, TextAnchor.MiddleLeft);
            checkText.wordWrap = true;
            checkHover = Box(8, Dev.RoundTex(8, Dev.Shade(0.04f), Color.clear));
            switchTrack = Box(9, Dev.RoundTex(9, Dev.Shade(0.15f), Dev.Shade(0.05f)));
            switchTrackOn = Box(9, Dev.HGradientTex(9, Dev.InfoTop, Dev.InfoBottom, 22, 40));

            // Sliders.
            trackStyle = Box(3, Dev.RoundTex(3, Dev.Shade(0.1f), Color.clear));
            fillStyle = Box(3, Dev.HGradientTex(3, Dev.InfoTop, Dev.InfoBottom, 10, 48));
            valueChip = Box(6, Dev.RoundTex(6, Dev.Shade(0.06f), Color.clear), new RectOffset(6, 6, 0, 0));
            valueChip.fontSize = 11;
            Dev.Strong(valueChip);
            valueChip.alignment = TextAnchor.MiddleCenter;
            valueChip.normal.textColor = Dev.Ink;

            // Choices: a tray with the picked one filled in the accent color.
            optBg = Box(9, Dev.RoundTex(9, Dev.Shade(0.06f), Color.clear), new RectOffset(3, 3, 3, 3));
            optBg.margin = new RectOffset(0, 0, 0, 0);
            OptionOn = Choice(12, true, new RectOffset(8, 8, 3, 3));
            OptionOff = Choice(12, false, new RectOffset(8, 8, 3, 3));
            pickOn = Choice(11, true, new RectOffset(0, 0, 0, 0));
            pickOff = Choice(11, false, new RectOffset(0, 0, 0, 0));
            pickName = Label(12, Dev.Ink, false, TextAnchor.MiddleLeft);
            pickName.clipping = TextClipping.Clip;

            // Buttons: neutral, lighting up in the accent color under the mouse.
            button = Choice(12, false, new RectOffset(10, 10, 4, 4));
            Dev.Strong(button);
            button.normal.background = Dev.RoundTex(8, Dev.Shade(0.06f), Dev.Shade(0.08f));
            button.hover.background = Dev.RoundTex(8, A(accent, 0.15f), A(accent, 0.38f));
            button.active.background = Dev.RoundTex(8, A(accent, 0.24f), A(accent, 0.5f));
            button.normal.textColor = Dev.Ink;
            button.hover.textColor = button.active.textColor = accent;
            button.border = new RectOffset(8, 8, 8, 8);
            var red = Dev.Hex("E0475B");
            danger = new GUIStyle(button);
            danger.normal.background = Dev.RoundTex(8, A(red, 0.08f), A(red, 0.22f));
            danger.hover.background = Dev.RoundTex(8, A(red, 0.15f), A(red, 0.36f));
            danger.active.background = Dev.RoundTex(8, A(red, 0.23f), A(red, 0.46f));
            danger.normal.textColor = danger.hover.textColor = danger.active.textColor = Dev.Dark ? Dev.Hex("FF8A99") : Dev.Hex("C7364A");

            // Text boxes: an accent outline while you type.
            field = new GUIStyle(Dev.Skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(8, 8, 8, 8)
            };
            field.normal.background = Dev.RoundTex(8, Dev.Shade(0.045f), Dev.Shade(0.11f));
            field.hover.background = Dev.RoundTex(8, Dev.Shade(0.06f), Dev.Shade(0.16f));
            field.focused.background = field.onFocused.background = Dev.RoundTex(8, Dev.Surface(1f), A(accent, 0.9f));
            field.active.background = field.focused.background;
            field.normal.textColor = field.hover.textColor = field.focused.textColor = field.onFocused.textColor = field.active.textColor = Dev.Ink;

            swatchOn = Label(12, accent, true, TextAnchor.MiddleLeft);
            tipStyle = new GUIStyle(GUIStyle.none)
            {
                fontSize = 12,
                wordWrap = true,
                richText = false,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 7, 8),
                border = new RectOffset(8, 8, 8, 8)
            };
            tipStyle.normal.background = Dev.RoundTex(8, new Color(Dev.Ink.r, Dev.Ink.g, Dev.Ink.b, 0.95f), Color.clear);
            tipStyle.normal.textColor = Dev.Dark ? Dev.Hex("1F232B") : Color.white;

            // The search bar.
            SearchText = new GUIStyle(Dev.Skin.textField) { fontSize = 13, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 8, 0, 0) };
            SearchText.normal.background = SearchText.hover.background = SearchText.focused.background = SearchText.active.background = null;
            SearchText.onFocused.background = SearchText.onNormal.background = null;
            // What you type reads in the palette's text color whatever state the box is in.
            SearchText.normal.textColor = SearchText.hover.textColor = SearchText.focused.textColor = SearchText.active.textColor =
                SearchText.onFocused.textColor = SearchText.onNormal.textColor = Dev.Ink;
            // The placeholder in the palette's secondary text color: the faintest one was nearly invisible on dark glass.
            SearchHint = Label(13, Dev.Muted, false, TextAnchor.MiddleLeft);
            // One even color (the bar is too short for shading: it showed as a dark half and a light half), and the
            // same glass when you click in, with an accent outline.
            searchBox = Box(17, Dev.GlassTex(17, 0.965f, default, 0, true));
            searchBoxFocused = Box(17, Dev.GlassTex(17, 0.985f, A(accent, 0.85f), 0, true));

            BrandText = Label(20, Dev.Ink, true, TextAnchor.MiddleLeft);
            BrandAccent = Label(20, accent, true, TextAnchor.MiddleLeft);

            // The theme picker's cards.
            cardRing = Box(8, Dev.RoundTex(8, Color.clear, accent));
            cardHover = Box(8, Dev.RoundTex(8, Color.clear, Dev.Shade(0.32f)));
            BadgeText = Label(10, Dev.Bad, true, TextAnchor.MiddleCenter);
            BadgeText.padding = new RectOffset(6, 6, 0, 0);
            // White, tinted with the badge's color when drawn.
            badgePill = Box(7, Dev.RoundTex(7, new Color(1f, 1f, 1f, 0.14f), Color.clear));
            KeyChip = Box(6, Dev.RoundTex(6, Dev.Shade(0.06f), Dev.Shade(0.1f)), new RectOffset(7, 7, 2, 2));
            KeyChip.fontSize = 11;
            Dev.Strong(KeyChip);
            KeyChip.alignment = TextAnchor.MiddleCenter;
            KeyChip.normal.textColor = Dev.Ink;
            KeyChip.clipping = TextClipping.Clip;

            chevronRight = Stroke(24, 2.2f, new[] { new[] { new Vector2(9, 6), new Vector2(15, 12), new Vector2(9, 18) } });
            chevronDown = Stroke(24, 2.2f, new[] { new[] { new Vector2(6, 9), new Vector2(12, 15), new Vector2(18, 9) } });
            check = Stroke(24, 2.6f, new[] { new[] { new Vector2(5, 13), new Vector2(10, 18), new Vector2(19, 6) } });
            cross = Stroke(24, 2.4f, new[] { new[] { new Vector2(6, 6), new Vector2(18, 18) }, new[] { new Vector2(18, 6), new Vector2(6, 18) } });
            magnifier = Magnifier(28);
            line = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            line.SetPixel(0, 0, Dev.Shade(0.07f));
            line.Apply();
        }

        private static GUIStyle Label(int size, Color color, bool bold, TextAnchor anchor)
        {
            var s = new GUIStyle(Dev.Skin.label) { fontSize = size, alignment = anchor, richText = true, wordWrap = false };
            s.normal.textColor = color;
            if (bold) Dev.Strong(s);
            s.margin = new RectOffset(0, 0, 0, 0);
            s.padding = new RectOffset(0, 0, 0, 0);
            return s;
        }

        /// <summary>One choice in a tray: filled in the accent color when picked, plain text otherwise.</summary>
        private static GUIStyle Choice(int size, bool picked, RectOffset padding)
        {
            var s = new GUIStyle(GUIStyle.none)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleCenter,
                padding = padding,
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(7, 7, 7, 7),
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Normal
            };
            if (picked)
            {
                Dev.Strong(s);
                s.normal.background = s.hover.background = s.active.background = Dev.HGradientTex(7, Dev.InfoTop, Dev.InfoBottom, 24, 48);
                s.normal.textColor = s.hover.textColor = s.active.textColor = OnAccent;
            }
            else
            {
                s.hover.background = Dev.RoundTex(7, Dev.Shade(0.06f), Color.clear);
                s.active.background = Dev.RoundTex(7, Dev.Shade(0.1f), Color.clear);
                s.normal.textColor = Dev.Muted;
                s.hover.textColor = s.active.textColor = Dev.Ink;
            }
            return s;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
            return Vector2.Distance(p, a + ab * t);
        }

        private static Texture2D Stroke(int size, float width, Vector2[][] lines)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, size - y - 0.5f);
                    float d = float.MaxValue;
                    foreach (var poly in lines)
                        for (int i = 0; i + 1 < poly.Length; i++)
                            d = Mathf.Min(d, SegmentDistance(p, poly[i], poly[i + 1]));
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(width / 2f + 0.5f - d));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D Magnifier(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            var center = new Vector2(11.5f, 11.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, size - y - 0.5f);
                    float ring = Mathf.Abs(Vector2.Distance(p, center) - 7.5f);
                    float handle = SegmentDistance(p, new Vector2(17, 17), new Vector2(24, 24));
                    float d = Mathf.Min(ring, handle);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1.6f - d));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static void Tinted(Rect r, Texture2D tex, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.DrawTexture(r, tex);
            GUI.color = old;
        }

        private static void Fill(GUIStyle style, Rect r)
        {
            if (Event.current.type == EventType.Repaint) style.Draw(r, false, false, false, false);
        }

        public static void DrawSearchBox(Rect r, bool focused) => Fill(focused ? searchBoxFocused : searchBox, r);

        public static void DrawMagnifier(Rect r, bool focused) => Tinted(r, magnifier, focused ? Dev.Info : Dev.Muted);

        public static void Divider()
        {
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, line);
        }

        /// <summary>A panel's title, with a plain line under it. Returns where it is, for dragging the panel.</summary>
        public static Rect ColumnHeader(string title)
        {
            var r = GUILayoutUtility.GetRect(10, 44, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(new Rect(r.x + 6f, r.y + 2f, r.width - 12f, r.height - 8f), title, HeaderText);
                Fill(headerLine, new Rect(r.x + 2f, r.yMax - 4f, r.width - 4f, 1f));
            }
            return r;
        }

        public static void ModuleRow(Module m, List<Module> siblings = null)
        {
            var r = GUILayoutUtility.GetRect(10, RowHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            bool hasSettings = m.DrawSettings != null;
            bool on = m.IsOn != null && m.IsOn();
            var arrow = new Rect(r.xMax - 30f, r.y, 30f, r.height);
            if (e.type == EventType.Repaint)
            {
                bool hover = r.Contains(e.mousePosition);
                // A row that's on is tinted in the accent color (no bar or glow at its side).
                if (on) rowOn.Draw(r, false, false, false, false);
                else if (hover || m.Expanded) rowHover.Draw(r, false, false, false, false);

                float left = 13f;
                var sw = m.Swatch?.Invoke();
                if (sw.HasValue)
                {
                    var dot = new Rect(r.x + left, r.y + (r.height - 10f) / 2f, 10f, 10f);
                    Dev.Dot(new Rect(dot.x - 1, dot.y - 1, dot.width + 2, dot.height + 2), new Color(0f, 0f, 0f, 0.2f));
                    Dev.Dot(dot, new Color(sw.Value.r, sw.Value.g, sw.Value.b, 1f));
                    left += 17f;
                }
                float right = hasSettings ? 30f : 10f;
                string badge = m.Badge?.Invoke();
                if (!string.IsNullOrEmpty(badge))
                {
                    float bw = BadgeText.CalcSize(new GUIContent(badge)).x;
                    var br = new Rect(r.xMax - right - bw, r.y + 9f, bw, 16f);
                    var badgeColor = m.BadgeColor != null ? m.BadgeColor() : Dev.Bad;
                    var oldBack = GUI.backgroundColor;
                    GUI.backgroundColor = badgeColor;
                    badgePill.Draw(br, false, false, false, false);
                    GUI.backgroundColor = oldBack;
                    BadgeText.normal.textColor = badgeColor;
                    GUI.Label(br, badge, BadgeText);
                    right += bw + 6f;
                }
                GUI.Label(new Rect(r.x + left, r.y, r.width - left - right, r.height), m.Name, on ? RowTextOn : RowText);
                if (hasSettings)
                {
                    var c = m.Expanded ? Dev.Info : arrow.Contains(e.mousePosition) ? Dev.Ink : Dev.Muted;
                    Tinted(new Rect(arrow.x + 8f, r.y + 10f, 14f, 14f), m.Expanded ? chevronDown : chevronRight, c);
                }
            }
            bool chevron = hasSettings && GUI.Button(arrow, GUIContent.none, GUIStyle.none);
            bool row = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (chevron || (row && m.OnClick == null && hasSettings))
                Later(() => m.Expanded = !m.Expanded);
            else if (row && m.OnClick != null) Later(m.OnClick);

            Tip(r, m.Tip ?? TipFor(m.Name));
            if (m.Expanded && hasSettings)
            {
                GUILayout.Space(3);
                GUILayout.BeginVertical(SettingsArea);
                string outer = Context;
                Context = m.Name ?? "";
                try { m.DrawSettings(); }
                finally { Context = outer; }
                GUILayout.EndVertical();
            }
            GUILayout.Space(4);
        }

        public static void Check(string label, bool value, Action<bool> set)
        {
            float textW = ColumnWidth - 40f - 60f;
            float h = Mathf.Max(28f, checkText.CalcHeight(new GUIContent(label), textW) + 10f);
            var r = GUILayoutUtility.GetRect(10, h, GUILayout.ExpandWidth(true));
            var e = Event.current;
            bool known = switchAnim.TryGetValue(label, out float t);
            if (!known) t = value ? 1f : 0f;
            if (e.type == EventType.Repaint) t = Mathf.MoveTowards(t, value ? 1f : 0f, Mathf.Min(Time.unscaledDeltaTime, 0.05f) * 8f);
            switchAnim[label] = t;
            if (e.type == EventType.Repaint)
            {
                if (r.Contains(e.mousePosition)) checkHover.Draw(r, false, false, false, false);
                GUI.Label(new Rect(r.x + 7f, r.y, r.width - 54f, r.height), label, checkText);
                var tr = new Rect(r.xMax - 41f, r.y + (r.height - 18f) / 2f, 33f, 18f);
                switchTrack.Draw(tr, false, false, false, false);
                if (t > 0f)
                {
                    var old = GUI.color;
                    GUI.color = new Color(old.r, old.g, old.b, old.a * t);
                    switchTrackOn.Draw(tr, false, false, false, false);
                    GUI.color = old;
                }
                var knob = new Rect(tr.x + 2f + 15f * t, tr.y + 2f, 14f, 14f);
                Dev.Dot(new Rect(knob.x, knob.y + 1f, knob.width, knob.height), new Color(0f, 0f, 0f, 0.22f));
                Dev.Dot(knob, Color.white);
            }
            Tip(r, TipFor(label));
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                bool v = !value;
                Later(() => set(v));
            }
            GUILayout.Space(2);
        }

        public static void Slider(string label, float value, float min, float max, Action<float> set, float shown = 1f, string suffix = "")
        {
            var head = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
            var r = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
            var track = new Rect(r.x + 7, r.y + 6, r.width - 14, 6);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            float At(float x) => Mathf.Clamp(Mathf.Round(Mathf.Lerp(min, max, Mathf.InverseLerp(track.xMin, track.xMax, x)) * shown) / shown, min, max);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = 0;
                        set(At(e.mousePosition.x));
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        set(At(e.mousePosition.x));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    bool hot = GUIUtility.hotControl == id;
                    var text = new GUIContent(Mathf.RoundToInt(value * shown).ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix);
                    float cw = Mathf.Max(28f, valueChip.CalcSize(text).x);
                    valueChip.Draw(new Rect(head.xMax - cw - 2f, head.y + 2f, cw, 16f), text, false, false, false, false);
                    GUI.Label(new Rect(head.x + 2f, head.y, head.width - cw - 10f, head.height), label, SettingHint);
                    trackStyle.Draw(track, false, false, false, false);
                    float t = Mathf.Clamp01(Mathf.InverseLerp(min, max, value));
                    if (t > 0f) fillStyle.Draw(new Rect(track.x, track.y, Mathf.Max(6f, track.width * t), track.height), false, false, false, false);
                    float size = hot || r.Contains(e.mousePosition) ? 16f : 14f;
                    var knob = new Rect(track.x + track.width * t - size / 2f, track.y + 3f - size / 2f, size, size);
                    Dev.Dot(new Rect(knob.x, knob.y + 1, knob.width, knob.height), new Color(0f, 0f, 0f, 0.22f));
                    Dev.Dot(knob, Dev.Info);
                    Dev.Dot(new Rect(knob.x + 3f, knob.y + 3f, knob.width - 6f, knob.height - 6f), Color.white);
                    break;
            }
            Tip(new Rect(head.x, head.y, head.width, r.yMax - head.y), TipFor(label));
            GUILayout.Space(4);
        }

        public static void Options(string label, int selected, string[] options, Action<int> set)
        {
            GUILayout.BeginVertical();
            if (!string.IsNullOrEmpty(label)) GUILayout.Label(label, SettingHint, GUILayout.Height(18));
            float max = ColumnWidth - 50f;
            float used = 0f;
            GUILayout.BeginHorizontal(optBg);
            for (int i = 0; i < options.Length; i++)
            {
                var style = i == selected ? OptionOn : OptionOff;
                float w = style.CalcSize(new GUIContent(options[i])).x;
                if (used > 0f && used + w > max)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);
                    GUILayout.BeginHorizontal(optBg);
                    used = 0f;
                }
                used += w;
                int n = i;
                if (GUILayout.Button(options[i], style, GUILayout.Height(24), GUILayout.ExpandWidth(true)) && n != selected) Later(() => set(n));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            Tip(GUILayoutUtility.GetLastRect(), TipFor(label));
            GUILayout.Space(5);
        }

        public static void Section(string label)
        {
            GUILayout.Space(7);
            GUILayout.Label((label ?? "").ToUpperInvariant(), SectionText, GUILayout.Height(16));
            GUILayout.Space(4);
        }

        public static void Button(string label, Action action, bool danger = false, Action rightClick = null, string tip = null)
        {
            var r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            Tip(r, tip ?? TipFor(label));
            var e = Event.current;
            if (rightClick != null && e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition))
            {
                Later(rightClick);
                e.Use();
            }
            if (GUI.Button(r, label, danger ? Ui.danger : button)) Later(action);
            GUILayout.Space(4);
        }

        public static void ButtonRow(params (string label, Action action)[] buttons)
        {
            var r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            if (buttons.Length == 0) return;
            const float gap = 6f;
            var widths = new float[buttons.Length];
            float content = 0f;
            for (int i = 0; i < buttons.Length; i++)
            {
                widths[i] = button.CalcSize(new GUIContent(buttons[i].label)).x;
                content += widths[i];
            }
            float free = r.width - gap * (buttons.Length - 1);
            float extra = (free - content) / buttons.Length;
            float x = r.x;
            for (int i = 0; i < buttons.Length; i++)
            {
                float w = extra >= 0f ? widths[i] + extra : free * widths[i] / content;
                if (i == buttons.Length - 1) w = r.xMax - x;
                var action = buttons[i].action;
                var br = new Rect(x, r.y, w, r.height);
                Tip(br, TipFor(buttons[i].label));
                if (GUI.Button(br, buttons[i].label, button)) Later(action);
                x += w + gap;
            }
            GUILayout.Space(4);
        }

        public static void Info(string text) => GUILayout.Label(text, SettingHint);

        public static string Field(string name, string text, int max)
        {
            var r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            GUI.SetNextControlName(name);
            string result = GUI.TextField(r, text ?? "", max, field);
            Tip(r, TipFor(name));
            GUILayout.Space(5);
            return result;
        }

        public static void SwatchRow(string label, Color a, Color b, bool selected, Action click)
        {
            var r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (selected) rowOn.Draw(r, false, false, false, false);
                else if (r.Contains(Event.current.mousePosition)) rowHover.Draw(r, false, false, false, false);
                float cy = r.y + (r.height - 16f) / 2f;
                Dev.Dot(new Rect(r.x + 7, cy, 16, 16), new Color(0f, 0f, 0f, 0.28f));
                Dev.Dot(new Rect(r.x + 8, cy + 1, 14, 14), new Color(a.r, a.g, a.b, 1f));
                Dev.Dot(new Rect(r.x + 25, cy, 16, 16), new Color(0f, 0f, 0f, 0.28f));
                Dev.Dot(new Rect(r.x + 26, cy + 1, 14, 14), new Color(b.r, b.g, b.b, 1f));
                GUI.Label(new Rect(r.x + 48, r.y, r.width - 72, r.height), label.ToUpperInvariant(), selected ? swatchOn : SettingText);
                if (selected) Tinted(new Rect(r.xMax - 22, cy + 1, 14, 14), check, Dev.Info);
            }
            Tip(r, selected ? $"{label} is the palette you're using." : $"Switch to the {label} palette.");
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Later(click);
            GUILayout.Space(3);
        }

        public static void TeamPick(string label, Color color, int team, Action<int> set)
        {
            var r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            var e = Event.current;
            const float bw = 24f;
            var seg = new Rect(r.xMax - bw * 3f - 8f, r.y + 3f, bw * 3f + 6f, 24f);
            if (e.type == EventType.Repaint)
            {
                if (r.Contains(e.mousePosition)) checkHover.Draw(r, false, false, false, false);
                float cy = r.y + (r.height - 10f) / 2f;
                Dev.Dot(new Rect(r.x + 7, cy - 1, 12, 12), new Color(0f, 0f, 0f, 0.15f));
                Dev.Dot(new Rect(r.x + 8, cy, 10, 10), new Color(color.r, color.g, color.b, 1f));
                GUI.Label(new Rect(r.x + 25, r.y, seg.x - r.x - 29, r.height), label, pickName);
                optBg.Draw(seg, false, false, false, false);
            }
            string[] names = { "A", "B", "-" };
            int[] values = { 1, 2, 0 };
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(seg.x + 3f + bw * i, seg.y + 3f, bw, seg.height - 6f);
                int v = values[i];
                if (GUI.Button(b, names[i], team == v ? pickOn : pickOff) && team != v) Later(() => set(v));
            }
            Tip(r, $"A or B puts {label} on that team. - takes them off.");
            GUILayout.Space(2);
        }

        public static void ColorDots(string label, Color[] colors, int selected, Action<int> set, string[] names = null)
        {
            if (!string.IsNullOrEmpty(label)) GUILayout.Label(label, SettingHint, GUILayout.Height(18));
            var r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            const float size = 22f;
            float gap = colors.Length > 1 ? Mathf.Min(10f, (r.width - size * colors.Length) / (colors.Length - 1)) : 0f;
            var e = Event.current;
            for (int i = 0; i < colors.Length; i++)
            {
                var dot = new Rect(r.x + i * (size + gap), r.y + (r.height - size) / 2f, size, size);
                if (e.type == EventType.Repaint)
                {
                    bool hot = i == selected || dot.Contains(e.mousePosition);
                    if (hot) Dev.Dot(new Rect(dot.x - 3f, dot.y - 3f, dot.width + 6f, dot.height + 6f), i == selected ? Dev.Ink : Dev.Shade(0.25f));
                    if (hot) Dev.Dot(new Rect(dot.x - 1.5f, dot.y - 1.5f, dot.width + 3f, dot.height + 3f), Dev.Surface(1f));
                    Dev.Dot(dot, new Color(colors[i].r, colors[i].g, colors[i].b, 1f));
                }
                int n = i;
                if (names != null && i < names.Length) Tip(dot, $"Use the {names[i]} accent.");
                if (GUI.Button(dot, GUIContent.none, GUIStyle.none) && n != selected) Later(() => set(n));
            }
            GUILayout.Space(4);
        }

        // The palette the mouse was over last frame (Layout and Repaint must draw the same text).
        private static Dev.Palette hovered, hoveredNext;

        /// <summary>
        /// The theme picker: palettes as little preview cards (light, then dark), the accents as swatches, and the
        /// style (font, particles, glow). The theme is shared by everything in Devicular.
        /// </summary>
        public static void ThemePicker()
        {
            var e = Event.current;
            if (e.type == EventType.Layout)
            {
                hovered = hoveredNext;
                hoveredNext = null;
            }
            int pal = Array.IndexOf(Dev.Palettes, Dev.Theme), acc = Array.IndexOf(Dev.Accents, Dev.AccentChoice);
            if (pal < 0) pal = 0;
            if (acc < 0) acc = 0;

            Section("Palette  \u00B7  " + Dev.Theme.Name);
            PaletteGrid(false, pal, acc);
            GUILayout.Space(3);
            PaletteGrid(true, pal, acc);
            var shown = hovered ?? Dev.Theme;
            GUILayout.Label("<b>" + shown.Name + "</b>  " + shown.Vibe, SettingHint, GUILayout.Height(32));

            Section("Accent  \u00B7  " + Dev.AccentChoice.Name);
            AccentGrid(pal, acc);

            Section("Style");
            Options("Font", Dev.FontChoice, Dev.Fonts, i => ThemeStore.SetStyle(i, Dev.ParticlesOn, Dev.GlowOn));
            Check("Particles", Dev.ParticlesOn, v => ThemeStore.SetStyle(Dev.FontChoice, v, Dev.GlowOn));
            Check("Glow & shine", Dev.GlowOn, v => ThemeStore.SetStyle(Dev.FontChoice, Dev.ParticlesOn, v));
            ButtonRow(("Surprise me", ThemeStore.Shuffle), ("Default", ThemeStore.Reset));
        }

        private static void PaletteGrid(bool dark, int current, int accent)
        {
            const int perRow = 4;
            const float h = 34f, gap = 5f;
            var list = new List<int>();
            for (int i = 0; i < Dev.Palettes.Length; i++)
                if (Dev.Palettes[i].Dark == dark) list.Add(i);
            for (int row = 0; row * perRow < list.Count; row++)
            {
                var rr = GUILayoutUtility.GetRect(10, h, GUILayout.ExpandWidth(true));
                float w = (rr.width - gap * (perRow - 1)) / perRow;
                for (int k = 0; k < perRow && row * perRow + k < list.Count; k++)
                {
                    int i = list[row * perRow + k];
                    PaletteCard(new Rect(rr.x + k * (w + gap), rr.y, w, h), Dev.Palettes[i], i == current, () => ThemeStore.Set(i, accent));
                }
                GUILayout.Space(gap);
            }
        }

        private static void PaletteCard(Rect r, Dev.Palette p, bool selected, Action pick)
        {
            var e = Event.current;
            bool hover = r.Contains(e.mousePosition);
            if (e.type == EventType.Repaint)
            {
                if (hover) hoveredNext = p;
                if (selected)
                {
                    if (Dev.GlowOn) Dev.Glow(r, Dev.Info, (Dev.Dark ? 0.5f : 0.32f) * Dev.Breath(2f));
                    cardRing.Draw(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), false, false, false, false);
                    cardRing.Draw(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), false, false, false, false);
                }
                else if (hover) cardHover.Draw(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), false, false, false, false);
                var lift = hover && !selected ? new Rect(r.x, r.y - 1f, r.width, r.height) : r;
                GUI.DrawTexture(lift, Dev.Preview(p));
                // A few of its particles, as a hint of what drifts across it.
                var pc = p.Snow;
                Dev.Dot(new Rect(lift.x + 7f, lift.y + 7f, 3f, 3f), new Color(pc.r, pc.g, pc.b, 0.95f));
                Dev.Dot(new Rect(lift.x + 13f, lift.y + 13f, 2f, 2f), new Color(pc.r, pc.g, pc.b, 0.8f));
                if (selected) Tinted(new Rect(lift.center.x - 7f, lift.center.y - 7f, 14f, 14f), check, p.Ink);
            }
            Tip(r, p.Name + ": " + p.Vibe);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !selected) Later(pick);
        }

        private static void AccentGrid(int palette, int current)
        {
            const int perRow = 7;
            const float size = 22f;
            var accents = Dev.Accents;
            var e = Event.current;
            for (int row = 0; row * perRow < accents.Length; row++)
            {
                var rr = GUILayoutUtility.GetRect(10, size + 10f, GUILayout.ExpandWidth(true));
                float gap = (rr.width - perRow * size) / (perRow - 1);
                for (int k = 0; k < perRow && row * perRow + k < accents.Length; k++)
                {
                    int i = row * perRow + k;
                    var a = accents[i];
                    var d = new Rect(rr.x + k * (size + gap), rr.y + 5f, size, size);
                    bool selected = i == current, hover = d.Contains(e.mousePosition);
                    if (e.type == EventType.Repaint)
                    {
                        if (selected && Dev.GlowOn)
                            Dev.SoftDot(new Rect(d.x - 10f, d.y - 10f, d.width + 20f, d.height + 20f), A(Color.Lerp(a.Top, a.Bottom, 0.5f), 0.6f * Dev.Breath(2.2f)));
                        if (selected || hover)
                        {
                            Dev.Dot(new Rect(d.x - 3f, d.y - 3f, d.width + 6f, d.height + 6f), selected ? Dev.Ink : Dev.Shade(0.28f));
                            Dev.Dot(new Rect(d.x - 1.5f, d.y - 1.5f, d.width + 3f, d.height + 3f), Dev.Surface(1f));
                        }
                        var dr = hover && !selected ? new Rect(d.x - 1f, d.y - 1f, d.width + 2f, d.height + 2f) : d;
                        GUI.DrawTexture(dr, Dev.AccentDot(a));
                    }
                    Tip(d, selected ? $"{a.Name} is the accent you're using." : $"Use the {a.Name} accent.");
                    int n = i;
                    if (GUI.Button(d, GUIContent.none, GUIStyle.none) && !selected) Later(() => ThemeStore.Set(palette, n));
                }
            }
            GUILayout.Space(4);
        }

        /// <summary>
        /// The corner title, all white: "Devicular's" and then the menu's name, like "Devicular's Casting Mod". It sits on
        /// a soft shadow so it reads over bright parts of the game too. The theme doesn't change it.
        /// </summary>
        public static void Brand(float x, float y, string title)
        {
            if (Event.current.type != EventType.Repaint) return;
            string text = string.IsNullOrEmpty(title) ? "Devicular's" : "Devicular's " + title;
            BrandText.normal.textColor = new Color(0f, 0f, 0f, 0.45f);
            GUI.Label(new Rect(x + 1f, y + 1.5f, 420f, 32f), text, BrandText);
            BrandText.normal.textColor = Color.white;
            GUI.Label(new Rect(x, y, 420f, 32f), text, BrandText);
        }

        public static void Key(string label, string key)
        {
            var r = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            var content = new GUIContent(key);
            float cw = Mathf.Min(KeyChip.CalcSize(content).x, r.width * 0.55f);
            KeyChip.Draw(new Rect(r.xMax - cw - 2f, r.y + 4f, cw, 18f), content, false, false, false, false);
            GUI.Label(new Rect(r.x + 2f, r.y, r.width - cw - 10f, r.height), label, SettingText);
        }
    }

    internal sealed class ClickGui
    {
        public readonly List<Column> Columns = new List<Column>();
        /// <summary>Which mod this menu belongs to, like "caster". Opening it closes any other Devicular menu.</summary>
        public readonly string Id;
        /// <summary>Shown after "Devicular's" in the corner, like "Casting Mod".</summary>
        public readonly string Title;
        private bool visible;
        /// <summary>This menu's tooltips (All-in-One: the menus share one DLL, so each keeps its own).</summary>
        public readonly Dictionary<string, string> Tips = new Dictionary<string, string>();
        public float Scale = 1f;
        public string Search = "";

        private Column dragging;
        private Vector2 dragOffset;
        private Rect searchRect;
        private float openedAt;

        public ClickGui(string id, string title)
        {
            Id = id;
            Title = title;
        }

        public bool Visible
        {
            get
            {
                // Another Devicular mod opened its menu: this one steps aside.
                if (visible && !string.Equals(Link.Get<string>("Menu.Open"), Id, StringComparison.Ordinal))
                {
                    visible = false;
                    GUIUtility.keyboardControl = 0;
                }
                return visible;
            }
            set
            {
                if (value == visible) return;
                visible = value;
                if (value)
                {
                    openedAt = Time.unscaledTime;
                    Link.Put("Menu.Open", Id);
                }
                else
                {
                    GUIUtility.keyboardControl = 0;
                    if (string.Equals(Link.Get<string>("Menu.Open"), Id, StringComparison.Ordinal)) Link.Put("Menu.Open", null);
                }
            }
        }

        public void Toggle() => Visible = !Visible;

        public void Layout()
        {
            float gap = 14f;
            float sw = Screen.width / Scale;
            int perRow = Mathf.Max(1, Mathf.Min(Columns.Count, Mathf.FloorToInt((sw - 40f + gap) / (Ui.ColumnWidth + gap))));
            for (int i = 0; i < Columns.Count; i++)
            {
                int row = i / perRow, slot = i % perRow;
                int inRow = Mathf.Min(perRow, Columns.Count - row * perRow);
                float rowWidth = inRow * Ui.ColumnWidth + (inRow - 1) * gap;
                float startX = Mathf.Max(20f, (sw - rowWidth) / 2f);
                Columns[i].Home = new Vector2(startX + slot * (Ui.ColumnWidth + gap), 96f + row * 380f);
                Columns[i].Pos = Columns[i].Home;
            }
        }

        public void ResetLayout()
        {
            Layout();
        }

        public bool Contains(Vector2 screen)
        {
            if (!Visible) return false;
            var p = screen / Scale;
            if (searchRect.Contains(p)) return true;
            foreach (var c in Columns)
                if (c.Body.Contains(p) || c.Header.Contains(p)) return true;
            return false;
        }

        private static bool Matches(Module m, string q) =>
            q.Length == 0 || m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || m.Keywords.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Builds this menu's tooltips: <paramref name="add"/> fills <see cref="Ui.Tips"/>, which is this menu's while it runs.</summary>
        public void WithTips(Action add)
        {
            var outer = Ui.Tips;
            Ui.Tips = Tips;
            try { add(); }
            finally { Ui.Tips = outer; }
        }

        public void Draw()
        {
            if (!Visible) return;
            var outerTips = Ui.Tips;
            Ui.Tips = Tips;
            try { DrawMenu(); }
            finally { Ui.Tips = outerTips; }
        }

        private void DrawMenu()
        {
            var e = Event.current;
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.18f));
            var oldColor = GUI.color;
            var oldMatrix = GUI.matrix;

            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            float sw = Screen.width / Scale;

            Ui.Brand(30f, 22f, Title);

            string searchId = Id + "-search";
            bool searching = GUI.GetNameOfFocusedControl() == searchId;
            searchRect = new Rect(sw / 2f - 190f, 18f, 380f, 36f);
            if (searching && Dev.GlowOn) Dev.Glow(searchRect, Dev.Info, 0.35f);
            if (e.type == EventType.Repaint) Ui.DrawSearchBox(searchRect, searching);
            if (e.type == EventType.Repaint) Ui.DrawMagnifier(new Rect(searchRect.x + 14, searchRect.y + 11, 15, 15), searching);
            GUI.SetNextControlName(searchId);
            Search = GUI.TextField(new Rect(searchRect.x + 38, searchRect.y + 4, searchRect.width - 50, searchRect.height - 8), Search ?? "", 40, Ui.SearchText);
            if (Search.Length == 0 && !searching)
                GUI.Label(new Rect(searchRect.x + 38, searchRect.y, searchRect.width - 50, searchRect.height), "Search modules...", Ui.SearchHint);

            HandleDrag(e);
            Ui.BeginTips();

            string q = (Search ?? "").Trim();
            foreach (var col in Columns)
            {
                var all = col.Dynamic != null ? col.Dynamic() : col.Modules;
                var mods = q.Length == 0 ? all : all.FindAll(m => Matches(m, q));
                if (q.Length > 0 && mods.Count == 0)
                {
                    col.Body = col.Header = Rect.zero;
                    continue;
                }

                // Where the panel was last drawn, moved along with a drag that happened since.
                var local = new Rect(col.Body.x - col.BodyAt.x, col.Body.y - col.BodyAt.y, col.Body.width, col.Body.height);
                if (e.type == EventType.Repaint && col.Body.width > 0)
                    Dev.Shadow(new Rect(col.Pos.x + local.x, col.Pos.y + local.y, local.width, local.height));
                GUILayout.BeginArea(new Rect(col.Pos.x, col.Pos.y, Ui.ColumnWidth, Mathf.Max(100f, Screen.height / Scale - col.Pos.y + 400f)));
                GUILayout.BeginVertical(Ui.ColumnBox, GUILayout.Width(Ui.ColumnWidth), GUILayout.MaxWidth(Ui.ColumnWidth));
                // The palette's particles drift behind the rows.
                if (e.type == EventType.Repaint && col.Body.width > 0)
                    Dev.Particles(col.ParticleKey ??= "col:" + col.Title, local, 14f, 0.75f);
                var hr = Ui.ColumnHeader(col.Title);
                if (e.type == EventType.Repaint) col.Header = new Rect(col.Pos.x + hr.x, col.Pos.y + hr.y, hr.width, hr.height);
                GUILayout.Space(6);
                foreach (var m in mods) Ui.ModuleRow(m, mods);
                if (mods.Count == 0 && !string.IsNullOrEmpty(col.Empty))
                    GUILayout.Label(col.Empty, Ui.HintCentered, GUILayout.Height(34));
                GUILayout.EndVertical();
                if (e.type == EventType.Repaint)
                {
                    var br = GUILayoutUtility.GetLastRect();
                    col.Body = new Rect(col.Pos.x + br.x, col.Pos.y + br.y, br.width, br.height);
                    col.BodyAt = col.Pos;
                }
                GUILayout.EndArea();
            }

            if (e.type == EventType.MouseDown && e.button == 0) GUIUtility.keyboardControl = 0;

            Ui.DrawTip(sw, Screen.height / Scale);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void HandleDrag(Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                for (int i = Columns.Count - 1; i >= 0; i--)
                {
                    var c = Columns[i];
                    if (!c.Header.Contains(e.mousePosition)) continue;
                    dragging = c;
                    dragOffset = e.mousePosition - c.Pos;
                    Columns.RemoveAt(i);
                    Columns.Add(c);
                    e.Use();
                    return;
                }
            }
            else if (dragging != null && e.type == EventType.MouseDrag)
            {
                dragging.Pos = e.mousePosition - dragOffset;
                dragging.Pos.x = Mathf.Clamp(dragging.Pos.x, -Ui.ColumnWidth + 60f, Screen.width / Scale - 60f);
                dragging.Pos.y = Mathf.Clamp(dragging.Pos.y, 0f, Screen.height / Scale - 40f);
                e.Use();
            }
            else if (dragging != null && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                dragging = null;
                e.Use();
            }
        }
    }
}
