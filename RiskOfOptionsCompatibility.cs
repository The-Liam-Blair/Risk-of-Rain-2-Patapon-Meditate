using RiskOfOptions.Options;
using RiskOfOptions;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PataponMeditate
{
    /// <summary>
    /// Class that initialises the risk of options config if that mod is present and enabled in the current modlist.
    /// </summary>
    internal class RiskOfOptionsCompatibility
    {
        public static bool enabled
        {
            get => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void SetupRiskOfOptionsConfigs()
        {
            ModSettingsManager.SetModDescription("Visually and audibly adjusts Seeker's Meditate skill to be Patapon-themed.", PataponMeditate.P_GUID, "Patapon Meditate");

            ModSettingsManager.SetModIcon(PataponMeditate.MainAssets.LoadAsset<Sprite>("modIcon.png"));

            ModSettingsManager.AddOption(new CheckBoxOption(PataponMeditate.EnablePerfectBeatBonusDamage));
        }
    }
}