using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MaydayCareer
{
    /// <summary>
    /// 给 Mayday Airways 补一层跨局进度存档。
    ///
    /// 游戏本身只持久化设置和装扮（settings.json / ui_hints.json），
    /// 金钱、欠款、已购升级都活在 GameState 这个 NetworkBehaviour 上，
    /// 随房间建立和销毁 —— 退出即清零。
    ///
    /// 本插件在每趟航班结算后把这些值写进 career.json，
    /// 在进入新一轮（DirectorStage.PreShift）时写回去。
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "local.mayday.career";
        public const string Name = "Mayday Career Save";
        public const string Version = "1.2.0";

        internal static ManualLogSource LogSource;
        internal static ConfigEntry<bool> AutoSave;
        internal static ConfigEntry<bool> ClearBeforeLoad;
        internal static ConfigEntry<bool> Verbose;

        internal static ConfigEntry<bool> SaveOnCrash;
        internal static ConfigEntry<bool> AutoLoadOnStart;
        internal static ConfigEntry<string> LoadKey;
        internal static ConfigEntry<bool> DumpOnLoad;
        internal static ConfigEntry<bool> SaveItems;
        internal static ConfigEntry<bool> LoadItems;
        internal static ConfigEntry<bool> SaveFuel;
        internal static ConfigEntry<bool> LoadFuel;
        internal static ConfigEntry<bool> SaveEngine;
        internal static ConfigEntry<bool> LoadEngine;
        internal static ConfigEntry<bool> SaveHull;
        internal static ConfigEntry<bool> LoadHull;
        internal static ConfigEntry<bool> GuardSave;
        internal static ConfigEntry<string> SaveKey;
        internal static ConfigEntry<bool> PerfWatch;

        public override void Load()
        {
            LogSource = base.Log;

            AutoSave = Config.Bind(
                "General",
                "AutoSave",
                true,
                "每趟航班结算后自动写入 career.json。关掉则只在手动调用时保存。");

            ClearBeforeLoad = Config.Bind(
                "General",
                "ClearBeforeLoad",
                false,
                "读档前先把本次开局给的初始资金清掉，再写入存档金额。\n" +
                "若读档后钱变成两倍，把这项改成 true。");

            Verbose = Config.Bind(
                "General",
                "Verbose",
                true,
                "把导演机的每次阶段切换打进日志。排查时开，稳定后可以关。");

            SaveOnCrash = Config.Bind(
                "General",
                "SaveOnCrash",
                false,
                "坠机（GameOver）时是否也存盘。\n" +
                "默认 false：存档停在最后一次成功落地，坠机不覆盖，退出重进可以回到摔之前。\n" +
                "改成 true 则坠机也计入生涯，钱亏了就真亏了。");

            AutoLoadOnStart = Config.Bind(
                "General",
                "AutoLoadOnStart",
                false,
                "进入新一轮（PreShift）时自动读档。\n" +
                "默认 false：只在你按下热键时才读，避免误覆盖当前进度。");

            LoadKey = Config.Bind(
                "General",
                "LoadKey",
                "F9",
                "手动读档热键。取 UnityEngine.InputSystem.Key 的名字，例如 F9 / F10 / K。");

            SaveKey = Config.Bind(
                "General",
                "SaveKey",
                "J",
                "手动存盘热键。在机舱里按一下就会立刻写 career.json，不用飞完一趟。\n" +
                "取 UnityEngine.InputSystem.Key 的名字，例如 J / F10 / L。\n" +
                "只在房主 / 服务端有效（纯客户端按了只打日志，不写盘）。");

            DumpOnLoad = Config.Bind(
                "Debug",
                "DumpOnLoad",
                true,
                "读档成功后把升级目录 / 物品目录 / 座位状态 / 补给站状态打进日志。\n" +
                "这是排查用的：interop 里读不到字符串字面量，「哪个升级会送什么实物」只能运行时打出来。\n" +
                "确认没问题之后关掉，日志会干净很多。");

            SaveItems = Config.Bind(
                "General",
                "SaveItems",
                true,
                "存盘时把客舱里花钱买的器材（灭火器、锤子、胶带、油桶、座位套件等）一起存进 career.json。\n" +
                "游戏本身完全不持久化世界物品，关掉这项就等于回到 0.6.0 的行为。\n" +
                "已排除：行李（Luggage 分类，游戏结算会自己清）、手持物、以及 Misc 分类的运行时临时物。");

            LoadItems = Config.Bind(
                "General",
                "LoadItems",
                true,
                "读档时把存档里的器材补回场景（幂等：已有同 id 物品就不再生成）。\n" +
                "站点重建（GroundCrew.ServerReanchor）之后会自动再补一次，最多重试 3 次。");

            SaveFuel = Config.Bind(
                "General",
                "SaveFuel",
                true,
                "存盘时记录左右油箱与储备油量（绝对升数）。");

            LoadFuel = Config.Bind(
                "General",
                "LoadFuel",
                true,
                "读档时把油量写回 FuelSystem。写入后 3 秒会自动回读校验并打日志。");

            SaveEngine = Config.Bind(
                "General",
                "SaveEngine",
                true,
                "存盘时记录每台发动机的状态（状态/故障位/转速/油量/温度/起火/电池）。");

            LoadEngine = Config.Bind(
                "General",
                "LoadEngine",
                true,
                "读档时写回发动机状态。写入后 3 秒自动回读校验。");

            SaveHull = Config.Bind(
                "General",
                "SaveHull",
                true,
                "存盘时记录机体状态：每块面板的损伤/脏污/破洞/补丁进度、区段维修阶段、掉漆与喷漆颜色。");

            LoadHull = Config.Bind(
                "General",
                "LoadHull",
                true,
                "读档时写回机体状态，写入后 3 秒自动回读校验。");

            GuardSave = Config.Bind(
                "General",
                "GuardSave",
                true,
                "存盘回退保护：当前局比存档更早（例如刚启动的新公司，第 0 段）时跳过存盘，\n" +
                "免得把真进度抹成初始值。想强行覆盖就设 false。");

            PerfWatch = Config.Bind(
                "General",
                "PerfWatch",
                true,
                "帧时间监测：每 10 秒汇总一次卡顿帧（>100ms）数量与当时阶段，\n" +
                "并对比本插件自身每帧耗时，用来判断卡顿来自游戏还是插件。\n" +
                "开销可忽略（每帧一次浮点比较）；没有卡顿的窗口不写任何日志。\n" +
                "确认过原因后可以关掉。");

            LogSource.LogInfo("[MaydayCareer] 已加载 v" + Version);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            LogSource.LogInfo("[MaydayCareer] 补丁数 = " + harmony.GetPatchedMethods().Count());
            CareerStore.Init();
            global::MaydayCareer.PerfWatch.Configure(PerfWatch.Value);
        }
    }

    internal static class EnumerableShim
    {
        public static int Count(this System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> e)
        {
            int n = 0;
            foreach (var _ in e) n++;
            return n;
        }
    }
}
