using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace AutoRef
{
    internal enum TeamSizeMode { Auto, ThreeVsThree, FourVsFour, OneVsOneTest }

    internal enum ColorAssignMode { AutoDetect, Manual }

    internal enum TeamId { None, A, B }

    internal static class Settings
    {
        public static ConfigEntry<KeyCode> ToggleKey;
        public static ConfigEntry<bool> ShowToggleButton;
        public static ConfigEntry<bool> ShowZoneBoxes;
        public static ConfigEntry<bool> ShowScoreboard;
        public static ConfigEntry<float> ScoreboardX;
        public static ConfigEntry<float> ScoreboardY;
        public static ConfigEntry<float> ScoreboardScale;

        public static ConfigEntry<float> TimeCapSeconds;
        public static ConfigEntry<float> HeadStartSeconds;
        public static ConfigEntry<TeamSizeMode> TeamSize;
        public static ConfigEntry<int> FirstTo;
        public static ConfigEntry<bool> RequireTaggersInfected;
        public static ConfigEntry<bool> RequireRunnersNotInfected;

        public static ConfigEntry<bool> RequireMasterClient;
        public static ConfigEntry<bool> HostAuthority;
        public static ConfigEntry<bool> BlockInterference;
        public static ConfigEntry<bool> RefereeNeverTags;
        public static ConfigEntry<bool> FreezeTaggers;
        public static ConfigEntry<bool> StunEarlyRunners;

        public static ConfigEntry<ColorAssignMode> ColorMode;
        public static ConfigEntry<string> TeamAName;
        public static ConfigEntry<string> TeamBName;
        public static ConfigEntry<string> TeamAColor;
        public static ConfigEntry<string> TeamBColor;
        public static ConfigEntry<TeamId> TaggersTeam;
        public static ConfigEntry<bool> AutoRotate;

        public static ConfigEntry<bool> AnnouncerEnabled;
        public static ConfigEntry<float> AnnouncerVolume;
        public static ConfigEntry<bool> PlayLocally;
        public static ConfigEntry<float> LocalVolume;
        public static ConfigEntry<bool> AutoCallouts;
        public static ConfigEntry<float> CalloutInterval;
        public static ConfigEntry<string> TtsVoice;
        public static ConfigEntry<bool> CountdownBeeps;

        public static ConfigEntry<float> BenchMaxDrop;
        public static ConfigEntry<float> BenchMargin;
        public static ConfigEntry<bool> AutoReferee;
        public static ConfigEntry<float> AutoStartHoldSeconds;
        public static ConfigEntry<float> AutoNextRoundSeconds;
        public static ConfigEntry<float> ReplayMarginSeconds;
        public static ConfigEntry<string> BenchZoneOverride;
        public static ConfigEntry<string> GazeboZoneOverride;

        private static ConfigFile session;

        public static void Bind(ConfigFile cfg)
        {
            session = new ConfigFile(Path.Combine(Paths.ConfigPath, "devicular.session.unsaved.cfg"), false) { SaveOnConfigSet = false };

            ToggleKey = cfg.Bind("UI", "ToggleKey", KeyCode.F6, "Key that opens/closes the referee panel (game window must be focused).");
            ShowToggleButton = cfg.Bind("UI", "ShowToggleButton", true, "Show a small 'AutoRef' button in the top-left corner while the panel is closed.");
            ShowZoneBoxes = cfg.Bind("UI", "ShowZoneBoxesInWorld", false,
                "Before a round starts, show a red box where taggers must sit (bench) and a green box where runners must be (under the gazebo). " +
                "The boxes exist only in your game - other players never see them. Hidden while a round is running.");

            ShowScoreboard = session.Bind("Scoreboard", "Show", true, "Show the scoreboard (scores, team names, clock, time to beat) on your screen.");
            ScoreboardX = session.Bind("Scoreboard", "CenterX", 0.5f,
                new ConfigDescription("Horizontal position of the scoreboard's center, 0 = left edge, 1 = right edge.", new AcceptableValueRange<float>(0f, 1f)));
            ScoreboardY = session.Bind("Scoreboard", "Top", 0.012f,
                new ConfigDescription("Vertical position of the scoreboard's top, 0 = top edge, 1 = bottom edge.", new AcceptableValueRange<float>(0f, 1f)));
            ScoreboardScale = session.Bind("Scoreboard", "Scale", 1f,
                new ConfigDescription("Scoreboard size.", new AcceptableValueRange<float>(0.6f, 2f)));

            TimeCapSeconds = cfg.Bind("Round", "TimeCapSeconds", 180f,
                new ConfigDescription("Maximum survival time per runner, in seconds.", new AcceptableValueRange<float>(10f, 3600f)));
            HeadStartSeconds = cfg.Bind("Round", "HeadStartSeconds", 10f,
                new ConfigDescription("Seconds between 'Runners, RUN!' and 'Taggers, TAG!'.", new AcceptableValueRange<float>(0f, 60f)));
            TeamSize = session.Bind("Round", "TeamSize", TeamSizeMode.Auto,
                "Auto = no format chosen yet: teams aren't detected until the referee picks one. OneVsOneTest = 1 tagger vs 1 runner for testing; " +
                "with fewer than 4 players the game runs classic Tag instead of Infection, which is accepted in this format.");
            FirstTo = session.Bind("Round", "FirstTo", 0,
                new ConfigDescription("Game length: 0 = no limit. 3 or 5 = the first team to that many points wins the game.", new AcceptableValueList<int>(0, 3, 5)));
            RequireTaggersInfected = cfg.Bind("Round", "RequireTaggersInfected", true,
                "Without host authority: taggers must already be infected (lava) before the round can start.");
            RequireRunnersNotInfected = cfg.Bind("Round", "RequireRunnersNotInfected", true,
                "Without host authority: runners must not be infected before the round can start.");

            AutoReferee = session.Bind("Round", "AutoReferee", false,
                "The referee's on/off switch, off each time the game starts. Off: it leaves the lobby alone. It says nothing, doesn't control tags or infection, " +
                "doesn't stun and doesn't run rounds. Turning it off hands the lobby back to the game. It keeps its value when you change lobbies.");
            AutoStartHoldSeconds = cfg.Bind("Round", "AutoStartHoldSeconds", 3f,
                new ConfigDescription("Everyone must stay ready this long before a round starts automatically.", new AcceptableValueRange<float>(1f, 30f)));
            AutoNextRoundSeconds = cfg.Bind("Round", "AutoNextRoundSeconds", 8f,
                new ConfigDescription("After a round ends (and the announcements are done), wait this long before going back to setup automatically.",
                    new AcceptableValueRange<float>(2f, 120f)));

            ReplayMarginSeconds = cfg.Bind("Round", "ReplayMarginSeconds", 2f,
                new ConfigDescription("Two-second rule: when both teams' runtimes for a point are this close or closer, both runs reset " +
                                      "and the score stays as it is. 0 = only an exact tie resets.", new AcceptableValueRange<float>(0f, 30f)));

            RequireMasterClient = cfg.Bind("Host", "RequireMasterClient", true,
                "A round can only start while you are the room's host (Photon Master Client). Losing host mid-round pauses the round.");
            HostAuthority = cfg.Bind("Host", "HostAuthority", true,
                "While you are the host: keep you (the referee) infected; during a round nobody is infected until 'Taggers, TAG!', " +
                "then everyone except the alive runners is; and tags that aren't tagger -> alive runner are refused.");
            BlockInterference = cfg.Bind("Host", "BlockInterference", true,
                "Refuse tags by eliminated runners and spectators on alive runners. Note: a player who keeps touching after a refused tag " +
                "re-sends it every frame, and the game's own anti-spam check may flag them if they hold on for about half a second. " +
                "Off = such tags go through and are flagged as INTERFERENCE for you to judge (Revive restores the runner).");
            RefereeNeverTags = cfg.Bind("Host", "RefereeNeverTags", true, "Your own gorilla never tags anyone (even when infected).");
            FreezeTaggers = cfg.Bind("Host", "FreezeTaggers", true,
                "From START until 'Taggers, TAG!', a tagger who leaves the bench gets the game's tag stun (the freeze after being tagged) over and over " +
                "until 'Taggers, TAG!' or until they're back on the bench. Taggers sitting on the bench aren't stunned. Only works while you are the host.");
            StunEarlyRunners = cfg.Bind("Host", "StunEarlyRunners", true,
                "A runner who leaves the gazebo during '3, 2, 1' (before 'Runners, RUN!') gets the game's tag stun once. " +
                "Only works while you are the host.");

            ColorMode = session.Bind("Teams", "ColorMode", ColorAssignMode.AutoDetect,
                "AutoDetect = find two groups of players sharing a color code. Manual = use the codes below.");
            TeamAName = session.Bind("Teams", "TeamAName", "Team A", "Name of team A (spoken by the announcer).");
            TeamBName = session.Bind("Teams", "TeamBName", "Team B", "Name of team B (spoken by the announcer).");
            TeamAColor = session.Bind("Teams", "TeamAColor", "", "Team A color code as three digits 0-9 (R G B), e.g. 900.");
            TeamBColor = session.Bind("Teams", "TeamBColor", "", "Team B color code as three digits 0-9 (R G B), e.g. 009.");
            TaggersTeam = session.Bind("Teams", "TaggersTeam", TeamId.A, "Which team is tagging in the next round (switches automatically).");
            AutoRotate = session.Bind("Teams", "AutoRotate", true, "Switch Taggers and Runners after every completed round.");

            AnnouncerEnabled = cfg.Bind("Announcer", "Enabled", true,
                "Voice announcer. Lines are sent through YOUR voice chat (mixed with your mic), so every player hears them without the mod. " +
                "Gorilla Tag voice is proximity-based: players hear it from where your gorilla is.");
            AnnouncerVolume = cfg.Bind("Announcer", "VolumeForPlayers", 0.9f,
                new ConfigDescription("Announcer loudness in your voice stream.", new AcceptableValueRange<float>(0f, 1f)));
            PlayLocally = cfg.Bind("Announcer", "PlayLocally", true, "Also play the announcer for you.");
            LocalVolume = cfg.Bind("Announcer", "LocalVolume", 0.8f,
                new ConfigDescription("Announcer volume for you.", new AcceptableValueRange<float>(0f, 1f)));
            AutoCallouts = cfg.Bind("Announcer", "AutoCallouts", true,
                "Before a round, periodically call out players who aren't in their starting position.");
            CalloutInterval = cfg.Bind("Announcer", "CalloutIntervalSeconds", 20f,
                new ConfigDescription("Seconds between automatic position call-outs.", new AcceptableValueRange<float>(8f, 120f)));
            TtsVoice = cfg.Bind("Announcer", "TtsVoice", "Microsoft David Desktop",
                "Windows text-to-speech voice for spoken names/times (e.g. 'Microsoft Zira Desktop').");
            CountdownBeeps = cfg.Bind("Announcer", "LocalBeeps", true, "Short beeps for you on tags and false starts.");

            BenchMaxDrop = cfg.Bind("Zones", "BenchMaxDrop", 2.0f,
                new ConfigDescription("A tagger counts as 'on the bench' when the first surface under their body within this many meters is the gazebo picnic table.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            BenchMargin = cfg.Bind("Zones", "BenchMargin", 0.35f,
                new ConfigDescription("How far (meters) the bench zone reaches beyond the picnic table's edges, on top of a built-in 0.5 m. The box also reaches 0.9 m below the tabletop, so a tagger standing right next to the table counts. A tagger inside this box, or standing on the table, is on the bench.",
                    new AcceptableValueRange<float>(0f, 2f)));
            BenchZoneOverride = cfg.Bind("Zones", "BenchZoneOverride", "",
                "Optional manual bench box 'centerX,centerY,centerZ,sizeX,sizeY,sizeZ,yawDegrees'. Empty = use the map geometry.");
            GazeboZoneOverride = cfg.Bind("Zones", "GazeboZoneOverride", "",
                "Optional manual gazebo box 'centerX,centerY,centerZ,sizeX,sizeY,sizeZ,yawDegrees'. Empty = use the map geometry.");

            if (TaggersTeam.Value == TeamId.None) TaggersTeam.Value = TeamId.A;
        }

        public static void ResetSession()
        {
            if (session == null) return;
            foreach (var key in new System.Collections.Generic.List<ConfigDefinition>(session.Keys))
            {
                var entry = session[key];
                if (entry == AutoReferee) continue;
                entry.BoxedValue = entry.DefaultValue;
            }
        }

        public static bool IsOneVsOneTest => TeamSize.Value == TeamSizeMode.OneVsOneTest;

        public static bool FormatChosen => TeamSize.Value != TeamSizeMode.Auto;

        public static bool RefereeOn => AutoReferee == null || AutoReferee.Value;

        public static int RequiredTeamSize
        {
            get
            {
                switch (TeamSize.Value)
                {
                    case TeamSizeMode.ThreeVsThree: return 3;
                    case TeamSizeMode.FourVsFour: return 4;
                    case TeamSizeMode.OneVsOneTest: return 1;
                    default: return 0;
                }
            }
        }
    }
}
