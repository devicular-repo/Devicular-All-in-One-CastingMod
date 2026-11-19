using System;
using System.Globalization;
using UnityEngine;

namespace DevicularCaster
{
    /// <summary>
    /// The "Band" score bar: a see-through dark band across the top (or bottom) of the screen with a diamond in each
    /// team's color at its ends. Next to each diamond is the team's name (white), or the diamond's color (like BLUE)
    /// when the team has no name, then its points (a light tint of the team color). In the middle, the round clock
    /// with the time to beat under it (or the round's state, the same as the Classic bar shows).
    ///
    /// Everything is sized from the band's height, so it keeps the same look at any Size.
    /// </summary>
    internal static class ScoreBand
    {
        // Layout, in band heights from the middle of the screen.
        private const float BandHeight = 40f;
        private const float HalfWidth = 4.9f;
        private const float DiamondW = 1.2f, DiamondH = 0.95f;
        private const float Inner = 1.6f;
        // The team names sit between the points and the diamonds, and shrink to fit that space.
        private const float NameCenter = 3.08f, NameWidth = 2.25f, NameSize = 0.55f;

        private static readonly Color DefaultA = new Color(0.04f, 0f, 0.96f);
        private static readonly Color DefaultB = new Color(1f, 0.05f, 0.05f);
        private static readonly Color White = new Color(0.95f, 0.94f, 0.95f);
        private static readonly Color Band = new Color(0f, 0f, 0f, 0.5f);

        private static Texture2D diamond;
        private static Font sans, mono;
        private static GUIStyle nameStyle, innerStyle, clockStyle, beatStyle;

        /// <summary>The band's size on screen at this scale (for placing it).</summary>
        public static Vector2 Measure(float scale)
        {
            float h = BandHeight * scale;
            return new Vector2(h * (HalfWidth * 2f + DiamondW), h * DiamondH);
        }

        /// <summary>Draws the band centered across the screen, its top edge at <paramref name="top"/>.</summary>
        public static void Draw(ScoreView v, float scale, float top)
        {
            Init();
            float h = BandHeight * scale;
            float cx = Screen.width / 2f;
            float mid = top + h * DiamondH / 2f;
            var colorA = Solid(v.ColorA ?? DefaultA);
            var colorB = Solid(v.ColorB ?? DefaultB);

            var old = GUI.color;
            GUI.color = new Color(Band.r, Band.g, Band.b, Band.a * old.a);
            GUI.DrawTexture(new Rect(cx - h * HalfWidth, mid - h / 2f, h * HalfWidth * 2f, h), Texture2D.whiteTexture);
            float dw = h * DiamondW, dh = h * DiamondH;
            GUI.color = new Color(colorA.r, colorA.g, colorA.b, old.a);
            GUI.DrawTexture(new Rect(cx - h * HalfWidth - dw / 2f, mid - dh / 2f, dw, dh), diamond);
            GUI.color = new Color(colorB.r, colorB.g, colorB.b, old.a);
            GUI.DrawTexture(new Rect(cx + h * HalfWidth - dw / 2f, mid - dh / 2f, dw, dh), diamond);
            GUI.color = old;

            innerStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(h * 0.8f));
            clockStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(h * 0.475f));
            beatStyle.fontSize = Mathf.Max(6, Mathf.RoundToInt(h * 0.3f));

            TeamName(cx - h * NameCenter, mid, h, NameOf(v.NameA, "TEAM A", colorA));
            Number(cx - h * Inner, mid, h, v.PointsA, innerStyle, Tint(colorA));
            Number(cx + h * Inner, mid, h, v.PointsB, innerStyle, Tint(colorB));
            TeamName(cx + h * NameCenter, mid, h, NameOf(v.NameB, "TEAM B", colorB));

