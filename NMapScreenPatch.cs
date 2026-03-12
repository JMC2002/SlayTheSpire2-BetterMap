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

        // 挂到场景树根节点，完全独立于游戏UI层级
        // 避免父节点 ProcessMode/Visible 变化影响我们的节点
        var root = screen.GetTree().Root;
        root.AddChild(_panel);
        ModLogger.Info($"MapOverviewPanel 挂载到: {root.Name} (SceneTree Root)");

        _panel.EnsureBuilt();
        return _panel;
    }

    // SetMap 完成后重建全景图（地图数据已就绪）
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

    // Open 后显示面板（数据在 SetMap 时已经建好）
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