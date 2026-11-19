using System;
using System.Collections.Generic;
using System.Threading;
using Photon.Voice;
using Photon.Voice.Unity;
using POpusCodec;
using UnityEngine;

using OpusChannels = POpusCodec.Enums.Channels;
using OpusRate = POpusCodec.Enums.SamplingRate;
using OpusApp = POpusCodec.Enums.OpusApplicationType;
using OpusDelay = POpusCodec.Enums.Delay;

namespace Devicular
{
    /// <summary>
    /// Sound for live spectating and replays, compressed with the Opus encoder the game already ships and handed out as
    /// 20 ms packets the moment each one is ready. <see cref="SoundHub"/> gives every packet to the live stream and to
    /// the replay recorder, each with its own time, so a replay's sound is exactly what a live viewer hears.
    ///
    /// 3D (the default): every game sound and every player's voice is sent on its own with its position, so the page
    /// can place it around the viewer's own camera (<see cref="SoundSources"/> for game sounds, voice chat for voices).
    /// Nothing about how the caster hears the game is changed.
    ///
    /// Mix ([Live] 3D sound = false): one recording of what the caster hears, voices included.
    ///
    /// The caster's mic is sent as a flat commentary track either way.
    /// </summary>
    internal sealed class SoundCapture
    {
        public const byte TrackGame = 0, TrackMic = 1, TrackVoice = 2;
        private const byte MsgAudio = 6, MsgSoundInfo = 7;

        private readonly SoundHub hub;
        private readonly SoundSources sources;
        private readonly AudioFeed game = new AudioFeed(TrackGame);
        private readonly AudioFeed mic = new AudioFeed(TrackMic);
        private readonly MicTap micTap;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private volatile int generation;
        private volatile int gameRate = 48000;
        private volatile bool threeD = true;
        private bool running, lastWantMix, lastWantMic, lastWant3D;
        private float nextCheck;
        private LocalVoice attachedVoice;
        private MixTap activeTap;
        private int gamePackets, micPackets, voicePackets, sourcePackets;

        // Each player's voice, by actor number. The main thread adds and removes them; the sound thread encodes them.
        private readonly object voiceGate = new object();
        private readonly Dictionary<int, PlayerVoice> voices = new Dictionary<int, PlayerVoice>();
        private PlayerVoice[] voiceList = new PlayerVoice[0];
        private readonly HashSet<AudioSource> voiceSources = new HashSet<AudioSource>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<PlayerVoice> retired = new System.Collections.Concurrent.ConcurrentQueue<PlayerVoice>();
        private string sentSoundInfo;

        private static readonly HarmonyLib.AccessTools.FieldRef<VRRig, AudioSource> VoiceAudio =
            HarmonyLib.AccessTools.FieldRefAccess<VRRig, AudioSource>("voiceAudio");

        private static readonly System.Reflection.MethodInfo LinkGetter =
            HarmonyLib.AccessTools.PropertyGetter(typeof(Speaker), "RemoteVoiceLink");

        private static RemoteVoiceLink LinkOf(Speaker speaker) =>
            LinkGetter != null ? LinkGetter.Invoke(speaker, null) as RemoteVoiceLink : null;

        public int GamePerSecond { get; private set; }
        public int MicPerSecond { get; private set; }
        public int VoicesPerSecond { get; private set; }
        public int SoundsPerSecond { get; private set; }
        public int VoiceCount { get; private set; }
        public string GameStatus { get; private set; } = "";
        public string MicStatus { get; private set; } = "";
        public volatile string Problem = "";

        private readonly Func<bool> threeDSetting, micSetting;

        /// <param name="threeD">The mod's "3D sound" setting: each sound with its position, or one mix of what you hear.
        /// With 3D sound, sounds that aren't anywhere in the world (menus, music, other mods' sounds like the Auto Ref
        /// announcer) are never taken: the viewer page only plays sounds from the world.</param>
        /// <param name="mic">Whether the mod sends or records your mic (only used to describe what's happening).</param>
        public SoundCapture(SoundHub hub, Func<bool> threeD, Func<bool> mic)
        {
            this.hub = hub;
            threeDSetting = threeD;
            micSetting = mic;
            micTap = new MicTap(this);
            sources = new SoundSources(hub, () => wake.Set());
        }

