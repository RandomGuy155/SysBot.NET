using System;
using System.Collections.Generic;

namespace SysBot.Base;

public static class EchoUtil
{
    public static readonly ForwarderList<Action<string>> Forwarders = new();
    public static readonly ForwarderList<Action<string, string?>> TaggedForwarders = new();

    public static void Echo(string message, string? gameTag = null)
    {
        foreach (var fwd in Forwarders.Snapshot)
        {
            try
            {
                fwd(message);
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo($"Exception: {ex} occurred while trying to echo: {message} to the forwarder: {fwd}");
                LogUtil.LogSafe(ex);
            }
        }
        foreach (var fwd in TaggedForwarders.Snapshot)
        {
            try
            {
                fwd(message, gameTag);
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo($"Exception: {ex} occurred while trying to echo: {message} to the forwarder: {fwd}");
                LogUtil.LogSafe(ex);
            }
        }
        LogUtil.LogInfo(message);
    }
}
