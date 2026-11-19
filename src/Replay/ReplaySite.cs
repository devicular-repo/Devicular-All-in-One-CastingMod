using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace DevicularReplay
{
    internal static class ReplaySite
    {
        public const string Url = "https://scrimreplay.vercel.app/";
        public static string Status = "";

        public static void Open(string replayPath)
        {
            try
            {
                Application.OpenURL(Url);
                if (!string.IsNullOrEmpty(replayPath) && File.Exists(replayPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + replayPath + "\"");
                    Status = $"Opened {Url}. Drag {Path.GetFileName(replayPath)} from the folder that opened onto the page.";
                }
                else Status = $"Opened {Url}. Drop a replay from BepInEx\\Devicular\\replays onto the page.";
            }
            catch (Exception e)
            {
                Status = "Couldn't open the replay site: " + e.Message;
            }
        }
    }
}