        /// <summary>Main thread, every frame. Sound is captured while the live stream or a replay recording wants it.</summary>
        public void Tick()
        {
            bool active = hub.Open;
            bool setting3D = threeDSetting();
            bool want3D = active && setting3D;
            bool wantMix = active && !setting3D;
            bool wantMic = active && hub.WantsMic;
            threeD = setting3D;
            if ((want3D || wantMix || wantMic) && !running) Start();
            else if (!want3D && !wantMix && !wantMic && running) Stop();
            if (Time.unscaledTime >= nextCheck || want3D != lastWant3D || wantMix != lastWantMix || wantMic != lastWantMic)
            {
                nextCheck = Time.unscaledTime + 1f;
                lastWant3D = want3D;
                lastWantMix = wantMix;
                lastWantMic = wantMic;
                // Voices first, so the game sounds' scan below already knows which sources are voices.
                AttachVoices(want3D);
                AttachGame(wantMix);
                AttachMic(wantMic);
                GamePerSecond = Interlocked.Exchange(ref gamePackets, 0);
                MicPerSecond = Interlocked.Exchange(ref micPackets, 0);
                VoicesPerSecond = Interlocked.Exchange(ref voicePackets, 0);
                SoundsPerSecond = Interlocked.Exchange(ref sourcePackets, 0);
            }
            sources.Tick(want3D, voiceSources, false);
        }

        /// <summary>Clears what's buffered, for a new stream.</summary>
        public void ResetFeeds()
        {
            game.Reset();
            mic.Reset();
            sources.ResetFeeds();
            lock (voiceGate)
                foreach (var v in voiceList) v.Feed.Reset();
            sentSoundInfo = null;
        }

        public void Shutdown()
        {
            Stop();
            GameStatus = "";
            MicStatus = "";
        }

        private void Start()
        {
            ResetFeeds();
            Problem = "";
            running = true;
            int gen = ++generation;
            new Thread(() => Run(gen)) { IsBackground = true, Name = Mod.Name + " sound" }.Start();
        }

        private void Stop()
        {
            AttachVoices(false);
            sources.Stop();
            if (!running) return;
            running = false;
            generation++;
            wake.Set();
            micTap.Active = false;
            if (activeTap != null) activeTap.Active = false;
            ResetFeeds();
        }

        // ---------- each player's voice ----------

        private sealed class PlayerVoice
        {
            public int Actor;
            public AudioFeed Feed;
            public AudioSource Source;
            public Speaker Speaker;
            public RemoteVoiceLink Link;
            public Action<FrameOut<float>> Handler;
            public volatile bool Active, Released;
            public volatile int Rate = 48000, Channels = 1, FrameUs = 20000;
            // Sound thread only:
            public OpusEncoder Encoder;
            public int EncRate, EncChannels;
            public float[] Frame;
        }

