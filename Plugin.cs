using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System.Reflection;

namespace WalkieTalkieTimeMod
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class PluginBase : BaseUnityPlugin
    {
        const string modGUID = "belea.WalkieTalkieTimeMod";
        const string modName = "Walkie Talkie Time Mod";
        const string modVersion = "1.0.0";

        internal static WTTConfig Config { get; private set; } = null;

        private readonly Harmony harmony = new Harmony(modGUID);
        public static PluginBase Instance;

        internal ManualLogSource mls;

        void Awake()
        {
            if (Instance == null)
                Instance = this;

            mls = BepInEx.Logging.Logger.CreateLogSource(modGUID);
            harmony.PatchAll(typeof(PluginBase));
            harmony.PatchAll(typeof(WTTConfig));

            Config = new WTTConfig(((BaseUnityPlugin)this).Config);

            mls.LogInfo("Walkie Talkie Time app has been installed...");
        }

        public bool ClockShouldBeVisible()
        {
            PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
            if (localPlayer == null)
                return false;

            // outside in normal conditions
            if (!WTTConfig.Instance.ShowTimeOnlyWithWalkieTalkie.Value && !localPlayer.isInsideFactory && !localPlayer.isInHangarShipRoom)
                return true;

            // holding nothing
            GrabbableObject objectCurrentlyHeld = localPlayer.ItemSlots[localPlayer.currentItemSlot];
            if (objectCurrentlyHeld == null)
                return false;

            // not holding walkie talkie
            if (!objectCurrentlyHeld.TryGetComponent(out WalkieTalkie walkieTalkie))
                return false;

            // return on state of walkie talkie
            return walkieTalkie.isBeingUsed;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TimeOfDay), "Update")]
        public static void UpdatePatch(TimeOfDay __instance)
        {
            if (__instance.sunDirect == null || __instance.sunIndirect == null)
                return;

            if (__instance.currentDayTimeStarted && !(bool)typeof(TimeOfDay).GetField("timeStartedThisFrame", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) && __instance.sunAnimator != null)
                HUDManager.Instance.SetClockVisible(Instance.ClockShouldBeVisible());
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TimeOfDay), "PlayerSeesNewTimeOfDay")]
        public static void PlayerSeesNewTimeOfDayPatch(TimeOfDay __instance)
        {
            if (Instance.ClockShouldBeVisible() && __instance.playersManager.shipHasLanded)
            {
                typeof(TimeOfDay).GetField("dayModeLastTimePlayerWasOutside", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, __instance.dayMode);
                HUDManager.Instance.SetClockIcon(__instance.dayMode);
                if (__instance.currentLevel.planetHasTime)
                {
                    __instance.PlayTimeMusicDelayed(__instance.timeOfDayCues[(int)__instance.dayMode], 0.5f, playRandomDaytimeMusic: true);
                }
            }

            return;
        }
    }
}
