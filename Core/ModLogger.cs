using System;
using Godot;

namespace BetterMap.Core;

/// <summary>
/// 封装日志打印，自动附带时间戳 [HH:mm:ss]
/// </summary>
public static class ModLogger
{
    private static MegaCrit.Sts2.Core.Logging.Logger _logger =
        new(VersionInfo.Name, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    private static string Timestamp => DateTime.Now.ToString("HH:mm:ss");

    public static void Info(string message)
    {
        _logger.Info($"[{Timestamp}] {message}");
    }

    public static void Warn(string message)
    {
        _logger.Warn($"[{Timestamp}] {message}");
    }

    public static void Error(string message)
    {
        _logger.Error($"[{Timestamp}] {message}");
    }
}