        /// <summary>
        /// Keeps one entry per remote player whose voice the caster can hear. Each copies the player's decoded voice
        /// as it arrives from voice chat. Game sounds are taken from each AudioSource instead of from what the caster
        /// hears, so voices are never in them and nobody is heard twice.
        /// </summary>
        private void AttachVoices(bool want)
        {
            var seen = new HashSet<int>();
            voiceSources.Clear();
            if (want)
            {
                foreach (var p in Roster.Players)
                {
                    if (p.Rig == null || p.Ghost) continue;
                    AudioSource source = null;
                    try { source = VoiceAudio(p.Rig); } catch { }
                    if (source == null) continue;
                    voiceSources.Add(source);
                    var speaker = source.GetComponent<Speaker>();
                    if (speaker == null) continue;
                    seen.Add(p.Actor);
                    PlayerVoice v;
                    lock (voiceGate) voices.TryGetValue(p.Actor, out v);
                    if (v != null && v.Source != source)
                    {
                        Release(v);
                        v = null;
                    }
                    if (v == null)
                    {
                        v = new PlayerVoice { Actor = p.Actor, Feed = new AudioFeed(TrackVoice, p.Actor, 0.3), Source = source, Speaker = speaker };
                        var pv = v;
                        v.Handler = frame =>
                        {
                            if (!pv.Active || !hub.Open) return;
                            var buf = frame.Buf;
                            if (buf == null || buf.Length == 0) return;
                            int rate = VoiceRate.Of(buf.Length, pv.Channels, pv.FrameUs, pv.Rate, "players' voice");
                            pv.Feed.Push(buf, buf.Length, rate, pv.Channels, SoundHub.Now);
                            wake.Set();
                        };
                        lock (voiceGate) voices[p.Actor] = v;
                    }
                    RemoteVoiceLink link = null;
                    try { link = LinkOf(speaker); } catch { }
                    if (!ReferenceEquals(link, v.Link))
                    {
                        if (v.Link != null) v.Link.FloatFrameDecoded -= v.Handler;
                        v.Link = link;
                        if (link != null)
                        {
                            v.Rate = link.Info.SamplingRate;
                            v.Channels = Math.Max(1, link.Info.Channels);
                            v.FrameUs = link.Info.FrameDurationUs > 0 ? link.Info.FrameDurationUs : 20000;
                            link.FloatFrameDecoded += v.Handler;
                        }
                    }
                    // Players the caster muted (or all of them, with voice chat off) have their speaker switched off.
                    v.Active = link != null && speaker.isActiveAndEnabled && !source.mute;
                }
            }
            PlayerVoice[] old;
            lock (voiceGate) old = new List<PlayerVoice>(voices.Values).ToArray();
            foreach (var v in old)
                if (!seen.Contains(v.Actor)) Release(v);
            lock (voiceGate)
            {
                voiceList = new List<PlayerVoice>(voices.Values).ToArray();
                VoiceCount = 0;
                foreach (var v in voiceList)
                    if (v.Active) VoiceCount++;
            }
            if (want) SendSoundInfo();
        }

        private void Release(PlayerVoice v)
        {
            v.Active = false;
            v.Released = true;
            retired.Enqueue(v);
            try { if (v.Link != null) v.Link.FloatFrameDecoded -= v.Handler; } catch { }
            v.Link = null;
            lock (voiceGate) voices.Remove(v.Actor);
        }

        /// <summary>
        /// Tells viewers how loud a voice is at each distance, read from the game's own voice AudioSource, so voices
        /// fade with distance in the browser exactly as they do in the game.
        /// </summary>
        private void SendSoundInfo()
        {
            AudioSource src = null;
            lock (voiceGate)
                foreach (var v in voiceList)
                    if (v.Source != null) { src = v.Source; break; }
            if (src == null) return;
            string json = "{\"voice\":" + SoundTable.Json(src) + "}";
            if (json == sentSoundInfo) return;
            sentSoundInfo = json;
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var msg = new byte[1 + bytes.Length];
            msg[0] = MsgSoundInfo;
            Buffer.BlockCopy(bytes, 0, msg, 1, bytes.Length);
            hub.SoundInfo(msg);
        }

        // ---------- mix mode: what the caster hears ----------

        private void AttachGame(bool want)
        {
            gameRate = AudioSettings.outputSampleRate;
            AudioListener listener = null;
            if (want)
            {
                foreach (var l in UnityEngine.Object.FindObjectsOfType<AudioListener>())
                {
                    if (l == null || !l.isActiveAndEnabled) continue;
                    listener = l;
                    break;
                }
            }
            MixTap tap = null;
            if (listener != null)
            {
                tap = listener.GetComponent<MixTap>();
                if (tap == null) tap = listener.gameObject.AddComponent<MixTap>();
                tap.Owner = this;
            }
            if (activeTap != null && activeTap != tap)
            {
                // Take it off the listener, so it isn't left filtering everything you hear.
                activeTap.Active = false;
                try { UnityEngine.Object.Destroy(activeTap); } catch { }
            }
            activeTap = tap;
            if (tap != null) tap.Active = true;
            GameStatus = !want ? "" : listener == null ? "No audio listener found yet." : $"from '{listener.name}', {gameRate / 1000f:0.#} kHz";
        }

        internal void PushGame(float[] data, int channels)
        {
            if (!hub.Open) return;
            game.Push(data, data.Length, gameRate, channels, SoundHub.Now);
            wake.Set();
        }

        // ---------- the caster's mic ----------

        private static Recorder FindRecorder()
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

