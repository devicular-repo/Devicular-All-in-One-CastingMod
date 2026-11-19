using System;
using System.Collections.Generic;
using UnityEngine;

namespace Devicular
{
    /// <summary>
    /// The player color fix: every frame, each player's gorilla is set to the color of its GamePlayer's gorilla
    /// (VRRig.GamePlayerRef.rig.playerColor), so a player's new color shows up straight away.
    ///
    /// Every Devicular mod (Auto Ref, Caster, Replay, Live Replay) has it and calls <see cref="UpdateColor"/> each
    /// frame, so it works with any of them installed. With several installed, only the first one to run does the work.
    /// </summary>
    internal static class PlayerColors
    {
        private const string OwnerLink = "PlayerColors.Owner";
        private static int frame = -1;
        private static bool reported;

        public static void UpdateColor()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;

            // One mod does it for all of them.
            string owner = Link.Get<string>(OwnerLink);
            if (owner == null)
            {
                owner = Mod.Name;
                Link.Put(OwnerLink, owner);
            }
            if (owner != Mod.Name) return;

            var ns = NetworkSystem.Instance;
            if (ns == null || !ns.InRoom) return;
            IReadOnlyList<VRRig> rigs;
            try { rigs = VRRigCache.ActiveRigs; }
            catch { return; }
            if (rigs == null) return;

            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                try
                {
                    // Other players only: never your own gorilla.
                    if (rig == null || rig.isOfflineVRRig || rig.isLocal || rig.isMyPlayer) continue;
                    var gamePlayer = rig.GamePlayerRef;
                    if (gamePlayer == null || gamePlayer.rig == null) continue;
                    if (!reported)
                    {
                        reported = true;
                        Mod.Log?.LogInfo("Color: player color fix running; a player's GamePlayer links to " +
                                         (ReferenceEquals(gamePlayer.rig, rig) ? "their own gorilla." : "a different gorilla."));
                    }
                    rig.SetColor(gamePlayer.rig.playerColor);
                }
                catch
                {
                }
            }
        }
    }
}
