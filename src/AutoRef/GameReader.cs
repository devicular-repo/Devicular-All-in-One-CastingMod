using System;
using System.Collections.Generic;
using GorillaGameModes;
using UnityEngine;

namespace AutoRef
{
    internal sealed class PlayerSnapshot
    {
        public NetPlayer Net;
        public VRRig Rig;
        public int Actor;
        public string UserId;
        public string Key;
        public string Name;
        public string Platform;
        public bool IsLocal;
        public bool ColorKnown;
        public ColorCode Color;
        public Color RawColor;
        public bool Infected;
        public Vector3 Position;
        public float FirstSeen;

        public bool OnBench;
        public bool UnderGazebo;
        public string Surface;

        internal bool Seen;
    }

    internal sealed class GameReader
    {
        public readonly List<PlayerSnapshot> Players = new List<PlayerSnapshot>();
        public readonly HashSet<int> Infected = new HashSet<int>();
        private readonly Dictionary<int, PlayerSnapshot> byActor = new Dictionary<int, PlayerSnapshot>();

        public bool InRoom;
        public string RoomName = "";
        public bool IsPrivate;
        public bool IsMaster;
        public string ModeName = "-";
        public GameModeType ModeType = GameModeType.None;
        public bool HasTagManager;
        public bool IsInfectionMode;
        public bool InfectionActive;
        public bool ClassicTagMode;
        public bool WaitingForNextInfectionRound;
        public int LocalActor = -1;
        public string MasterName = "";
        public bool LocalInfected;
        public string VoiceChatOn = "";
        public string PttType = "";
        public string LastError;

        private float nextErrorLog;

        public PlayerSnapshot Get(int actor) => byActor.TryGetValue(actor, out var s) ? s : null;

        public bool TagsDetectable(bool allowClassicTag) => InfectionActive || (allowClassicTag && ClassicTagMode);

        public void Refresh()
        {
            try
            {
                RefreshInner();
                LastError = null;
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
                if (Time.unscaledTime > nextErrorLog)
                {
                    nextErrorLog = Time.unscaledTime + 10f;
                    Plugin.Log.LogError("GameReader failed (a game update may have changed something): " + e);
                }
            }
        }

        private void RefreshInner()
        {
            Infected.Clear();
            var ns = NetworkSystem.Instance;
            InRoom = ns != null && ns.InRoom;
            if (!InRoom)
            {
                Players.Clear();
                byActor.Clear();
                RoomName = "";
                ModeName = "-";
                ModeType = GameModeType.None;
                HasTagManager = IsInfectionMode = InfectionActive = ClassicTagMode = WaitingForNextInfectionRound = false;
                IsMaster = LocalInfected = false;
                MasterName = "";
                LocalActor = -1;
                return;
            }

            RoomName = ns.RoomName ?? "";
            IsPrivate = ns.SessionIsPrivate;
            IsMaster = ns.IsMasterClient;
            var master = ns.MasterClient;
            MasterName = master != null ? (string.IsNullOrEmpty(master.NickName) ? "Player " + master.ActorNumber : master.NickName) : "?";
            try
            {
                var computer = GorillaNetworking.GorillaComputer.instance;
                VoiceChatOn = computer != null ? computer.voiceChatOn ?? "" : "";
                PttType = computer != null ? computer.pttType ?? "" : "";
            }
            catch
            {
                VoiceChatOn = PttType = "";
            }

            var mgr = GorillaGameManager.instance;
            ModeType = GameMode.CurrentGameModeType;
            ModeName = mgr != null ? mgr.GameModeName() : ModeType.ToString().ToUpperInvariant();

            var tagMgr = mgr as GorillaTagManager;
            HasTagManager = tagMgr != null;
            IsInfectionMode = tagMgr != null && ModeType == GameModeType.Infection;
            ClassicTagMode = IsInfectionMode && tagMgr.isCurrentlyTag;
            InfectionActive = IsInfectionMode && !tagMgr.isCurrentlyTag;
            WaitingForNextInfectionRound = IsInfectionMode && IsMaster && tagMgr.waitingToStartNextInfectionGame;
            if (tagMgr != null)
            {
                if (tagMgr.isCurrentlyTag)
                {
                    if (tagMgr.currentIt != null) Infected.Add(tagMgr.currentIt.ActorNumber);
                }
                else
                {
                    var list = tagMgr.currentInfected;
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null) Infected.Add(list[i].ActorNumber);
                }
            }

            for (int i = 0; i < Players.Count; i++) Players[i].Seen = false;

            var localRig = VRRig.LocalRig;
            var local = ns.LocalPlayer;
            LocalActor = local != null ? local.ActorNumber : -1;
            LocalInfected = LocalActor >= 0 && Infected.Contains(LocalActor);

            var rigs = VRRigCache.ActiveRigs;
            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null) continue;
                bool isLocal = rig == localRig;
                NetPlayer np = isLocal ? local : rig.Creator;
                if (np == null || np.IsNull || !np.InRoom) continue;
                int actor = np.ActorNumber;

                if (!byActor.TryGetValue(actor, out var s) || s.Net != np)
                {
                    if (s != null) Players.Remove(s);
                    s = new PlayerSnapshot { FirstSeen = Time.unscaledTime };
                    byActor[actor] = s;
                    Players.Add(s);
                }

                s.Seen = true;
                s.Net = np;
                s.Rig = rig;
                s.Actor = actor;
                s.IsLocal = isLocal;
                s.UserId = np.UserId ?? "";
                s.Key = string.IsNullOrEmpty(s.UserId) ? "actor:" + actor : s.UserId;
                string visible = rig.playerNameVisible;
                s.Name = !string.IsNullOrEmpty(visible) ? visible : (!string.IsNullOrEmpty(np.NickName) ? np.NickName : "Player " + actor);
                try { s.Platform = np.Platform ?? ""; } catch { s.Platform = ""; }
                s.ColorKnown = isLocal || (rig.colorInitialized && Time.unscaledTime - s.FirstSeen >= 2f);
                s.RawColor = rig.playerColor;
                s.Color = ColorCode.FromColor(rig.playerColor);
                s.Infected = Infected.Contains(actor);
                s.Position = rig.transform.position;
            }

            for (int i = Players.Count - 1; i >= 0; i--)
            {
                if (Players[i].Seen) continue;
                byActor.Remove(Players[i].Actor);
                Players.RemoveAt(i);
            }
            Players.Sort((a, b) => a.Actor.CompareTo(b.Actor));
        }

        public bool TryGetTagger(int taggedActor, out int taggerActor)
        {
            taggerActor = -1;
            try
            {
                var mgr = GorillaGameManager.instance;
                return mgr != null && mgr.lastTaggedActorNr != null && mgr.lastTaggedActorNr.TryGetValue(taggedActor, out taggerActor);
            }
            catch
            {
                return false;
            }
        }

        public string NameOf(int actor)
        {
            var p = Get(actor);
            if (p != null) return p.Name;
            try
            {
                var np = NetworkSystem.Instance?.GetPlayer(actor);
                if (np != null && !string.IsNullOrEmpty(np.NickName)) return np.NickName;
            }
            catch { }
            return "Player " + actor;
        }
    }
}
