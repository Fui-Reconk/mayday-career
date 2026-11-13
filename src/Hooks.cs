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
                    //
                    // 开了 AutoLoadOnStart 的话，ResetRunHook 通常已经读过一次并置上了
                    // 一次性标记，这里的 ApplyOnce 会直接返回 —— 留着它当兜底：
                    // 万一某个版本 ServerResetRun 没被调用，读档至少还能在 PreShift 发生。
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
    /// 开局重置之后立刻读档（只在 AutoLoadOnStart 打开时生效）。
    ///
    /// 为什么挂这里而不是等 PreShift：
    /// PreShift 是整条启动链的最后一环。在它之前
    /// GameState.ServerResetRun -> GameDirector.OnStartServer
    /// -> 各 GroundService.OnStartServer -> PassengerDirector.ServerInitSeats
    /// 都已经跑完，而且全都是在"升级列表为空"的前提下做的判断。
    /// 想让这些系统读到正确数据，就得把读档提到它们之前、重置之后。
    ///
    /// 默认关着（AutoLoadOnStart 默认 false），手动热键那条路完全不受影响。
    ///
    /// 已知风险：如果某个版本里 ServerResetRun 每腿都会调用，这里就会每腿重新读一次档
    /// （金钱会被拉回存档值）。看到日志里这行反复出现，就把 AutoLoadOnStart 关掉。
    /// </summary>
    [HarmonyPatch]
    internal static class ResetRunHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.GameState), "ServerResetRun")]
        private static void Postfix()
        {
            if (!Plugin.AutoLoadOnStart.Value) return;

            try
            {
                CareerStore.LogNetworkMode();

                // 重置的当口 GameState 可能还没挂上 —— 这时读档没有意义，
                // 留给 PreShift 的 ApplyOnce 兜底。
                if (global::Mayday.GameState.Instance == null)
                {
                    Plugin.LogSource.LogWarning(
                        "[MaydayCareer] ServerResetRun 后自动读档跳过：GameState 还没就绪");
                    return;
                }

                Plugin.LogSource.LogInfo("[MaydayCareer] ServerResetRun 之后自动读档（AutoLoadOnStart=true）");
                CareerStore.ApplyForce();
            }
            catch (System.Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] ServerResetRun 自动读档异常: " + e);
            }
        }
    }

    /// <summary>
    /// 站点重建完成后再补一次物品。
    ///
    /// 为什么不用 GroundCrew.Reanchored 静态事件：
    /// 那是个 Il2CppSystem.Action&lt;int&gt;，在 IL2CPP 下挂托管委托要过
    /// Il2CppInterop 的委托封送，容易在运行时炸；而 ServerReanchor 是
    /// 那个事件的发出点（public，签名 Void(Int32, ReanchorKind)），
    /// 用 Harmony postfix 挂在它后面，语义完全等价、时机也一样
    /// （所有 GroundService.ServerOnReanchor 都跑完了）。
    ///
    /// 这个时机晚于 GameDirector.CleanupItems()，所以补出来的物品不会被清场带走。
    /// </summary>
    [HarmonyPatch]
    internal static class ReanchorHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Airport.GroundCrew), "ServerReanchor")]
        private static void Postfix()
        {
            try { CareerStore.OnSiteReanchored(); }
            catch (System.Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 站点重建后补物品异常: " + e); }
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
