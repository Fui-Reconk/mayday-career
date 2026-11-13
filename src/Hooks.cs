using HarmonyLib;

namespace MaydayCareer
{
    /// <summary>
    /// 飞行结束出报告时存盘。
    /// ServerShowReport(FlightReport) 是 GameState 上的结算入口，
    /// 此时账本已经算完、钱已经入账，是航班结束的准确时刻。
    /// </summary>
    [HarmonyPatch]
    internal static class SaveHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.GameState), "ServerShowReport")]
        private static void Postfix()
        {
            if (!Plugin.AutoSave.Value) return;
            try { CareerStore.Save(); }
            catch (System.Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 存盘异常: " + e); }
        }
    }

    /// <summary>
    /// 整轮结束（RunSummary）时再存一次。
    /// 一趟 run 含多个航段，ServerShowReport 是按航段出的，
    /// 这个才是整轮收尾 —— 两者都存，后写的覆盖先写的。
    /// </summary>
    [HarmonyPatch]
    internal static class RunSummaryHook
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
    /// 开局重置后读档。
    /// ServerResetRun(int,int) 会把 Money/Leg/Upgrades 全部重置成新开局状态，
    /// 所以必须在它之后写回，否则会被覆盖 —— 和 ApplyDefaults 把
    /// maxConnections 写回 4 是同一类坑。
    /// </summary>
    [HarmonyPatch]
    internal static class LoadHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.GameState), "ServerResetRun")]
        private static void Postfix()
        {
            try { CareerStore.Apply(); }
            catch (System.Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 读档异常: " + e); }
        }
    }
}
