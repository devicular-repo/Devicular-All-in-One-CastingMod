using System;
using System.Collections.Generic;
using GorillaGameModes;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace AutoRef
{
    internal struct TagEvent
    {
        public int Tagged;
        public int Tagger;
        public double TagRealtime;
        public double Latency;
    }

    internal static class TagHook
    {
        public static readonly Queue<TagEvent> Events = new Queue<TagEvent>();
        public static string Status = "Tag broadcast hook not installed - using the infection list only.";
        public static bool Installed;

        public static void Install(Harmony harmony)
        {
            int count = 0;
            try
            {
                var serializer = AccessTools.TypeByName("GameModeSerializer");
                var remote = serializer == null ? null : AccessTools.Method(serializer, "BroadcastTag",
                    new[] { typeof(NetPlayer), typeof(NetPlayer), typeof(PhotonMessageInfo) });
                if (remote != null)
                {
                    harmony.Patch(remote, postfix: new HarmonyMethod(typeof(TagHook), nameof(RemotePostfix)));
                    count++;
                }

                var host = AccessTools.Method(typeof(GameMode), nameof(GameMode.BroadcastTag), new[] { typeof(NetPlayer), typeof(NetPlayer) });
                if (host != null)
                {
                    harmony.Patch(host, postfix: new HarmonyMethod(typeof(TagHook), nameof(HostPostfix)));
                    count++;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Tag broadcast hook failed, falling back to the infection list: " + e);
            }

            Installed = count > 0;
            Status = count == 2 ? "Tag broadcast hook active (exact tag times and taggers)."
                : count == 1 ? "Tag broadcast hook partly active."
                : "Tag broadcast hook unavailable (game update?) - using the infection list only.";
            Plugin.Log.LogInfo(Status);
        }

        private static void RemotePostfix(NetPlayer __0, NetPlayer __1, PhotonMessageInfo __2)
        {
            try
            {
                if (__0 == null || __1 == null || __2.Sender == null || !__2.Sender.IsMasterClient) return;
                double latency = PhotonNetwork.Time - __2.SentServerTime;
                if (double.IsNaN(latency) || latency < 0) latency = 0;
                if (latency > 1.0) latency = 1.0;
                Record(__0.ActorNumber, __1.ActorNumber, latency);
            }
            catch
            {
            }
        }

        private static void HostPostfix(NetPlayer __0, NetPlayer __1)
        {
            try
            {
                var ns = NetworkSystem.Instance;
                if (__0 == null || __1 == null || ns == null || !ns.IsMasterClient) return;
                Record(__0.ActorNumber, __1.ActorNumber, 0);
            }
            catch
            {
            }
        }

        private static void Record(int tagged, int tagger, double latency)
        {
            Events.Enqueue(new TagEvent
            {
                Tagged = tagged,
                Tagger = tagger,
                Latency = latency,
                TagRealtime = Time.realtimeSinceStartupAsDouble - latency
            });
            while (Events.Count > 64) Events.Dequeue();
        }
    }
}
