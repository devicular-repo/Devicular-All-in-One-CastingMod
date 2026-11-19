using System;
using System.Collections.Generic;
using System.Globalization;
using Photon.Pun;
using UnityEngine;

namespace DevicularCaster
{
    internal sealed class Overlays
    {
        public bool ScoreBar = true;
        public bool ScoreBarBottom;
        /// <summary>The score bar's size as the menu shows it (1 = 100%), from ScoreBarMin to ScoreBarMax.</summary>
        public float ScoreBarSize = 1f;
        public const float ScoreBarMin = 0.5f, ScoreBarMax = 1.5f;
        // How big 100% is: 1.4 times the bars' own base size, for both styles.
        private const float ScoreBarBase = 1.4f;
        public static readonly string[] ScoreBarStyles = { "Classic", "Band" };

        /// <summary>Which score bar: 0 Classic (tiles and team names), 1 Band. Saved in the caster's settings.</summary>
        public int ScoreBarStyle
        {
            get => Plugin.ScoreBarStyle != null && Plugin.ScoreBarStyle.Value == ScoreBarStyles[1] ? 1 : 0;
            set
            {
                if (Plugin.ScoreBarStyle != null) Plugin.ScoreBarStyle.Value = ScoreBarStyles[Mathf.Clamp(value, 0, ScoreBarStyles.Length - 1)];
            }
        }
        public bool PlayerList = true;
        public bool ShowPing;
        public bool PlayerListRight;
        public float PlayerListSize = 1f;
        public bool HideAll;

        /// <summary>A player's platform (STEAM, QUEST, PC...), from the name tags. Shows as a logo at the end of their row.</summary>
        public Func<int, VRRig, string> Platform;

        private bool ready;
        private int builtVersion = -1;
        private Texture2D barTex, barHotTex, washTex, cameraTex, steamTex, metaTex;
        private GUIStyle tabNumber, rowName, header;
        private Font listFont;
        private static readonly Color Lava = new Color(1f, 0.42f, 0.17f);
        private static readonly Color LogoTint = new Color(1f, 1f, 1f, 0.72f);
        // The real Steam and Meta logos (built into the DLL), in full color. If they can't be loaded, white drawn
        // ones are used instead (tinted with LogoTint).
        private static Texture2D steamImage, metaImage;
        private static bool imagesTried;

        /// <summary>The real Steam logo, shared with the name tags. Null if it couldn't be loaded.</summary>
        internal static Texture2D SteamImage
        {
            get
            {
                LoadImages();
                return steamImage;
            }
        }

        /// <summary>The real Meta logo, shared with the name tags. Null if it couldn't be loaded.</summary>
        internal static Texture2D MetaImage
        {
            get
            {
                LoadImages();
                return metaImage;
            }
        }

        private static void LoadImages()
        {
            if (imagesTried) return;
            imagesTried = true;
            steamImage = LoadLogo("steam.png");
            metaImage = LoadLogo("meta.png");
        }

        // The player list, at Size 100%: rows, the slant on their left edges, the number's space and the gaps.
        private const float RowH = 30f, Slant = 9f, TabW = 36f, BarW = 300f, RowGap = 3f, TeamGap = 8f;


        private void Init()
        {
            if (ready && builtVersion == Dev.Version) return;
            ready = true;
            builtVersion = Dev.Version;
            // A see-through black bar slanted on both ends (/ ... /); a more solid one for the player you're spectating.
            barTex = Slanted(300, 30, Slant, Slant, new Color(0f, 0f, 0f, 0.55f));
            barHotTex = Slanted(300, 30, Slant, Slant, new Color(0f, 0f, 0f, 0.75f));
            // The color wash over it: the player's color on the left, fading out by just past the middle.
            washTex = Wash(300, 30, Slant);
            if (!washLogged)
            {
                washLogged = true;
                Mod.Log?.LogInfo($"Player list: the game blends in {(LinearLight ? "linear" : "gamma")} light; washes are {washTex.format}.");
            }
            cameraTex = CameraIcon(40, 28);

            LoadImages();
            steamTex = steamImage != null ? steamImage : SteamLogo(64);
            metaTex = metaImage != null ? metaImage : MetaLogo(72, 44);
            // Segoe UI in bold: the font and weight the menu draws its titles with. Not "Segoe UI Semibold": that
            // isn't a real font family, and in this game's Unity (6) text in it didn't draw at all (the list showed
            // other text instead, like "Keybinds" or the clock). If CheckText still finds the text broken, the
            // game's own font is used.
            listFont = defaultFont ? null : Dev.SharedFont("Segoe UI", "Arial");
            tabNumber = ListText(TextAnchor.MiddleCenter);
            rowName = ListText(TextAnchor.MiddleLeft);
            header = ListText(TextAnchor.LowerLeft);

        }

