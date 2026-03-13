using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using BetterMap.Core;
using Godot;

namespace BetterMap.Patches;

[HarmonyPatch(typeof(NMapScreen))]
public static class NMapScreenPatch
{
    private static MapOverviewPanel _panel;
    private static CanvasLayer _panelLayer;

    private static bool IsValid(GodotObject obj)
    {
        try { return GodotObject.IsInstanceValid(obj); }
        catch { return false; }
    }

    private static MapOverviewPanel GetOrCreate(NMapScreen screen)
    {
        if (_panel != null && IsValid(_panel)) return _panel;

        var globalUi = screen.GetParent(); // NGlobalUi

        _panelLayer = new CanvasLayer
        {
            Name = "BetterMap_PanelLayer",
            Layer = 2,
        };

        // 挂到 NGlobalUi 下，AddChild 默认加到最后
        // _clAbove 是在 SetupCanvas 里动态创建并 AddChild 的，
        // 所以 _panelLayer 先加入，_clAbove 后加入，
        // 同 layer=2 内 _clAbove 排在后面 → 后渲染 → 覆盖我们的小地图
        globalUi.AddChild(_panelLayer);

        _panel = MapOverviewPanel.Create();
        _panelLayer.AddChild(_panel);

        ModLogger.Info($"MapOverviewPanel 挂载到 NGlobalUi 下 CanvasLayer(layer=2)");

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