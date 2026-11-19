using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using POpusCodec;
using UnityEngine;

namespace Devicular
{
    /// <summary>
    /// 3D game sound for live spectating and replays. While a recording or the live stream wants sound, every
    /// AudioSource in the scene gets a <see cref="SourceTap"/>, a filter that copies the source's sound before Unity
    /// places it around the caster (it passes the sound on unchanged, so the caster hears nothing different).
    /// - 3D sources (tags, hand taps, splashes...) are each sent on their own, with their position, volume and how
    ///   they fade with distance, so the page can place them around the viewer's own camera.
    /// - 2D sources aren't anywhere in the world: menu sounds, music, and other mods' sounds such as the Devicular
    ///   Auto Ref announcer. They're left out unless the "flat sounds" setting is on; then they're mixed into one
    ///   flat track.
    /// Players' voices are never touched: <see cref="SoundCapture"/> takes them straight from voice chat. When the
    /// recording or stream stops, every tap is taken off again, so the game's sound is exactly as it was.
    /// </summary>
    internal sealed class SoundSources
    {
        public const byte TrackSource = 3;
        private const byte MsgPositions = 8, MsgSourceInfo = 9;
        private const int MaxTaps = 1500;
        private const float Gate = 0.002f;
        private static readonly long IdleTicks = Stopwatch.Frequency * 2;

        internal static SoundSources Current;

        private readonly SoundHub hub;
        private readonly Action wake;
        private readonly Dictionary<int, SourceTap> taps = new Dictionary<int, SourceTap>();
        private readonly HashSet<SourceTap> hotSet = new HashSet<SourceTap>();
        private readonly ConcurrentQueue<SourceTap> woke = new ConcurrentQueue<SourceTap>();
        private readonly Dictionary<AudioSource, bool> savedPostEffects = new Dictionary<AudioSource, bool>();
        private readonly HashSet<int> skipped = new HashSet<int>();
        // Sources with other filters of their own: their spatializer order is left alone.
        private readonly HashSet<int> keepOrder = new HashSet<int>();
        private volatile SourceTap[] hot = new SourceTap[0];
        private bool active;
        private float nextScan, nextPositions;

        /// <summary>The 2D sound, mixed. Track 0, encoded like the old game-sound track.</summary>
        public readonly AudioFeed Ambience = new AudioFeed(SoundCapture.TrackGame);
        private readonly object ambGate = new object();
        private float[] ambBuf = new float[8192];
        private int ambFrames;
        private long ambLastTicks;
        private static readonly long PassGapTicks = Stopwatch.Frequency * 3 / 1000;

        public volatile int OutputRate = 48000;
        public int TapCount => taps.Count;
        public int HotCount { get; private set; }
        public int OneShotsPerSecond { get; private set; }
        private int oneShots;
        private float nextCount;
        private HashSet<AudioSource> voiceSources;
        private volatile bool takeFlat;
        // Which mixing pass the flat track is on; each tap remembers the last pass it added to.
        private int ambPass;

        // One-shot clips, read once: mono, 48 kHz, at a given pitch. Null when Unity can't hand out a clip's samples.
        private readonly Dictionary<AudioClip, float[]> clips = new Dictionary<AudioClip, float[]>();
        private readonly Dictionary<long, float[]> pitched = new Dictionary<long, float[]>();
        private readonly HashSet<AudioClip> unreadable = new HashSet<AudioClip>();

        public SoundSources(SoundHub hub, Action wake)
        {
            this.hub = hub;
            this.wake = wake;
        }

        public SourceTap[] Hot => hot;

        /// <summary>Main thread, every frame.</summary>
        /// <param name="flat">Also take 2D sounds (menus, music, other mods' sounds like the Auto Ref announcer).</param>
        public void Tick(bool want, HashSet<AudioSource> voiceSources, bool flat)
        {
            if (!want)
            {
                if (active) Stop();
                return;
            }
            if (flat != takeFlat)
            {
                takeFlat = flat;
                nextScan = 0f;
            }
            if (!active)
            {
                active = true;
                Current = this;
                Patch();
                nextScan = 0f;
            }
            this.voiceSources = voiceSources;
            OutputRate = AudioSettings.outputSampleRate;
            if (Time.unscaledTime >= nextCount)
            {
                nextCount = Time.unscaledTime + 1f;
                OneShotsPerSecond = oneShots;
                oneShots = 0;
            }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                Scan(voiceSources);
            }

