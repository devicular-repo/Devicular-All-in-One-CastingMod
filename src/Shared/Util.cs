using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Devicular
{
    internal readonly struct ColorCode : IEquatable<ColorCode>
    {
        public readonly int R, G, B;

        public ColorCode(int r, int g, int b)
        {
            R = Mathf.Clamp(r, 0, 9);
            G = Mathf.Clamp(g, 0, 9);
            B = Mathf.Clamp(b, 0, 9);
        }

        public static ColorCode FromColor(Color c) =>
            new ColorCode(Mathf.RoundToInt(c.r * 9f), Mathf.RoundToInt(c.g * 9f), Mathf.RoundToInt(c.b * 9f));

        public Color ToColor() => new Color(R / 9f, G / 9f, B / 9f, 1f);

        public string Compact => $"{R}{G}{B}";

        public string Name
        {
            get
            {
                Color.RGBToHSV(ToColor(), out float h, out float s, out float v);
                if (v < 0.25f) return "Black";
                if (s < 0.25f) return v > 0.8f ? "White" : "Grey";
                float deg = h * 360f;
                string hue = deg < 15f || deg >= 345f ? "red"
                    : deg < 40f ? "orange"
                    : deg < 68f ? "yellow"
                    : deg < 95f ? "lime"
                    : deg < 160f ? "green"
                    : deg < 195f ? "cyan"
                    : deg < 255f ? "blue"
                    : deg < 285f ? "purple"
                    : deg < 320f ? "magenta"
                    : "pink";
                if (hue == "orange" && v < 0.7f) hue = "brown";
                string name = v < 0.6f && hue != "brown" ? "dark " + hue : s < 0.5f ? "light " + hue : hue;
                return char.ToUpperInvariant(name[0]) + name.Substring(1);
            }
        }

        public override string ToString() => $"{R} {G} {B}";

        public bool Equals(ColorCode o) => R == o.R && G == o.G && B == o.B;
        public override bool Equals(object obj) => obj is ColorCode c && Equals(c);
        public override int GetHashCode() => R * 100 + G * 10 + B;
        public static bool operator ==(ColorCode a, ColorCode b) => a.Equals(b);
        public static bool operator !=(ColorCode a, ColorCode b) => !a.Equals(b);

        public static bool TryParse(string text, out ColorCode code)
        {
            code = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var tokens = new List<string>();
            foreach (var t in Regex.Split(text.Trim(), "[^0-9]+"))
                if (t.Length > 0) tokens.Add(t);

            if (tokens.Count == 1 && tokens[0].Length == 3)
            {
                code = new ColorCode(tokens[0][0] - '0', tokens[0][1] - '0', tokens[0][2] - '0');
                return true;
            }
            if (tokens.Count == 3 && tokens.TrueForAll(t => t.Length == 1))
            {
                code = new ColorCode(tokens[0][0] - '0', tokens[1][0] - '0', tokens[2][0] - '0');
                return true;
            }
            return false;
        }

        public static ColorCode? ParseOrNull(string text) => TryParse(text, out var c) ? c : (ColorCode?)null;
    }

    internal static class TimeFmt
    {
        public static string Clock(double seconds)
        {
            bool negative = seconds < 0;
            long centis = negative ? (long)Math.Ceiling(-seconds * 100.0 - 1e-6) : (long)Math.Floor(seconds * 100.0 + 1e-6);
            long minutes = centis / 6000;
            long rest = centis % 6000;
            return (negative ? "-" : "") + minutes.ToString(CultureInfo.InvariantCulture) + ":" +
                   (rest / 100).ToString("00", CultureInfo.InvariantCulture) + "." +
                   (rest % 100).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Seconds(double seconds) => seconds.ToString("0.00", CultureInfo.InvariantCulture);
    }

    internal static class Colors
    {
        public static readonly Color Good = new Color(0.45f, 0.95f, 0.45f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.38f);
        public static readonly Color Warn = new Color(1f, 0.78f, 0.3f);
        public static readonly Color Info = new Color(0.8f, 0.85f, 0.95f);
        public static readonly Color Dim = new Color(0.62f, 0.64f, 0.7f);
        public static readonly Color TaggerDefault = new Color(1f, 0.55f, 0.15f);
        public static readonly Color RunnerDefault = new Color(0.3f, 0.75f, 1f);

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public static string Wrap(string text, Color c) => $"<color=#{Hex(c)}>{text}</color>";
    }

    internal static class Str
    {
        public static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");
    }
}