        // Bold, not italic: the lean comes from tilting the text as it's drawn (Leaning).
        private GUIStyle ListText(TextAnchor anchor) => new GUIStyle(GUIStyle.none)
        {
            font = listFont,
            fontStyle = FontStyle.Bold,
            alignment = anchor,
            richText = false,
            clipping = TextClipping.Overflow,
            wordWrap = false
        };

        /// <summary>How far the list's text leans, like italics (sideways per unit of height).</summary>
        private const float Lean = 0.2f;

        /// <summary>Draws a label leaning to the right, by tilting the drawing around the label's middle.</summary>
        private static void Leaning(Rect r, string text, GUIStyle style)
        {
            var old = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -Lean;
            shear.m03 = Lean * r.center.y;
            GUI.matrix = old * shear;
            GUI.Label(r, text, style);
            GUI.matrix = old;
        }

        /// <summary>The text cut short (with no ellipsis) so it fits in <paramref name="width"/>.</summary>
        private static string Fit(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= width) return text;
            int n = text.Length;
            while (n > 1 && style.CalcSize(new GUIContent(text.Substring(0, n))).x > width) n--;
            return text.Substring(0, n);
        }

        private int textChecks;
        private bool defaultFont;

        /// <summary>
        /// Checks the list's text really is made from what's written (two different strings, two different widths).
        /// If it isn't, the list switches to the game's own font and checks again. Each result goes in the log.
        /// </summary>
        private void CheckText()
        {
            if (textChecks >= 2) return;
            textChecks++;
            try
            {
                float a = rowName.CalcSize(new GUIContent("0")).x, b = rowName.CalcSize(new GUIContent("WWWWWWWWWW")).x;
                if (b > a + 1f)
                {
                    Mod.Log?.LogInfo($"Player list: text ok (font {(listFont != null ? listFont.name : "the game's own")}).");
                    textChecks = 2;
                    return;
                }
                if (!defaultFont)
                {
                    Mod.Log?.LogWarning($"Player list: its text isn't drawing with {(listFont != null ? listFont.name : "its font")} (widths {a:0} and {b:0}), so it uses the game's own font.");
                    defaultFont = true;
                    // Rebuild the styles with the game's font before the next frame (Init runs again).
                    ready = false;
                }
                else
                {
                    Mod.Log?.LogWarning($"Player list: its text still isn't drawing as written (widths {a:0} and {b:0}).");
                    textChecks = 2;
                }
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning("Player list: text check failed: " + e.Message);
                textChecks = 2;
            }
        }

        private static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        /// <summary>
        /// A solid shape whose left edge (and right edge, when <paramref name="rightSlant"/> isn't 0) leans like a
        /// forward slash: <paramref name="leftSlant"/> pixels further right at the top than at the bottom.
        /// </summary>
        private static Texture2D Slanted(int w, int h, float leftSlant, float rightSlant, Color color)
        {
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // t: 0 at the bottom row, 1 at the top (texture rows count up from the bottom).
                float t = (y + 0.5f) / h;
                float left = leftSlant * t;
                float right = w - rightSlant * (1f - t);
                for (int x = 0; x < w; x++)
                {
                    float cx = x + 0.5f;
                    float cover = Mathf.Clamp01(cx - left + 0.5f) * Mathf.Clamp01(right - cx + 0.5f);
                    px[y * w + x] = new Color(color.r, color.g, color.b, color.a * cover);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>One of the logo images built into the DLL (Images\*.png), with smooth scaling. Null if it can't be read.</summary>
        private static Texture2D LoadLogo(string file)
        {
            try
            {
                using (var s = typeof(Overlays).Assembly.GetManifestResourceStream("Devicular.Images." + file))
                {
                    if (s == null) return null;
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave };
                    if (!ImageConversion.LoadImage(tex, bytes, false))
                    {
                        UnityEngine.Object.Destroy(tex);
                        return null;
                    }
                    tex.filterMode = FilterMode.Trilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.Apply(true, true);
                    return tex;
                }
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning($"Player list: couldn't load the {file} logo: {e.Message}");
                return null;
            }
        }

        // The washes' fade (color and lava): full strength behind the number, then one long, even ease out to nothing
        // by WashEnd of the way across. The curve is flat where it starts and where it ends, so no line shows anywhere.
        private const float WashSolid = 0.05f, WashEnd = 0.8f;
        private static bool washLogged;

        /// <summary>
        /// How strong the wash is at <paramref name="x"/> pixels across a row <paramref name="w"/> wide (1 to 0), as
        /// it should look. The game blends in linear light, where half strength still looks bright, so there the curve
        /// is converted to match: otherwise the color stays strong and then drops off over a short stretch.
        /// </summary>
        private static float WashFade(float x, float w)
        {
            float t = Mathf.Clamp01((x / w - WashSolid) / (WashEnd - WashSolid));
            float f = 1f - t * t * (3f - 2f * t);
            return LinearLight ? Mathf.GammaToLinearSpace(f) : f;
        }

        private static bool LinearLight => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>
        /// A texture for a wash, in half floats when the graphics card has them: the faint end of the fade is too fine
        /// for 8 bits in linear light (it would show steps). Half-float textures aren't decoded from sRGB, so a wash
        /// with colors in it gives them already in linear light (see WashColor).
        /// </summary>
        private static Texture2D NewWashTex(int w, int h)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)) return NewTex(w, h);
            return new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        /// <summary>A color for a wash texture: converted to linear light when the texture won't do it itself.</summary>
        private static Color WashColor(Texture2D tex, Color c)
        {
            if (tex.format != TextureFormat.RGBAHalf || !LinearLight) return c;
            var lin = c.linear;
            lin.a = c.a;
            return lin;
        }