            // Sources that started making sound since last frame.
            bool changed = false;
            while (woke.TryDequeue(out var t))
                if (t != null && hotSet.Add(t)) changed = true;
            long now = Stopwatch.GetTimestamp();
            List<SourceTap> cold = null;
            foreach (var t in hotSet)
            {
                if (t == null || t.Source == null || now - Volatile.Read(ref t.LastAudioTicks) > IdleTicks)
                    (cold ??= new List<SourceTap>()).Add(t);
            }
            if (cold != null)
            {
                foreach (var t in cold)
                {
                    hotSet.Remove(t);
                    if (t != null) t.Hot = false;
                }
                changed = true;
            }
            if (changed)
            {
                var arr = new SourceTap[hotSet.Count];
                hotSet.CopyTo(arr);
                hot = arr;
            }
            HotCount = hotSet.Count;

            if (Time.unscaledTime >= nextPositions && hub.Open)
            {
                nextPositions = Time.unscaledTime + 1f / 30f;
                SendPositions();
            }
        }

        public void Stop()
        {
            active = false;
            if (Current == this) Current = null;
            // Take every tap off, so nothing of the mod stays in the game's sound between recordings.
            foreach (var t in taps.Values)
            {
                if (t == null) continue;
                t.Active = false;
                try { UnityEngine.Object.Destroy(t); } catch { }
            }
            taps.Clear();
            skipped.Clear();
            keepOrder.Clear();
            foreach (var kv in savedPostEffects)
                try { if (kv.Key != null) kv.Key.spatializePostEffects = kv.Value; } catch { }
            savedPostEffects.Clear();
            hotSet.Clear();
            hot = new SourceTap[0];
            HotCount = 0;
            while (woke.TryDequeue(out _)) { }
            ResetFeeds();
        }

        public void ResetFeeds()
        {
            Ambience.Reset();
            lock (ambGate) ambFrames = 0;
            foreach (var t in taps.Values)
                if (t != null)
                {
                    t.Feed?.Reset();
                    t.Events = null;
                    t.SentInfo = null;
                }
        }