        private void AttachMic(bool want)
        {
            if (!want)
            {
                micTap.Active = false;
                MicStatus = "";
                return;
            }
            var rec = FindRecorder();
            LocalVoice voice = null;
            try { voice = rec != null ? rec.Voice : null; } catch { }
            if (voice == null)
            {
                micTap.Active = false;
                MicStatus = rec == null ? "no voice chat yet" : "the game isn't recording your mic (is voice chat on?)";
                return;
            }
            if (!ReferenceEquals(voice, attachedVoice))
            {
                try
                {
                    var info = voice.Info;
                    micTap.Rate = info.SamplingRate;
                    micTap.Channels = Math.Max(1, info.Channels);
                    micTap.FrameUs = info.FrameDurationUs > 0 ? info.FrameDurationUs : 20000;
                    // A pre-processor sees every mic frame, before the game decides whether to transmit it, so
                    // commentary reaches viewers even when players in the room can't hear you.
                    if (voice is LocalVoiceFramed<float> vf) vf.AddPreProcessor(micTap);
                    else if (voice is LocalVoiceFramed<short> vs) vs.AddPreProcessor(micTap);
                    else
                    {
                        micTap.Active = false;
                        MicStatus = "your mic uses a format that can't be read";
                        return;
                    }
                    attachedVoice = voice;
                }
                catch (Exception e)
                {
                    micTap.Active = false;
                    MicStatus = "couldn't read your mic: " + e.Message;
                    return;
                }
            }
            micTap.Active = true;
            bool recording = false;
            try { recording = rec.IsRecording; } catch { }
            MicStatus = recording ? $"{micTap.Rate / 1000f:0.#} kHz" : "the game isn't recording your mic right now";
        }

        internal void PushMic(float[] data, int samples, int rate, int channels)
        {
            if (!hub.Open) return;
            mic.Push(data, samples, rate, channels, SoundHub.Now);
            wake.Set();
        }

        // ---------- encoding ----------

        private void Run(int gen)
        {
            OpusEncoder gameEnc = null, ambEnc = null, micEnc = null;
            int gameR = 0, gameC = 0, ambR = 0, ambC = 0, micR = 0, micC = 0;
            float[] gameFrame = null, ambFrame = null, micFrame = null;
            var encoded = new HashSet<SourceTap>();
            long nextSweep = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (gen == generation)
                {
                    bool did = false, eventsPending = false;
                    if (threeD)
                    {
                        sources.FlushStaleAmbience();
                        if (Pump(sources.Ambience, ref ambEnc, ref ambR, ref ambC, ref ambFrame, OpusApp.RestrictedLowDelay, 96000, true))
                        {
                            did = true;
                            Interlocked.Increment(ref gamePackets);
                        }
                        foreach (var t in sources.Hot)
                        {
                            if (t == null) continue;
                            if (t.EventDriven && t.Events != null)
                            {
                                if (t.Events.Pending) eventsPending = true;
                                while (PumpEvents(t))
                                {
                                    did = true;
                                    encoded.Add(t);
                                    Interlocked.Increment(ref sourcePackets);
                                }
                            }
                            else if (t.Feed != null && Pump(t.Feed, ref t.Encoder, ref t.EncRate, ref t.EncChannels, ref t.Frame, OpusApp.RestrictedLowDelay, 40000, true))
                            {
                                did = true;
                                encoded.Add(t);
                                Interlocked.Increment(ref sourcePackets);
                            }
                        }
                        // Free the encoders of sounds that have gone quiet.
                        if (clock.ElapsedMilliseconds >= nextSweep)
                        {
                            nextSweep = clock.ElapsedMilliseconds + 2000;
                            var hotNow = new HashSet<SourceTap>(sources.Hot);
                            encoded.RemoveWhere(t =>
                            {
                                if (t != null && hotNow.Contains(t)) return false;
                                if (t != null)
                                {
                                    try { t.Encoder?.Dispose(); } catch { }
                                    t.Encoder = null;
                                }
                                return true;
                            });
                        }
                    }
                    else if (Pump(game, ref gameEnc, ref gameR, ref gameC, ref gameFrame, OpusApp.RestrictedLowDelay, 96000))
                    {
                        did = true;
                        Interlocked.Increment(ref gamePackets);
                    }
                    if (Pump(mic, ref micEnc, ref micR, ref micC, ref micFrame, OpusApp.Voip, 40000))
                    {
                        did = true;
                        Interlocked.Increment(ref micPackets);
                    }
                    while (retired.TryDequeue(out var gone))
                    {
                        try { gone.Encoder?.Dispose(); } catch { }
                        gone.Encoder = null;
                    }
                    PlayerVoice[] list;
                    lock (voiceGate) list = voiceList;
                    foreach (var v in list)
                    {
                        if (v.Released) continue;
                        if (Pump(v.Feed, ref v.Encoder, ref v.EncRate, ref v.EncChannels, ref v.Frame, OpusApp.Voip, 32000, true))
                        {
                            did = true;
                            Interlocked.Increment(ref voicePackets);
                        }
                    }
                    // Rebuilt one-shots go out as each 20 ms passes, so check often while any are playing.
                    if (!did) wake.WaitOne(eventsPending ? 5 : 30);
                }
            }
            catch (Exception e)
            {
                Problem = e.Message;
                Mod.Log?.LogWarning("Sound capture stopped: " + e);
            }
            finally
            {
                try { gameEnc?.Dispose(); } catch { }
                try { ambEnc?.Dispose(); } catch { }
                try { micEnc?.Dispose(); } catch { }
                foreach (var t in encoded)
                {
                    if (t == null) continue;
                    try { t.Encoder?.Dispose(); } catch { }
                    t.Encoder = null;
                }
                PlayerVoice[] list;
                lock (voiceGate) list = voiceList;
                foreach (var v in list)
                {
                    try { v.Encoder?.Dispose(); } catch { }
                    v.Encoder = null;
                }
            }
        }

