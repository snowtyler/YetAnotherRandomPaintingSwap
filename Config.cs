using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace RandomPaintingSwap;

internal static class PluginConfig
{
    internal static ConfigEntry<bool> enableDebugLog;
    internal static ConfigEntry<float> customPaintingChance;

    internal static void Init(ConfigFile InConfig)
    {
        enableDebugLog = InConfig.Bind
        (
            "General",
            "DebugLog",
            false,
            "Print extra logs for debugging"
        );

        customPaintingChance = InConfig.Bind
        (
            "General",
            "CustomPaintingChance",
            1.0f,
            "The chance of a painting being replaced by a custom painting (1 = 100%, 0.5 = 50%)"
        );
    }
}