            // The clock (red while paused), and under it what the Classic bar says there, so both show the same:
            // the time to beat (green once it's beaten, yellow when close, red while behind), else GET READY or
            // REPLAY, PAUSED, HEAD START, FINAL, or the last round's time.
            float clockH = h * 0.5f, beatH = h * 0.34f;
            float clockTop = mid - (clockH + beatH) / 2f;
            Label(new Rect(cx - h * 1.4f, clockTop, h * 2.8f, clockH), Mmss(v.Time), clockStyle, v.Paused ? BadTint : White);
            string below = Below(v, out var belowColor);
            Label(new Rect(cx - h * 1.4f, clockTop + clockH, h * 2.8f, beatH), below, beatStyle, belowColor);
        }

        private static readonly Color GoodTint = new Color(0.55f, 1f, 0.6f);
        private static readonly Color WarnTint = new Color(1f, 0.87f, 0.45f);
        private static readonly Color BadTint = new Color(1f, 0.55f, 0.55f);

        /// <summary>The line under the clock: the same thing, in the same order, as the Classic bar's line.</summary>
        private static string Below(ScoreView v, out Color color)
        {
            color = White;
            var lap = ScoreView.CompareLap(v.Time, v.Lap, v.Margin);
            if (lap != LapState.None)
            {
                color = lap == LapState.Ahead ? GoodTint : lap == LapState.Close ? WarnTint : BadTint;
                return Mmss(v.Lap);
            }
            if (!string.IsNullOrEmpty(v.Note)) return v.Note;
            if (v.Paused)
            {
                color = BadTint;
                return "PAUSED";
            }
            if (v.HeadStart && v.Running) return "HEAD START";
            if (v.Finished) return "FINAL";
            return Mmss(double.IsNaN(v.Last) ? 0.0 : v.Last);
        }

        /// <summary>
        /// The team's name, or, when it has none (blank or the default TEAM A / TEAM B), the name of its diamond's
        /// color, like BLUE or RED.
        /// </summary>
        private static string NameOf(string name, string defaultName, Color color)
        {
            string n = Str.Clean(name ?? "").Trim();
            if (n.Length == 0 || string.Equals(n, defaultName, StringComparison.OrdinalIgnoreCase))
                n = ColorCode.FromColor(color).Name;
            return n.ToUpperInvariant();
        }

        /// <summary>A team name, white, made smaller if it's too long for the space next to the diamond.</summary>
        private static void TeamName(float x, float mid, float h, string text)
        {
            float maxW = h * NameWidth;
            int size = Mathf.Max(7, Mathf.RoundToInt(h * NameSize));
            nameStyle.fontSize = size;
            var content = new GUIContent(text);
            float w = nameStyle.CalcSize(content).x;
            if (w > maxW)
            {
                size = Mathf.Max(7, Mathf.FloorToInt(size * maxW / w));
                nameStyle.fontSize = size;
            }
            Label(new Rect(x - maxW / 2f, mid - h / 2f, maxW, h), text, nameStyle, White);
        }

        private static void Number(float x, float mid, float h, int value, GUIStyle style, Color color) =>
            Label(new Rect(x - h, mid - h / 2f, h * 2f, h), Math.Max(0, value).ToString(CultureInfo.InvariantCulture), style, color);

        private static void Label(Rect r, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        /// <summary>Minutes and seconds, like 01:05. A countdown (before the round starts) shows as -00:05.</summary>
        private static string Mmss(double t)
        {
            if (double.IsNaN(t) || double.IsInfinity(t)) t = 0;
            bool negative = t < 0;
            int total = negative ? (int)Math.Ceiling(-t) : (int)Math.Floor(t);
            total = Math.Min(total, 99 * 60 + 59);
            string s = (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
            return negative && total > 0 ? "-" + s : s;
        }

        private static Color Solid(Color c) => new Color(c.r, c.g, c.b, 1f);

        /// <summary>The team color made light, like the lavender and pink points in the reference.</summary>
        private static Color Tint(Color c) => Color.Lerp(c, Color.white, 0.62f);

        private static void Init()
        {
            if (diamond != null && nameStyle != null) return;
            diamond = MakeDiamond(96, 76);
            // Shared copies of the fonts (see Dev.SharedFont: a second copy of a font shows the wrong text).
            sans = Dev.SharedFont("Arial", "Segoe UI");
            mono = Dev.SharedFont("Consolas", "Courier New", "Lucida Console");
            nameStyle = Style(sans, FontStyle.Bold);
            innerStyle = Style(sans, FontStyle.Normal);
            clockStyle = Style(mono, FontStyle.Bold);
            beatStyle = Style(mono, FontStyle.Bold);
        }

        private static GUIStyle Style(Font font, FontStyle fontStyle) => new GUIStyle(GUIStyle.none)
        {
            font = font,
            fontStyle = fontStyle,
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Overflow,
            richText = false,
            wordWrap = false
        };

        /// <summary>A smooth-edged white diamond, tinted with the team color when drawn.</summary>
        private static Texture2D MakeDiamond(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[w * h];
            float hw = w / 2f, hh = h / 2f;
            // Distance to the edge, in pixels, for |x|/hw + |y|/hh = 1.
            float k = 1f / Mathf.Sqrt(1f / (hw * hw) + 1f / (hh * hh));
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - hw) / hw, dy = Mathf.Abs(y + 0.5f - hh) / hh;
                    float d = (dx + dy - 1f) * k;
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
