using System;

namespace Devicular
{
    /// <summary>
    /// A buffer of timestamped samples from one source. Input at any rate is converted to an Opus rate (resampled to
    /// 48 kHz if needed) and at most two channels.
    /// </summary>
    internal sealed class AudioFeed
    {
        public readonly byte Track;
        public readonly int Actor;
        private readonly double resync;
        private readonly object gate = new object();
        private readonly float[] ring;
        private readonly float[] last = new float[2];
        private int read, count;
        private double writeT = double.NaN;
        private int rate, channels, srcRate, srcChannels;
        private double resPos;

        /// <param name="resync">How far the timeline may drift from the clock before it starts again. Sound captured
        /// on this PC arrives evenly; voices from the network arrive in bursts, so they get more room.</param>
        /// <param name="samples">Buffer size in samples (all channels together); must be even.</param>
        public AudioFeed(byte track, int actor = 0, double resync = 0.08, int samples = 48000 * 2)
        {
            Track = track;
            Actor = actor;
            this.resync = resync;
            ring = new float[Math.Max(4096, samples & ~1)];
        }

        private static bool IsOpusRate(int r) => r == 48000 || r == 24000 || r == 16000 || r == 12000 || r == 8000;

        public void Reset()
        {
            lock (gate)
            {
                read = count = 0;
                writeT = double.NaN;
                resPos = 0;
                last[0] = last[1] = 0f;
            }
        }

        public bool Format(out int r, out int ch)
        {
            lock (gate)
            {
                r = rate;
                ch = channels;
                return rate > 0 && channels > 0;
            }
        }

        private void Put(float v)
        {
            if (count == ring.Length)
            {
                read = (read + channels) % ring.Length;
                count -= channels;
            }
            ring[(read + count) % ring.Length] = v;
            count++;
        }

        /// <summary>Adds interleaved samples. <paramref name="now"/> is the stream time just after the last one.</summary>
        public void Push(float[] data, int samples, int inRate, int inChannels, double now)
        {
            if (data == null || inRate <= 0 || inChannels <= 0) return;
            int inFrames = Math.Min(samples, data.Length) / inChannels;
            if (inFrames <= 0) return;
            lock (gate)
            {
                if (inRate != srcRate || inChannels != srcChannels)
                {
                    srcRate = inRate;
                    srcChannels = inChannels;
                    rate = IsOpusRate(inRate) ? inRate : 48000;
                    channels = Math.Min(2, inChannels);
                    read = count = 0;
                    writeT = double.NaN;
                    resPos = 0;
                    last[0] = last[1] = 0f;
                }
                double startT = now - inFrames / (double)inRate;
                if (double.IsNaN(writeT) || Math.Abs(writeT - startT) > resync)
                {
                    // First block, or a gap (the mic paused, the game hitched): start the timeline again here.
                    read = count = 0;
                    resPos = 0;
                    writeT = startT;
                }
                if (rate == inRate)
                {
                    for (int f = 0; f < inFrames; f++)
                    {
                        int b = f * inChannels;
                        for (int c = 0; c < channels; c++) Put(data[b + c]);
                    }
                }
                else
                {
                    double step = inRate / (double)rate;
                    double pos = resPos;
                    while (pos < inFrames - 1)
                    {
                        int i0 = (int)Math.Floor(pos);
                        float frac = (float)(pos - i0);
                        for (int c = 0; c < channels; c++)
                        {
                            float s0 = i0 < 0 ? last[c] : data[i0 * inChannels + c];
                            float s1 = data[(i0 + 1) * inChannels + c];
                            Put(s0 + (s1 - s0) * frac);
                        }
                        pos += step;
                    }
                    resPos = pos - inFrames;
                    for (int c = 0; c < channels; c++) last[c] = data[(inFrames - 1) * inChannels + c];
                }
                // Move the timeline on by exactly the samples added, then nudge it slightly toward the clock. Blocks
                // arrive unevenly, but the sound card's clock is steady, so this keeps packet times 20 ms apart while
                // still following any slow drift between the two clocks.
                double end = writeT + inFrames / (double)inRate;
                writeT = end + (now - end) * 0.02;
            }
        }

        /// <summary>Takes one frame of samples, and the stream time of its first sample.</summary>
        public bool TryPop(float[] frame, int frameChannels, out double t)
        {
            lock (gate)
            {
                t = 0;
                if (rate <= 0 || frameChannels != channels || count < frame.Length) return false;
                t = writeT - (count / channels) / (double)rate;
                int first = Math.Min(frame.Length, ring.Length - read);
                Array.Copy(ring, read, frame, 0, first);
                if (first < frame.Length) Array.Copy(ring, 0, frame, first, frame.Length - first);
                read = (read + frame.Length) % ring.Length;
                count -= frame.Length;
                return true;
            }
        }
    }
}
