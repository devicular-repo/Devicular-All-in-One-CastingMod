using System;
using System.Collections.Generic;
using System.Diagnostics;
using GorillaTag.Audio;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace AutoRef
{
    internal sealed class VoiceBroadcaster
    {
        private readonly AnnouncerFeed feed = new AnnouncerFeed();
        private readonly Func<IAudioDesc> factory;
        private Recorder applied;
        private float nextCheck, nextHookScan, busyUntil, retryAt;
        private bool vadSuppressed, savedVad, micStalledLogged;
        private int silentSeconds;
        private string micInfo = "";
        private readonly Queue<float> recentApplies = new Queue<float>();

        public string Status = "Voice announcer not active.";
        public bool Live { get; private set; }
        public bool Transmitting { get; private set; }
        public string OtherHooks { get; private set; } = "";
        public int FramesPerSecond { get; private set; }
        public EventLog Log;

        private string lastLoggedStatus;
        private long lastFrameCount;
        private int applyCount;

        public VoiceBroadcaster()
        {
            factory = CreateReader;
        }

        private bool Busy => feed.IsPlaying || Time.unscaledTime < busyUntil;

        public void Update(bool enabled, GameReader game)
        {
            var rec = SafeRecorder();

            if (rec != null && Live)
            {
                if (Busy)
                {
                    if (!vadSuppressed)
                    {
                        savedVad = rec.VoiceDetection;
                        vadSuppressed = true;
                    }
                    if (rec.VoiceDetection) rec.VoiceDetection = false;
                }
                else if (vadSuppressed)
                {
                    vadSuppressed = false;
                    if (savedVad) rec.VoiceDetection = true;
                }
            }

            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;

            long frames = MixReader.FramesRead;
            FramesPerSecond = (int)(frames - lastFrameCount);
            lastFrameCount = frames;

            ScanOtherHooks();

            if (!enabled || rec == null || !game.InRoom)
            {
                if (applied != null && (!enabled || rec != applied)) Restore();
                Live = false;
                Status = !enabled ? "Voice announcer off - announcements only play for you."
                    : rec == null ? "Voice announcer: no voice recorder yet (join a room with voice chat)."
                    : "Voice announcer waits until you are in a room.";
                LogStatus();
                return;
            }

            try
            {
                string problem = EnsureOwnMixer(rec);
                Live = problem == null;
                Transmitting = rec.TransmitEnabled;
                LogMicState();

                string detail = $" [{FramesPerSecond} audio frames/s, recording {rec.IsRecording}, transmitting now {rec.IsCurrentlyTransmitting}]";
                if (problem != null)
                    Status = problem + detail;
                else if (game.VoiceChatOn == "FALSE")
                    Status = "Your voice chat is OFF (gorilla computer) - players can't hear the announcer. Turn voice ON and use OPEN MIC." + detail;
                else if (!string.IsNullOrEmpty(game.PttType) && game.PttType != "OPEN MIC")
                    Status = $"Your mic mode is {game.PttType} - players only hear the announcer while you transmit. Use OPEN MIC." + detail;
                else if (!Transmitting)
                    Status = "Your mic isn't transmitting right now (muted?) - players can't hear the announcer." + detail;
                else if (!rec.IsRecording)
                    Status = "Your voice recorder isn't recording - players can't hear the announcer." + detail;
                else if (FramesPerSecond == 0)
                    Status = "Photon Voice isn't reading audio from the announcer mixer - players can't hear it." + detail;
                else
                    Status = "Voice announcer live: players hear it from your gorilla's position." +
                             (MixReader.MicStalled ? " (your mic isn't delivering audio - sending the announcer over silence)" : micInfo) + detail;
            }
            catch (Exception e)
            {
                Live = false;
                Status = "Voice announcer failed: " + e.Message;
                Plugin.Log.LogWarning("Voice announcer failed: " + e);
                nextCheck = Time.unscaledTime + 10f;
            }
            LogStatus();
        }

        private string EnsureOwnMixer(Recorder rec)
        {
            string reason;
            if (applied != rec)
            {
                reason = null;
            }
            else if (!rec.IsRecording)
            {
                silentSeconds = 0;
                return null;
            }
            else if (!VoiceGuard.InputIsOurs(rec))
            {
                reason = "something replaced your voice input with " + VoiceGuard.InputName(rec);
            }
            else if (FramesPerSecond == 0 && rec.TransmitEnabled)
            {
                if (++silentSeconds < 3) return null;
                reason = "the voice chat stopped reading the announcer mixer";
            }
            else
            {
                silentSeconds = 0;
                return null;
            }

            if (Time.unscaledTime < retryAt)
                return $"Something keeps replacing your voice input - the announcer takes it back in {Mathf.CeilToInt(retryAt - Time.unscaledTime)} s.";
            while (recentApplies.Count > 0 && Time.unscaledTime - recentApplies.Peek() > 60f) recentApplies.Dequeue();
            if (recentApplies.Count >= 3)
            {
                recentApplies.Clear();
                retryAt = Time.unscaledTime + 60f;
                Log?.Add("--", $"Voice: {reason ?? "your voice input keeps changing"} - happened 3 times this minute, so the announcer waits a minute " +
                               "before taking it back (players won't hear it until then, you still will).", Colors.Bad);
                return "Something keeps replacing your voice input - the announcer takes it back in 60 s.";
            }
            recentApplies.Enqueue(Time.unscaledTime);

            if (reason != null) Log?.Add("--", $"Voice: {reason} - taking it back.", Colors.Warn);
            Apply(rec);
            return null;
        }

        private void Apply(Recorder rec)
        {
            applyCount++;
            silentSeconds = 0;
            Log?.Add("--", $"Voice: routing your mic through the announcer mixer (#{applyCount}; input was {VoiceGuard.InputName(rec)}, " +
                           $"sampling {(int)rec.SamplingRate} Hz, recording {rec.IsRecording}).", Colors.Dim);
            applied = rec;
            VoiceGuard.Guard(rec, factory);
            rec.InputFactory = factory;
            rec.SourceType = Recorder.InputSourceType.Factory;
            rec.RestartRecording(true);
            lastFrameCount = MixReader.FramesRead;
            if (!VoiceGuard.InputIsOurs(rec))
                Log?.Add("--", $"Voice: after restarting, the recorder still reads {VoiceGuard.InputName(rec)}.", Colors.Bad);
        }

        private void ScanOtherHooks()
        {
            if (Time.unscaledTime < nextHookScan) return;
            nextHookScan = Time.unscaledTime + 15f;
            string hooks = VoiceGuard.OtherHooks(Plugin.Guid);
            if (hooks == OtherHooks) return;
            OtherHooks = hooks;
            if (hooks.Length > 0)
                Log?.Add("--", $"Voice: other mods hook your voice recorder: {hooks}. While the announcer is on, AutoRef's mixer overrides them.", Colors.Info);
        }

        private void LogMicState()
        {
            bool stalled = MixReader.MicStalled;
            if (stalled == micStalledLogged) return;
            micStalledLogged = stalled;
            Log?.Add("--", stalled
                ? "Voice: your microphone stopped delivering audio (another app or mod may have taken it) - the announcer keeps going over silence."
                : "Voice: your microphone is delivering audio again.", stalled ? Colors.Warn : Colors.Dim);
        }

        private void LogStatus()
        {
            string key = Status;
            int cut = key.IndexOf(" [", StringComparison.Ordinal);
            if (cut > 0) key = key.Substring(0, cut);
            if (key == lastLoggedStatus) return;
            lastLoggedStatus = key;
            Log?.Add("--", "Voice: " + Status, Colors.Dim);
        }

        private static Recorder SafeRecorder()
        {
            try
            {
                var ns = NetworkSystem.Instance;
                if (ns == null) return null;
                var primary = ns.VoiceConnection != null ? ns.VoiceConnection.PrimaryRecorder : null;
                return primary != null ? primary : ns.LocalRecorder;
            }
            catch
            {
                try { return NetworkSystem.Instance != null ? NetworkSystem.Instance.LocalRecorder : null; }
                catch { return null; }
            }
        }

        private IAudioDesc CreateReader()
        {
            IAudioReader<float> mic = null;
            try
            {
                var rec = applied ?? SafeRecorder();
                if (rec != null && UnityMicrophone.devices != null && UnityMicrophone.devices.Length > 0)
                {
                    string device = rec.UnityMicrophoneDevice;
                    if (!string.IsNullOrEmpty(device) && Array.IndexOf(UnityMicrophone.devices, device) < 0) device = null;
                    int rate = (int)rec.SamplingRate;
                    UnityMicrophone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
                    if (rate < minFreq || (maxFreq != 0 && rate > maxFreq)) rate = maxFreq;
                    if (rate <= 0) rate = 48000;
                    var logger = new Photon.Voice.Unity.Logger();
                    mic = rec is GTRecorder gt
                        ? new GTMicWrapper(device, rate, gt.AllowPitchAdjustment, gt.PitchAdjustment, gt.AllowVolumeAdjustment, gt.VolumeAdjustment, logger)
                        : new MicWrapper(device, rate, logger);
                    if (mic.Error != null)
                    {
                        Plugin.Log.LogWarning("Microphone unavailable for the announcer mix: " + mic.Error);
                        mic.Dispose();
                        mic = null;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not open the microphone for the announcer mix: " + e.Message);
                mic = null;
            }
            micInfo = mic == null ? " (no microphone found - sending announcer only)" : "";
            var reader = new MixReader(mic, feed);
            Log?.Add("--", mic == null
                ? "Voice: announcer mixer created without a microphone (silence + announcer)."
                : $"Voice: announcer mixer created - mic {reader.SamplingRate} Hz, {reader.Channels} channel(s).", Colors.Dim);
            return reader;
        }

        public void Play(AudioClip clip, float volume)
        {
            if (clip == null) return;
            busyUntil = Time.unscaledTime + clip.length + 0.2f;
            feed.Push(clip, volume);
        }

        public void Stop()
        {
            feed.Clear();
            busyUntil = 0f;
        }

        public void Restore()
        {
            var rec = applied;
            applied = null;
            Live = false;
            feed.Clear();
            VoiceGuard.Release();
            if (rec == null) return;
            try
            {
                if (vadSuppressed && savedVad) rec.VoiceDetection = true;
                vadSuppressed = false;
                rec.SourceType = Recorder.InputSourceType.Microphone;
                rec.InputFactory = null;
                rec.RestartRecording(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not restore the microphone: " + e);
            }
        }
    }

    internal sealed class MixReader : IAudioReader<float>
    {
        private const double MicStallSeconds = 0.5;

        private readonly IAudioReader<float> mic;
        private readonly AnnouncerFeed feed;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastMicAt;
        private long produced;

        public MixReader(IAudioReader<float> mic, AnnouncerFeed feed)
        {
            this.mic = mic;
            this.feed = feed;
            SamplingRate = mic != null ? mic.SamplingRate : 48000;
            Channels = mic != null ? Math.Max(1, mic.Channels) : 1;
            MicStalled = false;
        }

        public int SamplingRate { get; }
        public int Channels { get; }
        public string Error => null;

        public static long FramesRead;

        public static volatile bool MicStalled;

        public bool Read(float[] buffer)
        {
            bool ok = ReadInner(buffer);
            if (ok) FramesRead++;
            return ok;
        }

        private bool ReadInner(float[] buffer)
        {
            double now = clock.Elapsed.TotalSeconds;
            bool fromMic = false;
            if (mic != null)
            {
                try { fromMic = mic.Read(buffer); }
                catch { fromMic = false; }
            }

            if (fromMic)
            {
                lastMicAt = now;
                produced = (long)(now * SamplingRate * Channels);
                MicStalled = false;
            }
            else
            {
                if (mic != null && now - lastMicAt < MicStallSeconds) return false;
                if (mic != null) MicStalled = true;
                double due = now * SamplingRate * Channels;
                if (produced + buffer.Length > due) return false;
                Array.Clear(buffer, 0, buffer.Length);
                produced += buffer.Length;
            }
            feed.MixInto(buffer, SamplingRate, Channels);
            return true;
        }

        public void Dispose()
        {
            try { mic?.Dispose(); } catch { }
        }
    }

    internal sealed class AnnouncerFeed
    {
        private sealed class Segment
        {
            public float[] Samples;
            public int Rate;
            public float Gain;
            public double Pos;
        }

        private readonly object gate = new object();
        private readonly Queue<Segment> queue = new Queue<Segment>();

        public bool IsPlaying
        {
            get { lock (gate) return queue.Count > 0; }
        }

        public void Push(AudioClip clip, float volume)
        {
            if (clip == null) return;
            int ch = Math.Max(1, clip.channels);
            var raw = new float[clip.samples * ch];
            clip.GetData(raw, 0);
            var mono = new float[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float s = 0f;
                for (int c = 0; c < ch; c++) s += raw[i * ch + c];
                mono[i] = s / ch;
            }
            lock (gate) queue.Enqueue(new Segment { Samples = mono, Rate = clip.frequency, Gain = Mathf.Clamp01(volume) });
        }

        public void Clear()
        {
            lock (gate) queue.Clear();
        }

        public void MixInto(float[] buffer, int rate, int channels)
        {
            lock (gate)
            {
                if (queue.Count == 0) return;
                int frames = buffer.Length / channels;
                for (int f = 0; f < frames; f++)
                {
                    if (queue.Count == 0) break;
                    var seg = queue.Peek();
                    int i0 = (int)seg.Pos;
                    if (i0 >= seg.Samples.Length - 1)
                    {
                        queue.Dequeue();
                        f--;
                        continue;
                    }
                    float t = (float)(seg.Pos - i0);
                    float v = (seg.Samples[i0] + (seg.Samples[i0 + 1] - seg.Samples[i0]) * t) * seg.Gain;
                    for (int c = 0; c < channels; c++)
                    {
                        int k = f * channels + c;
                        buffer[k] = Mathf.Clamp(buffer[k] * 0.35f + v, -1f, 1f);
                    }
                    seg.Pos += (double)seg.Rate / rate;
                }
            }
        }
    }
}
