using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DiscoverAllValuables;

[BepInPlugin("Nixeld.DiscoverAllValuables", "DiscoverAllValuables", "1.0")]
public class DiscoverAllValuables : BaseUnityPlugin
{
    internal static DiscoverAllValuables Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        Instance = this;

        ValuableDiscovery.DiscoverAllEnabled = Config.Bind(
            "General",
            "DiscoverAllValuables",
            true,
            "When true, every valuable and cosmetic box is marked as discovered and shown on the map. When false, the game behaves as per usual.");

        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        Patch();

        Logger.LogInfo(
            $"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded (DiscoverAllValuables={ValuableDiscovery.DiscoverAllEnabled.Value}).");
    }

    internal void Patch()
    {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch()
    {
        Harmony?.UnpatchSelf();
    }
}

internal static class ValuableDiscovery
{
    private static readonly FieldInfo? DiscoveredField =
        AccessTools.Field(typeof(ValuableObject), "discovered");

    internal static ConfigEntry<bool>? DiscoverAllEnabled { get; set; }

    internal static void DiscoverIfEnabled(ValuableObject valuable)
    {
        if (DiscoverAllEnabled is not { Value: true })
        {
            return;
        }

        if (!IsAlive(valuable))
        {
            return;
        }
        try
        {
            valuable.discovered = true;
            valuable.Discover(ValuableDiscoverGraphic.State.Discover);
        }
        catch (Exception ex)
        {
            DiscoverAllValuables.Logger.LogWarning(
                $"Could not discover {valuable.name}: {ex.Message}");
        }

        EnsureMapMarker(valuable);
    }

    internal static int DiscoverAllInScene()
    {
        int count = 0;
        foreach (ValuableObject vo in UnityEngine.Object.FindObjectsOfType<ValuableObject>())
        {
            if (!IsAlive(vo))
            {
                continue;
            }
            try
            {
                vo.discovered = true;
                vo.Discover(ValuableDiscoverGraphic.State.Discover);
                count++;
            }
            catch (Exception ex)
            {
                DiscoverAllValuables.Logger.LogWarning(
                    $"Could not discover {vo.name}: {ex.Message}");
            }

            EnsureMapMarker(vo);
        }

        // Also discover ValuableDiscoverCustom objects (e.g. cosmetic boxes)
        foreach (ValuableDiscoverCustom custom in UnityEngine.Object.FindObjectsOfType<ValuableDiscoverCustom>())
        {
            if (custom == null)
            {
                continue;
            }
            try
            {
                custom.Discover();
                count++;
            }
            catch (Exception ex)
            {
                DiscoverAllValuables.Logger.LogWarning(
                    $"Could not discover custom valuable {custom.name}: {ex.Message}");
            }
        }
        return count;
    }

    private static bool IsAlive(ValuableObject? valuable)
    {
        return valuable != null && valuable.gameObject.activeInHierarchy && valuable.enabled;
    }

    private static void EnsureMapMarker(ValuableObject valuable)
    {
        Map? instance = Map.Instance;
        if (instance == null || MapAlreadyHasMarker(valuable))
        {
            return;
        }
        try
        {
            instance.AddValuable(valuable);
        }
        catch (Exception ex)
        {
            DiscoverAllValuables.Logger.LogWarning(
                $"Could not add map marker for {valuable.name}: {ex.Message}");
        }
    }

    private static bool MapAlreadyHasMarker(ValuableObject valuable)
    {
        foreach (MapValuable mv in Resources.FindObjectsOfTypeAll<MapValuable>())
        {
            if (mv != null && mv.target == valuable)
            {
                return true;
            }
        }
        return false;
    }
}

[HarmonyPatch(typeof(LevelGenerator), nameof(LevelGenerator.GenerateDone))]
internal static class LevelGeneratorGenerateDonePatch
{
    private static void Postfix()
    {
        if (ValuableDiscovery.DiscoverAllEnabled is { Value: true })
        {
            int valuables = ValuableDiscovery.DiscoverAllInScene();
            DiscoverAllValuables.Logger.LogInfo(
                $"DiscoverAllValuables: after level generation, updated {valuables} valuables including cosmetic boxes (map markers ensured for all alive instances).");
        }
    }
}

[HarmonyPatch(typeof(ValuableObject), nameof(ValuableObject.Start))]
internal static class ValuableObjectStartPatch
{
    private static void Postfix(ValuableObject __instance)
    {
        if (ValuableDiscovery.DiscoverAllEnabled is not { Value: true })
        {
            return;
        }
        if (!DiscoveryPatches.SafeLevelGenDone())
        {
            return;
        }
        ValuableDiscovery.DiscoverIfEnabled(__instance);
    }
}

internal static class DiscoveryPatches
{
    internal static bool SafeLevelGenDone()
    {
        try
        {
            return SemiFunc.LevelGenDone();
        }
        catch
        {
            return false;
        }
    }
}
