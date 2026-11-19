using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace Devicular
{
    /// <summary>What drifts across the menus and panels with a palette.</summary>
    internal enum Fx
    {
        None,
        /// <summary>Flakes falling and drifting.</summary>
        Snow,
        /// <summary>Twinkling stars, a few with a soft glow.</summary>
        Stars,
        /// <summary>Glowing sparks rising and fading.</summary>
        Embers,
        /// <summary>Bubbles rising and wobbling.</summary>
        Bubbles,
        /// <summary>Petals tumbling down in the wind.</summary>
        Petals,
        /// <summary>Glowing specks wandering and pulsing.</summary>
        Fireflies
    }

    internal static class Dev
    {
        public sealed class Palette
        {
            public string Name, Vibe;
            public bool Dark;
            public Fx Effect;
            /// <summary>PanelTop/Bottom: the panel gradient. GlowA/B: the two colored lights. Snow: the particle color.</summary>
            public Color PanelTop, PanelBottom, GlowA, GlowB, Ink, Muted, Faint, Pill, Snow;
        }

        public sealed class Accent
        {
            public string Name;
            public Color Top, Bottom;
        }

        public static readonly Palette[] Palettes =
        {
            // Light
            P("Snow", false, Fx.Snow, "FFFFFF", "F6F8FB", "A9BBFF", "FFD3DE", "1F2633", "7B8794", "A7B0BC", "E6E9ED", "B4C6E6", "Clean white with soft blue and pink light, and falling snow."),
            P("Frost", false, Fx.Snow, "F3F7FF", "E6EEFB", "8FB4FF", "AEE3FF", "1B2433", "6C7F96", "9DAFC4", "DAE4F2", "A6C2EC", "Icy blue, with falling snow."),
            P("Blush", false, Fx.Petals, "FFF7F9", "FBEBF0", "FFB3C8", "FFD6BF", "2E2228", "8C7881", "BBA7B0", "F1DFE5", "F0A9BF", "Warm pink, with tumbling petals."),
            P("Mint", false, Fx.Bubbles, "F5FCF9", "E7F6EF", "96E3C2", "B3E3FF", "1D2A26", "6C887E", "9FB8AE", "DBEEE6", "7CCFB0", "Fresh green, with rising bubbles."),
            P("Sand", false, Fx.Fireflies, "FFFBF4", "F7EEDF", "FFD596", "FFBFAA", "2C261E", "887C6C", "B8AC9A", "EDE3D3", "E0A94F", "Warm sand, with drifting golden light."),
            P("Sakura", false, Fx.Petals, "FFF6F9", "FCE3EC", "FF8FB8", "FFC7A8", "3A1F2B", "9A7383", "C5A3B1", "F7D3E0", "FF8FB8", "Cherry blossom pink, with falling petals."),
            P("Lavender", false, Fx.Snow, "F9F6FF", "ECE5FB", "B69CFF", "9CD3FF", "251E38", "7D7396", "ADA4C4", "E3DAF8", "B39DF2", "Soft purple haze, with falling snow."),
            P("Lagoon", false, Fx.Bubbles, "F2FDFC", "DDF3F1", "4FD1C5", "7FB8FF", "15302E", "5F8580", "98B5B1", "D2EEEA", "4FC4B8", "Tropical water, with rising bubbles."),
            // Dark
            P("Midnight", true, Fx.Stars, "1F232B", "16191F", "4B5CC4", "8A4FB5", "ECEFF4", "9AA3AF", "687280", "2E333C", "DCE5F5", "Deep blue night, with twinkling stars."),
            P("Obsidian", true, Fx.Stars, "1A1A1F", "0D0D10", "4A4A5C", "2E2E3A", "F4F4F5", "A1A1AA", "6B6B75", "2A2A31", "C8C8D2", "Black glass. The accent does the talking."),
            P("Neon", true, Fx.Stars, "15102B", "0A0718", "00E5FF", "FF2BD6", "F2F4FF", "A7A9D0", "6E7099", "27204F", "7DF9FF", "Cyberpunk: cyan and magenta light on deep violet."),
            P("Synthwave", true, Fx.Stars, "2B1242", "13081F", "FF4FA3", "FF9E3D", "FFF0FA", "C9A8DA", "8D6CA4", "3D1D59", "FF9AD5", "Retro sunset: purple, pink and orange."),
            P("Aurora", true, Fx.Fireflies, "0F1F26", "071218", "2EE6A6", "8A5CFF", "E8FFF8", "93BDB3", "5D827B", "183238", "8CFFD6", "Northern lights: green and violet glow."),
            P("Abyss", true, Fx.Bubbles, "0B1B30", "050E1B", "1E90FF", "00D1C1", "EAF4FF", "98AFCC", "617895", "142A4A", "79CFFF", "Deep ocean blue, with rising bubbles."),
            P("Ember", true, Fx.Embers, "241614", "120A09", "FF5A1F", "FFB13B", "FFF3EC", "CCA99B", "8F7166", "3A2420", "FF9A3C", "Charcoal and fire, with rising sparks."),
            P("Crimson", true, Fx.Embers, "230F18", "11070C", "FF2D55", "A0153E", "FFEFF3", "C99BA8", "8B6370", "3B1826", "FF5C7A", "Blood red, with red sparks."),
            P("Forest", true, Fx.Fireflies, "13221B", "09140F", "4CD964", "C8E66B", "EEF8F0", "9EBBA6", "66826E", "1E3529", "C4FF8A", "Night woods, with fireflies."),
            P("Royal", true, Fx.Stars, "1A1724", "0D0B13", "E8B54A", "7B5CFF", "FFF8E7", "C2B79E", "837A69", "2C2638", "F5D27A", "Gold on black, with golden stars."),
        };

        public static readonly Accent[] Accents =
        {
            A("Grey", "A2A9B2", "737B86"),
            A("Blue", "7DB0FF", "3F7DE8"),
            A("Violet", "B39BFF", "7A5CF0"),
            A("Pink", "FF9CC0", "E25A8E"),
            A("Mint", "6FDCB2", "2DAE82"),
            A("Amber", "FFC872", "EE962C"),
            A("Cyan", "7FF3FF", "14B8D9"),
            A("Red", "FF8A8A", "E0384B"),
            A("Lime", "C8F56A", "6FC02A"),
            A("Gold", "FFE28A", "D19A12"),
            // Two-tone
            A("Sunset", "FFB347", "FF4F8B"),
            A("Ocean", "4FE3FF", "4A5BFF"),
            A("Candy", "FF8AD8", "8F6BFF"),
            A("Toxic", "D4FF4F", "14D1A0"),
            A("Inferno", "FFD23F", "FF2E3D"),
            A("Galaxy", "D07DFF", "3D5AFE"),
            A("Aurora", "5CFFB0", "8A5CFF"),
            A("Ice", "C9F4FF", "4C9BFF"),
        };

        /// <summary>Menu fonts: the game's own, Segoe UI, and Bahnschrift (Windows' sporty DIN-style font).</summary>
        public static readonly string[] Fonts = { "Classic", "Modern", "Sport" };

        private static Palette P(string name, bool dark, Fx fx, string top, string bottom, string ga, string gb, string ink, string muted, string faint, string pill, string snow, string vibe) =>
            new Palette
            {
                Name = name, Dark = dark, Effect = fx, Vibe = vibe, PanelTop = Hex(top), PanelBottom = Hex(bottom), GlowA = Hex(ga), GlowB = Hex(gb),
                Ink = Hex(ink), Muted = Hex(muted), Faint = Hex(faint), Pill = Hex(pill), Snow = Hex(snow)
            };

        private static Accent A(string name, string top, string bottom) => new Accent { Name = name, Top = Hex(top), Bottom = Hex(bottom) };

        public static Palette Theme = Palettes[0];
        public static Accent AccentChoice = Accents[0];
        public static int Version { get; private set; }

        public static Color Ink = Hex("1F2633");
        public static Color Muted = Hex("7B8794");
        public static Color Faint = Hex("A7B0BC");
        public static Color AccentTop = Hex("A2A9B2");
        public static Color AccentBottom = Hex("737B86");
        public static Color GlowBlue = Hex("A9BBFF");
        public static Color GlowPink = Hex("FFD3DE");
        public static Color SnowColor = Hex("B4C6E6");
        public static readonly Color Good = Hex("23A06F");
        public static readonly Color Bad = Hex("E0475B");
        public static readonly Color Warn = Hex("D9821A");
        /// <summary>The accent as one color you can read on this palette (text, fills, outlines).</summary>
        public static Color Info = Hex("3F7DE8");
        /// <summary>Both ends of the accent, readable on this palette, for gradients.</summary>
        public static Color InfoTop = Hex("7DB0FF"), InfoBottom = Hex("3F7DE8");
        /// <summary>Text on a solid accent fill.</summary>
        public static Color OnAccent = Color.white;
        public static bool Dark;

        /// <summary>Style settings, kept with the theme: the menu font, the particles, and glow and shine.</summary>
        public static int FontChoice = 1;
        public static bool ParticlesOn = true, GlowOn = true;

        /// <summary>The particles in use: the palette's, unless they're turned off.</summary>
        public static Fx Effect => ParticlesOn ? Theme.Effect : Fx.None;

        public static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>An accent color made light enough to read on dark panels, or dark enough on light ones.</summary>
        private static Color Readable(Color c, bool dark)
        {
            c.a = 1f;
            float l = Lum(c);
            if (dark) return l >= 0.45f ? c : Color.Lerp(c, Color.white, (0.45f - l) / Mathf.Max(0.01f, 1f - l));
            if (l <= 0.47f) return c;
            float k = 0.47f / l;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        public static void ApplyTheme(int palette, int accent, int font, bool particles, bool glow)
        {
            Theme = Palettes[Mathf.Clamp(palette, 0, Palettes.Length - 1)];
            AccentChoice = Accents[Mathf.Clamp(accent, 0, Accents.Length - 1)];
            FontChoice = Mathf.Clamp(font, 0, Fonts.Length - 1);
            ParticlesOn = particles;
            GlowOn = glow;
            Dark = Theme.Dark;
            Ink = Theme.Ink;
            Muted = Theme.Muted;
            Faint = Theme.Faint;
            GlowBlue = Theme.GlowA;
            GlowPink = Theme.GlowB;
            SnowColor = Theme.Snow;
            AccentTop = AccentChoice.Top;
            AccentBottom = AccentChoice.Bottom;
            InfoTop = Readable(AccentChoice.Top, Dark);
            InfoBottom = Readable(AccentChoice.Bottom, Dark);
            Info = Dark ? InfoTop : InfoBottom;
            OnAccent = Lum(Color.Lerp(InfoTop, InfoBottom, 0.5f)) > 0.6f ? Hex("141821") : Color.white;
            // The panel backgrounds are only drawn through WindowBackground, which remakes them, so the old ones can go.
            foreach (var t in backgrounds.Values)
                if (t != null) UnityEngine.Object.Destroy(t);
            backgrounds.Clear();
            Ready = false;
            Version++;
        }

        public static Color Shade(float a) => Dark ? new Color(1f, 1f, 1f, a * 1.15f) : new Color(0f, 0f, 0f, a);

        public static Color Surface(float a)
        {
            if (Dark) return new Color(Theme.PanelTop.r + 0.05f, Theme.PanelTop.g + 0.05f, Theme.PanelTop.b + 0.06f, a);
            var c = Color.Lerp(Theme.PanelTop, Theme.GlowA, 0.12f);
            return new Color(c.r, c.g, c.b, a);
        }

        private static Color Black(float a) => Shade(a);
        private static Color White(float a) => Surface(a);
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public const float Sidebar = 196f;
        public const int Corner = 22;

        public static bool Ready { get; private set; }
        public static GUISkin Skin;
        public static Font Regular, Semibold;

        public static GUIStyle Title, Sub, Caps, Body, Bold, Hint, Small, Clock, BigNumber, Brand,
            Card, WarnCard, Row, Primary, Ghost, SmallGhost, Chrome, SegBg, SegOn, SegOff, TabOn, TabOff,
            HudPill, Pill, ShadowStyle, Danger, CodeField, CodePlaceholder;

        private static Texture2D dot, track, trackOn, softDot, ring, indicatorEdge;
        private static readonly Dictionary<long, Texture2D> backgrounds = new Dictionary<long, Texture2D>();
        private static GUIStyle Indicator, GlowStyle;
        private const int GlowRadius = 16, GlowBlur = 22;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color OnLight(Color c)
        {
            if (c == Colors.Good) return Good;
            if (c == Colors.Bad) return Bad;
            if (c == Colors.Warn) return Warn;
            if (c == Colors.Info) return Info;
            if (c == Colors.Dim) return Muted;
            if (c == Color.white) return Ink;
            float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (Dark)
            {
                if (l >= 0.55f) return new Color(c.r, c.g, c.b, 1f);
                return Color.Lerp(new Color(c.r, c.g, c.b, 1f), Color.white, Mathf.Clamp01((0.55f - l) / Mathf.Max(0.01f, 1f - l)));
            }
            if (l <= 0.5f) return new Color(c.r, c.g, c.b, 1f);
            float k = 0.5f / l;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        public static string W(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(OnLight(c))}>{text}</color>";

        // ---------- fonts ----------

        private static readonly Dictionary<int, Font[]> fontCache = new Dictionary<int, Font[]>();

        /// <summary>Loads the chosen menu font from Windows. Classic, or a font that isn't there, keeps the game's own.</summary>
        private static void LoadFonts()
        {
            Regular = Semibold = null;
            if (FontChoice <= 0) return;
            if (!fontCache.TryGetValue(FontChoice, out var set))
            {
                set = new Font[2];
                try
                {
                    string regular = FontChoice == 1 ? "Segoe UI" : "Bahnschrift";
                    string semibold = FontChoice == 1 ? "Segoe UI Semibold" : null;
                    HashSet<string> installed = null;
                    try { installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase); }
                    catch { }
                    bool Has(string n) => installed == null || installed.Count == 0 || installed.Contains(n);
                    if (Has(regular)) set[0] = OsFont(regular);
                    if (set[0] != null && semibold != null && Has(semibold)) set[1] = OsFont(semibold);
                    if (set[0] == null) Mod.Log?.LogWarning($"Menu font: {regular} isn't installed, so the game's own font is used.");
                }
                catch (Exception e)
                {
                    Mod.Log?.LogWarning("Menu font: couldn't load it, so the game's own font is used: " + e.Message);
                    set[0] = set[1] = null;
                }
                fontCache[FontChoice] = set;
            }
            Regular = set[0];
            Semibold = set[0] != null ? set[1] : null;
        }

        private static Font OsFont(string name) => SharedFont(name, "Segoe UI", "Arial");

        private static readonly Dictionary<string, Font> osFonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A Windows font (with fallbacks), loaded once and shared by everything that uses it: the menus, the player
        /// list, the score bars. Two copies of the same font confuse the game's text cache (Unity 6), and labels drawn
        /// with the second copy then show some other text.
        /// </summary>
        public static Font SharedFont(string name, params string[] fallbacks)
        {
            if (osFonts.TryGetValue(name, out var f) && f != null) return f;
            var names = new List<string> { name };
            if (fallbacks != null) names.AddRange(fallbacks);
            try
            {
                f = Font.CreateDynamicFontFromOSFont(names.ToArray(), 16);
                if (f != null) f.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning($"Couldn't load the {name} font: {e.Message}");
                f = null;
            }
            osFonts[name] = f;
            return f;
        }

        /// <summary>Makes a style's text the strong weight: the semibold face when there is one, else bold.</summary>
        public static void Strong(GUIStyle s)
        {
            if (Semibold != null)
            {
                s.font = Semibold;
                s.fontStyle = FontStyle.Normal;
            }
            else s.fontStyle = FontStyle.Bold;
        }

        public static void Init()
        {
            if (Ready) return;
            Ready = true;

            LoadFonts();

            Skin = UnityEngine.Object.Instantiate(GUI.skin);
            Skin.hideFlags = HideFlags.HideAndDontSave;
            if (Regular != null) Skin.font = Regular;
            Skin.settings.cursorColor = Info;
            Skin.settings.selectionColor = Alpha(Info, 0.3f);

            var label = Skin.label;
            label.normal.textColor = Ink;
            label.fontSize = 13;
            label.richText = true;
            label.wordWrap = false;
            label.padding = new RectOffset(0, 0, 2, 2);
            label.margin = new RectOffset(4, 4, 2, 2);
            if (Regular != null) label.font = Regular;

            Skin.button = Make(RoundTex(11, Black(0.07f), Black(0.06f)), RoundTex(11, Alpha(Info, 0.14f), Alpha(Info, 0.4f)), RoundTex(11, Alpha(Info, 0.22f), Alpha(Info, 0.5f)), 11,
                Ink, 13, true, new RectOffset(14, 14, 7, 7));
            Skin.button.hover.textColor = Skin.button.active.textColor = Info;
            Skin.textField = Make(RoundTex(11, Black(0.05f), Black(0.08f)), RoundTex(11, Black(0.07f), Black(0.12f)), null, 11,
                Ink, 13, false, new RectOffset(10, 10, 6, 6));
            Skin.textField.focused.background = RoundTex(11, White(0.95f), Alpha(Info, 0.85f));
            Skin.textField.focused.textColor = Ink;
            Skin.textField.onFocused.background = Skin.textField.focused.background;
            Skin.textField.alignment = TextAnchor.MiddleLeft;
            Skin.box = Make(GlassTex(16, 0.55f), null, null, 16, Ink, 13, false, new RectOffset(16, 16, 12, 12));
            Skin.window = new GUIStyle(GUIStyle.none);
            Skin.scrollView = new GUIStyle(GUIStyle.none);

            var thumb = RoundTex(3, Alpha(Info, 0.45f), Color.clear);
            Skin.verticalScrollbar = new GUIStyle(GUIStyle.none) { fixedWidth = 6, margin = new RectOffset(6, 0, 2, 2) };
            Skin.verticalScrollbarThumb = new GUIStyle(GUIStyle.none) { fixedWidth = 6, border = new RectOffset(3, 3, 3, 3) };
            Skin.verticalScrollbarThumb.normal.background = thumb;
            Skin.verticalScrollbarThumb.hover.background = RoundTex(3, Alpha(Info, 0.65f), Color.clear);
            Skin.verticalScrollbarThumb.active.background = RoundTex(3, Alpha(Info, 0.85f), Color.clear);
            Skin.verticalScrollbarUpButton = new GUIStyle(GUIStyle.none) { fixedHeight = 0, fixedWidth = 0 };
            Skin.verticalScrollbarDownButton = new GUIStyle(GUIStyle.none) { fixedHeight = 0, fixedWidth = 0 };
            Skin.horizontalScrollbar = new GUIStyle(GUIStyle.none) { fixedHeight = 0 };
            Skin.horizontalScrollbarThumb = new GUIStyle(GUIStyle.none) { fixedHeight = 0 };
            Skin.horizontalScrollbarLeftButton = new GUIStyle(GUIStyle.none);
            Skin.horizontalScrollbarRightButton = new GUIStyle(GUIStyle.none);

            Body = new GUIStyle(label);
            Bold = Text(13, Ink, true);
            Title = Text(26, Ink, true);
            Brand = Text(21, Ink, true);
            Sub = Text(13, Muted, false);
            Sub.wordWrap = true;
            Caps = Text(11, Muted, true);
            Hint = Text(12, Muted, false);
            Hint.wordWrap = true;
            Small = Text(12, Ink, false);
            Small.wordWrap = true;
            Clock = Text(44, Ink, true);
            Clock.alignment = TextAnchor.MiddleLeft;
            BigNumber = Text(30, Ink, true);
            BigNumber.alignment = TextAnchor.MiddleCenter;

            Card = new GUIStyle(Skin.box) { margin = new RectOffset(0, 0, 0, 12), padding = new RectOffset(18, 18, 14, 14) };
            WarnCard = Make(RoundTex(14, new Color(0.88f, 0.28f, 0.36f, 0.12f), new Color(0.88f, 0.28f, 0.36f, 0.35f)), null, null, 14,
                Dark ? Hex("FF8A99") : Hex("B3263A"), 13, true, new RectOffset(14, 14, 10, 10));
            WarnCard.wordWrap = true;
            WarnCard.margin = new RectOffset(0, 0, 0, 10);
            Row = Make(RoundTex(12, White(0.6f), Black(0.06f)), RoundTex(12, White(0.85f), Alpha(Info, 0.3f)), null, 12,
                Ink, 13, false, new RectOffset(12, 10, 7, 7));
            Row.margin = new RectOffset(0, 0, 0, 6);

            // The main button: the accent's gradient, a little brighter under the mouse.
            Color onPrimary = Lum(Color.Lerp(AccentTop, AccentBottom, 0.5f)) > 0.66f ? Hex("141821") : Color.white;
            Primary = Make(GradientTex(11, AccentTop, AccentBottom, 64), GradientTex(11, Lerp(AccentTop, Color.white, 0.15f), Lerp(AccentBottom, Color.white, 0.15f), 64),
                GradientTex(11, Lerp(AccentTop, Color.black, 0.08f), Lerp(AccentBottom, Color.black, 0.08f), 64), 11, onPrimary, 14, true, new RectOffset(18, 18, 9, 9));
            Primary.alignment = TextAnchor.MiddleCenter;
            Ghost = new GUIStyle(Skin.button) { alignment = TextAnchor.MiddleCenter };
            SmallGhost = new GUIStyle(Skin.button) { fontSize = 12, padding = new RectOffset(10, 10, 4, 4), alignment = TextAnchor.MiddleCenter };
            Chrome = Make(RoundTex(9, Black(0.06f), Color.clear), RoundTex(9, Alpha(Info, 0.16f), Color.clear), RoundTex(9, Alpha(Info, 0.26f), Color.clear), 9,
                Ink, 16, false, new RectOffset(0, 0, 0, 2));
            Chrome.hover.textColor = Chrome.active.textColor = Info;
            Chrome.alignment = TextAnchor.MiddleCenter;

            SegBg = Make(RoundTex(10, Black(0.06f), Black(0.06f)), null, null, 10, Ink, 12, false, new RectOffset(3, 3, 3, 3));
            SegBg.margin = new RectOffset(0, 0, 2, 2);
            SegOn = Make(HGradientTex(8, InfoTop, InfoBottom, 30, 40), null, null, 8, OnAccent, 12, true, new RectOffset(10, 10, 4, 4));
            SegOn.alignment = TextAnchor.MiddleCenter;
            SegOn.margin = new RectOffset(0, 0, 0, 0);
            SegOff = new GUIStyle(SegOn);
            SegOff.normal.background = null;
            SegOff.hover.background = RoundTex(8, Black(0.06f), Color.clear);
            SegOff.active.background = SegOff.hover.background;
            SegOff.normal.textColor = Muted;
            SegOff.hover.textColor = SegOff.active.textColor = Ink;
            SegOff.font = Regular;
            SegOff.fontStyle = FontStyle.Normal;

            TabOff = Text(14, Muted, false);
            TabOff.alignment = TextAnchor.MiddleLeft;
            TabOff.padding = new RectOffset(22, 10, 0, 0);
            TabOff.hover.textColor = Ink;
            TabOn = Text(14, Ink, true);
            TabOn.alignment = TextAnchor.MiddleLeft;
            TabOn.padding = new RectOffset(22, 10, 0, 0);

            HudPill = Make(GlassTex(20, 0.94f), null, null, 20, Ink, 18, true, new RectOffset(18, 18, 0, 0));
            HudPill.alignment = TextAnchor.MiddleCenter;
            Pill = Make(RoundTex(15, White(0.95f), Black(0.08f)), RoundTex(15, Surface(1f), Alpha(Info, 0.4f)), RoundTex(15, Theme.Pill, Alpha(Info, 0.5f)), 15,
                Ink, 13, true, new RectOffset(14, 14, 0, 0));
            Pill.alignment = TextAnchor.MiddleCenter;

            var red = Hex("E0475B");
            Danger = Make(RoundTex(11, new Color(red.r, red.g, red.b, 0.08f), new Color(red.r, red.g, red.b, 0.18f)),
                RoundTex(11, new Color(red.r, red.g, red.b, 0.14f), new Color(red.r, red.g, red.b, 0.26f)),
                RoundTex(11, new Color(red.r, red.g, red.b, 0.2f), new Color(red.r, red.g, red.b, 0.3f)), 11, Dark ? Hex("FF8A99") : Hex("C7364A"), 13, true, new RectOffset(14, 14, 7, 7));
            Danger.alignment = TextAnchor.MiddleCenter;

            CodeField = new GUIStyle(Skin.textField) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            CodePlaceholder = Text(16, Faint, true);
            CodePlaceholder.alignment = TextAnchor.MiddleCenter;

            ShadowStyle = new GUIStyle(GUIStyle.none) { border = new RectOffset(Corner + 24, Corner + 24, Corner + 24, Corner + 24) };
            ShadowStyle.normal.background = ShadowTex(Corner, 24, Dark ? 0.3f : 0.16f, new Color(0.12f, 0.15f, 0.2f));
            GlowStyle = new GUIStyle(GUIStyle.none) { border = new RectOffset(GlowRadius + GlowBlur, GlowRadius + GlowBlur, GlowRadius + GlowBlur, GlowRadius + GlowBlur) };
            GlowStyle.normal.background = ShadowTex(GlowRadius, GlowBlur, 1f, Color.white);

            dot = CircleTex(32);
            softDot = SoftDotTex(32);
            ring = RingTex(32, 2.2f);
            track = RoundTex(11, Black(0.12f), Color.clear, 22, 40);
            trackOn = HGradientTex(11, InfoTop, InfoBottom, 22, 40);
            Indicator = new GUIStyle(GUIStyle.none) { border = new RectOffset(12, 12, 12, 12) };
            Indicator.normal.background = HGradientTex(12, Lerp(Theme.Pill, Info, Dark ? 0.22f : 0.14f), Theme.Pill, 42, 64, Alpha(Info, 0.35f));
            indicatorEdge = VGradientTex(2, InfoTop, InfoBottom, 24, 4);
        }

        private static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, t);

        private static GUIStyle Text(int size, Color color, bool semibold)
        {
            var s = new GUIStyle(Skin.label) { fontSize = size, richText = true };
            s.normal.textColor = color;
            if (semibold) Strong(s);
            return s;
        }

        private static GUIStyle Make(Texture2D normal, Texture2D hover, Texture2D active, int radius, Color text, int size, bool semibold, RectOffset padding)
        {
            var s = new GUIStyle(GUIStyle.none)
            {
                border = new RectOffset(radius, radius, radius, radius),
                padding = padding,
                margin = new RectOffset(4, 4, 3, 3),
                fontSize = size,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                font = semibold && Semibold != null ? Semibold : Regular,
                fontStyle = semibold && Semibold == null ? FontStyle.Bold : FontStyle.Normal,
                clipping = TextClipping.Clip
            };
            s.normal.background = normal;
            s.hover.background = hover ?? normal;
            s.active.background = active ?? hover ?? normal;
            s.focused.background = normal;
            s.onNormal.background = normal;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = s.onNormal.textColor = text;
            return s;
        }

        // ---------- textures ----------

        private static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static float RoundDist(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r);
            float qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Color Over(Color top, Color bottom)
        {
            float a = top.a + bottom.a * (1f - top.a);
            if (a <= 0f) return Color.clear;
            var c = (top * top.a + bottom * bottom.a * (1f - top.a)) / a;
            c.a = a;
            return c;
        }

        /// <summary>A rounded rectangle. <paramref name="fill"/> may vary across it (see the gradient makers).</summary>
        private static Texture2D Shape(int r, int w, int h, Func<int, int, Color> fill, Color stroke, Color highlight = default)
        {
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = RoundDist(x + 0.5f, y + 0.5f, w, h, r);
                    float cover = Mathf.Clamp01(0.5f - d);
                    float inner = Mathf.Clamp01(0.5f - (d + 1f));
                    var f = fill(x, y);
                    f.a *= inner;
                    // A thin light line just inside the top edge, like light catching glass.
                    if (highlight.a > 0f && y >= h - 3 && y < h - 1)
                        f = Over(new Color(highlight.r, highlight.g, highlight.b, highlight.a * inner * (y == h - 2 ? 1f : 0.45f)), f);
                    var s = stroke;
                    s.a *= Mathf.Clamp01(cover - inner);
                    px[y * w + x] = Over(s, f);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public static Texture2D RoundTex(int r, Color fill, Color stroke, int height = 0, int width = 0)
        {
            int w = Math.Max(2 * r + 4, width), h = Math.Max(2 * r + 4, height);
            return Shape(r, w, h, (x, y) => fill, stroke);
        }

        /// <summary>A rounded rectangle shading from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
        public static Texture2D GradientTex(int r, Color top, Color bottom, int height, int width = 0)
        {
            int w = Math.Max(2 * r + 4, width), h = Math.Max(2 * r + 4, height);
            return Shape(r, w, h, (x, y) => Color.Lerp(bottom, top, (y + 0.5f) / h), Color.clear);
        }

        /// <summary>The same, shading from <paramref name="top"/> to <paramref name="bottom"/> with a stroke.</summary>
        public static Texture2D VGradientTex(int r, Color top, Color bottom, int height, int width = 0, Color stroke = default)
        {
            int w = Math.Max(2 * r + 4, width), h = Math.Max(2 * r + 4, height);
            return Shape(r, w, h, (x, y) => Color.Lerp(bottom, top, (y + 0.5f) / h), stroke);
        }

        /// <summary>A rounded rectangle shading from <paramref name="left"/> to <paramref name="right"/>.</summary>
        public static Texture2D HGradientTex(int r, Color left, Color right, int height, int width, Color stroke = default)
        {
            int w = Math.Max(2 * r + 4, width), h = Math.Max(2 * r + 4, height);
            return Shape(r, w, h, (x, y) => Color.Lerp(left, right, (x + 0.5f) / w), stroke);
        }

        /// <summary>
        /// A glass panel: lit a little at the top and tinted by the palette's lights toward the bottom, with a fine
        /// edge and a highlight along the top. <paramref name="edge"/>, when given, colors the edge (like an outline
        /// in the accent color while you type).
        ///
        /// The texture is stretched around its rounded corners (<paramref name="r"/> pixels at each end stay as they
        /// are), so its height decides where the shading goes. The default, just the two corners, shades smoothly on
        /// a box of any height. <paramref name="height"/> is only for boxes always taller than it (the menu panels),
        /// where it spreads the shading over the whole panel; on a shorter box it would show as a hard line through
        /// the middle. <paramref name="flat"/> leaves out the shading altogether.
        /// </summary>
        public static Texture2D GlassTex(int r, float alpha, Color edge = default, int height = 0, bool flat = false)
        {
            Color top, bottom, stroke;
            if (Dark)
            {
                var t = Lerp(Theme.PanelTop, GlowBlue, 0.07f);
                top = new Color(t.r + 0.07f, t.g + 0.07f, t.b + 0.08f, alpha);
                var b = Lerp(Theme.PanelBottom, GlowPink, 0.05f);
                bottom = new Color(b.r + 0.035f, b.g + 0.035f, b.b + 0.04f, alpha);
                // A dark edge: a white one read as a light line along the top.
                stroke = new Color(0f, 0f, 0f, 0.3f);
            }
            else
            {
                top = Alpha(Lerp(Theme.PanelTop, GlowBlue, 0.08f), alpha);
                bottom = Alpha(Lerp(Theme.PanelBottom, GlowPink, 0.1f), alpha);
                stroke = new Color(0f, 0f, 0f, 0.075f);
            }
            if (edge.a > 0f) stroke = edge;
            if (flat) top = bottom = Color.Lerp(bottom, top, 0.5f);
            int size = Math.Max(2 * r + 4, height);
            // No light line along the top edge: it showed as a white strip at the top of every panel.
            return Shape(r, 2 * r + 4, size, (x, y) => Color.Lerp(bottom, top, (y + 0.5f) / size), stroke);
        }

        private static Texture2D ShadowTex(int r, int blur, float alpha, Color color)
        {
            int size = 2 * (r + blur) + 4;
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float inner = size - 2f * blur;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundDist(x + 0.5f - blur, y + 0.5f - blur, inner, inner, r);
                    float t = Mathf.Clamp01(d / blur);
                    float a = alpha * (1f - t) * (1f - t);
                    px[y * size + x] = new Color(color.r, color.g, color.b, d <= 0 ? alpha : a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D CircleTex(int size)
        {
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) - (c - 1f);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A round soft light: bright in the middle, fading out to nothing.</summary>
        private static Texture2D SoftDotTex(int size)
        {
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D RingTex(int size, float width)
        {
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float c = size / 2f, rr = c - width / 2f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) - rr);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(width / 2f + 0.5f - d));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public static Texture2D WindowBackground(int w, int h, bool sidebar = true)
        {
            long key = ((long)w << 32) | ((long)h << 1) | (sidebar ? 1L : 0L);
            if (backgrounds.TryGetValue(key, out var cached) && cached != null) return cached;
            var windowBg = NewTex(w, h);
            backgrounds[key] = windowBg;
            var px = new Color32[w * h];
            var top = Theme.PanelTop;
            var bottom = Theme.PanelBottom;
            float ar = Mathf.Min(260f, Mathf.Max(w, h) * 0.55f), br = Mathf.Min(230f, Mathf.Max(w, h) * 0.5f);
            float ax = w - ar * 0.44f, ay = h - ar * 0.28f;
            float bx = br * 0.6f, by = br * 0.4f;
            float glowK = Dark ? 0.8f : 1f;
            int side = sidebar ? (int)Sidebar : -1;
            for (int y = 0; y < h; y++)
            {
                var row = Color.Lerp(bottom, top, (float)y / h);
                for (int x = 0; x < w; x++)
                {
                    float d = RoundDist(x + 0.5f, y + 0.5f, w, h, Corner);
                    if (d > 0.5f)
                    {
                        px[y * w + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    var c = row;
                    float ga = Glow(x - ax, y - ay, ar) * 0.42f;
                    float gb = Glow(x - bx, y - by, br) * 0.45f;
                    c = Color.Lerp(c, GlowBlue, ga * glowK);
                    c = Color.Lerp(c, GlowPink, gb * glowK);
                    if (x < side) c = Color.Lerp(c, Color.white, Dark ? 0.03f : 0.25f);
                    if (x == side) c = Color.Lerp(c, Dark ? Color.white : Color.black, 0.07f);
                    float edge = Mathf.Clamp01(0.5f - d) - Mathf.Clamp01(0.5f - (d + 1f));
                    c = Color.Lerp(c, Dark ? Lerp(Info, Color.white, 0.3f) : new Color(0.55f, 0.6f, 0.68f), edge * (Dark ? 0.3f : 0.35f));
                    c.a = 0.965f * Mathf.Clamp01(0.5f - d);
                    px[y * w + x] = c;
                }
            }
            windowBg.SetPixels32(px);
            windowBg.Apply();
            return windowBg;
        }

        private static float Glow(float dx, float dy, float r)
        {
            float t = (dx * dx + dy * dy) / (r * r);
            return Mathf.Exp(-2.2f * t);
        }

        // ---------- the theme picker's previews ----------

        private static readonly Dictionary<Palette, Texture2D> previews = new Dictionary<Palette, Texture2D>();
        private static readonly Dictionary<Accent, Texture2D> accentDots = new Dictionary<Accent, Texture2D>();

        /// <summary>A small card showing a palette: its panel gradient and its two lights. Made once.</summary>
        public static Texture2D Preview(Palette p)
        {
            if (previews.TryGetValue(p, out var t) && t != null) return t;
            const int w = 112, h = 52, r = 10;
            float k = p.Dark ? 0.85f : 1f;
            t = Shape(r, w, h, (x, y) =>
            {
                var c = Color.Lerp(p.PanelBottom, p.PanelTop, (y + 0.5f) / h);
                c = Color.Lerp(c, p.GlowA, Glow(x - w * 0.9f, y - h * 0.1f, 48f) * 0.75f * k);
                c = Color.Lerp(c, p.GlowB, Glow(x - w * 0.12f, y - h * 0.95f, 42f) * 0.7f * k);
                return c;
            }, p.Dark ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.1f), p.Dark ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.8f));
            previews[p] = t;
            return t;
        }

        /// <summary>A round swatch of an accent: its two colors corner to corner, with a little shine. Made once.</summary>
        public static Texture2D AccentDot(Accent a)
        {
            if (accentDots.TryGetValue(a, out var t) && t != null) return t;
            const int size = 44;
            t = NewTex(size, size);
            var px = new Color[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - (c - 1f);
                    // Top-left is Top, bottom-right is Bottom (texture rows count up from the bottom).
                    float g = Mathf.Clamp01(((x + 0.5f) / size + (1f - (y + 0.5f) / size)) / 2f);
                    var col = Color.Lerp(a.Top, a.Bottom, g);
                    float shine = Glow(dx + c * 0.35f, dy - c * 0.4f, c * 0.55f) * 0.35f;
                    col = Color.Lerp(col, Color.white, shine);
                    col.a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = col;
                }
            t.SetPixels(px);
            t.Apply();
            accentDots[a] = t;
            return t;
        }

        // ---------- drawing helpers ----------

        public static void Dot(Rect r, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.DrawTexture(r, dot);
            GUI.color = old;
        }

        /// <summary>A soft round light, for glows and sparkles.</summary>
        public static void SoftDot(Rect r, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.DrawTexture(r, softDot);
            GUI.color = old;
        }

        private static void Ring(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.DrawTexture(r, ring);
            GUI.color = old;
        }

        public static void Swatch(Color color, float size = 14f)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            r.y += 3f;
            Dot(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0f, 0f, 0f, 0.18f));
            Dot(r, new Color(color.r, color.g, color.b, 1f));
        }

        private static readonly Dictionary<string, float> anim = new Dictionary<string, float>();

        private static float Animate(string key, float target)
        {
            anim.TryGetValue(key, out float v);
            if (!anim.ContainsKey(key)) v = target;
            if (Event.current.type == EventType.Repaint)
                v = Mathf.MoveTowards(v, target, Time.unscaledDeltaTime * 7f);
            anim[key] = v;
            return v;
        }

        public static bool Switch(bool value, string label, float width = 0f)
        {
            if (width > 0f) GUILayout.BeginHorizontal(GUILayout.Width(width));
            else GUILayout.BeginHorizontal();
            GUILayout.Label(label, Body, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            var tr = GUILayoutUtility.GetRect(40, 22, GUILayout.Width(40), GUILayout.Height(22));
            GUILayout.EndHorizontal();
            var row = GUILayoutUtility.GetLastRect();

            tr.y += 1f;
            float t = Animate(label, value ? 1f : 0f);
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.DrawTexture(tr, track);
                if (t > 0f && GlowOn) SoftDot(new Rect(tr.x - 12f, tr.y - 12f, tr.width + 24f, tr.height + 24f), Alpha(Info, 0.35f * t));
                GUI.color = new Color(1f, 1f, 1f, old.a * t);
                GUI.DrawTexture(tr, trackOn);
                GUI.color = old;
                var knob = new Rect(tr.x + 3f + 18f * t, tr.y + 3f, 16f, 16f);
                Dot(new Rect(knob.x, knob.y + 1f, knob.width, knob.height), new Color(0f, 0f, 0f, 0.18f));
                Dot(knob, Color.white);
            }
            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) value = !value;
            return value;
        }

        public static int Segmented(int selected, string[] options, float itemWidth)
        {
            int result = selected;
            GUILayout.BeginHorizontal(SegBg, GUILayout.ExpandWidth(false));
            for (int i = 0; i < options.Length; i++)
                if (GUILayout.Button(options[i], i == selected ? SegOn : SegOff, GUILayout.Width(itemWidth), GUILayout.Height(26)))
                    result = i;
            GUILayout.EndHorizontal();
            return result;
        }

        public static void DrawIndicator(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (GlowOn) Glow(r, Info, Dark ? 0.22f : 0.12f);
            Indicator.Draw(r, false, false, false, false);
            GUI.DrawTexture(new Rect(r.x + 6f, r.y + 11f, 3f, r.height - 22f), indicatorEdge);
        }

        /// <summary>A gently breathing strength for glows: 1 give or take a little.</summary>
        public static float Breath(float speed = 1.6f, float phase = 0f) => 0.84f + 0.16f * Mathf.Sin(Time.unscaledTime * speed + phase);

        /// <summary>A soft colored light around a rounded rectangle. <paramref name="spread"/> scales how far it reaches.</summary>
        public static void Glow(Rect r, Color color, float strength, float spread = 1f)
        {
            if (Event.current.type != EventType.Repaint || strength <= 0.001f || GlowStyle == null) return;
            float b = GlowBlur * Mathf.Clamp(spread, 0.2f, 2f);
            // The glow texture's corners need room: on a small rectangle, reach out further instead of squashing them.
            b = Mathf.Max(b, (2f * (GlowRadius + GlowBlur) - Mathf.Min(r.width, r.height)) / 2f + 1f);
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(strength) * old.a);
            GlowStyle.Draw(new Rect(r.x - b, r.y - b, r.width + 2f * b, r.height + 2f * b), false, false, false, false);
            GUI.color = old;
        }

        /// <summary>The shadow under a panel, with the accent's glow around it when Glow and shine is on.</summary>
        public static void Shadow(Rect window)
        {
            if (Event.current.type != EventType.Repaint) return;
            var r = new Rect(window.x - 24f, window.y - 20f, window.width + 48f, window.height + 50f);
            ShadowStyle.Draw(r, false, false, false, false);
            if (GlowOn) Glow(window, Info, (Dark ? 0.4f : 0.2f) * Breath(1.6f, window.x * 0.013f));
        }

        // ---------- particles ----------

        private struct Particle
        {
            public float X, Y, Size, Speed, Drift, Phase, Alpha, Tint;
        }

        private sealed class Field
        {
            public Fx Kind;
            public Particle[] P = new Particle[0];
        }

        private static readonly Dictionary<string, Field> fields = new Dictionary<string, Field>();
        private static readonly System.Random Rng = new System.Random();

        private static float R(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

        /// <summary>A new particle. X is a fraction of the width, Y is in pixels from the top.</summary>
        private static Particle Spawn(Fx kind, float h, bool anywhere)
        {
            bool near = Rng.NextDouble() < 0.35;
            var p = new Particle { X = R(0f, 1f), Phase = R(0f, 6.28f), Tint = R(0f, 1f) };
            switch (kind)
            {
                case Fx.Snow:
                    p.Y = anywhere ? R(0f, h) : -8f;
                    p.Size = near ? R(3.5f, 6f) : R(1.8f, 3.4f);
                    p.Speed = near ? R(26f, 48f) : R(12f, 24f);
                    p.Drift = R(6f, 20f);
                    p.Alpha = near ? 0.75f : 0.5f;
                    break;
                case Fx.Stars:
                    p.Y = R(0f, h);
                    p.Size = near ? R(2.4f, 3.6f) : R(1.2f, 2.2f);
                    // Near stars drift faster than far ones, so the field has depth and visibly moves.
                    p.Speed = near ? R(16f, 28f) : R(7f, 14f);
                    p.Drift = R(3f, 8f);
                    p.Alpha = near ? 0.95f : 0.6f;
                    break;
                case Fx.Embers:
                    p.Y = anywhere ? R(0f, h) : h + 6f;
                    p.Size = near ? R(2.4f, 3.8f) : R(1.4f, 2.4f);
                    p.Speed = near ? R(30f, 55f) : R(16f, 30f);
                    p.Drift = R(6f, 16f);
                    p.Alpha = near ? 0.95f : 0.7f;
                    break;
                case Fx.Bubbles:
                    p.Y = anywhere ? R(0f, h) : h + 10f;
                    p.Size = near ? R(7f, 12f) : R(4f, 7f);
                    p.Speed = near ? R(16f, 28f) : R(9f, 16f);
                    p.Drift = R(3f, 9f);
                    p.Alpha = near ? 0.6f : 0.42f;
                    break;
                case Fx.Petals:
                    p.Y = anywhere ? R(0f, h) : -10f;
                    p.Size = near ? R(5f, 7.5f) : R(3.2f, 5f);
                    p.Speed = near ? R(18f, 32f) : R(10f, 18f);
                    p.Drift = R(10f, 26f);
                    p.Alpha = near ? 0.85f : 0.6f;
                    break;
                case Fx.Fireflies:
                    p.Y = R(0f, h);
                    p.Size = near ? R(2.6f, 3.6f) : R(1.6f, 2.6f);
                    p.Speed = R(0.6f, 1.6f);
                    p.Drift = R(10f, 26f);
                    p.Alpha = near ? 1f : 0.75f;
                    break;
            }
            return p;
        }

        /// <summary>
        /// The palette's particles drifting across <paramref name="area"/> (a rounded rectangle with corner
        /// <paramref name="radius"/>), behind whatever is drawn next. Each <paramref name="key"/> keeps its own particles.
        /// </summary>
        public static void Particles(string key, Rect area, float radius, float density = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            var kind = Effect;
            if (kind == Fx.None || area.width < 20f || area.height < 20f) return;
            float w = area.width, h = area.height;
            if (!fields.TryGetValue(key, out var field)) fields[key] = field = new Field();
            // Capped, so a big panel (the referee's) doesn't draw so many that the game slows down.
            int want = Mathf.Clamp(Mathf.RoundToInt(w * h / 12000f * density), 4, 45);
            if (field.Kind != kind)
            {
                field.Kind = kind;
                field.P = new Particle[0];
            }
            if (field.P.Length != want)
            {
                int had = field.P.Length;
                Array.Resize(ref field.P, want);
                for (int i = had; i < want; i++) field.P[i] = Spawn(kind, h, true);
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float time = Time.unscaledTime;
            var baseColor = SnowColor;
            var hot = Lerp(SnowColor, Color.white, 0.55f);
            var ps = field.P;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                float x, y, a = p.Alpha;
                switch (kind)
                {
                    case Fx.Snow:
                        p.Y += p.Speed * dt;
                        if (p.Y > h + 8f) p = Spawn(kind, h, false);
                        x = p.X * w + Mathf.Sin(time * 0.6f + p.Phase) * p.Drift;
                        y = p.Y;
                        if (!Inside(area, x, y, radius, p.Size)) break;
                        Dot(Centered(area, x, y, p.Size, p.Size), Alpha(baseColor, a));
                        break;

                    case Fx.Stars:
                        p.Y -= p.Speed * dt;
                        if (p.Y < -4f)
                        {
                            p = Spawn(kind, h, true);
                            p.Y = h + 2f;
                        }
                        x = p.X * w + Mathf.Sin(time * 0.5f + p.Phase) * p.Drift;
                        y = p.Y;
                        if (!Inside(area, x, y, radius, p.Size)) break;
                        float tw = 0.5f + 0.5f * Mathf.Sin(time * (1.6f + p.Tint * 2.4f) + p.Phase);
                        tw = 0.3f + 0.7f * tw * tw;
                        if (p.Size > 2.3f)
                        {
                            SoftDot(Centered(area, x, y, p.Size * 5f, p.Size * 5f), Alpha(baseColor, a * tw * 0.35f));
                            // A tiny cross of light on only the brightest few (each one costs two more draws).
                            if (p.Tint > 0.8f)
                            {
                                SoftDot(Centered(area, x, y, p.Size * 6f, 1.2f), Alpha(hot, a * tw * 0.5f));
                                SoftDot(Centered(area, x, y, 1.2f, p.Size * 6f), Alpha(hot, a * tw * 0.5f));
                            }
                        }
                        Dot(Centered(area, x, y, p.Size, p.Size), Alpha(Lerp(baseColor, Color.white, 0.4f), a * tw));
                        break;

                    case Fx.Embers:
                        p.Y -= p.Speed * dt;
                        if (p.Y < -8f) p = Spawn(kind, h, false);
                        x = p.X * w + Mathf.Sin(time * 1.3f + p.Phase) * p.Drift;
                        y = p.Y;
                        if (!Inside(area, x, y, radius, p.Size)) break;
                        float life = Mathf.Clamp01(p.Y / (h * 0.85f));
                        float flicker = 0.65f + 0.35f * Mathf.Sin(time * (7f + p.Tint * 6f) + p.Phase);
                        var ember = Lerp(baseColor, GlowPink, p.Tint * 0.6f);
                        SoftDot(Centered(area, x, y, p.Size * 5.5f, p.Size * 5.5f), Alpha(ember, a * life * flicker * 0.45f));
                        Dot(Centered(area, x, y, p.Size, p.Size), Alpha(Lerp(ember, Color.white, 0.35f), a * life * flicker));
                        break;

                    case Fx.Bubbles:
                        p.Y -= p.Speed * dt;
                        if (p.Y < -12f) p = Spawn(kind, h, false);
                        x = p.X * w + Mathf.Sin(time * 1.1f + p.Phase) * p.Drift;
                        y = p.Y;
                        if (!Inside(area, x, y, radius, p.Size)) break;
                        float wob = 1f + 0.06f * Mathf.Sin(time * 4f + p.Phase);
                        Ring(Centered(area, x, y, p.Size * wob, p.Size / wob), Alpha(baseColor, a));
                        Dot(Centered(area, x - p.Size * 0.18f, y - p.Size * 0.2f, p.Size * 0.26f, p.Size * 0.26f), Alpha(Color.white, a * 0.9f));
                        break;

                    case Fx.Petals:
                        p.Y += p.Speed * dt;
                        p.X += 0.012f * dt * (0.5f + p.Tint);
                        if (p.X > 1.05f) p.X -= 1.1f;
                        if (p.Y > h + 10f) p = Spawn(kind, h, false);
                        x = p.X * w + Mathf.Sin(time * 0.9f + p.Phase) * p.Drift;
                        y = p.Y;
                        if (!Inside(area, x, y, radius, p.Size)) break;
                        float flip = 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(time * (1.6f + p.Tint) + p.Phase));
                        var petal = Lerp(baseColor, Color.white, p.Tint * 0.45f);
                        Dot(Centered(area, x, y, p.Size * 1.6f * flip, p.Size), Alpha(petal, a));
                        break;

                    case Fx.Fireflies:
                        x = p.X * w + Mathf.Sin(time * 0.37f * p.Speed + p.Phase) * p.Drift * 1.6f + Mathf.Sin(time * 0.83f + p.Phase * 1.7f) * p.Drift * 0.6f;
                        y = p.Y + Mathf.Cos(time * 0.29f * p.Speed + p.Phase) * p.Drift * 1.4f;
                        if (!Inside(area, x, y, radius, p.Size)) break;
                        float pulse = 0.5f + 0.5f * Mathf.Sin(time * (1.4f + p.Tint) + p.Phase);
                        pulse = 0.12f + 0.88f * pulse * pulse * pulse;
                        SoftDot(Centered(area, x, y, p.Size * 7f, p.Size * 7f), Alpha(baseColor, a * pulse * (Dark ? 0.55f : 0.45f)));
                        Dot(Centered(area, x, y, p.Size, p.Size), Alpha(Lerp(baseColor, Color.white, Dark ? 0.5f : 0.1f), a * pulse));
                        break;
                }
                ps[i] = p;
            }
        }

        private static bool Inside(Rect area, float x, float y, float radius, float size) =>
            RoundDist(x, y, area.width, area.height, radius) <= -size;

        private static Rect Centered(Rect area, float x, float y, float w, float h) =>
            new Rect(area.x + x - w / 2f, area.y + y - h / 2f, w, h);

        /// <summary>The palette's particles inside a window drawn at (0, 0) with the standard corner.</summary>
        public static void Snow(float w, float h) => Particles("window", new Rect(0f, 0f, w, h), Corner, 0.85f);
    }

    /// <summary>
    /// The theme every Devicular mod uses, kept in one file (BepInEx\config\devicular.theme.cfg): the palette, the
    /// accent, the menu font, the particles and glow. Changing it in the menu changes it everywhere straight away.
    /// </summary>
    internal static class ThemeStore
    {
        private const string FileName = "devicular.theme.cfg";
        private static string applied;
        private static readonly System.Random Rng = new System.Random();

        private static string FilePath => Path.Combine(Paths.ConfigPath, FileName);

        private static int PaletteIndex => Mathf.Max(0, Array.IndexOf(Dev.Palettes, Dev.Theme));
        private static int AccentIndex => Mathf.Max(0, Array.IndexOf(Dev.Accents, Dev.AccentChoice));

        /// <summary>Called once when the mod loads.</summary>
        public static void Load()
        {
            string shared = Link.Get<string>("Theme");
            if (shared == null)
            {
                shared = Read();
                Link.Put("Theme", shared);
                // The file gets the new settings (font, particles, glow) the first time.
                Write(shared);
            }
            Apply(shared);
        }

        /// <summary>Called every frame: picks up a theme another mod chose.</summary>
        public static void Poll()
        {
            string shared = Link.Get<string>("Theme");
            if (shared != null && !ReferenceEquals(shared, applied) && shared != applied) Apply(shared);
        }

        /// <summary>Picks a palette and accent, keeping the font, particles and glow.</summary>
        public static void Set(int palette, int accent) => Save(palette, accent, Dev.FontChoice, Dev.ParticlesOn, Dev.GlowOn);

        /// <summary>Picks the font, particles and glow, keeping the palette and accent.</summary>
        public static void SetStyle(int font, bool particles, bool glow) => Save(PaletteIndex, AccentIndex, font, particles, glow);

        /// <summary>
        /// Picks the whole theme by name (from a saved config). A name that isn't known keeps what's in use; nothing
        /// changes when it's the same theme already.
        /// </summary>
        public static void SetByName(string palette, string accent, string font, bool particles, bool glow)
        {
            int p = Array.FindIndex(Dev.Palettes, x => string.Equals(x.Name, palette, StringComparison.OrdinalIgnoreCase));
            int a = Array.FindIndex(Dev.Accents, x => string.Equals(x.Name, accent, StringComparison.OrdinalIgnoreCase));
            int f = Array.FindIndex(Dev.Fonts, x => string.Equals(x, font, StringComparison.OrdinalIgnoreCase));
            if (p < 0) p = PaletteIndex;
            if (a < 0) a = AccentIndex;
            if (f < 0) f = Dev.FontChoice;
            if (p == PaletteIndex && a == AccentIndex && f == Dev.FontChoice && particles == Dev.ParticlesOn && glow == Dev.GlowOn) return;
            Save(p, a, f, particles, glow);
        }

        /// <summary>A random palette and accent, different from the ones in use.</summary>
        public static void Shuffle()
        {
            int p = PaletteIndex, a = AccentIndex;
            if (Dev.Palettes.Length > 1)
                while (p == PaletteIndex) p = Rng.Next(Dev.Palettes.Length);
            if (Dev.Accents.Length > 1)
                while (a == AccentIndex) a = Rng.Next(Dev.Accents.Length);
            Set(p, a);
        }

        /// <summary>Back to Snow and Grey, with the Modern font, particles and glow.</summary>
        public static void Reset() => Save(0, 0, 1, true, true);

        private static void Save(int palette, int accent, int font, bool particles, bool glow)
        {
            palette = Mathf.Clamp(palette, 0, Dev.Palettes.Length - 1);
            accent = Mathf.Clamp(accent, 0, Dev.Accents.Length - 1);
            font = Mathf.Clamp(font, 0, Dev.Fonts.Length - 1);
            string value = Dev.Palettes[palette].Name + "|" + Dev.Accents[accent].Name + "|" + Dev.Fonts[font] + "|" +
                           (particles ? "On" : "Off") + "|" + (glow ? "On" : "Off");
            Link.Put("Theme", value);
            Apply(value);
            Write(value);
        }

        private static string Names<T>(T[] items, Func<T, string> name)
        {
            var list = new List<string>();
            foreach (var i in items) list.Add(name(i));
            return string.Join(", ", list);
        }

        private static void Write(string value)
        {
            string[] parts = (value ?? "").Split('|');
            string Part(int i, string fallback) => parts.Length > i && parts[i].Trim().Length > 0 ? parts[i].Trim() : fallback;
            try
            {
                File.WriteAllText(FilePath,
                    "## The menu and overlay theme for Devicular (Caster, Replay, Auto Ref). Pick it in the menu: Tab > Client > Themes.\n" +
                    "## Palette: " + Names(Dev.Palettes, p => p.Name) + ".\n" +
                    "## Accent: " + Names(Dev.Accents, a => a.Name) + ".\n" +
                    "## Font: Classic (the game's own), Modern (Segoe UI) or Sport (Bahnschrift).\n" +
                    "## Particles and Glow: On or Off.\n\n" +
                    "Palette = " + Part(0, "Snow") + "\nAccent = " + Part(1, "Grey") + "\nFont = " + Part(2, "Modern") +
                    "\nParticles = " + Part(3, "On") + "\nGlow = " + Part(4, "On") + "\n");
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning("Couldn't save the theme: " + e.Message);
            }
        }

        private static string Read()
        {
            string palette = null, accent = null, font = null, particles = null, glow = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                        if (key.Equals("Palette", StringComparison.OrdinalIgnoreCase)) palette = value;
                        else if (key.Equals("Accent", StringComparison.OrdinalIgnoreCase)) accent = value;
                        else if (key.Equals("Font", StringComparison.OrdinalIgnoreCase)) font = value;
                        else if (key.Equals("Particles", StringComparison.OrdinalIgnoreCase)) particles = value;
                        else if (key.Equals("Glow", StringComparison.OrdinalIgnoreCase)) glow = value;
                    }
                }
            }
            catch
            {
            }
            // Before the split, the theme was in the all-in-one Devicular settings.
            palette ??= Legacy.Get("Theme", "Palette") ?? "Snow";
            accent ??= Legacy.Get("Theme", "Accent") ?? "Grey";
            return palette + "|" + accent + "|" + (font ?? "Modern") + "|" + (particles ?? "On") + "|" + (glow ?? "On");
        }

        private static bool IsOn(string[] parts, int i) =>
            parts.Length <= i || !(parts[i].Trim().Equals("Off", StringComparison.OrdinalIgnoreCase) || parts[i].Trim().Equals("false", StringComparison.OrdinalIgnoreCase));

        private static void Apply(string value)
        {
            applied = value;
            string[] parts = (value ?? "").Split('|');
            int p = Array.FindIndex(Dev.Palettes, x => string.Equals(x.Name, parts[0].Trim(), StringComparison.OrdinalIgnoreCase));
            int a = parts.Length > 1 ? Array.FindIndex(Dev.Accents, x => string.Equals(x.Name, parts[1].Trim(), StringComparison.OrdinalIgnoreCase)) : 0;
            int f = parts.Length > 2 ? Array.FindIndex(Dev.Fonts, x => string.Equals(x, parts[2].Trim(), StringComparison.OrdinalIgnoreCase)) : 1;
            Dev.ApplyTheme(p < 0 ? 0 : p, a < 0 ? 0 : a, f < 0 ? 1 : f, IsOn(parts, 3), IsOn(parts, 4));
        }
    }
}
