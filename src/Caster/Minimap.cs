using UnityEngine;

namespace DevicularCaster
{
    internal sealed class Minimap
    {
        public bool Enabled;
        public int Corner = 1;
        public float Size = 1f;
        public float Zoom = 20f;
        public bool TurnWithView = true;

        private const float BasePixels = 240f;
        private const float Height = 60f;
        private const float Ceiling = 3f;

        private Camera cam;
        private RenderTexture texture;
        private bool placed;
        private Vector3 center;

        private void Ensure()
        {
            if (texture == null)
            {
                texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { name = "Devicular Minimap" };
                texture.Create();
            }
            if (cam == null)
            {
                var go = new GameObject("Devicular Minimap Camera");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
                cam.targetTexture = texture;
                cam.depth = -50f;
                cam.allowMSAA = false;
                cam.allowHDR = false;
                var listener = go.GetComponent<AudioListener>();
                if (listener != null) Object.Destroy(listener);
            }
        }

        public void LateUpdate(CasterCamera caster, bool hidden)
        {
            placed = false;
            if (!Enabled || hidden)
            {
                if (cam != null) cam.enabled = false;
                return;
            }
            Ensure();
            float yaw;
            var target = caster.Mode == CamMode.Follow ? caster.TargetPlayer : null;
            if (target != null && target.Head != null)
            {
                center = target.Head.position;
                yaw = target.Head.eulerAngles.y;
            }
            else
            {
                var view = caster.RenderingCamera;
                if (view == null)
                {
                    cam.enabled = false;
                    return;
                }
                center = view.transform.position;
                yaw = view.transform.eulerAngles.y;
                cam.cullingMask = view.cullingMask;
            }
            if (target != null)
            {
                var view = caster.RenderingCamera;
                if (view != null) cam.cullingMask = view.cullingMask;
            }
            cam.orthographicSize = Mathf.Clamp(Zoom, 5f, 80f);
            cam.nearClipPlane = Height - Ceiling;
            cam.farClipPlane = Height + 80f;
            cam.transform.SetPositionAndRotation(center + Vector3.up * Height, Quaternion.Euler(90f, TurnWithView ? yaw : 0f, 0f));
            cam.enabled = true;
            placed = true;
        }

        public Rect Area
        {
            get
            {
                float s = BasePixels * Mathf.Clamp(Size, 0.6f, 2f);
                const float pad = 16f;
                bool right = Corner == 1 || Corner == 3, bottom = Corner >= 2;
                float x = right ? Screen.width - s - pad : pad;
                float y = bottom ? Screen.height - s - pad : pad;
                return new Rect(x, y, s, s);
            }
        }

        public void Draw(CasterCamera caster)
        {
            if (!placed || texture == null || cam == null || Event.current.type != EventType.Repaint) return;
            // Just the map: no frame or shadow around it.
            GUI.DrawTexture(Area, texture, ScaleMode.StretchToFill, false);
        }

        public void Shutdown()
        {
            if (cam != null) Object.Destroy(cam.gameObject);
            if (texture != null)
            {
                texture.Release();
                Object.Destroy(texture);
            }
            cam = null;
            texture = null;
        }
    }
}
