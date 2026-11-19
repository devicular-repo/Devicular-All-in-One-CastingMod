using System;
using GorillaLocomotion;
using GorillaNetworking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DevicularCaster
{
    internal enum CamMode { Regular, FirstPerson, Freecam, Follow }

    internal sealed class CasterCamera
    {
        public static readonly string[] ModeNames = { "Regular", "First Person", "Freecam", "Spectator" };

        public CamMode Mode { get; private set; } = CamMode.Regular;
        public bool Ready { get; private set; }
        public Camera Cam { get; private set; }

        public float Fov = 100f;
        public float NearClip = 0.03f;
        public float MoveSmooth = 0.55f;
        public float TurnSmooth = 0.6f;
        public float FreeSpeed = 4f;
        public float MouseSensitivity = 0.15f;
        private const float OrbitBack = 1f;
        private const float OrbitSide = 0.36f;
        private const float OrbitUp = 0.86f;
        private const float HeadLift = 0.39f;
        private Vector3 bodyHeading = Vector3.forward;
        public bool FollowFirstPerson;
        public bool TrackHead = true;
        public Vector3 FollowNudge;

        public void ResetNudge() => FollowNudge = Vector3.zero;
        public bool ListenFromCamera;
        public bool AutoDirector;
        public float DirectorSeconds = 6f;

        public int TargetActor { get; private set; } = int.MinValue;

        public CastPlayer TargetPlayer => TargetActor == int.MinValue ? null : Roster.ByActor(TargetActor);

        public bool IsTarget(CastPlayer p) => p != null && Mode == CamMode.Follow && p.Actor == TargetActor;

        private Behaviour brain, virtualCamera;
        private float baseFov, baseNear;
        private bool lensSaved;
        private AudioListener playerListener, cameraListener;
        private Vector3 freePos, curPos, goalPos;
        private Vector2 freeRot;
        private Quaternion curRot, goalRot;
        private bool hasPose, hooked;
        private float nextDirector, nextInitTry;

        public string TargetName => TargetPlayer?.Name ?? "";

        public void TryInit()
        {
            if (Ready || Time.unscaledTime < nextInitTry) return;
            nextInitTry = Time.unscaledTime + 1f;
            try
            {
                var gt = GorillaTagger.Instance;
                if (gt == null || gt.thirdPersonCamera == null || GTPlayer.Instance == null) return;
                var cam = gt.thirdPersonCamera.GetComponentInChildren<Camera>(true);
                if (cam == null) return;
                Cam = cam;
                foreach (var b in gt.thirdPersonCamera.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null) continue;
                    string n = b.GetType().Name;
                    if (virtualCamera == null && (n == "CinemachineVirtualCamera" || n == "CinemachineCamera")) virtualCamera = b;
                    if (brain == null && n == "CinemachineBrain") brain = b;
                }
                playerListener = GTPlayer.Instance.GetComponentInChildren<AudioListener>();
                cameraListener = cam.GetComponent<AudioListener>();
                if (cameraListener == null) cameraListener = cam.gameObject.AddComponent<AudioListener>();
                cameraListener.enabled = false;
                if (!hooked)
                {
                    RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                    hooked = true;
                }
                Ready = true;
                Plugin.Log.LogInfo($"Caster camera ready (brain {(brain != null)}, virtual camera {(virtualCamera != null)}).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Caster camera not ready yet: " + e.Message);
            }
        }

        public void Shutdown()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
        }

        private bool Manual => Mode == CamMode.Freecam || Mode == CamMode.Follow;

        public void SetMode(CamMode mode)
        {
            if (!Ready) return;
            bool wasManual = Manual;
            Mode = mode;
            if (Manual && !wasManual)
            {
                baseFov = Cam.fieldOfView;
                baseNear = Cam.nearClipPlane;
                lensSaved = true;
            }
            if (brain != null) brain.enabled = !Manual;
            if (virtualCamera != null) virtualCamera.enabled = !Manual;
            Cam.gameObject.SetActive(mode != CamMode.FirstPerson);
            if (Manual)
            {
                curPos = Cam.transform.position;
                curRot = Cam.transform.rotation;
                hasPose = true;
                if (mode == CamMode.Freecam)
                {
                    freePos = curPos;
                    var eul = curRot.eulerAngles;
                    freeRot = new Vector2(eul.x > 180f ? eul.x - 360f : eul.x, eul.y);
                }
                if (mode == CamMode.Follow && TargetPlayer == null) PickAnyTarget();
            }
            else
            {
                RestoreLens();
            }
            ApplyListeners();
        }

        private void RestoreLens()
        {
            if (lensSaved)
            {
                Cam.fieldOfView = baseFov;
                Cam.nearClipPlane = baseNear;
                lensSaved = false;
            }
        }

        public void CycleMode(int dir)
        {
            int n = ModeNames.Length;
            SetMode((CamMode)(((int)Mode + dir + n) % n));
        }

        public void Follow(CastPlayer p)
        {
            if (p == null) return;
            TargetActor = p.Actor;
            // With the auto director on it carries on as usual from the player you picked; nothing holds it back.
            Director.Picked();
            if (Mode != CamMode.Follow) SetMode(CamMode.Follow);
        }

        public void FollowIndex(int index)
        {
            var p = Roster.ByIndex(index);
            if (p != null) Follow(p);
        }

        private void PickAnyTarget()
        {
            TargetActor = Roster.Players.Count > 0 ? Roster.Players[0].Actor : int.MinValue;
        }

        private void ApplyListeners()
        {
            bool fromCamera = ListenFromCamera && Ready && Mode != CamMode.FirstPerson && Cam.gameObject.activeInHierarchy;
            if (cameraListener != null) cameraListener.enabled = fromCamera;
            if (playerListener != null) playerListener.enabled = !fromCamera;
        }

        public void Update(bool typing, bool pointerOverMenu)
        {
            // Players' new colors, in every camera mode (see PlayerColors).
            PlayerColors.UpdateColor();
            if (!Ready) return;
            if (Cam == null)
            {
                Ready = false;
                return;
            }
            ApplyListeners();

            if (Mode == CamMode.Follow)
            {
                if (TargetPlayer == null) PickAnyTarget();
                // The auto director looks a few times a second, so a chase is caught as it starts; it decides
                // itself when a switch is worth it (see Director).
                if (AutoDirector && Time.unscaledTime >= nextDirector)
                {
                    nextDirector = Time.unscaledTime + 0.25f;
                    var best = Director.Pick(TargetActor, DirectorSeconds);
                    if (best != null) TargetActor = best.Actor;
                }
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            if (Mode == CamMode.Follow && !FollowFirstPerson && kb != null && !typing)
            {
                var n = Vector3.zero;
                if (kb.wKey.isPressed) n.z += 1f;
                if (kb.sKey.isPressed) n.z -= 1f;
                if (kb.dKey.isPressed) n.x += 1f;
                if (kb.aKey.isPressed) n.x -= 1f;
                if (kb.qKey.isPressed) n.y += 1f;
                if (kb.eKey.isPressed) n.y -= 1f;
                if (kb.rKey.wasPressedThisFrame) ResetNudge();
                float fast = kb.shiftKey.isPressed ? 3f : 1f;
                var nudge = FollowNudge + n * 0.9f * fast * dt;
                nudge.x = Mathf.Clamp(nudge.x, -2.5f, 2.5f);
                nudge.y = Mathf.Clamp(nudge.y, -1.2f, 2.5f);
                nudge.z = Mathf.Clamp(nudge.z, -3f, 1.4f);
                FollowNudge = nudge;
            }

            if (Mode == CamMode.Freecam)
            {
                var move = Vector3.zero;
                var turn = Vector2.zero;
                if (kb != null && !typing)
                {
                    if (kb.wKey.isPressed) move.z += 1f;
                    if (kb.sKey.isPressed) move.z -= 1f;
                    if (kb.dKey.isPressed) move.x += 1f;
                    if (kb.aKey.isPressed) move.x -= 1f;
                    if (kb.qKey.isPressed) move.y += 1f;
                    if (kb.eKey.isPressed) move.y -= 1f;
                    if (kb.upArrowKey.isPressed) turn.x -= 1f;
                    if (kb.downArrowKey.isPressed) turn.x += 1f;
                    if (kb.rightArrowKey.isPressed) turn.y += 1f;
                    if (kb.leftArrowKey.isPressed) turn.y -= 1f;
                }
                if (gp != null)
                {
                    var ls = gp.leftStick.ReadValue();
                    var rs = gp.rightStick.ReadValue();
                    if (ls.sqrMagnitude > 0.01f) { move.x += ls.x; move.z += ls.y; }
                    if (rs.sqrMagnitude > 0.01f) { turn.y += rs.x; turn.x -= rs.y; }
                    if (gp.leftShoulder.isPressed) move.y -= 1f;
                    if (gp.rightShoulder.isPressed) move.y += 1f;
                }
                float boost = kb != null && !typing && kb.shiftKey.isPressed ? 3f : 1f;
                var rot = Quaternion.Euler(freeRot.x, freeRot.y, 0f);
                freePos += (rot * new Vector3(move.x, 0f, move.z) + Vector3.up * move.y) * FreeSpeed * boost * dt;
                freeRot += turn * 90f * dt;

                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.isPressed && !pointerOverMenu)
                {
                    var d = mouse.delta.ReadValue();
                    freeRot += new Vector2(-d.y, d.x) * MouseSensitivity;
                }
                freeRot.x = Mathf.Clamp(freeRot.x, -89f, 89f);
            }
        }

        public void LateUpdate()
        {
            if (!Ready || !Manual || Cam == null) return;
            if (!ComputeGoal(out float scale)) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool insideHead = Mode == CamMode.Follow && FollowFirstPerson;
            float kp = insideHead ? 40f : Mathf.Lerp(30f, 2.5f, MoveSmooth);
            float kr = insideHead ? 30f : Mathf.Lerp(30f, 2.5f, TurnSmooth);
            if (!hasPose)
            {
                curPos = goalPos;
                curRot = goalRot;
                hasPose = true;
            }
            curPos = Vector3.Lerp(curPos, goalPos, 1f - Mathf.Exp(-kp * dt));
            curRot = Quaternion.Slerp(curRot, goalRot, 1f - Mathf.Exp(-kr * dt));
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, Fov, 1f - Mathf.Exp(-8f * dt));
            Cam.nearClipPlane = Mathf.Max(0.005f, (insideHead ? Mathf.Max(NearClip, 0.08f) : NearClip) * scale);
            Cam.transform.SetPositionAndRotation(curPos, curRot);
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != Cam || !Ready || !Manual || !hasPose) return;
            Cam.transform.SetPositionAndRotation(curPos, curRot);
        }

        private bool ComputeGoal(out float scale)
        {
            scale = 1f;
            switch (Mode)
            {
                case CamMode.Freecam:
                    goalPos = freePos;
                    goalRot = Quaternion.Euler(freeRot.x, freeRot.y, 0f);
                    return true;
                case CamMode.Follow:
                {
                    var target = TargetPlayer;
                    if (target == null || target.Head == null)
                    {
                        var own = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;
                        if (own == null) return false;
                        BodyPose(own, out scale);
                        return true;
                    }
                    var head = target.Head;
                    scale = target.Scale;
                    if (FollowFirstPerson)
                    {
                        goalPos = head.TransformPoint(0f, 0.12f, 0f);
                        goalRot = head.rotation;
                    }
                    else if (TrackHead)
                    {
                        goalPos = head.position + (head.forward * (FollowNudge.z - 1.75f) + head.right * (0.25f + FollowNudge.x) + head.up * (0.1f + FollowNudge.y)) * scale;
                        goalRot = Quaternion.LookRotation(head.forward * 1.75f + head.up * 0.275f, Vector3.up);
                    }
                    else BodyPose(head, target.Body, scale);
                    return true;
                }
            }
            return false;
        }

        private void BodyPose(VRRig rig, out float scale)
        {
            scale = Mathf.Max(0.05f, rig.transform.localScale.y);
            var head = rig.headMesh != null ? rig.headMesh.transform : rig.transform;
            var body = rig.bodyTransform != null ? rig.bodyTransform : head.parent != null ? head.parent : rig.transform;
            BodyPose(head, body, scale);
        }

        private void BodyPose(Transform head, Transform body, float scale)
        {
            if (body == null) body = head;
            var heading = Vector3.ProjectOnPlane(body.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (heading.sqrMagnitude >= 0.01f) bodyHeading = heading.normalized;
            var side = Vector3.Cross(Vector3.up, bodyHeading);
            goalPos = body.position + (bodyHeading * (FollowNudge.z - OrbitBack) + side * (OrbitSide + FollowNudge.x) + Vector3.up * (OrbitUp + FollowNudge.y)) * scale;
            var look = body.position + Vector3.up * (HeadLift * scale) - goalPos;
            goalRot = Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : bodyHeading, Vector3.up);
        }

        public Camera RenderingCamera
        {
            get
            {
                if (Ready && Cam != null && Cam.isActiveAndEnabled) return Cam;
                var gt = GorillaTagger.Instance;
                if (gt != null && gt.mainCamera != null)
                {
                    var main = gt.mainCamera.GetComponent<Camera>();
                    if (main != null) return main;
                }
                return Camera.main;
            }
        }
    }

    /// <summary>
    /// The auto director: follows the runner a tagger is closest to, so the camera is on the chase.
    ///
    /// Who's who: while an Auto Ref round runs, its own list (the taggers, and the runners still in). Otherwise,
    /// players in lava are taggers and everyone else runs. Each runner scores by how close the nearest tagger is,
    /// plus how fast that tagger is closing in. A real chase (a tagger within a few meters) cuts in quickly; between
    /// chases it only moves on every "Switch every" seconds, and only for something clearly better. When the player
    /// it's on is tagged, it goes straight to the runner a tagger is closest to.
    /// </summary>
    internal static class Director
    {
        private const float ChaseRange = 12f, HotRange = 5f, MinHold = 1.5f;

        private static float lastSwitch = -100f;
        private static readonly System.Collections.Generic.Dictionary<int, float> lastGap = new System.Collections.Generic.Dictionary<int, float>();
        private static float lastGapTime;
        private static readonly System.Collections.Generic.HashSet<int> taggers = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> runners = new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// You picked a player yourself: it counts like one of the director's own switches, so it keeps its usual
        /// timing from there (a chase still cuts in, and a tagged player is still left straight away).
        /// </summary>
        public static void Picked() => lastSwitch = Time.unscaledTime;

        private static Vector3 Where(CastPlayer p) => p.Body != null ? p.Body.position : p.Head.position;

        /// <summary>Who's tagging and who's running right now.</summary>
        private static void Roles(System.Collections.Generic.List<CastPlayer> players)
        {
            taggers.Clear();
            runners.Clear();
            var roles = Link.Call("Referee.Roles");
            var t = Link.At<System.Collections.Generic.HashSet<int>>(roles, 0);
            var r = Link.At<System.Collections.Generic.HashSet<int>>(roles, 1);
            if (t != null && r != null && !Roster.Replaying)
            {
                foreach (var p in players)
                {
                    if (t.Contains(p.Actor)) taggers.Add(p.Actor);
                    else if (r.Contains(p.Actor)) runners.Add(p.Actor);
                }
                return;
            }
            foreach (var p in players)
            {
                if (p.Tagged) taggers.Add(p.Actor);
                else runners.Add(p.Actor);
            }
        }

        /// <summary>The player to follow now: the current one, or a better chase.</summary>
        public static CastPlayer Pick(int currentActor, float calmSeconds)
        {
            var players = Roster.Players;
            if (players.Count == 0) return null;
            Roles(players);
            float now = Time.unscaledTime;
            float dt = now - lastGapTime;
            lastGapTime = now;

            CastPlayer best = null, current = null, closest = null;
            float bestScore = 0f, currentScore = -1f, bestGap = float.MaxValue, currentGap = float.MaxValue, closestGap = float.MaxValue;
            foreach (var p in players)
            {
                if (p.Head == null) continue;
                if (p.Actor == currentActor) current = p;
                // You (the caster) are never one of the director's picks.
                if (p.IsLocal || !runners.Contains(p.Actor)) continue;
                var pos = Where(p);
                float gap = float.MaxValue;
                foreach (var o in players)
                {
                    if (o.Head == null || !taggers.Contains(o.Actor)) continue;
                    gap = Mathf.Min(gap, Vector3.Distance(pos, Where(o)));
                }
                // How fast the nearest tagger is closing in (meters a second), from the last look.
                float closing = 0f;
                if (lastGap.TryGetValue(p.Actor, out float before) && dt > 0.05f && dt < 1f && gap < float.MaxValue)
                    closing = Mathf.Clamp((before - gap) / dt, 0f, 8f);
                lastGap[p.Actor] = gap;
                // The runner a tagger is nearest to, however far: where the camera goes when its player is tagged.
                if (closest == null || gap < closestGap)
                {
                    closest = p;
                    closestGap = gap;
                }

                float score = 0f;
                if (gap < ChaseRange) score = (ChaseRange - gap) / ChaseRange * 10f + closing * 0.6f;
                if (p.Actor == currentActor)
                {
                    currentScore = score;
                    currentGap = gap;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    bestGap = gap;
                }
            }

            // The current player isn't a runner still in (just tagged, gone, or a tagger): straight away to the runner
            // a tagger is closest to, without waiting and however far the chase is.
            bool currentDone = current == null || !runners.Contains(current.Actor);
            if (currentDone)
            {
                if (closest == null || closest.Actor == currentActor) return current;
                lastSwitch = now;
                return closest;
            }

            if (best == null || best.Actor == currentActor) return current;
            float held = now - lastSwitch;
            bool hot = bestGap < HotRange;
            bool currentHot = currentGap < HotRange;

            bool go;
            if (hot && !currentHot) go = held >= MinHold;
            else if (hot) go = held >= MinHold && bestScore > currentScore + 2.5f;
            else go = held >= Mathf.Max(2f, calmSeconds) && bestScore > currentScore + 2f;
            if (!go) return current;
            lastSwitch = now;
            return best;
        }
    }
}