        private void Scan(HashSet<AudioSource> voiceSources)
        {
            AudioSource[] all;
            try { all = UnityEngine.Object.FindObjectsOfType<AudioSource>(); }
            catch { return; }
            foreach (var src in all)
            {
                if (src == null) continue;
                int id = src.GetInstanceID();
                taps.TryGetValue(id, out var tap);
                if (voiceSources != null && voiceSources.Contains(src))
                {
                    // Voices come straight from voice chat instead; a voice never keeps a tap.
                    if (tap != null) Forget(tap, src);
                    skipped.Add(id);
                    continue;
                }
                if (!takeFlat && src.spatialBlend < 0.5f)
                {
                    // A 2D sound (a menu, music, another mod's sound like the Auto Ref announcer): not part of the
                    // world, so it isn't taken. Looked at again each scan, in case it becomes a 3D sound.
                    if (tap != null) Forget(tap, src);
                    continue;
                }
                if (tap == null)
                {
                    if (taps.Count >= MaxTaps || skipped.Contains(id)) continue;
                    tap = Adopt(src, false);
                    if (tap == null)
                    {
                        skipped.Add(id);
                        continue;
                    }
                }
                Refresh(tap, src);
            }
            // Forget sources that were destroyed.
            List<int> gone = null;
            foreach (var kv in taps)
                if (kv.Value == null || kv.Value.Source == null) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null)
                foreach (var id in gone) taps.Remove(id);
        }

        private void Refresh(SourceTap tap, AudioSource src)
        {
            tap.Positional = src.spatialBlend >= 0.5f;
            tap.Volume = src.mute ? 0f : src.volume;
            tap.Active = true;
            // A source rebuilt from its one-shot clips doesn't need its filter, so it's switched off (Unity then
            // skips it completely).
            bool filter = !tap.EventDriven;
            if (tap.enabled != filter) tap.enabled = filter;
            // With a spatializer plugin (the game uses Meta XR Audio), filters normally run after it, so the tap would
            // hear the sound already placed around the caster. Run the spatializer after the filters instead. That's
            // only done when the tap is the source's one filter, so the caster hears no difference.
            if (filter && src.spatialize && !src.spatializePostEffects && !savedPostEffects.ContainsKey(src) && !keepOrder.Contains(tap.Id))
            {
                if (HasOtherFilters(src)) keepOrder.Add(tap.Id);
                else
                {
                    savedPostEffects[src] = src.spatializePostEffects;
                    src.spatializePostEffects = true;
                }
            }
        }

        /// <summary>Takes a tap off a source and puts the source back the way it was.</summary>
        private void Forget(SourceTap tap, AudioSource src)
        {
            tap.Active = false;
            taps.Remove(tap.Id);
            if (hotSet.Remove(tap))
            {
                var arr = new SourceTap[hotSet.Count];
                hotSet.CopyTo(arr);
                hot = arr;
            }
            try { UnityEngine.Object.Destroy(tap); } catch { }
            if (src != null && savedPostEffects.TryGetValue(src, out bool was))
            {
                try { src.spatializePostEffects = was; } catch { }
                savedPostEffects.Remove(src);
            }
        }

        /// <summary>
        /// A player's voice: voice chat plays it through a Speaker on the same object. Voices are taken straight from
        /// voice chat, so their sources are never tapped or changed in any way.
        /// </summary>
        private bool IsVoice(AudioSource src)
        {
            if (voiceSources != null && voiceSources.Contains(src)) return true;
            try { return src.GetComponent<Photon.Voice.Unity.Speaker>() != null; }
            catch { return true; }
        }

        private static bool HasOtherFilters(AudioSource src)
        {
            try
            {
                var go = src.gameObject;
                if (go.GetComponent<AudioLowPassFilter>() != null || go.GetComponent<AudioHighPassFilter>() != null ||
                    go.GetComponent<AudioEchoFilter>() != null || go.GetComponent<AudioDistortionFilter>() != null ||
                    go.GetComponent<AudioReverbFilter>() != null || go.GetComponent<AudioChorusFilter>() != null) return true;
                // Another script filtering the sound (OnAudioFilterRead) also counts.
                foreach (var b in go.GetComponents<MonoBehaviour>())
                {
                    if (b == null || b is SourceTap) continue;
                    if (b.GetType().GetMethod("OnAudioFilterRead",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) != null) return true;
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Puts a tap on a source (main thread). Returns null if it can't.</summary>
        internal SourceTap Adopt(AudioSource src, bool startsNow)
        {
            try
            {
                // A filter on the listener's own object would hear everything the listener hears, not one source.
                if (src.GetComponent<AudioListener>() != null) return null;
                // Players' voices are never tapped (see IsVoice).
                if (IsVoice(src)) return null;
                var tap = src.GetComponent<SourceTap>();
                if (tap == null) tap = src.gameObject.AddComponent<SourceTap>();
                tap.Id = src.GetInstanceID();
                tap.Source = src;
                tap.Owner = this;
                taps[tap.Id] = tap;
                Refresh(tap, src);
                if (startsNow) Volatile.Write(ref tap.LastAudioTicks, Stopwatch.GetTimestamp());
                return tap;
            }
            catch
            {
                return null;
            }
        }

        // ---------- audio thread ----------

        internal void PushPositional(SourceTap tap, float[] data, int channels)
        {
            if (!hub.Open || channels <= 0) return;
            int frames = data.Length / channels;
            if (tap.Mono.Length < frames) tap.Mono = new float[frames];
            var mono = tap.Mono;
            float peak = 0f, scale = 1f / channels;
            for (int f = 0, i = 0; f < frames; f++)
            {
                float s = 0f;
                for (int c = 0; c < channels; c++) s += data[i++];
                s *= scale;
                mono[f] = s;
                float a = s < 0 ? -s : s;
                if (a > peak) peak = a;
            }
            long now = Stopwatch.GetTimestamp();
            if (peak >= Gate)
            {
                Volatile.Write(ref tap.LastAudioTicks, now);
                if (!tap.Hot)
                {
                    tap.Hot = true;
                    woke.Enqueue(tap);
                }
            }
            else if (!tap.Hot) return;
            var feed = tap.Feed ?? (tap.Feed = new AudioFeed(TrackSource, tap.Id, 0.08, 48000));
            feed.Push(mono, frames, OutputRate, 1, SoundHub.Now);
            wake();
        }

        internal void PushAmbient(SourceTap tap, float[] data, int channels)
        {
            if (!hub.Open || channels <= 0 || !takeFlat) return;
            int frames = data.Length / channels;
            long now = Stopwatch.GetTimestamp();
            // The filter already gets the sound at the source's volume, so it's mixed as it comes.
            lock (ambGate)
            {
                // Every source is filtered back to back in one mixing pass. A new pass has started when a source
                // comes round again: Unity often mixes several passes in a row with no gap, and adding those
                // together (instead of one after the other) garbled the sound. A gap or a different block size also
                // means a new pass.
                if (ambFrames > 0 && (tap.AmbPass == ambPass || frames != ambFrames || now - ambLastTicks > PassGapTicks)) FlushAmbient();
                if (ambFrames == 0)
                {
                    if (ambBuf.Length < frames * 2) ambBuf = new float[frames * 2];
                    Array.Clear(ambBuf, 0, frames * 2);
                    ambFrames = frames;
                    ambPass++;
                }
                tap.AmbPass = ambPass;
                int n = Math.Min(frames, ambFrames);
                for (int f = 0; f < n; f++)
                {
                    float l = data[f * channels];
                    float r = channels > 1 ? data[f * channels + 1] : l;
                    ambBuf[f * 2] += l;
                    ambBuf[f * 2 + 1] += r;
                }
                ambLastTicks = now;
            }
        }

        private void FlushAmbient()
        {
            Ambience.Push(ambBuf, ambFrames * 2, OutputRate, 2, SoundHub.Now);
            ambFrames = 0;
            wake();
        }

        /// <summary>The sound thread calls this so the last pass goes out when 2D sounds stop.</summary>
        internal void FlushStaleAmbience()
        {
            long now = Stopwatch.GetTimestamp();
            lock (ambGate)
                if (ambFrames > 0 && now - ambLastTicks > Stopwatch.Frequency / 30) FlushAmbient();
        }

        // ---------- what viewers need to place each sound ----------

        private void SendPositions()
        {
            var list = hot;
            if (list.Length == 0) return;
            var buf = new byte[1 + 4 + 2 + list.Length * 20];
            int n = 0, o = 7;
            foreach (var t in list)
            {
                if (t == null || t.Source == null || !t.Positional) continue;
                var src = t.Source;
                var p = src.transform.position;
                float vol = src.mute ? 0f : src.volume;
                t.Volume = vol;
                BitConverter.TryWriteBytes(new Span<byte>(buf, o, 4), t.Id);
                BitConverter.TryWriteBytes(new Span<byte>(buf, o + 4, 4), p.x);
                BitConverter.TryWriteBytes(new Span<byte>(buf, o + 8, 4), p.y);
                BitConverter.TryWriteBytes(new Span<byte>(buf, o + 12, 4), p.z);
                BitConverter.TryWriteBytes(new Span<byte>(buf, o + 16, 4), vol);
                o += 20;
                n++;
                SendInfo(t, src);
            }
            if (n == 0) return;
            buf[0] = MsgPositions;
            BitConverter.TryWriteBytes(new Span<byte>(buf, 5, 2), (ushort)n);
            if (o < buf.Length) Array.Resize(ref buf, o);
            hub.Positions(buf, SoundHub.Now);
        }

        private void SendInfo(SourceTap t, AudioSource src)
        {
            // A source's fade settings rarely change: look again once a second, not with every position.
            float now = Time.unscaledTime;
            if (t.SentInfo != null && now < t.InfoCheckAt) return;
            t.InfoCheckAt = now + 1f;
            string json = SoundTable.Json(src);
            if (json == t.SentInfo) return;
            t.SentInfo = json;
            var bytes = Encoding.UTF8.GetBytes(json);
            var msg = new byte[5 + bytes.Length];
            msg[0] = MsgSourceInfo;
            BitConverter.TryWriteBytes(new Span<byte>(msg, 1, 4), t.Id);
            Buffer.BlockCopy(bytes, 0, msg, 5, bytes.Length);
            hub.SourceInfo(t.Id, msg);
        }

        // ---------- one-shots (hand taps, tag sounds), rebuilt from their clips ----------
        //
        // Unity only runs a source's filter while the caster's listener could hear it, so a hand tap 20 m from the
        // caster is never copied. The game still plays every player's taps (each tap is sent to everyone), so the
        // mod watches PlayOneShot instead: it knows the clip, the volume and the source, and mixes the clip into that
        // source's own track at the moment it played, wherever the caster is.

        internal void OnOneShot(AudioSource src, AudioClip clip, float volumeScale)
        {
            if (!active || !hub.Open || src == null || clip == null) return;
            if (!src.isActiveAndEnabled || src.spatialBlend < 0.5f) return;
            if (voiceSources != null && voiceSources.Contains(src)) return;
            var samples = Samples(clip, src.pitch);
            if (samples == null) return;
            int id = src.GetInstanceID();
            if (!taps.TryGetValue(id, out var tap) || tap == null)
            {
                if (skipped.Contains(id)) return;
                tap = Adopt(src, false);
                if (tap == null)
                {
                    skipped.Add(id);
                    return;
                }
            }
            if (!tap.EventDriven)
            {
                tap.EventDriven = true;
                // Rebuilt from its clips from now on, so its filter is switched off.
                tap.enabled = false;
            }
            if (tap.Events == null) tap.Events = new EventTrack();
            tap.Events.Add(samples, SoundHub.Now, src.mute ? 0f : src.volume * volumeScale);
            Volatile.Write(ref tap.LastAudioTicks, Stopwatch.GetTimestamp());
            if (!tap.Hot)
            {
                tap.Hot = true;
                woke.Enqueue(tap);
            }
            oneShots++;
            wake();
        }

        private float[] Samples(AudioClip clip, float pitch)
        {
            if (unreadable.Contains(clip)) return null;
            if (!clips.TryGetValue(clip, out var mono))
            {
                mono = ReadClip(clip, out bool retry);
                if (mono == null)
                {
                    if (!retry) unreadable.Add(clip);
                    return null;
                }
                clips[clip] = mono;
            }
            if (pitch <= 0f || Mathf.Abs(pitch - 1f) < 0.005f) return mono;
            long key = (long)clip.GetInstanceID() * 10000 + Mathf.RoundToInt(Mathf.Clamp(pitch, 0.1f, 4f) * 1000f);
            if (pitched.TryGetValue(key, out var p)) return p;
            p = Resample(mono, Mathf.Clamp(pitch, 0.1f, 4f));
            if (pitched.Count > 2000) pitched.Clear();
            pitched[key] = p;
            return p;
        }

        // Unity hands out a clip's samples only when it's fully loaded and decompressed in memory.
        private static float[] ReadClip(AudioClip clip, out bool retry)
        {
            retry = false;
            try
            {
                if (clip.length > 15f || clip.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    Mod.Log?.LogInfo($"Sound: one-shot '{clip.name}' ({clip.loadType}, {clip.length:0.#} s) can't be read, so it's copied from what you hear instead.");
                    return null;
                }
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                    retry = true;
                    return null;
                }
                int ch = Math.Max(1, clip.channels), n = clip.samples;
                var data = new float[n * ch];
                if (!clip.GetData(data, 0)) return null;
                var mono = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += data[i * ch + c];
                    mono[i] = s / ch;
                }
                return clip.frequency == EventTrack.Rate ? mono : Resample(mono, clip.frequency / (float)EventTrack.Rate);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Linear resampling; <paramref name="step"/> input samples per output sample.</summary>
        private static float[] Resample(float[] src, float step)
        {
            int n = Math.Max(1, (int)(src.Length / step));
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                float pos = i * step;
                int i0 = (int)pos;
                if (i0 >= src.Length - 1)
                {
                    dst[i] = src[src.Length - 1];
                    continue;
                }
                float f = pos - i0;
                dst[i] = src[i0] + (src[i0 + 1] - src[i0]) * f;
            }
            return dst;
        }

        private static void PlayOneShotPostfix(AudioSource __instance, AudioClip __0, float __1)
        {
            var cur = Current;
            if (cur == null) return;
            try { cur.OnOneShot(__instance, __0, __1); }
            catch { }
        }

        // ---------- AudioSource.PlayClipAtPoint makes a source and plays it at once ----------

        private static bool patched;

        private static void Patch()
        {
            if (patched) return;
            patched = true;
            var harmony = new HarmonyLib.Harmony(Mod.Id + ".sound");
            try
            {
                var m = HarmonyLib.AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayClipAtPoint), new[] { typeof(AudioClip), typeof(Vector3), typeof(float) });
                harmony.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(SoundSources), nameof(PlayClipAtPointPrefix)));
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning("Sound: couldn't watch PlayClipAtPoint: " + e.Message);
            }
            try
            {
                var m = HarmonyLib.AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) });
                harmony.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(SoundSources), nameof(PlayOneShotPostfix)));
                Mod.Log?.LogInfo("Sound: watching one-shot sounds, so every player's hand taps are sent wherever you are.");
            }
            catch (Exception e)
            {
                Mod.Log?.LogWarning("Sound: couldn't watch one-shot sounds, so only sounds you can hear are sent: " + e.Message);
            }
        }

        // Does what Unity's own PlayClipAtPoint does, with the tap in place before the sound starts.
        private static bool PlayClipAtPointPrefix(AudioClip clip, Vector3 position, float volume, bool __runOriginal)
        {
            var cur = Current;
            if (!__runOriginal)
            {
                // Another Devicular mod (Replay or Live Replay) already played it: tap the source it made.
                if (cur != null && clip != null)
                {
                    try
                    {
                        var made = Link.Get<AudioSource>("Sound.OneShot");
                        if (made != null && made.clip == clip) cur.Adopt(made, true);
                    }
                    catch
                    {
                    }
                }
                return false;
            }
            if (cur == null || clip == null) return true;
            try
            {
                var go = new GameObject("One shot audio");
                go.transform.position = position;
                var src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.spatialBlend = 1f;
                src.volume = volume;
                cur.Adopt(src, true);
                Link.Put("Sound.OneShot", src);
                src.Play();
                UnityEngine.Object.Destroy(go, clip.length * (Time.timeScale < 0.01f ? 0.01f : Time.timeScale));
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>Copies one AudioSource's sound as the game plays it, and passes it on unchanged.</summary>
    internal sealed class SourceTap : MonoBehaviour
    {
        public volatile SoundSources Owner;
        public volatile bool Active, Positional, Hot;
        /// <summary>This source's sound is rebuilt from the one-shot clips it plays, so the filter copy isn't used.</summary>
        public volatile bool EventDriven;
        public volatile float Volume = 1f;
        public int Id;
        public AudioSource Source;
        public long LastAudioTicks;
        public float[] Mono = new float[0];
        public AudioFeed Feed;
        public EventTrack Events;
        public string SentInfo;
        public float InfoCheckAt;
        // Audio thread only: the flat track's mixing pass this source last added to.
        public int AmbPass = -1;
        // Sound thread only:
        public OpusEncoder Encoder;
        public int EncRate, EncChannels;
        public float[] Frame;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Active || EventDriven) return;
            var o = Owner;
            if (o == null || data == null) return;
            if (Positional) o.PushPositional(this, data, channels);
            else o.PushAmbient(this, data, channels);
        }
    }

    /// <summary>How loud an AudioSource is at each distance, the way Unity works it out, as JSON for the page.</summary>
    internal static class SoundTable
    {
        public static string Json(AudioSource src)
        {
            float min = Mathf.Max(0.01f, src.minDistance), max = Mathf.Max(min + 0.01f, src.maxDistance);
            var mode = src.rolloffMode;
            AnimationCurve curve = null;
            if (mode == AudioRolloffMode.Custom)
                try { curve = src.GetCustomCurve(AudioSourceCurveType.CustomRolloff); } catch { }
            const int steps = 32;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(400);
            sb.Append("{\"max\":").Append(max.ToString("0.###", inv));
            sb.Append(",\"blend\":").Append(Mathf.Clamp01(src.spatialBlend).ToString("0.###", inv));
            sb.Append(",\"rolloff\":\"").Append(mode).Append("\",\"gains\":[");
            for (int i = 0; i <= steps; i++)
            {
                float d = max * i / steps, g;
                if (mode == AudioRolloffMode.Linear) g = d <= min ? 1f : Mathf.Clamp01((max - d) / (max - min));
                else if (mode == AudioRolloffMode.Custom && curve != null) g = Mathf.Clamp01(curve.Evaluate(d / max));
                else g = d <= min ? 1f : min / d;
                if (i > 0) sb.Append(',');
                sb.Append(g.ToString("0.####", inv));
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
