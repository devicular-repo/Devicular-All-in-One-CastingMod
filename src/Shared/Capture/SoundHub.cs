using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Devicular
{
    /// <summary>What a captured sound message is.</summary>
    internal enum SoundKind
    {
        /// <summary>[6][track][channels][0][i32 id][f32 time][opus]: 20 ms of one track.</summary>
        Audio,
        /// <summary>[8][f32 time][u16 n] then n x (i32 id, 3 x f32 position, f32 volume).</summary>
        Positions,
        /// <summary>[7][json]: how voices fade with distance.</summary>
        SoundInfo,
        /// <summary>[9][i32 id][json]: how one game sound fades with distance.</summary>
        SourceInfo
    }

    /// <summary>Something that takes captured sound: the live stream, or the replay recorder.</summary>
    internal interface ISoundSink
    {
        /// <summary>True while it takes sound. Read from any thread.</summary>
        bool SoundOpen { get; }

        /// <summary>Its time zero, on <see cref="SoundHub.Now"/>'s clock.</summary>
        double SoundStart { get; }

        /// <summary>Whether it wants a track (the caster's mic can be off for one and on for the other).</summary>
        bool WantsTrack(byte track);

        /// <summary>A message with this sink's own time already written in. It may keep the array.</summary>
        void TakeSound(byte[] msg, SoundKind kind);
    }

    /// <summary>
    /// Sends the one sound capture (<see cref="SoundCapture"/>) to everything that wants it: the live stream and the
    /// replay recorder, so both get exactly the same 3D sound. Capture stamps everything with one clock
    /// (<see cref="Now"/>), and each sink gets its own copy with the time counted from its own start. A sink that starts
    /// later (a recording begun mid-stream) first gets the fade settings sent before it started. No Unity types.
    /// </summary>
    internal sealed class SoundHub
    {
        private static readonly long Epoch = Stopwatch.GetTimestamp();

        /// <summary>Seconds on the sound clock, from any thread.</summary>
        public static double Now => (Stopwatch.GetTimestamp() - Epoch) / (double)Stopwatch.Frequency;

        /// <summary>A Stopwatch timestamp on the sound clock.</summary>
        public static double FromTicks(long ticks) => (ticks - Epoch) / (double)Stopwatch.Frequency;

        public const byte TrackMic = 1;
        private const byte NoTrack = 255;

        // Copied on write (see Add), so the sound thread never sees an array change under it.
        private volatile ISoundSink[] sinks;
        private bool[] wasOpen;
        private readonly object gate = new object();
        private byte[] soundInfo;
        private readonly Dictionary<int, byte[]> sourceInfo = new Dictionary<int, byte[]>();

        public SoundHub(params ISoundSink[] sinks)
        {
            this.sinks = sinks;
            wasOpen = new bool[sinks.Length];
        }

        /// <summary>
        /// All-in-One: adds one more sink, so the replay recorder and the live stream share this one capture.
        /// Main thread, before either starts taking sound.
        /// </summary>
        public void Add(ISoundSink sink)
        {
            if (sink == null || Array.IndexOf(sinks, sink) >= 0) return;
            var next = new ISoundSink[sinks.Length + 1];
            Array.Copy(sinks, next, sinks.Length);
            next[sinks.Length] = sink;
            var open = new bool[next.Length];
            Array.Copy(wasOpen, open, wasOpen.Length);
            wasOpen = open;
            sinks = next;
        }

        /// <summary>True when anything takes sound.</summary>
        public bool Open
        {
            get
            {
                foreach (var s in sinks)
                    if (s.SoundOpen) return true;
                return false;
            }
        }

        /// <summary>True when anything that takes sound wants the caster's mic.</summary>
        public bool WantsMic
        {
            get
            {
                foreach (var s in sinks)
                    if (s.SoundOpen && s.WantsTrack(TrackMic)) return true;
                return false;
            }
        }

        /// <summary>Main thread, every frame: a sink that has just started gets the fade settings sent so far.</summary>
        public void Tick()
        {
            var sinks = this.sinks;
            for (int i = 0; i < sinks.Length; i++)
            {
                bool open = sinks[i].SoundOpen;
                if (open && !wasOpen[i])
                {
                    byte[] si;
                    byte[][] infos;
                    lock (gate)
                    {
                        si = soundInfo;
                        infos = new List<byte[]>(sourceInfo.Values).ToArray();
                    }
                    if (si != null) sinks[i].TakeSound((byte[])si.Clone(), SoundKind.SoundInfo);
                    foreach (var m in infos) sinks[i].TakeSound((byte[])m.Clone(), SoundKind.SourceInfo);
                }
                wasOpen[i] = open;
            }
        }

        /// <summary>A sound packet. <paramref name="t"/> is its time on <see cref="Now"/>'s clock.</summary>
        public void Audio(byte[] msg, double t) => Fan(msg, t, 8, SoundKind.Audio, msg.Length > 1 ? msg[1] : NoTrack);

        /// <summary>Where the playing game sounds are. <paramref name="t"/> is the time on <see cref="Now"/>'s clock.</summary>
        public void Positions(byte[] msg, double t) => Fan(msg, t, 1, SoundKind.Positions, NoTrack);

        public void SoundInfo(byte[] msg)
        {
            lock (gate) soundInfo = msg;
            Fan(msg, 0, -1, SoundKind.SoundInfo, NoTrack);
        }

        public void SourceInfo(int id, byte[] msg)
        {
            lock (gate)
            {
                if (sourceInfo.Count < 4000 || sourceInfo.ContainsKey(id)) sourceInfo[id] = msg;
            }
            Fan(msg, 0, -1, SoundKind.SourceInfo, NoTrack);
        }

        private void Fan(byte[] msg, double t, int timeAt, SoundKind kind, byte track)
        {
            var sinks = this.sinks;
            int last = -1;
            for (int i = 0; i < sinks.Length; i++)
                if (Takes(sinks[i], track)) last = i;
            for (int i = 0; i <= last; i++)
            {
                var s = sinks[i];
                if (!Takes(s, track)) continue;
                // The last sink gets the original; the others get copies, since each writes its own time in.
                var m = i == last ? msg : (byte[])msg.Clone();
                if (timeAt >= 0) BitConverter.TryWriteBytes(new Span<byte>(m, timeAt, 4), (float)(t - s.SoundStart));
                s.TakeSound(m, kind);
            }
        }

        private static bool Takes(ISoundSink s, byte track) => s.SoundOpen && (track == NoTrack || s.WantsTrack(track));
    }
}
