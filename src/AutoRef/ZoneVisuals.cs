using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutoRef
{
    internal sealed class ZoneVisuals
    {
        public static readonly Color TaggerRed = new Color(1f, 0.16f, 0.12f);
        public static readonly Color RunnerGreen = new Color(0.18f, 1f, 0.32f);

        private static readonly int[,] Edges =
        {
            { 0, 1 }, { 0, 2 }, { 0, 4 }, { 1, 3 }, { 1, 5 }, { 2, 3 },
            { 2, 6 }, { 3, 7 }, { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 }
        };

        private sealed class Box
        {
            public GameObject Go;
            public readonly LineRenderer[] Lines = new LineRenderer[12];
        }

        private GameObject root;
        private Box bench, gazebo;
        private bool failed;
        private readonly Vector3[] corners = new Vector3[8];

        public string Problem;
        public bool Available => root != null && !failed;
        public bool Failed => failed;

        public void Update(ZoneManager zones, bool show)
        {
            if (failed) return;
            if (!show)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            if (root == null && !Create()) return;
            if (!root.activeSelf) root.SetActive(true);
            Apply(bench, zones.Bench);
            Apply(gazebo, zones.Gazebo);
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        private bool Create()
        {
            try
            {
                var shader = FindShader();
                if (shader == null)
                {
                    failed = true;
                    Problem = "No see-through shader available in this game build - zone boxes are drawn on your monitor only.";
                    Plugin.Log.LogWarning(Problem);
                    return false;
                }

                root = new GameObject("AutoRef_ZoneBoxes");
                UnityEngine.Object.DontDestroyOnLoad(root);
                var cube = BuildCube();
                var lineMat = MakeMaterial(shader, Color.white);
                bench = MakeBox("TaggersBench", cube, MakeMaterial(shader, WithAlpha(TaggerRed, 0.22f)), lineMat, TaggerRed);
                gazebo = MakeBox("RunnersArea", cube, MakeMaterial(shader, WithAlpha(RunnerGreen, 0.09f)), lineMat, RunnerGreen);
                Plugin.Log.LogInfo($"Zone boxes created with shader '{shader.name}'.");
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Problem = "Zone boxes failed (" + e.Message + ") - drawn on your monitor only.";
                Plugin.Log.LogWarning("Zone boxes failed: " + e);
                Destroy();
                return false;
            }
        }

        private static Shader FindShader()
        {
            foreach (var name in new[] { "Sprites/Default", "UI/Default", "Hidden/Internal-Colored" })
            {
                var s = Shader.Find(name);
                if (s != null && s.isSupported) return s;
            }
            return null;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Material MakeMaterial(Shader shader, Color color)
        {
            var m = new Material(shader) { color = color, renderQueue = 3000 };
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)CullMode.Off);
            m.SetInt("_ZWrite", 0);
            return m;
        }

        private Box MakeBox(string name, Mesh cube, Material fill, Material lineMat, Color edgeColor)
        {
            var box = new Box { Go = new GameObject(name) };
            box.Go.transform.SetParent(root.transform, false);
            box.Go.AddComponent<MeshFilter>().sharedMesh = cube;
            var mr = box.Go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = fill;
            Quiet(mr);

            var edge = WithAlpha(edgeColor, 0.95f);
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject("Edge" + i);
                go.transform.SetParent(root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMat;
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.045f;
                lr.numCapVertices = 2;
                lr.startColor = lr.endColor = edge;
                Quiet(lr);
                box.Lines[i] = lr;
            }
            return box;
        }

        private static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void Apply(Box box, ZoneBox zone)
        {
            bool visible = zone.Valid;
            if (box.Go.activeSelf != visible) box.Go.SetActive(visible);
            foreach (var lr in box.Lines)
                if (lr.gameObject.activeSelf != visible) lr.gameObject.SetActive(visible);
            if (!visible) return;

            var t = box.Go.transform;
            t.SetPositionAndRotation(zone.Center, zone.Rotation);
            t.localScale = zone.Half * 2f;

            zone.Corners(corners);
            for (int i = 0; i < 12; i++)
            {
                box.Lines[i].SetPosition(0, corners[Edges[i, 0]]);
                box.Lines[i].SetPosition(1, corners[Edges[i, 1]]);
            }
        }

        private static Mesh BuildCube()
        {
            var v = new Vector3[8];
            var col = new Color[8];
            for (int i = 0; i < 8; i++)
            {
                v[i] = new Vector3((i & 4) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 1) != 0 ? 0.5f : -0.5f);
                col[i] = Color.white;
            }
            var tris = new[]
            {
                0, 1, 3, 0, 3, 2,
                4, 6, 7, 4, 7, 5,
                0, 4, 5, 0, 5, 1,
                2, 3, 7, 2, 7, 6,
                0, 2, 6, 0, 6, 4,
                1, 5, 7, 1, 7, 3
            };
            var mesh = new Mesh { name = "AutoRefZoneCube", vertices = v, colors = col, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
