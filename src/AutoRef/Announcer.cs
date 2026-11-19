using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoRef
{
    internal enum Priority { Normal, Critical }

    internal sealed class Announcer : MonoBehaviour
    {
        public static readonly Dictionary<string, string> BuiltIn = new Dictionary<string, string>
        {
            { "3", "3." }, { "2", "2." }, { "1", "1." },
            { "runners_run", "Runners, run!" },
            { "taggers_tag", "Taggers, tag!" },
            { "time_up", "Time is up!" },
            { "all_tagged", "All runners have been tagged!" },
            { "round_complete", "Round complete." },
            { "teams_switching", "Teams are switching." },
            { "prepare_next", "Prepare for the next round." },
            { "return_gazebo", "All players, return to Gazebo." },
            { "waiting_positions", "Waiting for all players to get into position." },
            { "round_reset", "Round reset. Everyone, return to Gazebo." },
            { "matching_colors", "Players, please put on matching color codes for your team." },
            { "rejoin_host", "The referee isn't the host. Everyone else, please leave and rejoin the room." },
            { "team_a_taggers", "Team A is now Taggers." },
            { "team_a_runners", "Team A is now Runners." },
            { "team_b_taggers", "Team B is now Taggers." },
            { "team_b_runners", "Team B is now Runners." },
        };

        private sealed class Item
        {
            public string Text;
            public string Key;
            public string FallbackKey;
            public Priority Priority;
            public AudioClip Clip;
            public float QueuedAt;
        }

        public readonly TtsService Tts = new TtsService();
        public readonly VoiceBroadcaster Voice = new VoiceBroadcaster();

        private readonly Dictionary<string, AudioClip> builtInClips = new Dictionary<string, AudioClip>();
        private readonly LinkedList<Item> queue = new LinkedList<Item>();
        private AudioSource voiceSource, fxSource;
        private AudioClip beepClip, tagSoundClip, alertClip;
        private Item current, last;
        private float currentEnd;
        private GameReader game;

        public string NowPlaying => current != null && Time.unscaledTime < currentEnd ? current.Text : null;
        public int QueueLength => queue.Count;
        public string LastText => last?.Text;

        private EventLog log;
        private bool audioChecked;

        public void Init(GameReader reader, EventLog eventLog)
        {
            game = reader;
            log = eventLog;
            Voice.Log = eventLog;
        }

        private void Awake()
        {
            foreach (var key in BuiltIn.Keys)
            {
                var clip = Wav.LoadResource(key + ".wav");
                if (clip != null) builtInClips[key] = clip;
            }
            beepClip = Wav.Tone("beep", 880f, 0.16f, 0.5f);
            tagSoundClip = Wav.Tone("tag", 520f, 0.12f, 0.45f, 2);
            alertClip = Wav.Tone("alert", 300f, 0.25f, 0.55f, 2);
        }

        private AudioSource LocalSource(ref AudioSource source, string name)
        {
            if (source == null)
            {
                var go = new GameObject(name);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
            }
            return source;
        }

        private AudioSource VoiceSource => LocalSource(ref voiceSource, "AutoRef Announcer Audio");
        private AudioSource FxSource => LocalSource(ref fxSource, "AutoRef Fx Audio");

        public void TestLocalSound()
        {
            var src = VoiceSource;
            src.Stop();
            src.clip = builtInClips.TryGetValue("runners_run", out var c) ? c : beepClip;
            src.volume = Mathf.Max(0.5f, Settings.LocalVolume.Value);
            src.Play();
            var listener = FindObjectOfType<AudioListener>();
            log?.Add("--", $"Local sound test: playing {src.isPlaying}, volume {src.volume:0.00}, listener " +
                           (listener != null ? $"'{listener.name}' enabled {listener.enabled} active {listener.gameObject.activeInHierarchy}" : "NOT FOUND") +
                           $", master volume {AudioListener.volume:0.00}, paused {AudioListener.pause}, output {AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}.",
                Colors.Info);
        }

        public void Say(string text, string key = null, Priority priority = Priority.Normal, string fallbackKey = null)
        {
            if (!Settings.RefereeOn) return;
            if (!Settings.AnnouncerEnabled.Value && !Settings.PlayLocally.Value) return;
            if (key == null) key = KeyFor(text);
            var item = new Item { Text = text, Key = key, FallbackKey = fallbackKey, Priority = priority, QueuedAt = Time.unscaledTime };
            if (key != null && builtInClips.TryGetValue(key, out var clip)) item.Clip = clip;
            else Tts.Request(text);

            if (priority == Priority.Critical)
            {
                StopCurrent();
                queue.AddFirst(item);
                Advance();
            }
            else
            {
                queue.AddLast(item);
            }
        }

        public void Prewarm(string text)
        {
            if (KeyFor(text) == null) Tts.Request(text);
        }

        public void RepeatLast()
        {
            if (last != null) Say(last.Text, last.Key);
        }

        public void StopAll()
        {
            queue.Clear();
            StopCurrent();
        }

        private void StopCurrent()
        {
            if (voiceSource != null) voiceSource.Stop();
            Voice.Stop();
            current = null;
            currentEnd = 0f;
        }

        private static string KeyFor(string text)
        {
            foreach (var kv in BuiltIn)
                if (string.Equals(kv.Value, text, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return null;
        }

        private void Update()
        {
            Tts.Update();
            if (!Settings.RefereeOn && (queue.Count > 0 || current != null)) StopAll();
            if (game != null) Voice.Update(Settings.AnnouncerEnabled.Value && Settings.RefereeOn, game);
            Advance();
        }

        private void Advance()
        {
            if (current != null && Time.unscaledTime < currentEnd) return;
            current = null;
            while (queue.Count > 0)
            {
                var item = queue.First.Value;
                if (item.Clip == null)
                {
                    if (Tts.TryGet(item.Text, out var clip)) item.Clip = clip;
                    else if (Tts.HasFailed(item.Text) || Time.unscaledTime - item.QueuedAt > 8f)
                    {
                        if (item.FallbackKey != null && builtInClips.TryGetValue(item.FallbackKey, out var fallback))
                        {
                            item.Clip = fallback;
                            item.Text = BuiltIn[item.FallbackKey];
                        }
                        else
                        {
                            queue.RemoveFirst();
                            Plugin.Log.LogWarning("Announcer skipped (no audio): " + item.Text);
                            continue;
                        }
                    }
                    else return;
                }
                queue.RemoveFirst();
                Play(item);
                return;
            }
        }

        private void Play(Item item)
        {
            current = last = item;
            currentEnd = Time.unscaledTime + item.Clip.length + 0.15f;
            AudioSource local = null;
            if (Settings.PlayLocally.Value)
            {
                local = VoiceSource;
                local.Stop();
                local.clip = item.Clip;
                local.volume = Mathf.Clamp01(Settings.LocalVolume.Value);
                local.Play();
            }
            bool sent = Settings.AnnouncerEnabled.Value && Voice.Live;
            if (sent) Voice.Play(item.Clip, Settings.AnnouncerVolume.Value);

            if (!audioChecked)
            {
                audioChecked = true;
                var listener = FindObjectOfType<AudioListener>();
                log?.Add("--", $"Audio check: listener {(listener != null ? $"'{listener.name}' enabled {listener.enabled}" : "NOT FOUND")}, " +
                               $"master volume {AudioListener.volume:0.00}, paused {AudioListener.pause}, output {AudioSettings.outputSampleRate} Hz, " +
                               $"clip {item.Clip.frequency} Hz {item.Clip.length:0.0}s.", Colors.Dim);
            }
            string you = local == null ? "you: off"
                : local.isPlaying ? $"you: playing at {local.volume * 100f:0}%" : "you: FAILED to play";
            string players = !Settings.AnnouncerEnabled.Value ? "players: announcer off"
                : !sent ? "players: NOT sent - " + Voice.Status
                : Voice.Transmitting ? "players: sent" : "players: sent, but your mic isn't transmitting";
            log?.Add("--", $"Announcer: \"{item.Text}\"  [{you} | {players}]", sent && Voice.Transmitting ? Colors.Dim : Colors.Warn);
        }

        public void Beep() => PlayFx(beepClip);

        public void TagSound() => PlayFx(tagSoundClip);

        /// <summary>The alert sound (a pause, a false start, a sub timing out). All-in-One: no banner on screen with it.</summary>
        public void Alert() => PlayFx(alertClip);

        private void PlayFx(AudioClip clip)
        {
            if (clip == null || !Settings.CountdownBeeps.Value || !Settings.RefereeOn) return;
            FxSource.PlayOneShot(clip, Mathf.Clamp01(Settings.LocalVolume.Value));
        }

        private void OnDestroy()
        {
            Voice.Restore();
            Tts.Shutdown();
            if (voiceSource != null) Destroy(voiceSource.gameObject);
            if (fxSource != null) Destroy(fxSource.gameObject);
        }

        private void OnApplicationQuit()
        {
            Voice.Restore();
            Tts.Shutdown();
        }
    }

    internal static class Wav
    {
        public static AudioClip LoadResource(string fileName)
        {
            try
            {
                var asm = typeof(Wav).Assembly;
                using (var s = asm.GetManifestResourceStream("AutoRef.Sounds." + fileName))
                {
                    if (s == null)
                    {
                        Plugin.Log.LogWarning("Missing embedded sound " + fileName);
                        return null;
                    }
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return FromWav(ms.ToArray(), Path.GetFileNameWithoutExtension(fileName));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not load sound {fileName}: {e.Message}");
                return null;
            }
        }

        public static AudioClip FromWav(byte[] d, string name)
        {
            if (d.Length < 44 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "WAVE")
                throw new InvalidDataException("not a WAV file");
            int pos = 12, channels = 1, rate = 22050, bits = 16, dataPos = -1, dataLen = 0;
            while (pos + 8 <= d.Length)
            {
                string id = Encoding.ASCII.GetString(d, pos, 4);
                int size = BitConverter.ToInt32(d, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(d, pos + 10);
                    rate = BitConverter.ToInt32(d, pos + 12);
                    bits = BitConverter.ToInt16(d, pos + 22);
                }
                else if (id == "data")
                {
                    dataPos = pos + 8;
                    dataLen = Math.Min(size, d.Length - dataPos);
                    break;
                }
                pos += 8 + size + (size & 1);
            }
            if (dataPos < 0 || bits != 16) throw new InvalidDataException("only 16-bit PCM WAV is supported");

            int count = dataLen / 2;
            if (count < 2) throw new InvalidDataException("empty WAV");
            var samples = new float[count];
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                float v = BitConverter.ToInt16(d, dataPos + i * 2) / 32768f;
                samples[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            if (peak > 0.01f)
            {
                float gain = 0.95f / peak;
                for (int i = 0; i < count; i++) samples[i] *= gain;
            }
            var clip = AudioClip.Create(name, count / Math.Max(1, channels), Math.Max(1, channels), rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip Tone(string name, float freq, float seconds, float volume, int repeats = 1)
        {
            const int rate = 48000;
            int one = (int)(rate * seconds), gap = (int)(rate * 0.06f);
            int total = one * repeats + gap * (repeats - 1);
            var data = new float[total];
            for (int r = 0; r < repeats; r++)
            {
                int offset = r * (one + gap);
                for (int i = 0; i < one; i++)
                {
                    float env = Mathf.Min(1f, i / (rate * 0.008f)) * Mathf.Min(1f, (one - i) / (rate * 0.03f));
                    data[offset + i] = Mathf.Sin(2f * Mathf.PI * freq * i / rate) * volume * env;
                }
            }
            var clip = AudioClip.Create(name, total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