        /// <summary>
        /// White, slanted on the left like the bars, fading out smoothly (see WashFade). Tinted with the player's
        /// color when drawn, it's the wash behind their number and name.
        /// </summary>
        private static Texture2D Wash(int w, int h, float slant)
        {
            var tex = NewWashTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float left = slant * (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float cx = x + 0.5f;
                    float cover = Mathf.Clamp01(cx - left + 0.5f);
                    px[y * w + x] = new Color(1f, 1f, 1f, cover * WashFade(cx, w));
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ---------- lava, for tagged players ----------

        // The lava flows: LavaFrames pictures, each LavaPeriod / LavaFrames pixels further along a pattern that
        // repeats every LavaPeriod pixels, so cycling them loops without a jump.
        private const int LavaFrames = 32, LavaPeriod = 120;
        private const float LavaFps = 8f;
        private static Texture2D[] lavaWash;

        // Gorilla Tag's own lava: the lava-looking texture on the game's lava materials, copied once so its pixels can
        // be read. Until there's a gorilla to take it from, the lava isn't made yet.
        private static Color[] gameLava;
        private static int gameLavaW, gameLavaH, lavaTries;
        private static float nextLavaTry;
        // How many pixels of the row one copy of the game's lava texture covers (it repeats across the row).
        private const int GameLavaTile = 64;

        private static readonly Color[] LavaStops =
        {
            new Color(0.24f, 0.02f, 0f), new Color(0.63f, 0.08f, 0f), new Color(1f, 0.29f, 0f), new Color(1f, 0.6f, 0.12f), new Color(1f, 0.88f, 0.54f)
        };
        private static readonly float[] LavaAt = { 0f, 0.42f, 0.62f, 0.8f, 1f };

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Smooth noise that repeats every <paramref name="period"/> cells across.</summary>
        private static float ValueNoise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
            float top = Mathf.Lerp(Hash(xa, y0), Hash(xb, y0), fx);
            float bottom = Mathf.Lerp(Hash(xa, y0 + 1), Hash(xb, y0 + 1), fx);
            return Mathf.Lerp(top, bottom, fy);
        }

        /// <summary>Three layers of noise: big lava blobs with smaller swirls in them. Repeats every LavaPeriod pixels across.</summary>
        private static float LavaNoise(float px, float py)
        {
            float n = 0f, amp = 0.55f, total = 0f, cell = 20f;
            for (int o = 0; o < 3; o++)
            {
                int cells = Mathf.RoundToInt(LavaPeriod / cell);
                n += ValueNoise(px / cell, py / cell + o * 7.3f, cells) * amp;
                total += amp;
                amp *= 0.5f;
                cell *= 0.5f;
            }
            return n / total;
        }

        /// <summary>
        /// How hot the lava is at a spot (0 to 1): glowing blobs, with thin bright cracks running through them. The
        /// cracks use noise at exactly twice the scale, so the pattern still repeats every LavaPeriod pixels.
        /// </summary>
        private static float LavaHeat(float px, float py)
        {
            float blob = LavaNoise(px, py);
            float v = LavaNoise(px * 2f + 31f, py * 2f + 17f);
            float vein = 1f - Mathf.Abs(2f * v - 1f);
            vein *= vein * vein;
            vein *= vein;
            return Mathf.Min(1f, 0.18f + 0.55f * blob + 0.55f * vein);
        }

        /// <summary>Dark crust, deep red, orange-red, orange, and hot yellow where it's brightest.</summary>
        private static Color LavaColor(float n)
        {
            n = Mathf.Clamp01(n);
            n = n * n * (3f - 2f * n);
            for (int i = 0; i < LavaAt.Length - 1; i++)
                if (n <= LavaAt[i + 1]) return Color.Lerp(LavaStops[i], LavaStops[i + 1], (n - LavaAt[i]) / (LavaAt[i + 1] - LavaAt[i]));
            return LavaStops[LavaStops.Length - 1];
        }

        /// <summary>
        /// The lava for tagged rows, once there's a gorilla to take Gorilla Tag's lava from. Made the first time a
        /// tagged player is shown. If the game's lava can't be found after a few tries, drawn lava is used instead.
        /// </summary>
        private static void EnsureLava()
        {
            if (lavaWash != null || Time.unscaledTime < nextLavaTry) return;
            nextLavaTry = Time.unscaledTime + 1f;
            lavaTries++;
            // null: no gorilla to look at yet, so try again in a second (a few times). Found or not, make the lava.
            bool? found = gameLava != null ? true : LoadGameLava();
            if (found == null && lavaTries < 6) return;
            MakeLava();
        }

        private static bool IsLavaName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("lava") || n.Contains("infect") || n.Contains("magma");
        }

