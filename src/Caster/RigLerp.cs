using System.Collections.Generic;
using UnityEngine;

namespace DevicularCaster
{
    internal static class RigLerp
    {
        public const float Min = 0.01f;
        public const float Max = 1f;
        public const float Normal = 0.16f;

        public static float Value = Normal;
        public static bool Custom { get; private set; }
        public static float GameDefault { get; private set; } = -1f;

        private static readonly Dictionary<VRRig, float> originals = new Dictionary<VRRig, float>();
        private static float nextScan;

        private static float Middle => Mathf.Clamp(GameDefault > 0f ? GameDefault : Normal, Min + 0.01f, Max - 0.01f);

        public static float Shown => Value <= Middle
            ? 50f * Mathf.InverseLerp(Min, Middle, Value)
            : 50f + 50f * Mathf.InverseLerp(Middle, Max, Value);

        public static void SetShown(float s)
        {
            if (Mathf.Approximately(s, 50f)) ResetToGame();
            else Set(s < 50f ? Mathf.Lerp(Min, Middle, s / 50f) : Mathf.Lerp(Middle, Max, (s - 50f) / 50f));
        }

        public static void Set(float v)
        {
            Value = Mathf.Clamp(v, Min, Max);
            Custom = true;
        }

        public static void ResetToGame()
        {
            Custom = false;
            Value = GameDefault > 0f ? GameDefault : Normal;
            Restore();
        }

        private static void Learn(float v)
        {
            if (GameDefault > 0f || v <= 0f) return;
            GameDefault = v;
            if (!Custom) Value = Mathf.Clamp(v, Min, Max);
        }

        public static void Tick()
        {
            var local = VRRig.LocalRig;
            if (GameDefault < 0f && !Custom && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                try
                {
                    var all = VRRigCache.AllRigs;
                    if (all != null)
                        foreach (var rig in all)
                            if (rig != null && rig != local && !rig.isOfflineVRRig)
                            {
                                Learn(rig.lerpValueBody);
                                break;
                            }
                }
                catch { }
            }
            if (!Custom) return;
            IReadOnlyList<VRRig> rigs = null;
            try { rigs = VRRigCache.ActiveRigs; }
            catch { }
            if (rigs == null) return;
            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null || rig == local || rig.isOfflineVRRig) continue;
                if (!originals.ContainsKey(rig))
                {
                    originals[rig] = rig.lerpValueBody;
                    Learn(rig.lerpValueBody);
                }
                rig.lerpValueBody = Value;
            }
        }

        public static void Restore()
        {
            foreach (var kv in originals)
                if (kv.Key != null) kv.Key.lerpValueBody = kv.Value;
            originals.Clear();
        }
    }
}
