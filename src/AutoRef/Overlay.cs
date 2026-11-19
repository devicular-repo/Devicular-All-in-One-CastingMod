using UnityEngine;

namespace AutoRef
{
    internal sealed class Overlay
    {
        private static readonly int[,] Edges =
        {
            { 0, 1 }, { 0, 2 }, { 0, 4 }, { 1, 3 }, { 1, 5 }, { 2, 3 },
            { 2, 6 }, { 3, 7 }, { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 }
        };

        private readonly RefereeController c;
        private readonly Vector3[] corners = new Vector3[8];
        private Camera cachedCamera;
        private float nextCameraLookup;

        public Overlay(RefereeController controller) => c = controller;

        /// <summary>
        /// The bench and gazebo boxes, drawn on screen when the zones' own visuals couldn't be made. All-in-One: no
        /// labels over players (the caster's name tags show who's who; the referee puts no text on screen).
        /// </summary>
        public void Draw()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!c.ShowZoneBoxesNow || !c.ZoneVisuals.Failed) return;

            var cam = FindCamera();
            if (cam == null) return;
            DrawBox(cam, c.Zones.Bench, ZoneVisuals.TaggerRed);
            DrawBox(cam, c.Zones.Gazebo, ZoneVisuals.RunnerGreen);
        }

        private Camera FindCamera()
        {
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled) return cachedCamera;
            if (Time.unscaledTime < nextCameraLookup) return Camera.main;
            nextCameraLookup = Time.unscaledTime + 2f;
            cachedCamera = null;
            try
            {
                var tagger = GorillaTagger.Instance;
                if (tagger != null && tagger.thirdPersonCamera != null && tagger.thirdPersonCamera.activeInHierarchy)
                {
                    var cam = tagger.thirdPersonCamera.GetComponentInChildren<Camera>();
                    if (cam != null && cam.isActiveAndEnabled && cam.targetTexture == null) cachedCamera = cam;
                }
            }
            catch
            {
            }
            if (cachedCamera == null) cachedCamera = Camera.main;
            return cachedCamera;
        }

        private static bool ToScreen(Camera cam, Vector3 world, out Vector2 screen)
        {
            var v = cam.WorldToViewportPoint(world);
            screen = new Vector2(v.x * Screen.width, (1f - v.y) * Screen.height);
            return v.z > 0.05f;
        }

        private void DrawBox(Camera cam, ZoneBox box, Color color)
        {
            if (!box.Valid) return;
            box.Corners(corners);
            for (int i = 0; i < Edges.GetLength(0); i++)
            {
                if (!ToScreen(cam, corners[Edges[i, 0]], out var a) || !ToScreen(cam, corners[Edges[i, 1]], out var b)) continue;
                DrawLine(a, b, color, 2f);
            }
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f || len > 20000f) return;
            var matrix = GUI.matrix;
            var old = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = old;
        }
    }
}
