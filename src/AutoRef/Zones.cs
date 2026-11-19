using System;
using System.Collections.Generic;
using System.Globalization;
using GorillaLocomotion;
using UnityEngine;

namespace AutoRef
{
    internal struct ZoneBox
    {
        public Vector3 Center;
        public Vector3 Half;
        public float Yaw;
        public bool Valid;

        public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);

        public bool Contains(Vector3 p)
        {
            if (!Valid) return false;
            var l = Quaternion.Inverse(Rotation) * (p - Center);
            return Mathf.Abs(l.x) <= Half.x && Mathf.Abs(l.y) <= Half.y && Mathf.Abs(l.z) <= Half.z;
        }

        public void Corners(Vector3[] into)
        {
            var rot = Rotation;
            int k = 0;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                into[k++] = Center + rot * new Vector3(x * Half.x, y * Half.y, z * Half.z);
        }

        public string Serialize()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(",", new[]
            {
                Center.x.ToString("0.00", c), Center.y.ToString("0.00", c), Center.z.ToString("0.00", c),
                (Half.x * 2).ToString("0.00", c), (Half.y * 2).ToString("0.00", c), (Half.z * 2).ToString("0.00", c),
                Yaw.ToString("0.0", c)
            });
        }

        public static bool TryParse(string text, out ZoneBox box)
        {
            box = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 7) return false;
            var v = new float[7];
            for (int i = 0; i < 7; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            box = new ZoneBox
            {
                Center = new Vector3(v[0], v[1], v[2]),
                Half = new Vector3(Mathf.Abs(v[3]) / 2, Mathf.Abs(v[4]) / 2, Mathf.Abs(v[5]) / 2),
                Yaw = v[6],
                Valid = true
            };
            return true;
        }
    }

    internal sealed class ZoneManager
    {
        private const string StructurePath = "Environment Objects/LocalObjects_Prefab/Forest/Terrain/campgroundstructure";

        private const float TableTop = 2.88f;
        private const float ExtraReach = 0.5f;
        private static ZoneBox DefaultBench => BenchBox(new Vector3(-63.55f, 0f, -63.54f), 1.03f, 0.93f, TableTop, -122f);

        private static ZoneBox BenchBox(Vector3 center, float halfX, float halfZ, float top, float yaw)
        {
            float m = Mathf.Clamp(Settings.BenchMargin.Value, 0f, 2f) + ExtraReach;
            float low = top - 0.9f, high = top + 2.5f;
            return new ZoneBox
            {
                Center = new Vector3(center.x, (low + high) / 2f, center.z),
                Half = new Vector3(halfX + m, (high - low) / 2f, halfZ + m),
                Yaw = yaw,
                Valid = true
            };
        }

        private float appliedMargin = -1f;

        private static readonly ZoneBox DefaultGazebo = new ZoneBox
        {
            Center = new Vector3(-62.15f, 4.56f, -62.34f),
            Half = new Vector3(4.61f, 2.99f, 4.16f),
            Yaw = 0f,
            Valid = true
        };

        public ZoneBox Bench = DefaultBench;
        public ZoneBox Gazebo = DefaultGazebo;
        public bool GeometryFound;
        public bool CollidersActive;
        public bool BenchOverridden;
        public bool GazeboOverridden;
        public string Status = "Looking for the Forest gazebo...";

        private readonly HashSet<Collider> benchColliders = new HashSet<Collider>();
        private readonly HashSet<Collider> roofColliders = new HashSet<Collider>();
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private Transform root;
        private float nextSearch;
        private string lastBenchOverride, lastGazeboOverride;

        public void Rescan()
        {
            root = null;
            GeometryFound = false;
            nextSearch = 0f;
        }

        public void Tick()
        {
            ApplyOverrides();

            if (GeometryFound && root == null)
            {
                GeometryFound = false;
                Status = "Gazebo objects were unloaded; searching again...";
            }

            if (!GeometryFound && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 10f;
                try
                {
                    TryBuildFromMap();
                }
                catch (Exception e)
                {
                    Status = "Map scan failed, using built-in zones: " + e.Message;
                    Plugin.Log.LogWarning("Zone scan failed: " + e);
                }
            }

            CollidersActive = GeometryFound && AnyActive(benchColliders) && AnyActive(roofColliders);
        }

        private void ApplyOverrides()
        {
            string b = Settings.BenchZoneOverride.Value, g = Settings.GazeboZoneOverride.Value;
            float margin = Settings.BenchMargin.Value;
            if (b == lastBenchOverride && g == lastGazeboOverride && Mathf.Approximately(margin, appliedMargin)) return;
            lastBenchOverride = b;
            lastGazeboOverride = g;
            appliedMargin = margin;
            BenchOverridden = ZoneBox.TryParse(b, out var bb);
            GazeboOverridden = ZoneBox.TryParse(g, out var gb);
            Bench = BenchOverridden ? bb : DefaultBench;
            Gazebo = GazeboOverridden ? gb : DefaultGazebo;
            Rescan();
        }

        private static bool AnyActive(HashSet<Collider> set)
        {
            foreach (var c in set)
                if (c != null && c.enabled && c.gameObject.activeInHierarchy) return true;
            return false;
        }

        private void TryBuildFromMap()
        {
            var structure = FindStructure();
            if (structure == null)
            {
                Status = "Forest gazebo not found in the loaded map - using built-in zones.";
                nextSearch = Time.unscaledTime + 60f;
                return;
            }

            var table = structure.Find("PincicTable");
            var roof = structure.Find("roof (1)");
            var roofWood = structure.Find("NormalObjects/roofwood");
            var slab = structure.Find("concrete slab");
            if (table == null || roof == null)
            {
                Status = "Gazebo found but its table/roof changed - using built-in zones.";
                return;
            }

            benchColliders.Clear();
            foreach (var c in table.GetComponentsInChildren<Collider>(true)) benchColliders.Add(c);
            roofColliders.Clear();
            foreach (var c in roof.GetComponentsInChildren<Collider>(true)) roofColliders.Add(c);
            if (roofWood != null)
                foreach (var c in roofWood.GetComponentsInChildren<Collider>(true)) roofColliders.Add(c);

            var tableMesh = table.Find("default") ?? table;
            if (!BenchOverridden && TryWorldBox(tableMesh, out var tMin, out var tMax, out float tYaw, out var tCenter))
            {
                Bench = BenchBox(tCenter, (tMax.x - tMin.x) / 2f, (tMax.z - tMin.z) / 2f, tMax.y, tYaw);
            }

            if (!GazeboOverridden && TryWorldBox(roof, out var rMin, out var rMax, out float rYaw, out var rCenter))
            {
                float floor = rMin.y - 2.5f;
                if (slab != null && TryWorldBox(slab, out _, out var sMax, out _, out _)) floor = sMax.y;
                float low = floor - 0.6f, high = rMax.y;
                Gazebo = new ZoneBox
                {
                    Center = new Vector3(rCenter.x, (low + high) / 2f, rCenter.z),
                    Half = new Vector3((rMax.x - rMin.x) / 2f, (high - low) / 2f, (rMax.z - rMin.z) / 2f),
                    Yaw = rYaw,
                    Valid = true
                };
            }

            root = structure;
            GeometryFound = true;
            Status = $"Forest gazebo found ({benchColliders.Count} table / {roofColliders.Count} roof colliders).";
            Plugin.Log.LogInfo($"Zones from map. Bench: {Bench.Serialize()}  Gazebo: {Gazebo.Serialize()}");
        }

        private static Transform FindStructure()
        {
            var go = GameObject.Find(StructurePath);
            if (go != null) return go.transform;

            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || t.name != "campgroundstructure") continue;
                var p = t.parent;
                if (p == null || p.name != "Terrain") continue;
                var pp = p.parent;
                if (pp == null || pp.name != "Forest") continue;
                if (!t.gameObject.scene.IsValid()) continue;
                return t;
            }
            return null;
        }

        private static bool TryWorldBox(Transform t, out Vector3 min, out Vector3 max, out float yaw, out Vector3 center)
        {
            min = max = center = Vector3.zero;
            yaw = 0f;
            Bounds local;
            var mc = t.GetComponent<MeshCollider>();
            var bc = t.GetComponent<BoxCollider>();
            var mf = t.GetComponent<MeshFilter>();
            if (mc != null && mc.sharedMesh != null) local = mc.sharedMesh.bounds;
            else if (bc != null) local = new Bounds(bc.center, bc.size);
            else if (mf != null && mf.sharedMesh != null) local = mf.sharedMesh.bounds;
            else return false;

            Vector3 right = t.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-4f)
            {
                right = t.forward;
                right.y = 0f;
            }
            yaw = right.sqrMagnitude < 1e-4f ? 0f : Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;
            var inv = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));

            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                var corner = new Vector3(x == 0 ? local.min.x : local.max.x, y == 0 ? local.min.y : local.max.y, z == 0 ? local.min.z : local.max.z);
                var w = inv * t.TransformPoint(corner);
                lo = Vector3.Min(lo, w);
                hi = Vector3.Max(hi, w);
            }

            var size = hi - lo;
            if (size.x > 40f || size.z > 40f || size.y > 40f) return false;

            var c = (lo + hi) / 2f;
            center = Quaternion.Euler(0f, yaw, 0f) * c;
            min = new Vector3(lo.x, lo.y, lo.z);
            max = new Vector3(hi.x, hi.y, hi.z);
            return true;
        }

        public void Evaluate(PlayerSnapshot p)
        {
            var pos = p.Position;
            Collider below = null;
            float dist = float.MaxValue;
            if (CollidersActive)
                below = FirstSolidBelow(pos, Mathf.Max(Settings.BenchMaxDrop.Value, 4f), out dist);
            p.Surface = below != null ? below.name : null;

            bool onTable = CollidersActive && !BenchOverridden &&
                           below != null && benchColliders.Contains(below) && dist >= 0.15f && dist <= Settings.BenchMaxDrop.Value;
            p.OnBench = onTable || Bench.Contains(pos);

            bool onRoof = below != null && roofColliders.Contains(below);
            p.UnderGazebo = Gazebo.Contains(pos) && !onRoof;
        }

        private Collider FirstSolidBelow(Vector3 pos, float maxDistance, out float distance)
        {
            distance = float.MaxValue;
            Collider best = null;
            var origin = pos + Vector3.up * 0.05f;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, maxDistance + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                var c = h.collider;
                if (c == null || h.distance >= distance) continue;
                if (IsIgnored(c)) continue;
                best = c;
                distance = h.distance;
            }
            return best;
        }

        private static bool IsIgnored(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) return true;
            if (c.GetComponentInParent<VRRig>() != null) return true;
            if (c.GetComponentInParent<RigContainer>() != null) return true;
            if (c.GetComponentInParent<GTPlayer>() != null) return true;
            return false;
        }
    }
}
