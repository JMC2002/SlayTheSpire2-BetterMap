using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using BetterMap.Core;
using Godot;
using JmcModLib.Utils;
using System.Reflection;

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

    [HarmonyPatch(nameof(NMapScreen._Input))]
    [HarmonyPrefix]
    public static bool Input_Prefix(NMapScreen __instance, InputEvent inputEvent)
    {
        try
        {
            if (IsPanelForScreen(_panel, __instance) && _panel!.TryHandleMinimapDrawingInput(inputEvent))
            {
                return false;
            }
        }
        catch (System.Exception ex)
        {
            ModLogger.Warn($"处理小地图涂鸦输入时发生异常: {ex.Message}");
        }

        return true;
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

    private static void ClearPanelReference(NMapScreen screen)
    {
        try
        {
            if (IsPanelForScreen(_panel, screen))
            {
                _panel = null;
                ModLogger.Debug("NMapScreen 生命周期结束，已清空小地图面板引用。");
            }
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"清理 NMapScreen 小地图面板引用时发生异常: {ex}");
        }
    }

    [HarmonyPatch]
    private static class ExitLifecyclePatch
    {
        // Godot Object.NOTIFICATION_PREDELETE。0.110 的 NMapScreen 在这个通知中执行旧版 _ExitTree 的清理。
        private const int PredeleteNotification = 1;

        private static MethodBase TargetMethod()
        {
            // 0.107.1–0.109.1：NMapScreen 自己重写 _ExitTree。
            MethodInfo? exitTree = AccessTools.DeclaredMethod(
                typeof(NMapScreen),
                nameof(NMapScreen._ExitTree));
            if (exitTree != null)
            {
                return exitTree;
            }

            // 0.110：删除 _ExitTree 重写，改在 _Notification(int what) 的 predelete 分支清理。
            return AccessTools.DeclaredMethod(
                       typeof(NMapScreen),
                       nameof(NMapScreen._Notification),
                       [typeof(int)])
                   ?? throw new MissingMethodException(
                       typeof(NMapScreen).FullName,
                       $"{nameof(NMapScreen._ExitTree)} / {nameof(NMapScreen._Notification)}");
        }

        [HarmonyPostfix]
        private static void Postfix(NMapScreen __instance, object[] __args)
        {
            // 旧版 _ExitTree 没有参数；0.110 的 _Notification 只在 predelete 通知时等价于旧生命周期。
            if (__args.Length > 0 &&
                (__args[0] is not int what || what != PredeleteNotification))
            {
                return;
            }

            ClearPanelReference(__instance);
        }
    }
}
