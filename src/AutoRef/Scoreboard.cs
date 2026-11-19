using UnityEngine;

namespace AutoRef
{
    internal sealed class Scoreboard
    {
        private readonly RefereeController c;
        private bool dragging;
        private Vector2 dragOffset, dragPos;

        public Rect ScreenRect { get; private set; }

        public Scoreboard(RefereeController controller) => c = controller;

        public void Draw(bool panelOpen, Rect panelRect)
        {
            // Devicular Caster's score bar already shows this score: don't draw a second one on top.
            if (!Settings.ShowScoreboard.Value || Link.Flag("Caster.ShowsRefereeScore") || !ScoreFeed.Read(out var view))
            {
                ScreenRect = Rect.zero;
                dragging = false;
                return;
            }
            float s = Mathf.Clamp(Settings.ScoreboardScale.Value, 0.6f, 2f);
            var size = ScoreBarArt.Measure(view, s);
            float w = size.x, h = size.y;

            float x = Settings.ScoreboardX.Value * Screen.width - w / 2f;
            float y = Settings.ScoreboardY.Value * Screen.height;
            if (dragging)
            {
                x = dragPos.x;
                y = dragPos.y;
            }
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - w));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - h));
            var rect = new Rect(x, y, w, h);
            ScreenRect = rect;
            HandleDrag(panelOpen, panelRect, rect, w);

            if (Event.current.type != EventType.Repaint) return;
            ScoreBarArt.Draw(rect, view, s);
        }

        private void HandleDrag(bool panelOpen, Rect panelRect, Rect rect, float w)
        {
            var e = Event.current;
            if (!panelOpen)
            {
                dragging = false;
                return;
            }
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition) && !panelRect.Contains(e.mousePosition))
            {
                dragging = true;
                dragOffset = e.mousePosition - rect.position;
                dragPos = rect.position;
                e.Use();
            }
            else if (dragging && e.type == EventType.MouseDrag)
            {
                dragPos = e.mousePosition - dragOffset;
                e.Use();
            }
            else if (dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                dragging = false;
                float nx = Mathf.Clamp01((rect.x + w / 2f) / Mathf.Max(1, Screen.width));
                float ny = Mathf.Clamp01(rect.y / Mathf.Max(1, Screen.height));
                Settings.ScoreboardX.Value = nx;
                Settings.ScoreboardY.Value = ny;
                e.Use();
            }
        }
    }
}
