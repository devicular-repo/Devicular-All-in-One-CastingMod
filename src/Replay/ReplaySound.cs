using System;
using System.IO;
using System.Text;

namespace DevicularReplay
{
    /// <summary>
    /// The sound of a replay being recorded: every sound message live spectating sends (sound packets, where game
    /// sounds are, how sounds fade), with times counted from the start of the recording. They go to a temporary file
    /// as they come, so a long match doesn't sit in memory, and are written after the replay's frames when it's saved:
    ///
    ///   "SND1", i32 count, then count x (i32 length, message)
    ///
    /// Older viewers stop reading after the frames, so they still play the replay (without sound).
    /// The temporary file deletes itself when it's closed, even if the game crashes. No Unity types.
    /// </summary>
    internal sealed class ReplaySound : IDisposable
    {
        public const string Magic = "SND1";
        private const long MaxBytes = 1L << 30;

        private readonly object gate = new object();
        private readonly FileStream file;
        private readonly BinaryWriter w;
        private bool sealedUp, disposed;

        public int Count { get; private set; }
        public long Bytes { get; private set; }

        public ReplaySound(string tempPath)
        {
            file = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
            w = new BinaryWriter(file, Encoding.UTF8, true);
        }

        /// <summary>Adds one message. Any thread. Ignored once the recording has stopped.</summary>
        public void Add(byte[] msg)
        {
            if (msg == null || msg.Length == 0) return;
            lock (gate)
            {
                if (sealedUp || disposed || Bytes + msg.Length + 4 > MaxBytes) return;
                try
                {
                    w.Write(msg.Length);
                    w.Write(msg);
                    Count++;
                    Bytes += msg.Length + 4;
                }
                catch
                {
                    // Out of disk space or similar: keep what's there.
                    sealedUp = true;
                }
            }
        }

        /// <summary>No more messages (the recording stopped).</summary>
        public void Seal()
        {
            lock (gate)
            {
                sealedUp = true;
                if (!disposed) w.Flush();
            }
        }

        /// <summary>Writes the sound section after a replay's frames.</summary>
        public void WriteSection(Stream output)
        {
            lock (gate)
            {
                if (disposed || Count == 0) return;
                sealedUp = true;
                w.Flush();
                var head = new byte[8];
                Encoding.ASCII.GetBytes(Magic, 0, 4, head, 0);
                BitConverter.TryWriteBytes(new Span<byte>(head, 4, 4), Count);
                output.Write(head, 0, 8);
                file.Position = 0;
                file.CopyTo(output);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                sealedUp = true;
                try { w.Dispose(); } catch { }
                try { file.Dispose(); } catch { }
            }
        }
    }
}
