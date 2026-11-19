using System;
using System.Globalization;
using UnityEngine;

namespace Devicular
{
    internal enum LapState { None, Behind, Close, Ahead }

    /// <summary>What the score bar shows: team names, points, the clock and the time to beat.</summary>
    internal struct ScoreView
    {
        public string NameA, NameB;
        public int PointsA, PointsB;
        public Color? ColorA, ColorB;
        public double Time;
        public double Lap;
        public double Last;
        public bool Running, Paused, HeadStart, Finished, Linked;
        public string Note;
        public float Margin;

        public static LapState CompareLap(double time, double lap, float margin)
        {
            if (double.IsNaN(lap)) return LapState.None;
            if (time >= lap + margin) return LapState.Ahead;
            if (lap - time <= margin) return LapState.Close;
            return LapState.Behind;
        }

        /// <summary>For handing to another Devicular mod through <see cref="Link"/>. Fields are only ever added at the end.</summary>
        public object[] Pack() => new object[]
        {
            NameA, NameB, PointsA, PointsB, ColorA, ColorB, Time, Lap, Last, Running, Paused, HeadStart, Finished, Linked, Note, Margin
        };

        public static bool Unpack(object[] a, out ScoreView v)
        {
            v = default;
            if (a == null || a.Length < 16) return false;
            v = new ScoreView
            {
                NameA = Link.At<string>(a, 0, ""),
                NameB = Link.At<string>(a, 1, ""),
                PointsA = Link.At(a, 2, 0),
                PointsB = Link.At(a, 3, 0),
                ColorA = a[4] is Color ca ? ca : (Color?)null,
                ColorB = a[5] is Color cb ? cb : (Color?)null,
                Time = Link.At(a, 6, 0.0),
                Lap = Link.At(a, 7, double.NaN),
                Last = Link.At(a, 8, double.NaN),
                Running = Link.At(a, 9, false),
                Paused = Link.At(a, 10, false),
                HeadStart = Link.At(a, 11, false),
                Finished = Link.At(a, 12, false),
                Linked = Link.At(a, 13, false),
                Note = Link.At<string>(a, 14),
                Margin = Link.At(a, 15, 2f)
            };
            return true;
        }
    }

    internal static class ScoreBarArt
    {
        private static GUIStyle bar, tile, center, big, name, time, sub;
        private static int built = -1;

        private static void Init()
        {
            if (bar != null && built == Dev.Version) return;
            built = Dev.Version;
            bar = Box(18, Dev.Surface(0.95f), Dev.Shade(0.08f));
            tile = Box(12, Dev.Surface(1f), Dev.Shade(0.06f));
            center = Box(12, Dev.Shade(0.045f), Dev.Shade(0.05f));
            big = Text(30, Dev.Ink);
            name = Text(15, Dev.Ink);
            time = Text(26, Dev.Ink);
            sub = Text(10, Dev.Muted);
        }

        private static GUIStyle Box(int radius, Color fill, Color stroke)
        {
            var s = new GUIStyle(GUIStyle.none) { border = new RectOffset(radius, radius, radius, radius) };
            s.normal.background = Dev.RoundTex(radius, fill, stroke);
            return s;
        }

        private static GUIStyle Text(int size, Color color)
        {
            var s = new GUIStyle(GUIStyle.none)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold
            };
            s.normal.textColor = color;
            return s;
        }

        private static void Scale(float s)
        {
            big.fontSize = Mathf.RoundToInt(30 * s);
            name.fontSize = Mathf.RoundToInt(15 * s);
            time.fontSize = Mathf.RoundToInt(26 * s);
            sub.fontSize = Mathf.RoundToInt(10 * s);
        }

        private static float NameWidth(ScoreView v, float s)
        {
            string a = Str.Clean(v.NameA ?? ""), b = Str.Clean(v.NameB ?? "");
            return Mathf.Clamp(Mathf.Max(name.CalcSize(new GUIContent(a)).x, name.CalcSize(new GUIContent(b)).x) + 18 * s, 76 * s, 190 * s);
        }

        public static Vector2 Measure(ScoreView v, float s)
        {
            Init();
            Scale(s);
            float nameW = NameWidth(v, s);
            float pad = 8 * s, tileW = 52 * s, gap = 8 * s, centerW = 150 * s, h = 66 * s;
            return new Vector2(pad * 2 + tileW * 2 + nameW * 2 + centerW + gap * 4, h);
        }

        public static void Draw(Rect rect, ScoreView v, float s)
        {
            Init();
            Scale(s);
            string nameA = Str.Clean(v.NameA ?? ""), nameB = Str.Clean(v.NameB ?? "");
            float nameW = NameWidth(v, s);
            float pad = 8 * s, tileW = 52 * s, gap = 8 * s, centerW = 150 * s;
            float x = rect.x, y = rect.y, h = rect.height;
            Dev.Shadow(rect);
            bar.Draw(rect, false, false, false, false);
            float cx = x + pad, inner = h - pad * 2;
            Tile(new Rect(cx, y + pad, tileW, inner), v.PointsA, v.ColorA ?? Dev.Info);
            cx += tileW + gap;
            GUI.Label(new Rect(cx, y + pad, nameW, inner), nameA, name);
            cx += nameW + gap;
            var c = new Rect(cx, y + pad, centerW, inner);
            center.Draw(c, false, false, false, false);
            Clock(c, v);
            cx += centerW + gap;
            GUI.Label(new Rect(cx, y + pad, nameW, inner), nameB, name);
            cx += nameW + gap;
            Tile(new Rect(cx, y + pad, tileW, inner), v.PointsB, v.ColorB ?? Dev.Hex("E25A8E"));
        }

        private static void Tile(Rect r, int points, Color team)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(Color.white, team, 0.3f);
            tile.Draw(r, false, false, false, false);
            GUI.backgroundColor = old;
            big.normal.textColor = Dev.OnLight(team);
            GUI.Label(r, points.ToString(CultureInfo.InvariantCulture), big);
        }

        private static void Clock(Rect r, ScoreView v)
        {
            var lap = ScoreView.CompareLap(v.Time, v.Lap, v.Margin);
            Color timeColor = v.Time < 0 ? Dev.Muted : v.Finished ? Dev.Info : Dev.Ink;
            if (v.Paused) timeColor = Dev.Bad;
            string subText;
            Color subColor = Dev.Muted;
            if (lap != LapState.None)
            {
                subText = "TO BEAT " + TimeFmt.Clock(v.Lap);
                subColor = lap == LapState.Ahead ? Dev.Good : lap == LapState.Close ? Dev.Warn : Dev.Bad;
            }
            else if (!string.IsNullOrEmpty(v.Note)) subText = v.Note;
            else if (v.Paused) subText = "PAUSED";
            else if (v.HeadStart && v.Running) subText = "HEAD START";
            else if (v.Finished) subText = "FINAL";
            else subText = double.IsNaN(v.Last) ? "" : "LAST " + TimeFmt.Clock(v.Last);
            float top = r.height * 0.62f;
            time.normal.textColor = timeColor;
            GUI.Label(new Rect(r.x, r.y + 1, r.width, top), TimeFmt.Clock(v.Time), time);
            sub.normal.textColor = subColor;
            GUI.Label(new Rect(r.x, r.y + top - 3, r.width, r.height - top), subText, sub);
        }
    }
}
