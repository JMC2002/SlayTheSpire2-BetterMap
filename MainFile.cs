using BetterMap.Core;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using System;
using System.Reflection;

namespace BetterMap;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    private static MegaCrit.Sts2.Core.Logging.Logger BaseLogger { get; } =
        new(VersionInfo.Name, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    // 抽离的日志函数：自动获取当前时间并添加 TAG
    public static void Log(string message, bool isError = false)
    {
        string timeTag = DateTime.Now.ToString("HH:mm:ss");
        string formattedMessage = $"[{timeTag}] [BetterMap] {message}";

        if (isError) BaseLogger.Error(formattedMessage);
        else BaseLogger.Info(formattedMessage);
    }

    public static void Initialize()
    {
        Log("======================================");
        Log("Better Map Mod 正在启动...");
        Log("======================================");

        Harmony harmony = new(VersionInfo.Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        Log("Harmony 补丁已应用。");
    }
}