        private bool Pump(AudioFeed feed, ref OpusEncoder enc, ref int encRate, ref int encChannels, ref float[] frame, OpusApp app, int bitrate, bool skipSilence = false)
        {
            if (!feed.Format(out int rate, out int channels)) return false;
            if (enc == null || rate != encRate || channels != encChannels)
            {
                try { enc?.Dispose(); } catch { }
                enc = new OpusEncoder((OpusRate)rate, channels == 2 ? OpusChannels.Stereo : OpusChannels.Mono, bitrate, app, OpusDelay.Delay20ms);
                encRate = rate;
                encChannels = channels;
                frame = new float[enc.FrameSizePerChannel * channels];
            }
            if (!feed.TryPop(frame, channels, out double t)) return false;
            if (skipSilence)
            {
                // Quiet stretches (a player not talking, a sound that has finished) aren't worth sending.
                float peak = 0f;
                for (int i = 0; i < frame.Length; i++)
                {
                    float a = frame[i] < 0 ? -frame[i] : frame[i];
                    if (a > peak) peak = a;
                }
                if (peak < 0.002f) return true;
            }
            var packet = enc.Encode(frame);
            if (packet.Count > 0) hub.Audio(Message(feed.Track, channels, feed.Actor, packet), t);
            return true;
        }

        /// <summary>Encodes the next 20 ms of a source rebuilt from its one-shot clips, once that time has passed.</summary>
        private bool PumpEvents(SourceTap t)
        {
            if (t.Encoder == null || t.EncRate != EventTrack.Rate || t.EncChannels != 1 || t.Frame == null)
            {
                try { t.Encoder?.Dispose(); } catch { }
                t.Encoder = new OpusEncoder(OpusRate.Sampling48000, OpusChannels.Mono, 40000, OpusApp.RestrictedLowDelay, OpusDelay.Delay20ms);
                t.EncRate = EventTrack.Rate;
                t.EncChannels = 1;
                t.Frame = new float[t.Encoder.FrameSizePerChannel];
            }
            if (!t.Events.TryPop(t.Frame, SoundHub.Now, out double ts)) return false;
            float peak = 0f;
            foreach (var s in t.Frame)
            {
                float a = s < 0 ? -s : s;
                if (a > peak) peak = a;
            }
            if (peak < 0.002f) return true;
            var packet = t.Encoder.Encode(t.Frame);
            if (packet.Count > 0) hub.Audio(Message(SoundSources.TrackSource, 1, t.Id, packet), ts);
            return true;
        }

