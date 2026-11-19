using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

namespace AutoRef
{
    internal sealed class TtsService
    {
        private const string WorkerScript = @"
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
try { if ($args.Count -gt 0 -and $args[0]) { $s.SelectVoice($args[0]) } } catch { }
$dir = $args[1]
$s.Rate = 1
$s.Volume = 100
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(48000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
[Console]::Out.WriteLine('READY')
[Console]::Out.Flush()
while ($true) {
  $line = [Console]::In.ReadLine()
  if ($line -eq $null) { break }
  $parts = $line.Split([char]9, 2)
  if ($parts.Length -lt 2) { continue }
  $id = $parts[0]
  $path = Join-Path $dir ($id + '.wav')
  $tmp = $path + '.tmp'
  try {
    $s.SetOutputToWaveFile($tmp, $fmt)
    $s.Speak($parts[1])
    $s.SetOutputToNull()
    Move-Item -Force -LiteralPath $tmp -Destination $path
    [Console]::Out.WriteLine('OK' + [char]9 + $id)
  } catch {
    try { $s.SetOutputToNull() } catch { }
    [Console]::Out.WriteLine('ERR' + [char]9 + $id + [char]9 + $_.Exception.Message)
  }
  [Console]::Out.Flush()
}
";

        private const int MaxInFlight = 2;

        private readonly string folder = Path.Combine(Paths.BepInExRootPath, "AutoRef", "tts");
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, string> pendingById = new Dictionary<string, string>();
        private readonly HashSet<string> inFlight = new HashSet<string>();
        private readonly HashSet<string> failed = new HashSet<string>();
        private readonly ConcurrentQueue<string> results = new ConcurrentQueue<string>();
        private readonly Queue<string> backlog = new Queue<string>();
        private Process worker;
        private bool workerReady;
        private bool disabled;
        private string workerVoice;
        private float restartAfter;
        private float workerStartedAt;
        private int starts;
        private bool purged;

        public string Status = "Text-to-speech not started yet.";
        public bool Available => !disabled;
        public bool Ready => workerReady;
        public int PendingCount => pendingById.Count;

        public bool TryGet(string text, out AudioClip clip)
        {
            text = Clean(text);
            if (clips.TryGetValue(text, out clip) && clip != null) return true;
            clip = null;
            Request(text);
            return false;
        }

        public bool HasFailed(string text) => disabled || failed.Contains(Clean(text));

        public void Request(string text)
        {
            text = Clean(text);
            if (text.Length == 0 || clips.ContainsKey(text) || failed.Contains(text) || disabled) return;
            string id = IdFor(text);
            if (pendingById.ContainsKey(id)) return;
            pendingById[id] = text;
            backlog.Enqueue(id);
        }

        public void Update()
        {
            if (!purged)
            {
                purged = true;
                Purge();
            }
            while (results.TryDequeue(out var line))
            {
                if (line == "READY")
                {
                    workerReady = true;
                    Status = "Text-to-speech ready (" + workerVoice + ").";
                    continue;
                }
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                string id = parts[1];
                inFlight.Remove(id);
                if (!pendingById.TryGetValue(id, out var text)) continue;
                pendingById.Remove(id);
                if (parts[0] == "OK") Load(text, Path.Combine(folder, id + ".wav"));
                else
                {
                    failed.Add(text);
                    Status = "Text-to-speech error: " + (parts.Length > 2 ? parts[2] : "unknown");
                }
            }

            if (worker != null && (Settings.TtsVoice.Value ?? "") != workerVoice)
            {
                Shutdown();
                clips.Clear();
                pendingById.Clear();
                backlog.Clear();
                failed.Clear();
                starts = 0;
            }

            if (worker != null && (HasExited(worker) || (!workerReady && Time.unscaledTime - workerStartedAt > 30f)))
            {
                Shutdown();
                RequeueAll();
                restartAfter = Time.unscaledTime + 3f;
                Status = "Text-to-speech worker stopped, restarting...";
            }

            Pump();
        }

        private void RequeueAll()
        {
            inFlight.Clear();
            backlog.Clear();
            foreach (var id in pendingById.Keys) backlog.Enqueue(id);
        }

        private void Pump()
        {
            if (backlog.Count == 0 || disabled) return;
            if (worker == null)
            {
                if (Time.unscaledTime < restartAfter) return;
                StartWorker();
                return;
            }
            if (!workerReady) return;
            try
            {
                bool wrote = false;
                while (backlog.Count > 0 && inFlight.Count < MaxInFlight)
                {
                    string id = backlog.Dequeue();
                    if (!pendingById.TryGetValue(id, out var text) || inFlight.Contains(id)) continue;
                    worker.StandardInput.WriteLine(id + "\t" + text);
                    inFlight.Add(id);
                    wrote = true;
                }
                if (wrote) worker.StandardInput.Flush();
            }
            catch (Exception e)
            {
                Status = "Text-to-speech write failed: " + e.Message;
                Shutdown();
                RequeueAll();
                restartAfter = Time.unscaledTime + 3f;
            }
        }

        private void StartWorker()
        {
            if (starts >= 4)
            {
                disabled = true;
                Status = "Text-to-speech unavailable - only the built-in lines will be spoken.";
                Plugin.Log.LogWarning(Status);
                return;
            }
            starts++;
            try
            {
                Directory.CreateDirectory(folder);
                string script = Path.Combine(folder, "tts_worker.ps1");
                File.WriteAllText(script, WorkerScript, new UTF8Encoding(false));
                string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(ps)) ps = "powershell.exe";
                workerVoice = Settings.TtsVoice.Value ?? "";
                var info = new ProcessStartInfo
                {
                    FileName = ps,
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" \"{workerVoice.Replace("\"", "")}\" \"{folder}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var p = new Process { StartInfo = info, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) results.Enqueue(e.Data.Trim()); };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Plugin.Log.LogWarning("TTS: " + e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                worker = p;
                workerReady = false;
                workerStartedAt = Time.unscaledTime;
                Status = "Text-to-speech starting...";
            }
            catch (Exception e)
            {
                worker = null;
                Status = "Text-to-speech could not start: " + e.Message;
                Plugin.Log.LogWarning(Status);
                restartAfter = Time.unscaledTime + 10f;
            }
        }

        private static bool HasExited(Process p)
        {
            try { return p.HasExited; }
            catch { return true; }
        }

        public void Shutdown()
        {
            var p = worker;
            worker = null;
            workerReady = false;
            inFlight.Clear();
            if (p == null) return;
            try { p.StandardInput.Close(); } catch { }
            try { if (!p.WaitForExit(300)) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
            Purge();
        }

        private void Purge()
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var f in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        (worker == null && name.Equals("tts_worker.ps1", StringComparison.OrdinalIgnoreCase)))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                if (worker == null && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
            }
            catch { }
        }

        private void Load(string text, string file)
        {
            try
            {
                clips[text] = Wav.FromWav(File.ReadAllBytes(file), "tts");
                try { File.Delete(file); } catch { }
            }
            catch (Exception e)
            {
                failed.Add(text);
                try { File.Delete(file); } catch { }
                Plugin.Log.LogWarning($"Could not load TTS clip for '{text}': {e.Message}");
            }
        }

        private static string IdFor(string text)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes((Settings.TtsVoice.Value ?? "") + "|" + text));
                var sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch == '\t' || ch == '\r' || ch == '\n') sb.Append(' ');
                else if (ch >= 32 && ch < 127) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }
    }
}