        /// <summary>
        /// Finds Gorilla Tag's lava and copies it so its pixels can be read. It looks at every texture on the lava
        /// materials a gorilla can turn into (the infected one), on any loaded material named lava, and at loaded
        /// textures named lava, and takes the one that looks most like lava: warm, bright enough and patterned.
        /// Black, flat or blue textures (like the infected material's "Black" placeholder) are skipped; a grey
        /// pattern is colored as lava. Everything it looked at goes in the log. Returns null when there's no
        /// gorilla yet, false when nothing looked like lava (the drawn lava is used then).
        /// </summary>
        private static bool? LoadGameLava()
        {
            try
            {
                var rig = VRRig.LocalRig;
                if (rig == null && GorillaTagger.Instance != null) rig = GorillaTagger.Instance.offlineVRRig;
                var rigMats = rig != null ? rig.materialsToChangeTo : null;
                if (rigMats == null || rigMats.Length == 0) return null;

                var mats = new List<(Material mat, float bonus)>();
                var seenMats = new HashSet<Material>();
                foreach (var m in rigMats)
                    if (m != null && IsLavaName(m.name) && seenMats.Add(m)) mats.Add((m, 1f));
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && IsLavaName(m.name) && seenMats.Add(m)) mats.Add((m, 0.5f));

                var pick = new LavaPick();
                var seenTex = new HashSet<Texture>();
                foreach (var (mat, bonus) in mats)
                {
                    if (pick.Looked >= 40) break;
                    foreach (var prop in mat.GetTexturePropertyNames())
                    {
                        var tex = mat.GetTexture(prop);
                        if (tex == null) continue;
                        seenTex.Add(tex);
                        pick.Consider(tex, mat.name + " " + prop, bonus + (IsLavaName(tex.name) ? 0.5f : 0f), TintOf(mat, prop));
                    }
                }
                foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if (pick.Looked >= 40) break;
                    if (tex != null && IsLavaName(tex.name) && seenTex.Add(tex)) pick.Consider(tex, "loaded texture", 0.7f, Color.white);
                }

