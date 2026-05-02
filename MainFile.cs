using Godot;
using HarmonyLib;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Modding;
using System.Reflection;
using ModVersionInfo = BetterMap.Core.VersionInfo;

namespace BetterMap;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public static void Initialize()
    {
        JmcModLib.Core.ModRegistry.Register(true, ModVersionInfo.Name, ModVersionInfo.Name, ModVersionInfo.Version)?
            .RegisterLogger(uIFlags: LogConfigUIFlags.All)
            .UseConfig()
            .Done();

        ModLogger.Info("======================================");
        ModLogger.Info("Better Map Mod 正在启动...");
        ModLogger.Info("======================================");

        Harmony harmony = new(ModVersionInfo.Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        ModLogger.Info("Harmony 补丁已应用。");
    }
}
