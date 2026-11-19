using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Devicular
{
    internal sealed class RecPlayer
    {
        public int Actor;
        public string Name = "";
        public Color Color = Color.white;
        public byte Team;
        public string TeamName = "";
        public Color TeamColor = Color.white;
        public bool IsLocal;
    }

    internal struct RecPose
    {
        public int Actor;
        public byte Mat;
        public float Scale;
        public Vector3 Root;
        public Quaternion RootRot;
        public Quaternion[] BoneRot;
        public Vector3[] BonePos;
    }

    internal sealed class RecFrame
    {
        public float Time;
        public RecPose[] Poses;
    }

    internal sealed class ReplayData
    {
        public DateTime Started;
        public string Map = "";
        public int Rate;
        public float Duration;
        public string[] Bones = new string[0];
        public readonly List<RecPlayer> Players = new List<RecPlayer>();
        public readonly List<RecFrame> Frames = new List<RecFrame>();
    }

    internal static class RigParts
    {
        private static readonly AccessTools.FieldRef<GorillaBodyRenderer, SkinnedMeshRenderer> defaultBody =
            AccessTools.FieldRefAccess<GorillaBodyRenderer, SkinnedMeshRenderer>("bodyDefault");

        public static SkinnedMeshRenderer Body(VRRig rig)
        {
            if (rig == null) return null;
            try
            {
                if (rig.bodyRenderer != null)
                {
                    var b = defaultBody(rig.bodyRenderer);
                    if (b != null) return b;
                    if (rig.bodyRenderer.ActiveBody != null) return rig.bodyRenderer.ActiveBody;
                }
            }
            catch
            {
            }
            return rig.mainSkin;
        }

        public static string PathOf(Transform root, Transform t)
        {
            if (t == null || root == null) return null;
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }

    /// <summary>How replays and live spectating write positions and rotations (DVR3 and the live stream use the same bytes).</summary>
    internal static class Wire
    {
        public static void W3(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x); w.Write(v.y); w.Write(v.z);
        }

        public static void WQ(BinaryWriter w, Quaternion q)
        {
            w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
        }

        public static void WC(BinaryWriter w, Color c)
        {
            w.Write(c.r); w.Write(c.g); w.Write(c.b);
        }

        public static void WQs(BinaryWriter w, Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-6f)
            {
                w.Write(0L);
                return;
            }
            w.Write((short)Mathf.RoundToInt(q.x / m * 32767f));
            w.Write((short)Mathf.RoundToInt(q.y / m * 32767f));
            w.Write((short)Mathf.RoundToInt(q.z / m * 32767f));
            w.Write((short)Mathf.RoundToInt(q.w / m * 32767f));
        }

        public static void WH(BinaryWriter w, Vector3 v)
        {
            w.Write(Mathf.FloatToHalf(v.x));
            w.Write(Mathf.FloatToHalf(v.y));
            w.Write(Mathf.FloatToHalf(v.z));
        }
    }

    /// <summary>
    /// Reads every player's pose the way replays store it. Shared by the recorder and live spectating.
    /// </summary>
    internal sealed class PoseSampler
    {
        public string[] Bones = new string[0];
        public readonly Dictionary<int, RecPlayer> Players = new Dictionary<int, RecPlayer>();

        private readonly Dictionary<VRRig, Transform[]> boneMaps = new Dictionary<VRRig, Transform[]>();
        private readonly List<CastPlayer> list = new List<CastPlayer>();
        private readonly List<RecPose> poses = new List<RecPose>();

        public void Reset()
        {
            Bones = new string[0];
            Players.Clear();
            boneMaps.Clear();
        }

        private Transform[] BonesOf(VRRig rig)
        {
            if (boneMaps.TryGetValue(rig, out var map) && map != null) return map;
            var root = rig.transform;
            if (Bones.Length == 0)
            {
                var body = RigParts.Body(rig);
                var bones = body != null ? body.bones : null;
                if (bones == null || bones.Length == 0) return null;
                var paths = new List<string>();
                void Add(Transform t)
                {
                    if (t == null || !t.IsChildOf(root) || t == root) return;
                    string path = RigParts.PathOf(root, t);
                    if (!string.IsNullOrEmpty(path) && !paths.Contains(path)) paths.Add(path);
                }
                foreach (var b in bones) Add(b);
                if (rig.headMesh != null) Add(rig.headMesh.transform);
                Add(rig.bodyTransform);
                Add(rig.leftHandTransform);
                Add(rig.rightHandTransform);
                Bones = paths.OrderBy(x => x.Count(ch => ch == '/')).ToArray();
            }
            map = new Transform[Bones.Length];
            for (int i = 0; i < map.Length; i++) map[i] = root.Find(Bones[i]);
            boneMaps[rig] = map;
            return map;
        }

        public RecPose[] Sample()
        {
            list.Clear();
            list.AddRange(Roster.Players);
            if (Roster.Local != null) list.Add(Roster.Local);
            poses.Clear();
            foreach (var p in list)
            {
                var rig = p.Rig;
                if (rig == null) continue;
                var map = BonesOf(rig);
                if (map == null) continue;
                if (!Players.TryGetValue(p.Actor, out var rp)) Players[p.Actor] = rp = new RecPlayer { Actor = p.Actor };
                rp.Name = p.Name;
                rp.Color = p.Color;
                rp.IsLocal = p.IsLocal;
                Roster.CaptureTeam(p, out int team, out string teamName, out Color teamColor);
                if (team > 0)
                {
                    rp.Team = (byte)team;
                    rp.TeamName = teamName;
                    rp.TeamColor = teamColor;
                }
                var pose = new RecPose
                {
                    Actor = p.Actor,
                    Mat = (byte)Mathf.Clamp(rig.setMatIndex, 0, 255),
                    Scale = rig.transform.localScale.y,
                    Root = rig.transform.position,
                    RootRot = rig.transform.rotation,
                    BoneRot = new Quaternion[Bones.Length],
                    BonePos = new Vector3[Bones.Length]
                };
                var rigRoot = rig.transform;
                var toRig = Quaternion.Inverse(rigRoot.rotation);
                for (int i = 0; i < map.Length; i++)
                {
                    var t = map[i];
                    if (t == null) continue;
                    pose.BoneRot[i] = toRig * t.rotation;
                    pose.BonePos[i] = rigRoot.InverseTransformPoint(t.position);
                }
                poses.Add(pose);
            }
            return poses.ToArray();
        }
    }
}
