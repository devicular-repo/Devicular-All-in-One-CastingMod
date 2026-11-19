using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DevicularCaster
{
    internal sealed class Nametags
    {
        public bool Enabled = true;
        public float Size = 1f;
        public bool ThroughWalls = true;
        public bool ShowName = true;
        public bool ShowFps;
        public bool ShowPlatform;

        private static readonly AccessTools.FieldRef<VRRig, int> RigFps = Field<int>("fps");
        private static readonly AccessTools.FieldRef<VRRig, RigContainer> RigBox = Field<RigContainer>("rigContainer");
        private static readonly AccessTools.FieldRef<VRRig, HashSet<string>> RigOwned = Field<HashSet<string>>("_playerOwnedCosmetics");
        private readonly Dictionary<int, string> platforms = new Dictionary<int, string>();
        private readonly Dictionary<int, int> fpsShown = new Dictionary<int, int>();
        private float nextFps;

        private static AccessTools.FieldRef<VRRig, T> Field<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<VRRig, T>(name); }
            catch { return null; }
        }

        private static int FpsOf(VRRig rig)
        {
            int fps = 0;
            try { if (RigFps != null) fps = RigFps(rig); } catch { }
            try
            {
                var box = RigBox != null ? RigBox(rig) : null;
                if (box != null) fps = Math.Max(fps, box.PlayerStats.FPS);
            }
            catch { }
            return fps;
        }

        public string PlatformOf(int actor, VRRig rig)
        {
            if (platforms.TryGetValue(actor, out var known)) return known;
            string result = null;
            try
            {
                string tag = NetworkSystem.Instance != null ? NetworkSystem.Instance.GetPlayerPlatform(actor) : "";
                if (!string.IsNullOrEmpty(tag) && !string.Equals(tag, "OTHER", StringComparison.OrdinalIgnoreCase))
                    result = tag.ToUpperInvariant();
            }
            catch { }
            if (result == null)
            {
                HashSet<string> owned = null;
                try { owned = RigOwned != null ? RigOwned(rig) : null; } catch { }
                if (owned == null || owned.Count == 0) return "";
                bool steam = false, pc = false;
                foreach (var item in owned)
                {
                    if (item == null) continue;
                    if (item.Contains("S. FIRST LOGIN")) steam = true;
                    else if (item.Contains("FIRST LOGIN")) pc = true;
                }
                result = steam ? "STEAM" : pc ? "PC" : "QUEST";
            }
            platforms[actor] = result;
            return result;
        }

        /// <summary>The name and FPS lines. The platform is a line too only when its logo can't be shown.</summary>
        private string Compose(CastPlayer p, bool refreshFps, bool platformLogo)
        {
            var info = new List<string>();
            if (ShowFps && p.Rig != null && !p.Ghost)
            {
                if (refreshFps || !fpsShown.ContainsKey(p.Actor)) fpsShown[p.Actor] = FpsOf(p.Rig);
                int fps = fpsShown[p.Actor];
                if (fps > 0) info.Add(fps.ToString(CultureInfo.InvariantCulture) + " FPS");
            }
            if (ShowPlatform && !platformLogo && p.Rig != null && !p.Ghost)
            {
                string platform = PlatformOf(p.Actor, p.Rig);
                if (platform.Length > 0) info.Add(platform);
            }
            string name = ShowName ? Str.Clean(p.Name).Replace("<", "").Replace(">", "") : "";
            // Always stacked: the name, then each item (FPS, platform) on its own line under it, never side by side.
            string lines = string.Join("\n", info);
            if (name.Length == 0) return lines;
            if (lines.Length == 0) return name;
            return name + "\n<size=62%>" + lines + "</size>";
        }

        // ---------- the platform as a logo ----------

        // The platform shows as its logo (the Steam or Meta one, like the player list's) right next to the name, in
        // its own colors, instead of a line saying it. If the logo can't be shown, the platform is a line in the
        // stack as before.
        private readonly Dictionary<int, SpriteRenderer> logos = new Dictionary<int, SpriteRenderer>();
        private Sprite steamSprite, metaSprite;
        private bool spritesTried;
        private Material logoMaterial;
        private int logoMaterialMode = -1;

        /// <summary>Where a tag's logo goes, worked out from its text's layout (again only when the text changes).</summary>
        private sealed class LogoPlace
        {
            public string Text;
            public Sprite Sprite;
            public TMP_FontAsset Font;
            public Vector3 Position;
            public float Height;
            /// <summary>How far the tag moves left so the name and logo together are centered over the head.</summary>
            public float Shift;
        }

        private readonly Dictionary<int, LogoPlace> places = new Dictionary<int, LogoPlace>();
        // In the tag's own units: the gap between the name and the logo, and the logo's height in capital letters.
        private const float LogoGap = 0.12f, LogoCaps = 1.15f;

        /// <summary>The logo for this player's platform: Steam's, or Meta's for Quest and Oculus PC. Null to show none.</summary>
        private Sprite LogoFor(CastPlayer p)
        {
            if (!ShowPlatform || p.Rig == null || p.Ghost) return null;
            string platform = PlatformOf(p.Actor, p.Rig);
            if (platform.Length == 0) return null;
            if (!spritesTried)
            {
                spritesTried = true;
                steamSprite = MakeSprite(Overlays.SteamImage);
                metaSprite = MakeSprite(Overlays.MetaImage);
            }
            return platform.IndexOf("STEAM", StringComparison.OrdinalIgnoreCase) >= 0 ? steamSprite : metaSprite;
        }

        /// <summary>A sprite one unit tall from a logo image (the whole rectangle, as the image isn't readable).</summary>
        private static Sprite MakeSprite(Texture2D tex)
        {
            if (tex == null) return null;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.height, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>
        /// The logos' material: drawn over walls like the text while "Show through walls" is on, hidden by them while
        /// it's off. Null if neither shader is there (the sprite's own material is used then).
        /// </summary>
        private Material LogoMaterial(bool throughWalls)
        {
            if (logoMaterial == null)
            {
                var shader = Shader.Find("UI/Default");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;
                logoMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                logoMaterialMode = -1;
            }
            int mode = throughWalls ? (int)UnityEngine.Rendering.CompareFunction.Always : (int)UnityEngine.Rendering.CompareFunction.LessEqual;
            if (mode != logoMaterialMode)
            {
                logoMaterialMode = mode;
                // UI/Default reads its depth test from this; over walls it also draws with the overlay text.
                logoMaterial.SetInt("unity_GUIZTestMode", mode);
                logoMaterial.renderQueue = throughWalls ? 4000 : 3000;
            }
            return logoMaterial;
        }

        /// <summary>
        /// Where the logo goes beside the tag's top line (the name): just right of its last letter, as tall as its
        /// capitals and level with them. With no text, it sits alone just over the head.
        /// </summary>
        private static LogoPlace Place(TextMeshPro tag, string text, Sprite sprite)
        {
            var place = new LogoPlace { Text = text, Sprite = sprite, Font = tag.font, Height = 0.38f };
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
            if (text.Length > 0)
            {
                tag.ForceMeshUpdate();
                var info = tag.textInfo;
                if (info != null && info.lineCount > 0)
                {
                    var line = info.lineInfo[0];
                    float right = float.MinValue, top = float.MinValue;
                    for (int i = line.firstCharacterIndex; i <= line.lastCharacterIndex && i < info.characterCount; i++)
                    {
                        var ch = info.characterInfo[i];
                        if (!ch.isVisible) continue;
                        right = Mathf.Max(right, ch.topRight.x);
                        top = Mathf.Max(top, ch.topRight.y);
                    }
                    if (right > float.MinValue)
                    {
                        float caps = Mathf.Max(0.05f, top - line.baseline);
                        place.Height = Mathf.Clamp(caps * LogoCaps, 0.15f, 0.8f);
                        float width = place.Height * aspect;
                        place.Position = new Vector3(right + LogoGap + width / 2f, line.baseline + caps / 2f, 0f);
                        place.Shift = (LogoGap + width) / 2f;
                        return place;
                    }
                }
            }
            place.Position = new Vector3(0f, place.Height / 2f, 0f);
            return place;
        }

        /// <summary>Shows (or hides) a tag's logo next to its name. Returns how far the tag moves left to stay centered.</summary>
        private float ShowLogo(int actor, TextMeshPro tag, string text, Sprite sprite, bool throughWalls)
        {
            logos.TryGetValue(actor, out var logo);
            if (sprite == null)
            {
                if (logo != null && logo.gameObject.activeSelf) logo.gameObject.SetActive(false);
                return 0f;
            }
            if (logo == null)
            {
                var go = new GameObject("Logo");
                go.transform.SetParent(tag.transform, false);
                logo = go.AddComponent<SpriteRenderer>();
                logos[actor] = logo;
            }
            if (!places.TryGetValue(actor, out var place) || place.Text != text || place.Sprite != sprite || place.Font != tag.font)
            {
                place = Place(tag, text, sprite);
                places[actor] = place;
            }
            logo.transform.localPosition = place.Position;
            logo.transform.localScale = new Vector3(place.Height, place.Height, 1f);
            var mat = LogoMaterial(throughWalls);
            if (mat != null && logo.sharedMaterial != mat) logo.sharedMaterial = mat;
            if (logo.sprite != sprite) logo.sprite = sprite;
            if (!logo.gameObject.activeSelf) logo.gameObject.SetActive(true);
            return place.Shift;
        }

        private static readonly Color Lava = new Color(1f, 0.45f, 0.15f);
        private readonly Dictionary<int, TextMeshPro> tags = new Dictionary<int, TextMeshPro>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private GameObject root;
        private TMP_FontAsset font;
        private Shader overlay;
        private bool searched;

        // Gorilla Tag's own name label over each gorilla, to borrow its font.
        private static readonly AccessTools.FieldRef<VRRig, TextMeshPro> RigNameText = Field<TextMeshPro>("playerText1");
        private bool gameFont;
        private float nextFontTry;

        /// <summary>The font the game writes players' names with, read off a gorilla's name label. Null until one exists.</summary>
        private static TMP_FontAsset GameNameFont()
        {
            if (RigNameText == null) return null;
            try
            {
                var own = VRRig.LocalRig;
                var text = own != null ? RigNameText(own) : null;
                if (text != null && text.font != null) return text.font;
                var rigs = VRRigCache.ActiveRigs;
                if (rigs == null) return null;
                for (int i = 0; i < rigs.Count; i++)
                {
                    text = rigs[i] != null ? RigNameText(rigs[i]) : null;
                    if (text != null && text.font != null) return text.font;
                }
            }
            catch
            {
            }
            return null;
        }

        private void Resolve()
        {
            // The game's name font, as soon as there's a gorilla to read it from (tags made before then switch to it).
            if (!gameFont && Time.unscaledTime >= nextFontTry)
            {
                nextFontTry = Time.unscaledTime + 1f;
                var game = GameNameFont();
                if (game != null)
                {
                    gameFont = true;
                    searched = true;
                    font = game;
                    foreach (var t in tags.Values)
                        if (t != null) t.font = font;
                    Plugin.Log?.LogInfo("Name tags: using the game's name font (" + font.name + ").");
                }
            }
            if (searched) return;
            searched = true;
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var want in new[] { "agency", "utopium", "anton", "liberation" })
            {
                font = fonts.FirstOrDefault(f => f != null && f.name.ToLowerInvariant().Contains(want));
                if (font != null) break;
            }
            if (font == null) font = TMP_Settings.defaultFontAsset;
            overlay = Shader.Find("TextMeshPro/Mobile/Distance Field Overlay") ?? Shader.Find("TextMeshPro/Distance Field Overlay");
        }

        private TextMeshPro Create(int actor)
        {
            if (root == null)
            {
                root = new GameObject("Devicular Nametags");
                Object.DontDestroyOnLoad(root);
            }
            var go = new GameObject("Devicular Tag " + actor);
            go.transform.SetParent(root.transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.fontSize = 4.8f;
            tmp.alignment = TextAlignmentOptions.Bottom;
            tmp.rectTransform.pivot = new Vector2(0.5f, 0f);
            tmp.rectTransform.sizeDelta = new Vector2(24f, 1f);
            tmp.richText = true;
            tmp.enableWordWrapping = false;
            tmp.outlineWidth = 0.12f;
            tmp.outlineColor = new Color32(0, 0, 0, 200);
            tags[actor] = tmp;
            return tmp;
        }

        public void Update(CasterCamera cam, bool hidden)
        {
            if (!Enabled || hidden)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            Resolve();
            var camera = cam.RenderingCamera;
            if (camera == null) return;
            if (root != null && !root.activeSelf) root.SetActive(true);

            seen.Clear();
            bool refreshFps = Time.unscaledTime >= nextFps;
            if (refreshFps) nextFps = Time.unscaledTime + 0.5f;
            if (Roster.Players.Count == 0 && !Roster.Replaying) platforms.Clear();
            foreach (var p in Roster.Players)
            {
                if (p.Head == null) continue;
                if (cam.FollowFirstPerson && cam.IsTarget(p)) continue;
                var logoSprite = LogoFor(p);
                string text = Compose(p, refreshFps, logoSprite != null);
                if (text.Length == 0 && logoSprite == null) continue;
                seen.Add(p.Actor);
                if (!tags.TryGetValue(p.Actor, out var tmp) || tmp == null) tmp = Create(p.Actor);
                if (tmp.text != text) tmp.text = text;
                var color = p.Tagged ? Lava : new Color(p.Color.r, p.Color.g, p.Color.b, 1f);
                if (tmp.color != color) tmp.color = color;
                bool wantOverlay = ThroughWalls && overlay != null;
                var mat = tmp.fontMaterial;
                if (mat != null)
                {
                    if (wantOverlay && mat.shader != overlay) mat.shader = overlay;
                    else if (!wantOverlay && overlay != null && mat.shader == overlay && font != null) tmp.fontMaterial = new Material(font.material);
                }
                var t = tmp.transform;
                float scale = 0.25f * p.Scale * Mathf.Clamp(Size, 0.4f, 3f);
                t.localScale = new Vector3(scale, scale, scale);
                t.position = p.Head.position + Vector3.up * 0.36f * p.Scale;
                t.LookAt(camera.transform.position);
                t.Rotate(0f, 180f, 0f);
                // Active before the logo is placed: the text's layout is only there for a tag that's showing.
                if (!tmp.gameObject.activeSelf) tmp.gameObject.SetActive(true);
                // The logo goes right of the name; the tag moves left by half of it, so name and logo stay centered.
                float shift = ShowLogo(p.Actor, tmp, text, logoSprite, wantOverlay);
                if (shift > 0f) t.position -= t.right * (shift * scale);
            }

            List<int> gone = null;
            foreach (var kv in tags)
            {
                if (seen.Contains(kv.Key)) continue;
                if (kv.Value != null && kv.Value.gameObject.activeSelf) kv.Value.gameObject.SetActive(false);
                if (!Roster.Replaying && Roster.ByActor(kv.Key) == null) (gone ??= new List<int>()).Add(kv.Key);
            }
            if (gone != null)
                foreach (var a in gone)
                {
                    if (tags[a] != null) Object.Destroy(tags[a].gameObject);
                    tags.Remove(a);
                    logos.Remove(a);
                    places.Remove(a);
                }
        }

        public void Shutdown()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            tags.Clear();
            logos.Clear();
            places.Clear();
        }
    }
}
