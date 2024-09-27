﻿using RiskOfOptions.Options;
using RiskOfOptions;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RiskOfPata
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
            ModSettingsManager.SetModDescription("Adds a Patapon-themed skin to the Seeker's Meditate skill.", RiskOfPata.P_GUID, "Patapon Meditation");

            // todo: mod icon
            int x 
            ModSettingsManager.SetModIcon(RiskOfPata.MainAssets.LoadAsset<Sprite>("modIcon.png"));

            ModSettingsManager.AddOption(new CheckBoxOption(RiskOfPata.EnablePerfectBeatBonusDamage));
        }
    }
}