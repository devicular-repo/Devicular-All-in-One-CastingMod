using System;
using System.Text;
using GorillaNetworking;
using GorillaTagScripts;
using PlayFab;
using PlayFab.CloudScriptModels;
using PlayFab.Json;
using UnityEngine;

namespace DevicularCaster
{
    internal sealed class RoomLink
    {
        public const int MaxLength = 10;

        public string Status { get; private set; } = "";
        public bool Busy { get; private set; }

        private string pending = "";
        private float busyUntil;

        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(MaxLength);
            foreach (char ch in text.ToUpperInvariant())
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                if (sb.Length == MaxLength) break;
            }
            return sb.ToString();
        }

        public void Join(string code)
        {
            code = Sanitize(code);
            if (code.Length == 0)
            {
                Status = "Type a room code.";
                return;
            }
            if (Busy) return;
            if (GorillaServer.Instance == null || PhotonNetworkController.Instance == null || NetworkSystem.Instance == null)
            {
                Status = "The game isn't ready yet - try again in a moment.";
                return;
            }
            if (NetworkSystem.Instance.InRoom) return;

            pending = code;
            Busy = true;
            busyUntil = Time.unscaledTime + 20f;
            Status = $"Checking {code}...";
            try
            {
                GorillaServer.Instance.CheckForBadName(new CheckForBadNameRequest { name = code, forRoom = true, forTroop = false },
                    OnChecked, OnCheckFailed);
            }
            catch (Exception e)
            {
                Busy = false;
                Status = "Couldn't join: " + e.Message;
            }
        }

        private void OnChecked(ExecuteFunctionResult result)
        {
            int verdict = -1;
            try
            {
                if (result?.FunctionResult is JsonObject json && json.TryGetValue("result", out var value))
                    int.TryParse(value?.ToString(), out verdict);
            }
            catch
            {
                verdict = -1;
            }

            if (verdict != 0)
            {
                Busy = false;
                Status = verdict < 0 ? "Couldn't check that room code - try again." : $"The game doesn't allow the room code {pending}.";
                return;
            }

            try
            {
                var party = FriendshipGroupDetection.Instance;
                var join = party != null && party.IsInParty ? JoinType.ForceJoinWithParty : JoinType.Solo;
                Status = $"Joining {pending}...";
                busyUntil = Time.unscaledTime + 20f;
                PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(pending, join);
            }
            catch (Exception e)
            {
                Busy = false;
                Status = "Couldn't join: " + e.Message;
            }
        }

        private void OnCheckFailed(PlayFabError error)
        {
            Busy = false;
            Status = "Couldn't check the room code: " + (error?.ErrorMessage ?? "unknown error");
        }

        public void Disconnect()
        {
            Busy = false;
            Status = "";
            try
            {
                _ = NetworkSystem.Instance?.ReturnToSinglePlayer();
            }
            catch (Exception e)
            {
                Status = "Couldn't disconnect: " + e.Message;
            }
        }

        public void Tick(bool inRoom)
        {
            if (!Busy) return;
            if (inRoom)
            {
                Busy = false;
                Status = "";
            }
            else if (Time.unscaledTime > busyUntil)
            {
                Busy = false;
                Status = $"Couldn't join {pending} - try again.";
            }
        }
    }
}
