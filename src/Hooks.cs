using HarmonyLib;

namespace MaydayCareer
{
    /// <summary>
    /// 飞行结束存盘。
    ///
    /// 原以为该挂 GameState.ServerShowReport，实测那方法整局都没被调用
    /// （跑完一趟后日志里一条都没有）。真正跑结算的是导演机的 ServerReport()，
    /// 它在 Flight -> Arrived 的转换里触发。
    /// </summary>
    [HarmonyPatch]
    internal static class SaveHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Airport.GameDirector), "ServerReport")]
        private static void Postfix()
        {
            if (!Plugin.AutoSave.Value) return;
            try { CareerStore.Save(); }
            catch (System.Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 存盘异常: " + e); }
        }
    }

    /// <summary>
    /// 整轮结束再存一次（GameOver 那条线）。
    /// </summary>
    [HarmonyPatch]
    internal static class RunEndHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.GameState), "ServerShowRunSummary")]
        private static void Postfix()
        {
            if (!Plugin.AutoSave.Value) return;
            try { CareerStore.Save(); }
            catch (System.Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 存盘异常: " + e); }
        }
    }

    /// <summary>
    /// 阶段机监听。
    ///
    /// 两个用途：
    ///   1. Verbose 时把每次阶段切换打进日志 —— 没有这个根本不知道流程走到哪
    ///   2. 进入 PreShift（新的一轮开始）时读档，用一次性标记避免重复应用
    ///
    /// 不取 SetStage 的参数，直接读 __instance.Stage，避免 IL2CPP 下参数名对不上。
    /// </summary>
    [HarmonyPatch]
    internal static class StageHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Airport.GameDirector), "SetStage")]
        private static void Postfix(global::Mayday.Airport.GameDirector __instance)
        {
            try
            {
                var stage = __instance.Stage;
                if (Plugin.Verbose.Value)
                    Plugin.LogSource.LogInfo("[MaydayCareer] 阶段 -> " + stage);

                if (stage == global::Mayday.Airport.DirectorStage.PreShift)
                    CareerStore.ApplyOnce();
            }
            catch (System.Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 阶段处理异常: " + e);
            }
        }
    }

    /// <summary>
    /// 手动重开一轮时清除一次性标记，让读档可以再执行一次。
    /// </summary>
    [HarmonyPatch]
    internal static class RestartHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Airport.GameDirector), "ServerRestartRun")]
        private static void Postfix()
        {
            CareerStore.ResetApplyFlag();
            try { CareerStore.ApplyOnce(); }
            catch (System.Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 读档异常: " + e); }
        }
    }
}