                if (pick.Best == null)
                {
                    Mod.Log?.LogInfo($"Player list: looked at {pick.Looked} textures, none looked like Gorilla Tag's lava, so tagged rows use drawn lava.");
                    return false;
                }
                gameLava = pick.Best;
                gameLavaW = pick.BestW;
                gameLavaH = pick.BestH;
                Mod.Log?.LogInfo($"Player list: tagged rows use the game's lava ({pick.BestWhere}).");
                return true;
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning("Player list: couldn't read the game's lava, so tagged rows use drawn lava: " + e.Message);
                return false;
            }
        }

        /// <summary>The color a material multiplies a texture by: the emission color for emission maps, else its base color.</summary>
        private static Color TintOf(Material mat, string prop)
        {
            string p = prop.ToLowerInvariant();
            string[] names = p.Contains("emiss") ? new[] { "_EmissionColor" } : p.Contains("main") || p.Contains("base") || p.Contains("albedo") ? new[] { "_BaseColor", "_Color" } : new string[0];
            foreach (var n in names)
                if (mat.HasProperty(n))
                {
                    var c = mat.GetColor(n);
                    float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    // HDR emission colors can be far above 1; only the hue and a sane brightness matter here.
                    if (max > 1f) c = new Color(c.r / max, c.g / max, c.b / max);
                    if (max > 0.05f) return new Color(c.r, c.g, c.b, 1f);
                }
            return Color.white;
        }

        /// <summary>Copies a texture (any kind, readable or not) at up to 128 pixels across.</summary>
        private static Color[] ReadSmall(Texture tex, out int w, out int h)
        {
            w = Mathf.Clamp(tex.width, 16, 128);
            h = Mathf.Clamp(tex.height, 16, 128);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels();
            UnityEngine.Object.Destroy(copy);
            return px;
        }

        /// <summary>Scores the textures it's shown and keeps the one that looks most like lava.</summary>
        private sealed class LavaPick
        {
            public Color[] Best;
            public int BestW, BestH, Looked;
            public string BestWhere;
            private float bestScore = float.MinValue;

            public void Consider(Texture tex, string where, float bonus, Color tint)
            {
                if (tex.dimension != UnityEngine.Rendering.TextureDimension.Tex2D || tex.width < 4 || tex.height < 4) return;
                Looked++;
                Color[] px;
                int w, h;
                try { px = ReadSmall(tex, out w, out h); }
                catch (Exception e) { Mod.Log?.LogInfo($"  lava? {where}: {tex.name} couldn't be read ({e.Message})"); return; }

                int n = px.Length;
                float sr = 0, sg = 0, sb = 0, sl = 0, sl2 = 0, ss = 0, minL = 1f, maxL = 0f;
                var lum = new float[n];
                for (int i = 0; i < n; i++)
                {
                    var c = px[i] * tint;
                    c.a = 1f;
                    px[i] = c;
                    float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    lum[i] = l;
                    sr += c.r; sg += c.g; sb += c.b;
                    sl += l; sl2 += l * l;
                    ss += max > 0.001f ? (max - min) / max : 0f;
                    if (l < minL) minL = l;
                    if (l > maxL) maxL = l;
                }
                float mr = sr / n, mg = sg / n, mb = sb / n, ml = sl / n, sat = ss / n;
                float detail = Mathf.Sqrt(Mathf.Max(0f, sl2 / n - ml * ml));
                float warm = mr - mb;
                string avg = ColorUtility.ToHtmlStringRGB(new Color(mr, mg, mb));

                string verdict;
                float score;
                bool grey = sat < 0.15f;
                if (Mathf.Max(mr, Mathf.Max(mg, mb)) < 0.06f) { verdict = "too dark"; score = float.MinValue; }
                else if (detail < 0.025f) { verdict = "flat, no pattern"; score = float.MinValue; }
                else if (!grey && warm < 0.08f) { verdict = "not lava colored"; score = float.MinValue; }
                else if (grey)
                {
                    // A grey pattern (cracks, noise): its light parts become hot lava, its dark parts crust.
                    verdict = "grey pattern, colored as lava";
                    score = detail * 3f + bonus;
                    float span = Mathf.Max(0.01f, maxL - minL);
                    for (int i = 0; i < n; i++) px[i] = LavaColor((lum[i] - minL) / span);
                }
                else
                {
                    verdict = "lava colored";
                    score = 1f + warm * 2f + sat + detail * 3f + bonus;
                    // Brighten dark lava so it glows on the row: its brightest bits go to near full.
                    var peaks = new float[n];
                    for (int i = 0; i < n; i++) peaks[i] = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                    Array.Sort(peaks);
                    float peak = peaks[Mathf.Min(n - 1, (int)(n * 0.95f))];
                    float gain = Mathf.Clamp(0.95f / Mathf.Max(0.01f, peak), 1f, 3f);
                    if (gain > 1.01f)
                        for (int i = 0; i < n; i++) { var c = px[i] * gain; c.a = 1f; px[i] = c; }
                }
                Mod.Log?.LogInfo($"  lava? {where}: {tex.name} {tex.width}x{tex.height}, avg #{avg}, pattern {detail:0.000}: {verdict}" + (score > float.MinValue ? $" (score {score:0.00})" : ""));
                if (score > float.MinValue && score > bestScore)
                {
                    bestScore = score;
                    Best = px;
                    BestW = w;
                    BestH = h;
                    BestWhere = $"{where}, {tex.name}, {verdict}";
                }
            }
        }

        /// <summary>The game's lava at a spot on the row (in row pixels), repeating every GameLavaTile pixels, smoothly.</summary>
        private static Color GameLavaAt(float px, float py)
        {
            float u = px / GameLavaTile * gameLavaW - 0.5f, v = py / GameLavaTile * gameLavaH - 0.5f;
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
            float fx = u - x0, fy = v - y0;
            Color P(int x, int y)
            {
                x = ((x % gameLavaW) + gameLavaW) % gameLavaW;
                y = ((y % gameLavaH) + gameLavaH) % gameLavaH;
                return gameLava[y * gameLavaW + x];
            }
            var top = Color.Lerp(P(x0, y0), P(x0 + 1, y0), fx);
            var bottom = Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx);
            var c = Color.Lerp(top, bottom, fy);
            c.a = 1f;
            return c;
        }

        /// <summary>
        /// Makes the row's lava wash once (slanted on the left, fading out past the middle, like the color wash), in
        /// LavaFrames steps of the flow: Gorilla Tag's lava sliding along, or drawn lava if the game's wasn't found.
        /// </summary>
        private static void MakeLava()
        {
            if (lavaWash != null) return;
            lavaWash = new Texture2D[LavaFrames];
            const int w = 300, h = 30;
            bool game = gameLava != null;
            for (int f = 0; f < LavaFrames; f++)
            {
                // One full repeat of the pattern over all the frames, so the flow loops without a jump.
                float shift = f * (float)(game ? GameLavaTile : LavaPeriod) / LavaFrames;
                var tex = NewWashTex(w, h);
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                {
                    float t = (y + 0.5f) / h, gy = h - y - 0.5f;
                    float left = Slant * t;
                    for (int x = 0; x < w; x++)
                    {
                        float cx = x + 0.5f;
                        float cover = Mathf.Clamp01(cx - left + 0.5f);
                        var c = WashColor(tex, game ? GameLavaAt(cx - shift, gy) : LavaColor(LavaHeat(cx - shift, gy)));
                        c.a = cover * WashFade(cx, w) * 0.95f;
                        px[y * w + x] = c;
                    }
                }
                tex.SetPixels(px);
                tex.Apply();
                lavaWash[f] = tex;
            }
        }

        private static int LavaFrame => Mathf.FloorToInt(Time.unscaledTime * LavaFps) % LavaFrames;

        /// <summary>A small white video camera: the mark on the row of the player you're spectating.</summary>
        private static Texture2D CameraIcon(int w, int h)
        {
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            float bx0 = w * 0.05f, bx1 = w * 0.66f, by0 = h * 0.18f, by1 = h * 0.82f, r = h * 0.14f;
            float mid = h / 2f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = x + 0.5f, cy = y + 0.5f;
                    // The body: a rounded rectangle.
                    float qx = Mathf.Max(Mathf.Max(bx0 + r - cx, cx - (bx1 - r)), 0f);
                    float qy = Mathf.Max(Mathf.Max(by0 + r - cy, cy - (by1 - r)), 0f);
                    float body = Mathf.Clamp01(r - Mathf.Sqrt(qx * qx + qy * qy) + 0.5f);
                    // The lens: a wedge opening to the right.
                    float lens = 0f;
                    if (cx > bx1 + 1f && cx < w - 1f)
                    {
                        float t = (cx - (bx1 + 1f)) / (w - 1f - (bx1 + 1f));
                        float half = h * (0.14f + 0.22f * t);
                        lens = Mathf.Clamp01(half - Mathf.Abs(cy - mid) + 0.5f) * Mathf.Clamp01(w - 1f - cx + 0.5f);
                    }
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Max(body, lens));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>A white Steam mark: a thin circle with the wheel-and-arm logo inside. Tinted when drawn.</summary>
        private static Texture2D SteamLogo(int size)
        {
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float s = size;
            var center = new Vector2(s / 2f, s / 2f);
            var wheel = new Vector2(s * 0.62f, s * 0.6f);
            var hub = new Vector2(s * 0.38f, s * 0.38f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float dc = Vector2.Distance(p, center);
                    float circle = Mathf.Clamp01(s * 0.05f - Mathf.Abs(dc - s * 0.44f) + 0.5f);
                    float dw = Vector2.Distance(p, wheel);
                    float ring = Mathf.Clamp01(s * 0.05f - Mathf.Abs(dw - s * 0.12f) + 0.5f);
                    float hubDisc = Mathf.Clamp01(s * 0.08f - Vector2.Distance(p, hub) + 0.5f);
                    float arm = Mathf.Clamp01(s * 0.045f - SegDist(p, hub, wheel + (hub - wheel).normalized * s * 0.12f) + 0.5f);
                    float tail = Mathf.Clamp01(s * 0.045f - SegDist(p, new Vector2(s * 0.08f, s * 0.46f), hub) + 0.5f);
                    float a = Mathf.Max(Mathf.Max(circle, ring), Mathf.Max(hubDisc, Mathf.Max(arm, tail)));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A white Meta mark: the infinity loop. Tinted when drawn.</summary>
        private static Texture2D MetaLogo(int w, int h)
        {
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            var pts = new List<Vector2>();
            float a = w * 0.44f;
            for (int i = 0; i <= 160; i++)
            {
                float t = i / 160f * Mathf.PI * 2f;
                float d = 1f + Mathf.Sin(t) * Mathf.Sin(t);
                pts.Add(new Vector2(w / 2f + a * Mathf.Cos(t) / d, h / 2f + a * 1.25f * Mathf.Sin(t) * Mathf.Cos(t) / d));
            }
            float half = h * 0.075f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = float.MaxValue;
                    for (int i = 0; i + 1 < pts.Count; i++) d = Mathf.Min(d, SegDist(p, pts[i], pts[i + 1]));
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(half - d + 0.5f));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, float offset)
        {
            var fill = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + offset, r.y + offset, r.width, r.height), text, style);
            style.normal.textColor = fill;
            GUI.Label(r, text, style);
        }

        public void Draw(ScoreView view, CasterCamera cam)
        {
            if (HideAll || Event.current.type != EventType.Repaint) return;
            Init();
            if (ScoreBar) DrawScoreBar(view);
            if (PlayerList && Roster.Ordered.Count > 0) DrawPlayerList(cam);
        }

        private void DrawScoreBar(ScoreView v)
        {
            float s = Mathf.Clamp(ScoreBarSize, ScoreBarMin, ScoreBarMax) * ScoreBarBase;
            if (ScoreBarStyle == 1)
            {
                var band = ScoreBand.Measure(s);
                ScoreBand.Draw(v, s, ScoreBarBottom ? Screen.height - band.y - 14f : 12f);
                return;
            }
            var size = ScoreBarArt.Measure(v, s);
            float x = (Screen.width - size.x) / 2f;
            float y = ScoreBarBottom ? Screen.height - size.y - 14f : 12f;
            ScoreBarArt.Draw(new Rect(x, y, size.x, size.y), v, s);
        }

        /// <summary>The platform logo for this player: Steam, Meta (Quest or Oculus PC), or none when it isn't known.</summary>
        private Texture2D PlatformLogo(CastPlayer p)
        {
            if (Platform == null || p.Rig == null || p.Ghost) return null;
            string platform;
            try { platform = Platform(p.Actor, p.Rig) ?? ""; }
            catch { return null; }
            if (platform.IndexOf("STEAM", StringComparison.OrdinalIgnoreCase) >= 0) return steamTex;
            if (platform.Length > 0) return metaTex;
            return null;
        }

        private readonly HashSet<int> outRunners = new HashSet<int>();
        private bool refereeRoles;

        /// <summary>Who's out (tagged): the Auto Ref's list during a round, otherwise players in lava.</summary>
        private void ReadOuts()
        {
            outRunners.Clear();
            var roles = Link.Call("Referee.Roles");
            var outs = Link.At<HashSet<int>>(roles, 2);
            refereeRoles = outs != null && !Roster.Replaying;
            if (refereeRoles)
                foreach (var a in outs) outRunners.Add(a);
        }

        private bool IsOut(CastPlayer p) => refereeRoles ? outRunners.Contains(p.Actor) : p.Tagged;

        /// <summary>
        /// The player list ("color wash"): one dark slanted row per player, washed from the left in their color
        /// (Gorilla Tag's lava, flowing, once they're tagged), with a big white number, their name, and their Steam
        /// or Meta logo. The player you're spectating gets a lighter row and a camera mark. With the team filter on,
        /// the two teams have a small gap between them.
        /// </summary>
        private void DrawPlayerList(CasterCamera cam)
        {
            float k = Mathf.Clamp(PlayerListSize, 0.6f, 1.6f);
            float rowH = RowH * k, gap = RowGap * k, numW = TabW * k, barW = BarW * k, teamGap = TeamGap * k;
            tabNumber.fontSize = Mathf.RoundToInt(18 * k);
            rowName.fontSize = Mathf.RoundToInt(16 * k);
            header.fontSize = Mathf.RoundToInt(12 * k);
            CheckText();
            ReadOuts();
            bool grouped = Roster.TeamFilter && Roster.TeamsKnown;

            float total = 0f;
            int lastTeam = int.MinValue;
            foreach (var p in Roster.Ordered)
            {
                if (grouped && lastTeam != int.MinValue && p.Team != lastTeam) total += teamGap;
                lastTeam = p.Team;
                total += rowH + gap;
            }
            float x = PlayerListRight ? Screen.width - barW - 24f : 24f;
            float y = Screen.height - 24f - total;

            if (ShowPing && PhotonNetwork.InRoom)
            {
                header.normal.textColor = Color.white;
                Shadowed(new Rect(x + Slant * k, y - 20f * k, barW, 16f * k), "PING " + PhotonNetwork.GetPing().ToString(CultureInfo.InvariantCulture) + " MS", header, Mathf.Max(1f, k));
            }

            lastTeam = int.MinValue;
            var old = GUI.color;
            foreach (var p in Roster.Ordered)
            {
                if (grouped && lastTeam != int.MinValue && p.Team != lastTeam) y += teamGap;
                lastTeam = p.Team;
                bool watching = cam.IsTarget(p);
                bool tagged = IsOut(p);
                var bar = new Rect(x, y, barW, rowH);

                // The row, and the wash of the player's color (lava once they're tagged) over its left side.
                GUI.DrawTexture(bar, watching ? barHotTex : barTex);
                if (tagged) EnsureLava();
                if (tagged && lavaWash != null)
                {
                    // Tagged: flowing lava instead of their color.
                    GUI.DrawTexture(bar, lavaWash[LavaFrame]);
                }
                else
                {
                    var wash = new Color(p.Color.r, p.Color.g, p.Color.b, 1f);
                    GUI.color = new Color(wash.r, wash.g, wash.b, (watching ? 0.8f : 0.55f) * old.a);
                    GUI.DrawTexture(bar, washTex);
                    GUI.color = old;
                }

                tabNumber.normal.textColor = Color.white;
                Leaning(new Rect(x + 2f * k, y, numW, rowH), p.Index >= 0 ? p.Index.ToString(CultureInfo.InvariantCulture) : "-", tabNumber);

                // Right to left: the logo, then the camera mark (spectating). The name fills what's left.
                var logo = PlatformLogo(p);
                float logoH = rowH * 0.56f;
                float logoW = logo != null && logo.height > 0 ? logoH * logo.width / logo.height : logoH;
                // Both logos are centered on the same spot (where the round Steam one sits), so the wider Meta loop
                // reaches a little further right instead of starting further left. They sit half a slant in from the
                // right end, so the slanted corner doesn't cut into the bottom of the wide Meta one.
                float logoMid = bar.xMax - (10f + Slant / 2f) * k - logoH / 2f;
                var logoRect = new Rect(logoMid - logoW / 2f, y + (rowH - logoH) / 2f, logoW, logoH);
                float right = logo != null ? logoRect.x : bar.xMax - (4f + Slant / 2f) * k;

                if (watching)
                {
                    float camH = rowH * 0.42f, camW = camH * 40f / 28f;
                    var camRect = new Rect(right - 8f * k - camW, y + (rowH - camH) / 2f, camW, camH);
                    GUI.DrawTexture(camRect, cameraTex);
                    right = camRect.x;
                }
                float nameX = x + numW + 6f * k;
                float nameW = Mathf.Max(10f, right - 8f * k - nameX);
                rowName.normal.textColor = Color.white;
                Leaning(new Rect(nameX, y, nameW, rowH), Fit(Str.Clean(p.Name).ToUpperInvariant(), rowName, nameW), rowName);
                if (logo != null)
                {
                    // The real logos keep their own colors; the drawn white ones are softened a little.
                    bool image = logo == steamImage || logo == metaImage;
                    GUI.color = image ? old : new Color(LogoTint.r, LogoTint.g, LogoTint.b, LogoTint.a * old.a);
                    GUI.DrawTexture(logoRect, logo);
                    GUI.color = old;
                }
                y += rowH + gap;
            }
        }
    }
}
