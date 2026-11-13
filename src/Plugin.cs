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
        public const string Version = "0.8.0";

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
        internal static ConfigEntry<string> SaveKey;

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

            LogSource.LogInfo("[MaydayCareer] 已加载 v" + Version);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            LogSource.LogInfo("[MaydayCareer] 补丁数 = " + harmony.GetPatchedMethods().Count());
            CareerStore.Init();
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
