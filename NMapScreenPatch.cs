using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using BetterMap.Core;
using Godot;
using JmcModLib.Utils;

namespace BetterMap.Patches;

[HarmonyPatch(typeof(NMapScreen))]
public static class NMapScreenPatch
{
    private static MapOverviewPanel? _panel;

    private static bool IsValid(GodotObject? obj)
    {
        if (obj == null) return false;
        try { return GodotObject.IsInstanceValid(obj); }
        catch { return false; }
    }

    private static bool IsSameNode(Node? left, Node? right)
    {
        if (left == null || right == null) return false;
        if (!IsValid(left) || !IsValid(right)) return false;

        try { return left == right || left.GetInstanceId() == right.GetInstanceId(); }
        catch { return false; }
    }

    private static bool IsPanelForScreen(MapOverviewPanel? panel, NMapScreen screen)
    {
        if (!IsValid(panel) || !IsValid(screen)) return false;

        try { return IsSameNode(panel!.GetParent(), screen); }
        catch { return false; }
    }

    private static MapOverviewPanel GetOrCreate(NMapScreen screen)
    {
        if (IsPanelForScreen(_panel, screen)) return _panel!;

        if (IsValid(_panel))
        {
            try
            {
                _panel!.HidePanel();
            }
            catch (System.Exception ex)
            {
                ModLogger.Warn($"清理旧小地图面板时发生异常: {ex.Message}");
            }

            ModLogger.Debug("检测到旧 NMapScreen 残留的小地图面板，重新挂载到当前地图屏幕。");
        }

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
            var panel = GetOrCreate(__instance);
            panel.BuildOverview(__instance);
            panel.ShowPanel();
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
            if (_panel is { } panel && IsPanelForScreen(panel, __instance))
                panel.HidePanel();
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"Close_Postfix 异常: {ex}");
        }
    }

    [HarmonyPatch(nameof(NMapScreen._ExitTree))]
    [HarmonyPostfix]
    public static void ExitTree_Postfix(NMapScreen __instance)
    {
        try
        {
            if (IsPanelForScreen(_panel, __instance))
            {
                _panel = null;
                ModLogger.Debug("NMapScreen 离开场景树，已清空小地图面板引用。");
            }
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"ExitTree_Postfix 异常: {ex}");
        }
    }
}