        /// <summary>[6][track][channels][0][i32 id][f32 time][opus]. The hub writes each listener's own time in.</summary>
        private static byte[] Message(byte track, int channels, int actor, ArraySegment<byte> opus)
        {
            var m = new byte[12 + opus.Count];
            m[0] = MsgAudio;
            m[1] = track;
            m[2] = (byte)channels;
            BitConverter.TryWriteBytes(new Span<byte>(m, 4, 4), actor);
            Buffer.BlockCopy(opus.Array, opus.Offset, m, 12, opus.Count);
            return m;
        }

        public string Describe()
        {
            if (!string.IsNullOrEmpty(Problem)) return "Sound stopped: " + Problem;
            string m = micSetting() ? (MicPerSecond > 0 ? $"mic {MicStatus}" : $"mic: {(string.IsNullOrEmpty(MicStatus) ? "starting" : MicStatus)}") : "mic off";
            if (!threeD)
            {
                string g = GamePerSecond > 0 ? $"game sound {GameStatus}" : $"game sound: {(string.IsNullOrEmpty(GameStatus) ? "starting" : GameStatus)}";
                return "Sound (mix of what you hear): " + g + "  ·  " + m;
            }
            string s = $"3D sound: {sources.HotCount} playing of {sources.TapCount} sources" + (SoundsPerSecond > 0 ? $", {SoundsPerSecond} packets/s" : "") +
                       (sources.OneShotsPerSecond > 0 ? $", {sources.OneShotsPerSecond} taps/s" : "");
            string v = $"voices: {VoiceCount} player{(VoiceCount == 1 ? "" : "s")}" + (VoicesPerSecond > 0 ? $", {VoicesPerSecond} packets/s" : ", nobody talking");
            return s + "  ·  " + v + "  ·  " + m;
        }
    }

    /// <summary>Mix mode only: sits on the active AudioListener and copies what the caster hears (nothing is changed).</summary>
    internal sealed class MixTap : MonoBehaviour
    {
        public volatile SoundCapture Owner;
        public volatile bool Active;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Active) return;
            Owner?.PushGame(data, channels);
        }
    }

    /// <summary>A Photon Voice processor that copies the caster's mic frames and passes them on unchanged.</summary>
    internal sealed class MicTap : IProcessor<float>, IProcessor<short>
    {
        private readonly SoundCapture owner;
        private float[] converted = new float[0];
        public volatile bool Active;
        public volatile int Rate = 48000, Channels = 1, FrameUs = 20000;

        public MicTap(SoundCapture owner)
        {
            this.owner = owner;
        }

        public float[] Process(float[] buf)
        {
            if (Active && buf != null) owner.PushMic(buf, buf.Length, VoiceRate.Of(buf.Length, Channels, FrameUs, Rate, "your mic"), Channels);
            return buf;
        }

        public short[] Process(short[] buf)
        {
            if (Active && buf != null)
            {
                if (converted.Length < buf.Length) converted = new float[buf.Length];
                for (int i = 0; i < buf.Length; i++) converted[i] = buf[i] / 32768f;
                owner.PushMic(converted, buf.Length, VoiceRate.Of(buf.Length, Channels, FrameUs, Rate, "your mic"), Channels);
            }
            return buf;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The real sample rate of a Photon Voice frame. A voice's settings give the rate it's encoded at, but frames can
    /// reach processors and decoders at another rate (for example the mic's own 48 kHz while the voice is sent at
    /// 24 kHz). Every frame lasts the voice's frame duration, so its length gives the true rate.
    /// </summary>
    internal static class VoiceRate
    {
        private static readonly int[] Rates = { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> logged = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

        public static int Of(int samples, int channels, int frameUs, int stated, string what)
        {
            if (samples <= 0 || frameUs <= 0) return stated;
            double r = samples / (double)Math.Max(1, channels) * 1e6 / frameUs;
            int best = stated;
            double bestErr = double.MaxValue;
            foreach (int c in Rates)
            {
                double err = Math.Abs(c - r) / c;
                if (err < bestErr) { bestErr = err; best = c; }
            }
            if (bestErr > 0.03) return stated;
            if (best != stated && logged.TryAdd(what + best, 0))
                Mod.Log?.LogInfo($"Sound: {what} frames are {best} Hz (the voice settings say {stated} Hz).");
            return best;
        }
    }
}
