using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using BetterMap.Core;
using Godot;

namespace BetterMap.Patches;

[HarmonyPatch(typeof(NMapScreen))]
public static class NMapScreenPatch
{
    private static MapOverviewPanel _panel;
    private static CanvasLayer _panelLayer; // layer=3，始终在所有游戏UI之上

    private static bool IsValid(GodotObject obj)
    {
        try { return GodotObject.IsInstanceValid(obj); }
        catch { return false; }
    }

    private static MapOverviewPanel GetOrCreate(NMapScreen screen)
    {
        if (_panel != null && IsValid(_panel)) return _panel;

        var root = screen.GetTree().Root;

        // 创建专属 CanvasLayer，layer=3 高于游戏所有UI层（layer=0,1,2）
        _panelLayer = new CanvasLayer
        {
            Name = "BetterMap_PanelLayer",
            Layer = 3,
        };
        root.AddChild(_panelLayer);

        _panel = MapOverviewPanel.Create();
        _panelLayer.AddChild(_panel);

        ModLogger.Info($"MapOverviewPanel 挂载到 CanvasLayer(layer=3) 下");

        _panel.EnsureBuilt();
        return _panel;
    }

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