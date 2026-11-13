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

                switch (stage)
                {
                    // 新一轮开始。默认不自动读档 —— 只在按下热键时读，
                    // 免得开局就把当前进度覆盖掉。想在开局自动读的把 AutoLoadOnStart 打开。
                    case global::Mayday.Airport.DirectorStage.PreShift:
                        CareerStore.LogNetworkMode();
                        if (Plugin.AutoLoadOnStart.Value) CareerStore.ApplyOnce();
                        else Plugin.LogSource.LogInfo(
                            "[MaydayCareer] 手动模式：按 " + Plugin.LoadKey.Value + " 读档（" + CareerStore.FilePath + "）");
                        break;

                    // 落地 / 下一段周转：都是成功飞行之后，可以安全存盘。
                    case global::Mayday.Airport.DirectorStage.Arrived:
                    case global::Mayday.Airport.DirectorStage.Turnaround:
                        if (Plugin.AutoSave.Value) CareerStore.Save();
                        break;

                    // 坠机轮结束：默认不存。
                    // 游戏的设计是失败即可整轮作废（GameOver 后立刻回 PreShift），
                    // 在这里存盘等于把"一轮作废"改成"检查点"，而且会当场污染下一轮。
                    // 想让坠机也计入生涯的，把配置里的 SaveOnCrash 打开。
                    case global::Mayday.Airport.DirectorStage.GameOver:
                        if (Plugin.AutoSave.Value && Plugin.SaveOnCrash.Value) CareerStore.Save();
                        else Plugin.LogSource.LogInfo("[MaydayCareer] 坠机，按设置不覆盖存档");
                        break;
                }
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
            if (Plugin.AutoLoadOnStart.Value) CareerStore.ApplyOnce();
        }
    }

    /// <summary>
    /// 每帧轮询热键。
    ///
    /// 挂在 Mirror.NetworkManager.Update 上：IL2CPP 下不能自建 MonoBehaviour，
    /// 而 Update 是唯一能确定每帧都执行的现成方法（NetworkManager 全程存在）。
    /// </summary>
    [HarmonyPatch]
    internal static class TickHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mirror.NetworkManager), "Update")]
        private static void Postfix()
        {
            Hotkey.Poll();
        }
    }
}
