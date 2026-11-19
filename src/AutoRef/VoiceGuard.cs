using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Voice;
using Photon.Voice.Unity;

namespace AutoRef
{
    internal static class VoiceGuard
    {
        private static Recorder target;
        private static Func<IAudioDesc> factory;

        private static AccessTools.FieldRef<Recorder, Recorder.InputSourceType> sourceTypeField;
        private static AccessTools.FieldRef<Recorder, Func<IAudioDesc>> inputFactoryField;
        private static AccessTools.FieldRef<Recorder, IAudioDesc> inputSourceField;

        public static bool Installed { get; private set; }

        public static void Install(Harmony harmony)
        {
            try { sourceTypeField = AccessTools.FieldRefAccess<Recorder, Recorder.InputSourceType>("sourceType"); }
            catch (Exception e) { Plugin.Log.LogWarning("VoiceGuard: no sourceType field: " + e.Message); }
            try { inputFactoryField = AccessTools.FieldRefAccess<Recorder, Func<IAudioDesc>>("inputFactory"); }
            catch (Exception e) { Plugin.Log.LogWarning("VoiceGuard: no inputFactory field: " + e.Message); }
            try { inputSourceField = AccessTools.FieldRefAccess<Recorder, IAudioDesc>("inputSource"); }
            catch (Exception e) { Plugin.Log.LogWarning("VoiceGuard: no inputSource field: " + e.Message); }

            int hooked = 0;
            hooked += Hook(harmony, AccessTools.Method(typeof(Recorder), "CreateLocalVoiceAudioAndSource"), prefix: nameof(BeforeCreate));
            hooked += Hook(harmony, AccessTools.PropertyGetter(typeof(Recorder), nameof(Recorder.SourceType)), postfix: nameof(SourceTypeResult));
            hooked += Hook(harmony, AccessTools.PropertyGetter(typeof(Recorder), nameof(Recorder.InputFactory)), postfix: nameof(InputFactoryResult));
            Installed = hooked > 0;
            Plugin.Log.LogInfo($"VoiceGuard: {hooked}/3 recorder hooks installed.");
        }

        private static int Hook(Harmony harmony, System.Reflection.MethodBase original, string prefix = null, string postfix = null)
        {
            if (original == null) return 0;
            try
            {
                harmony.Patch(original,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(VoiceGuard), prefix) { priority = HarmonyLib.Priority.Last },
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(VoiceGuard), postfix) { priority = HarmonyLib.Priority.Last });
                return 1;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"VoiceGuard: could not hook {original.Name}: {e.Message}");
                return 0;
            }
        }

        public static void Guard(Recorder rec, Func<IAudioDesc> ourFactory)
        {
            target = rec;
            factory = ourFactory;
        }

        public static void Release()
        {
            target = null;
            factory = null;
        }

        private static bool Guarding(Recorder r) => factory != null && target != null && ReferenceEquals(r, target);

        public static IAudioDesc CurrentInput(Recorder rec)
        {
            if (rec == null || inputSourceField == null) return null;
            try { return inputSourceField(rec); }
            catch { return null; }
        }

        public static bool InputIsOurs(Recorder rec)
        {
            if (inputSourceField == null) return true;
            return CurrentInput(rec) is MixReader;
        }

        public static string InputName(Recorder rec)
        {
            var input = CurrentInput(rec);
            if (input == null) return "nothing";
            var t = input.GetType();
            string asm = t.Assembly.GetName().Name;
            return asm.StartsWith("PhotonVoice", StringComparison.OrdinalIgnoreCase) || asm == "Assembly-CSharp"
                ? t.Name + " (the game's own input)"
                : $"{t.Name} from {asm}";
        }

        public static string OtherHooks(string ownId)
        {
            var byOwner = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var m in Harmony.GetAllPatchedMethods())
                {
                    var type = m.DeclaringType;
                    if (type == null) continue;
                    bool voice = typeof(Recorder).IsAssignableFrom(type)
                                 || (type.Namespace ?? "").StartsWith("Photon.Voice", StringComparison.Ordinal)
                                 || type.Name == "GTMicWrapper";
                    if (!voice) continue;
                    var info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    string member = m.Name.StartsWith("get_") || m.Name.StartsWith("set_") ? m.Name.Substring(4) : m.Name;
                    foreach (var owner in info.Owners)
                    {
                        if (owner == ownId) continue;
                        if (!byOwner.TryGetValue(owner, out var set)) byOwner[owner] = set = new SortedSet<string>();
                        set.Add(member);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("VoiceGuard: could not list other voice hooks: " + e.Message);
            }
            return string.Join("; ", byOwner.Select(kv => $"{kv.Key} ({string.Join(", ", kv.Value)})"));
        }

        private static void BeforeCreate(Recorder __instance)
        {
            if (!Guarding(__instance)) return;
            try
            {
                if (sourceTypeField != null) sourceTypeField(__instance) = Recorder.InputSourceType.Factory;
                if (inputFactoryField != null) inputFactoryField(__instance) = factory;
            }
            catch
            {
            }
        }

        private static void SourceTypeResult(Recorder __instance, ref Recorder.InputSourceType __result)
        {
            if (Guarding(__instance)) __result = Recorder.InputSourceType.Factory;
        }

        private static void InputFactoryResult(Recorder __instance, ref Func<IAudioDesc> __result)
        {
            if (Guarding(__instance)) __result = factory;
        }
    }
}
