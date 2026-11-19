using System;

namespace Devicular
{
    /// <summary>
    /// The sound of one AudioSource rebuilt from the one-shot clips it plays (hand taps, tag sounds). Each clip is mixed
    /// in at the moment it was played, so quick taps that overlap blend the way they do in the game. 20 ms frames are
    /// handed out once their time has passed, so a clip played a moment later can still be mixed into them. Mono,
    /// 48 kHz. No Unity types, so it can be tested on its own.
    /// </summary>
    internal sealed class EventTrack
    {
        public const int Rate = 48000, FrameSamples = 960;
        private const double FrameSeconds = (double)FrameSamples / Rate;
        private readonly object gate = new object();
        private float[] buf = new float[Rate];
        private int len;
        private double baseT = double.NaN;

        public bool Pending
        {
            get { lock (gate) return len > 0; }
        }

        /// <summary>Mixes in a clip (mono, 48 kHz) that started at stream time <paramref name="t"/>.</summary>
        public void Add(float[] clip, double t, float gain)
        {
            if (clip == null || clip.Length == 0 || gain <= 0f) return;
            lock (gate)
            {
                if (len == 0 || double.IsNaN(baseT)) baseT = t;
                int offset = (int)Math.Round((t - baseT) * Rate);
                int skip = 0;
                if (offset < 0)
                {
                    // Part of this clip is already out; mix in the rest.
                    skip = -offset;
                    offset = 0;
                }
                int n = clip.Length - skip;
                if (n <= 0) return;
                int need = offset + n;
                if (need > Rate * 20) return;
                if (buf.Length < need) Array.Resize(ref buf, Math.Max(need, buf.Length * 2));
                if (need > len) Array.Clear(buf, len, need - len);
                for (int i = 0; i < n; i++) buf[offset + i] += clip[skip + i] * gain;
                if (need > len) len = need;
            }
        }

        /// <summary>Takes the next 20 ms, once it has fully played. <paramref name="t"/> is its stream time.</summary>
        public bool TryPop(float[] frame, double now, out double t)
        {
            lock (gate)
            {
                t = baseT;
                if (len == 0 || double.IsNaN(baseT) || now < baseT + FrameSeconds) return false;
                int n = Math.Min(FrameSamples, len);
                Array.Copy(buf, 0, frame, 0, n);
                if (n < frame.Length) Array.Clear(frame, n, frame.Length - n);
                len -= n;
                if (len > 0) Array.Copy(buf, n, buf, 0, len);
                baseT += FrameSeconds;
                return true;
            }
        }
    }
}
