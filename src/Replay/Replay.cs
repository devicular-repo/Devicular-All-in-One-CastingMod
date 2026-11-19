using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace DevicularReplay
{
    internal static class ReplayFiles
    {
        private const string Magic = "DVR3";

        public static string Folder
        {
            get
            {
                string dir = Path.Combine(Paths.BepInExRootPath, "Devicular", "replays");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static List<FileInfo> List()
        {
            try
            {
                return new DirectoryInfo(Folder).GetFiles("*.dvr").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            }
            catch
            {
                return new List<FileInfo>();
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileOp
        {
            public IntPtr Hwnd;
            public uint Func;
            public string From;
            public string To;
            public ushort Flags;
            public int Aborted;
            public IntPtr Mappings;
            public string Title;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref ShFileOp op);

        public static bool Delete(string path, out string error)
        {
            error = "";
            try
            {
                if (!File.Exists(path)) return true;
                var op = new ShFileOp { Func = 3, From = Path.GetFullPath(path) + "\0\0", Flags = 0x0040 | 0x0010 | 0x0004 | 0x0400 };
                int result = SHFileOperation(ref op);
                if (result == 0 && op.Aborted == 0 && !File.Exists(path)) return true;
                error = result != 0 ? "Windows error " + result : "it's still there";
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static Vector3 R3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static Quaternion RQ(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static Color RC(BinaryReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1f);

        private static Quaternion RQs(BinaryReader r)
        {
            short x = r.ReadInt16(), y = r.ReadInt16(), z = r.ReadInt16(), w = r.ReadInt16();
            if (x == 0 && y == 0 && z == 0 && w == 0) return new Quaternion(0f, 0f, 0f, 0f);
            var q = new Quaternion(x / 32767f, y / 32767f, z / 32767f, w / 32767f);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        private static Vector3 RH(BinaryReader r) =>
            new Vector3(Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16()));

        /// <summary>Writes a replay. With <paramref name="sound"/>, its sound goes after the frames (see <see cref="ReplaySound"/>).</summary>
        public static void Save(ReplayData d, string path, ReplaySound sound = null)
        {
            using (var fs = File.Create(path))
            using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
            using (var w = new BinaryWriter(gz))
            {
                w.Write(Magic.ToCharArray());
                w.Write(3);
                w.Write(d.Started.Ticks);
                w.Write(d.Map ?? "");
                w.Write(d.Rate);
                w.Write(d.Duration);
                w.Write(d.Bones.Length);
                foreach (var b in d.Bones) w.Write(b ?? "");
                w.Write(d.Players.Count);
                foreach (var p in d.Players)
                {
                    w.Write(p.Actor);
                    w.Write(p.Name ?? "");
                    Wire.WC(w, p.Color);
                    w.Write(p.Team);
                    w.Write(p.TeamName ?? "");
                    Wire.WC(w, p.TeamColor);
                    w.Write(p.IsLocal);
                }
                w.Write(d.Frames.Count);
                foreach (var f in d.Frames)
                {
                    w.Write(f.Time);
                    w.Write((byte)f.Poses.Length);
                    foreach (var p in f.Poses)
                    {
                        w.Write(p.Actor);
                        w.Write(p.Mat);
                        w.Write(p.Scale);
                        Wire.W3(w, p.Root);
                        Wire.WQ(w, p.RootRot);
                        for (int i = 0; i < d.Bones.Length; i++)
                        {
                            Wire.WQs(w, p.BoneRot[i]);
                            Wire.WH(w, p.BonePos[i]);
                        }
                    }
                }
                if (sound != null && sound.Count > 0)
                {
                    w.Flush();
                    sound.WriteSection(gz);
                }
            }
        }

        public static ReplayData Load(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new BinaryReader(gz))
            {
                string magic = new string(r.ReadChars(4));
                if (magic == "DVR1" || magic == "DVR2") throw new InvalidDataException("this replay was made by an older version that didn't save arm and head movement - record a new one");
                if (magic != Magic) throw new InvalidDataException("not a Devicular replay");
                r.ReadInt32();
                var d = new ReplayData
                {
                    Started = new DateTime(r.ReadInt64()),
                    Map = r.ReadString(),
                    Rate = r.ReadInt32(),
                    Duration = r.ReadSingle()
                };
                int bones = r.ReadInt32();
                d.Bones = new string[bones];
                for (int i = 0; i < bones; i++) d.Bones[i] = r.ReadString();
                int players = r.ReadInt32();
                for (int i = 0; i < players; i++)
                {
                    d.Players.Add(new RecPlayer
                    {
                        Actor = r.ReadInt32(),
                        Name = r.ReadString(),
                        Color = RC(r),
                        Team = r.ReadByte(),
                        TeamName = r.ReadString(),
                        TeamColor = RC(r),
                        IsLocal = r.ReadBoolean()
                    });
                }
                int frames = r.ReadInt32();
                for (int i = 0; i < frames; i++)
                {
                    var f = new RecFrame { Time = r.ReadSingle() };
                    int n = r.ReadByte();
                    f.Poses = new RecPose[n];
                    for (int k = 0; k < n; k++)
                    {
                        var p = new RecPose
                        {
                            Actor = r.ReadInt32(),
                            Mat = r.ReadByte(),
                            Scale = r.ReadSingle(),
                            Root = R3(r),
                            RootRot = RQ(r),
                            BoneRot = new Quaternion[bones],
                            BonePos = new Vector3[bones]
                        };
                        for (int b = 0; b < bones; b++)
                        {
                            p.BoneRot[b] = RQs(r);
                            p.BonePos[b] = RH(r);
                        }
                        f.Poses[k] = p;
                    }
                    d.Frames.Add(f);
                }
                return d;
            }
        }
    }

    /// <summary>
    /// Records replays. With [Replay] Record sound on, it also takes the same 3D sound live spectating sends (game
    /// sounds, players' voices, and the caster's mic if Record my mic is on) through <see cref="SoundHub"/>, and saves it
    /// in the replay file.
    /// </summary>
    internal sealed class ReplayRecorder : ISoundSink
    {
        public bool Recording { get; private set; }
        public float Rate = 30f;
        public volatile string Status = "Not recording.";

        private ReplayData data;
        private readonly PoseSampler sampler = new PoseSampler();
        private float startedAt, nextSample;
        private volatile ReplaySound sound;
        private long soundStartTicks;

        /// <summary>How much sound the recording has so far, for the menu.</summary>
        public string SoundStatus
        {
            get
            {
                var s = sound;
                if (s == null) return Plugin.ReplaySound.Value ? "" : "Sound recording is off.";
                return s.Count == 0 ? "Sound: waiting for any to play." : $"Sound: {s.Bytes / 1048576.0:0.0} MB so far.";
            }
        }

        bool ISoundSink.SoundOpen => sound != null;

        double ISoundSink.SoundStart => SoundHub.FromTicks(Interlocked.Read(ref soundStartTicks));

        bool ISoundSink.WantsTrack(byte track) => track != SoundHub.TrackMic || Plugin.ReplayMic.Value;

        void ISoundSink.TakeSound(byte[] msg, SoundKind kind) => sound?.Add(msg);

        public float Elapsed => Recording ? Time.realtimeSinceStartup - startedAt : 0f;

        public void Start()
        {
            if (Recording) return;
            var ns = NetworkSystem.Instance;
            if (ns == null || !ns.InRoom)
            {
                Status = "Join a room to record.";
                return;
            }
            data = new ReplayData
            {
                Started = DateTime.Now,
                Map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                Rate = Mathf.RoundToInt(Rate)
            };
            sampler.Reset();
            // The frames and the sound share time zero.
            startedAt = Time.realtimeSinceStartup;
            Interlocked.Exchange(ref soundStartTicks, System.Diagnostics.Stopwatch.GetTimestamp());
            sound = null;
            if (Plugin.ReplaySound.Value)
            {
                try { sound = new ReplaySound(Path.Combine(ReplayFiles.Folder, $".sound-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{Guid.NewGuid():N}.tmp")); }
                catch { }
            }
            nextSample = 0f;
            Recording = true;
            Status = "Recording...";
        }

        public void Stop()
        {
            if (!Recording) return;
            Recording = false;
            var d = data;
            data = null;
            var snd = sound;
            sound = null;
            snd?.Seal();
            if (d == null || d.Frames.Count < 5)
            {
                snd?.Dispose();
                Status = "Too short - nothing saved.";
                return;
            }
            d.Duration = d.Frames[d.Frames.Count - 1].Time;
            d.Bones = sampler.Bones;
            d.Players.AddRange(sampler.Players.Values);
            string path = Path.Combine(ReplayFiles.Folder, $"replay_{d.Started:yyyy-MM-dd_HH-mm-ss}.dvr");
            Status = "Saving...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ReplayFiles.Save(d, path, snd);
                    bool withSound = snd != null && snd.Count > 0;
                    Status = $"Saved {Path.GetFileName(path)} ({TimeFmt.Clock(d.Duration)}{(withSound ? ", with sound" : "")}).";
                }
                catch (Exception e)
                {
                    Status = "Couldn't save the replay: " + e.Message;
                }
                finally
                {
                    snd?.Dispose();
                }
            });
        }

        public void Tick(bool inRoom, bool replaying)
        {
            if (Recording && (!inRoom || replaying))
            {
                Stop();
                return;
            }
            if (!Recording) return;

            float now = Time.realtimeSinceStartup - startedAt;
            if (now < nextSample) return;
            nextSample = now + 1f / Mathf.Clamp(Rate, 5f, 60f);
            data.Frames.Add(new RecFrame { Time = now, Poses = sampler.Sample() });
        }
    }

    internal sealed class Ghost
    {
        public GameObject Root;
        public Transform[] Bones;
        public SkinnedMeshRenderer Body;
        public Material Skin;
        public Material[] SkinSet;
        public byte Mat = 255;
        public CastPlayer Cast;
    }

    internal static class GhostTemplate
    {
        private static GameObject template;
        private static string headPath, bodyPath, leftPath, rightPath, skinPath;
        private static bool failed;
        public static string Error = "";

        public static bool Ready => template != null || Build();

        private static bool Build()
        {
            if (failed) return false;
            var src = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;
            if (src == null) return false;
            try
            {
                var skin = RigParts.Body(src);
                if (skin == null) throw new Exception("no gorilla body found");
                var root = src.transform;
                skinPath = RigParts.PathOf(root, skin.transform);
                headPath = src.headMesh != null ? RigParts.PathOf(root, src.headMesh.transform) : null;
                bodyPath = src.bodyTransform != null ? RigParts.PathOf(root, src.bodyTransform) : null;
                leftPath = src.leftHandTransform != null ? RigParts.PathOf(root, src.leftHandTransform) : null;
                rightPath = src.rightHandTransform != null ? RigParts.PathOf(root, src.rightHandTransform) : null;
                string facePath = src.bodyRenderer != null && src.bodyRenderer.faceRenderer != null
                    ? RigParts.PathOf(root, src.bodyRenderer.faceRenderer.transform) : null;

                var holder = new GameObject("Devicular Ghost Template");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                var clone = UnityEngine.Object.Instantiate(src.gameObject, holder.transform, false);
                clone.name = "Ghost";
                for (int pass = 0; pass < 6; pass++)
                {
                    bool removed = false;
                    foreach (var comp in clone.GetComponentsInChildren<Component>(true))
                    {
                        if (comp == null || comp is Transform || comp is MeshFilter || comp is Renderer) continue;
                        try
                        {
                            UnityEngine.Object.DestroyImmediate(comp);
                            removed = true;
                        }
                        catch
                        {
                        }
                    }
                    if (!removed) break;
                }
                Prune(clone.transform, skinPath, facePath, new[] { headPath, bodyPath, leftPath, rightPath });
                foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                Enable(clone.transform, skinPath);
                if (facePath != null) Enable(clone.transform, facePath);
                var cloneSkin = clone.transform.Find(skinPath)?.GetComponent<SkinnedMeshRenderer>();
                if (cloneSkin == null) throw new Exception("the copied body has no skin");
                cloneSkin.updateWhenOffscreen = true;
                template = clone;
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Error = e.Message;
                return false;
            }
        }

        private static void Prune(Transform root, string skinPath, string facePath, string[] extra)
        {
            var keep = new HashSet<Transform> { root };
            void KeepUp(Transform t)
            {
                for (var cur = t; cur != null && keep.Add(cur); cur = cur.parent) { }
            }
            var skin = root.Find(skinPath);
            if (skin != null)
            {
                KeepUp(skin);
                var smr = skin.GetComponent<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    foreach (var b in smr.bones)
                        if (b != null) KeepUp(b);
                    if (smr.rootBone != null) KeepUp(smr.rootBone);
                }
            }
            if (facePath != null)
            {
                var face = root.Find(facePath);
                if (face != null) KeepUp(face);
            }
            foreach (var p in extra)
            {
                if (p == null) continue;
                var t = root.Find(p);
                if (t != null) KeepUp(t);
            }
            var doomed = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!keep.Contains(t) && t.parent != null && keep.Contains(t.parent)) doomed.Add(t.gameObject);
            foreach (var go in doomed) UnityEngine.Object.DestroyImmediate(go);
        }

        private static void Enable(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null) r.enabled = true;
            for (var cur = t; cur != null && cur != root; cur = cur.parent) cur.gameObject.SetActive(true);
        }

        public static Ghost Spawn(RecPlayer rp, string[] boneNames, Transform parent)
        {
            if (!Ready) return null;
            var go = UnityEngine.Object.Instantiate(template, parent, false);
            go.name = "Devicular Ghost " + rp.Name;
            go.SetActive(true);
            var skin = go.transform.Find(skinPath)?.GetComponent<SkinnedMeshRenderer>();
            if (skin == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            var bones = new Transform[boneNames.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = string.IsNullOrEmpty(boneNames[i]) ? null : go.transform.Find(boneNames[i]);

            var src = GorillaTagger.Instance.offlineVRRig;
            Material baseMat = null;
            try { baseMat = src.bodyRenderer != null ? src.bodyRenderer.myDefaultSkinMaterialInstance : null; } catch { }
            if (baseMat == null && src.materialsToChangeTo != null && src.materialsToChangeTo.Length > 0) baseMat = src.materialsToChangeTo[0];
            var mat = baseMat != null ? new Material(baseMat) : new Material(skin.sharedMaterial);
            mat.color = rp.Color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", rp.Color);
            var set = skin.sharedMaterials;
            if (set.Length == 0) set = new Material[1];
            set[0] = mat;
            skin.sharedMaterials = set;

            Transform Find(string path) => path != null ? go.transform.Find(path) : null;
            var ghost = new Ghost { Root = go, Bones = bones, Body = skin, Skin = mat, SkinSet = set };
            ghost.Cast = new CastPlayer
            {
                Actor = rp.Actor,
                Name = rp.Name,
                Color = rp.Color,
                IsLocal = rp.IsLocal,
                Team = rp.Team,
                TeamName = rp.TeamName,
                TeamColor = rp.TeamColor,
                Ghost = true,
                Head = Find(headPath) ?? go.transform,
                Body = Find(bodyPath) ?? go.transform,
                LeftHand = Find(leftPath),
                RightHand = Find(rightPath),
                Index = rp.IsLocal ? 0 : -1
            };
            return ghost;
        }
    }

    internal sealed class ReplayPlayer
    {
        public ReplayData Data { get; private set; }
        public string FileName { get; private set; } = "";
        public bool Active => Data != null;
        public bool Playing;
        public float Time;
        public float Speed = 1f;
        public bool HideLive = true;
        public string Status = "";

        private readonly Dictionary<int, Ghost> ghosts = new Dictionary<int, Ghost>();
        private readonly List<CastPlayer> casts = new List<CastPlayer>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private GameObject root;
        private CastPlayer localCast;
        private float nextHideScan;

        public float Duration => Data != null ? Data.Duration : 0f;

        public void Load(string path)
        {
            Close();
            try
            {
                Data = ReplayFiles.Load(path);
            }
            catch (Exception e)
            {
                Data = null;
                Status = "Couldn't open the replay: " + e.Message;
                return;
            }
            if (!GhostTemplate.Ready)
            {
                Status = "Couldn't build the gorilla ghosts: " + GhostTemplate.Error;
                Data = null;
                return;
            }
            FileName = Path.GetFileNameWithoutExtension(path);
            root = new GameObject("Devicular Replay");
            UnityEngine.Object.DontDestroyOnLoad(root);
            foreach (var rp in Data.Players)
            {
                var g = GhostTemplate.Spawn(rp, Data.Bones, root.transform);
                if (g == null) continue;
                ghosts[rp.Actor] = g;
                casts.Add(g.Cast);
                if (rp.IsLocal) localCast = g.Cast;
            }
            Roster.ReplaySource = () => casts;
            Roster.ReplayLocal = () => localCast;
            Time = 0f;
            Playing = true;
            Status = $"Playing {FileName}: {TimeFmt.Clock(Data.Duration)}, {ghosts.Count} players.";
            Apply(0f);
        }

        public void Close()
        {
            foreach (var g in ghosts.Values)
                if (g.Skin != null) UnityEngine.Object.Destroy(g.Skin);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            ghosts.Clear();
            casts.Clear();
            localCast = null;
            Roster.ReplaySource = null;
            Roster.ReplayLocal = null;
            ShowLive();
            if (Data != null) Status = "Replay closed.";
            Data = null;
            Playing = false;
        }

        public void Seek(float t) => Time = Mathf.Clamp(t, 0f, Duration);

        /// <summary>The replay's gorillas, for other Devicular mods (Replay.Ghosts in <see cref="Link"/>): the caster camera follows them.</summary>
        public object[] PackGhosts()
        {
            if (!Active) return null;
            var list = new object[casts.Count];
            for (int i = 0; i < casts.Count; i++) list[i] = casts[i].Pack();
            return list;
        }

        /// <summary>Replay.State in <see cref="Link"/>: time, playing, speed, length, file name.</summary>
        public object[] PackState() => Active ? new object[] { Time, Playing, Speed, Duration, FileName } : null;

        public void TogglePlay()
        {
            if (!Playing && Time >= Duration) Seek(0f);
            Playing = !Playing;
        }

        public void Tick(float dt)
        {
            if (!Active) return;
            if (Playing)
            {
                Time += dt * Speed;
                if (Time >= Duration)
                {
                    Time = Duration;
                    Playing = false;
                }
            }
            Apply(Time);
            if (HideLive) HideLivePlayers();
            else ShowLive();
        }

        private void HideLivePlayers()
        {
            if (UnityEngine.Time.unscaledTime < nextHideScan) return;
            nextHideScan = UnityEngine.Time.unscaledTime + 2f;
            IReadOnlyList<VRRig> rigs;
            try { rigs = VRRigCache.ActiveRigs; }
            catch { return; }
            if (rigs == null) return;
            foreach (var rig in rigs)
            {
                if (rig == null) continue;
                foreach (var r in rig.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    r.enabled = false;
                    hidden.Add(r);
                }
            }
        }

        private void ShowLive()
        {
            foreach (var r in hidden)
                if (r != null) r.enabled = true;
            hidden.Clear();
            nextHideScan = 0f;
        }

        private int FrameAt(float t)
        {
            var frames = Data.Frames;
            int lo = 0, hi = frames.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (frames[mid].Time <= t) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private static bool Find(RecFrame f, int actor, out RecPose pose)
        {
            foreach (var p in f.Poses)
                if (p.Actor == actor)
                {
                    pose = p;
                    return true;
                }
            pose = default;
            return false;
        }

        private void Apply(float t)
        {
            if (Data == null || Data.Frames.Count == 0) return;
            int i = FrameAt(t);
            var a = Data.Frames[i];
            var b = i + 1 < Data.Frames.Count ? Data.Frames[i + 1] : a;
            float k = b.Time > a.Time ? Mathf.Clamp01((t - a.Time) / (b.Time - a.Time)) : 0f;
            var src = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;
            foreach (var kv in ghosts)
            {
                var g = kv.Value;
                if (!Find(a, kv.Key, out var pa))
                {
                    if (g.Root.activeSelf) g.Root.SetActive(false);
                    continue;
                }
                if (!Find(b, kv.Key, out var pb)) pb = pa;
                if (!g.Root.activeSelf) g.Root.SetActive(true);
                var tr = g.Root.transform;
                tr.SetPositionAndRotation(Vector3.Lerp(pa.Root, pb.Root, k), Quaternion.Slerp(pa.RootRot, pb.RootRot, k));
                float scale = Mathf.Lerp(pa.Scale, pb.Scale, k);
                tr.localScale = new Vector3(scale, scale, scale);
                for (int n = 0; n < g.Bones.Length; n++)
                {
                    var bone = g.Bones[n];
                    if (bone == null) continue;
                    var ra = pa.BoneRot[n];
                    if (ra.x == 0f && ra.y == 0f && ra.z == 0f && ra.w == 0f) continue;
                    var rb = pb.BoneRot[n];
                    if (rb.x == 0f && rb.y == 0f && rb.z == 0f && rb.w == 0f) rb = ra;
                    bone.SetPositionAndRotation(tr.TransformPoint(Vector3.Lerp(pa.BonePos[n], pb.BonePos[n], k)), tr.rotation * Quaternion.Slerp(ra, rb, k));
                }
                byte mat = k < 0.5f ? pa.Mat : pb.Mat;
                if (mat != g.Mat)
                {
                    g.Mat = mat;
                    var set = (Material[])g.SkinSet.Clone();
                    if (mat != 0 && src != null && src.materialsToChangeTo != null && mat < src.materialsToChangeTo.Length && src.materialsToChangeTo[mat] != null)
                        set[0] = src.materialsToChangeTo[mat];
                    else set[0] = g.Skin;
                    g.Body.sharedMaterials = set;
                    g.Cast.Tagged = mat != 0;
                }
                g.Cast.Scale = scale;
            }
        }
    }

    internal sealed class Timeline
    {
        private GUIStyle bar, button, play, label, speedOn, speedOff;
        private int builtVersion = -1;
        private bool scrubbing;
        private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f };

        public void Draw(ReplayPlayer player)
        {
            if (!player.Active) return;
            if (bar == null || builtVersion != Dev.Version)
            {
                builtVersion = Dev.Version;
                bar = new GUIStyle(GUIStyle.none) { border = new RectOffset(20, 20, 20, 20) };
                bar.normal.background = Dev.RoundTex(20, Dev.Surface(0.95f), Dev.Shade(0.08f));
                button = new GUIStyle(Dev.SmallGhost) { fontSize = 12, padding = new RectOffset(4, 4, 2, 2), alignment = TextAnchor.MiddleCenter };
                play = new GUIStyle(Dev.Primary) { fontSize = 13, padding = new RectOffset(4, 4, 2, 2), alignment = TextAnchor.MiddleCenter };
                label = new GUIStyle(Dev.Bold) { alignment = TextAnchor.MiddleCenter, fontSize = 13, clipping = TextClipping.Overflow };
                speedOn = new GUIStyle(Dev.SegOn) { padding = new RectOffset(2, 2, 2, 2), fontSize = 12 };
                speedOff = new GUIStyle(Dev.SegOff) { padding = new RectOffset(2, 2, 2, 2), fontSize = 12 };
            }
            float w = Mathf.Min(1000f, Screen.width - 40f), h = 68f;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height - h - 18f, w, h);
            if (Event.current.type == EventType.Repaint)
            {
                Dev.Shadow(rect);
                bar.Draw(rect, false, false, false, false);
            }
            float y = rect.y + (h - 36f) / 2f;
            float x = rect.x + 16f;
            if (GUI.Button(new Rect(x, y, 92f, 36f), player.Playing ? "Pause" : "Play", play)) Ui.Later(player.TogglePlay);
            x += 100f;
            if (GUI.Button(new Rect(x, y + 4f, 54f, 28f), "-5s", button)) Ui.Later(() => player.Seek(player.Time - 5f));
            x += 60f;
            if (GUI.Button(new Rect(x, y + 4f, 54f, 28f), "+5s", button)) Ui.Later(() => player.Seek(player.Time + 5f));
            x += 70f;

            float right = rect.xMax - 16f;
            if (GUI.Button(new Rect(right - 32f, y + 4f, 32f, 28f), "×", Dev.Chrome)) Ui.Later(player.Close);
            right -= 44f;
            if (Event.current.type == EventType.Repaint)
                Dev.SegBg.Draw(new Rect(right - Speeds.Length * 58f - 1f, y + 1f, Speeds.Length * 58f + 2f, 34f), false, false, false, false);
            for (int i = Speeds.Length - 1; i >= 0; i--)
            {
                float s = Speeds[i];
                var r = new Rect(right - 56f, y + 4f, 54f, 28f);
                if (GUI.Button(r, s.ToString("0.##", CultureInfo.InvariantCulture) + "x", Mathf.Approximately(player.Speed, s) ? speedOn : speedOff))
                    Ui.Later(() => player.Speed = s);
                right -= 58f;
            }
            right -= 8f;
            var timeRect = new Rect(right - 170f, rect.y, 170f, h);
            GUI.Label(timeRect, TimeFmt.Clock(player.Time) + "  /  " + TimeFmt.Clock(player.Duration), label);
            right = timeRect.x - 12f;
            DrawScrub(new Rect(x, rect.y + h / 2f - 3f, Mathf.Max(60f, right - x), 6f), player);
        }

        private void DrawScrub(Rect track, ReplayPlayer player)
        {
            var hit = new Rect(track.x - 8f, track.y - 14f, track.width + 16f, track.height + 28f);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            float At(float mx) => Mathf.InverseLerp(track.xMin, track.xMax, mx) * player.Duration;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && hit.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        scrubbing = true;
                        player.Seek(At(e.mousePosition.x));
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        player.Seek(At(e.mousePosition.x));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        scrubbing = false;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    var old = GUI.color;
                    GUI.color = new Color(Dev.Muted.r, Dev.Muted.g, Dev.Muted.b, 0.35f * old.a);
                    GUI.DrawTexture(track, Texture2D.whiteTexture);
                    float t = player.Duration > 0f ? Mathf.Clamp01(player.Time / player.Duration) : 0f;
                    GUI.color = new Color(Dev.Info.r, Dev.Info.g, Dev.Info.b, old.a);
                    GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Texture2D.whiteTexture);
                    GUI.color = old;
                    var knob = new Rect(track.x + track.width * t - 9f, track.y - 6f, 18f, 18f);
                    Dev.Dot(new Rect(knob.x, knob.y + 1f, knob.width, knob.height), new Color(0f, 0f, 0f, 0.25f));
                    Dev.Dot(knob, scrubbing ? Dev.Info : Color.white);
                    break;
            }
        }
    }
}
