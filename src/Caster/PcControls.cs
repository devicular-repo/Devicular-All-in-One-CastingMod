using GorillaLocomotion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DevicularCaster
{
    internal sealed class PcControls
    {
        public const float Reach = 6f;

        public bool Move { get; private set; }
        public bool Clicker;
        public float Speed = 7.5f;
        public float LookSensitivity = 0.15f;
        public string ClickStatus = "";

        private CamMode? before;
        private TransformFollow follow;
        private Transform hand;

        public static bool Allowed
        {
            get
            {
                var ns = NetworkSystem.Instance;
                return ns == null || !ns.InRoom || ns.SessionIsPrivate;
            }
        }

        public void SetMove(bool on, CasterCamera cam)
        {
            if (Move == on) return;
            Move = on;
            if (on)
            {
                if (cam.Mode == CamMode.Freecam || cam.Mode == CamMode.Follow)
                {
                    before = cam.Mode;
                    cam.SetMode(CamMode.Regular);
                }
            }
            else if (before.HasValue)
            {
                if (cam.Mode == CamMode.Regular || cam.Mode == CamMode.FirstPerson) cam.SetMode(before.Value);
                before = null;
            }
        }

        public void Update(CasterCamera cam, bool typing, bool pointerOverMenu)
        {
            bool allowed = Allowed;
            if (Move && allowed && !typing && (cam.Mode == CamMode.Regular || cam.Mode == CamMode.FirstPerson)) Fly(pointerOverMenu);
            Click(cam, Clicker && allowed && !typing && !pointerOverMenu);
        }

        private void Fly(bool pointerOverMenu)
        {
            var player = GTPlayer.Instance;
            var kb = Keyboard.current;
            if (player == null || player.headCollider == null || kb == null) return;
            var head = player.headCollider.transform;
            float speed = Speed * (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 2f : 1f);
            var dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += head.forward;
            if (kb.sKey.isPressed) dir -= head.forward;
            if (kb.dKey.isPressed) dir += head.right;
            if (kb.aKey.isPressed) dir -= head.right;
            if (kb.eKey.isPressed) dir += Vector3.up;
            if (kb.qKey.isPressed) dir -= Vector3.up;
            if (dir.sqrMagnitude > 0f) head.position += dir.normalized * (speed * Time.deltaTime);

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed && !pointerOverMenu)
            {
                var delta = mouse.delta.ReadValue();
                head.Rotate(Vector3.up, delta.x * LookSensitivity, Space.World);
                head.Rotate(Vector3.right, -delta.y * LookSensitivity, Space.Self);
            }

            var body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.AddForce(-Physics.gravity * body.mass);
                body.linearVelocity = Vector3.zero;
            }
        }

        private void Click(CasterCamera cam, bool active)
        {
            var mouse = Mouse.current;
            bool pressing = active && mouse != null && mouse.leftButton.isPressed;
            if (!pressing)
            {
                Release();
                return;
            }
            var tagger = GorillaTagger.Instance;
            var player = GTPlayer.Instance;
            var view = cam.RenderingCamera;
            if (tagger == null || player == null || view == null || tagger.rightHandTriggerCollider == null) return;
            if (hand == null)
            {
                hand = tagger.rightHandTriggerCollider.transform;
                follow = tagger.rightHandTriggerCollider.GetComponent<TransformFollow>();
            }
            if (follow == null) return;
            var ray = view.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 500f, player.locomotionEnabledLayers)) return;
            var origin = player.headCollider != null ? player.headCollider.transform.position : player.transform.position;
            if ((hit.point - origin).magnitude > Reach * Mathf.Max(0.1f, player.scale))
            {
                ClickStatus = "Too far from your gorilla. Get within " + Reach.ToString("0") + " m.";
                Release();
                return;
            }
            ClickStatus = "";
            if (follow.enabled) follow.enabled = false;
            hand.position = hit.point;
        }

        private void Release()
        {
            if (follow != null && !follow.enabled) follow.enabled = true;
        }

        public void Shutdown()
        {
            Release();
        }
    }
}
