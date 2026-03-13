using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using BetterMap.Core;
using Godot;

namespace BetterMap.Patches;

[HarmonyPatch(typeof(NMapScreen))]
public static class NMapScreenPatch
{
    private static MapOverviewPanel _panel;

    private static bool IsValid(GodotObject obj)
    {
        try { return GodotObject.IsInstanceValid(obj); }
        catch { return false; }
    }

    private static MapOverviewPanel GetOrCreate(NMapScreen screen)
    {
        if (_panel != null && IsValid(_panel)) return _panel;

        _panel = MapOverviewPanel.Create();
        
        screen.AddChild(_panel);

        var mapContainer = screen.GetNodeOrNull<Control>("TheMap");
        if (mapContainer != null)
        {
            screen.MoveChild(_panel, mapContainer.GetIndex() + 1);
        }

        ModLogger.Info($"MapOverviewPanel 已挂载到 NMapScreen 内部");

        _panel.EnsureBuilt();
        return _panel;
    }

    // SetMap 完成后重建全景图
    [HarmonyPatch(nameof(NMapScreen.SetMap))]
    [HarmonyPostfix]
    public static void SetMap_Postfix(NMapScreen __instance)
    {
        ModLogger.Info("NMapScreen.SetMap Postfix");
        try
        {
            GetOrCreate(__instance).BuildOverview(__instance);
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"SetMap_Postfix 异常: {ex}");
        }
    }

    // Open 后显示面板
    [HarmonyPatch(nameof(NMapScreen.Open))]
    [HarmonyPostfix]
    public static void Open_Postfix(NMapScreen __instance)
    {
        ModLogger.Info("NMapScreen.Open Postfix");
        try
        {
            GetOrCreate(__instance).ShowPanel();
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"Open_Postfix 异常: {ex}");
        }
    }

    // Close 后隐藏面板
    [HarmonyPatch(nameof(NMapScreen.Close))]
    [HarmonyPostfix]
    public static void Close_Postfix(NMapScreen __instance)
    {
        ModLogger.Info("NMapScreen.Close Postfix");
        try
        {
            if (_panel != null && IsValid(_panel))
                _panel.HidePanel();
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"Close_Postfix 异常: {ex}");
        }
    }
}