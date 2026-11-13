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
        public const string Version = "0.2.0";

        internal static ManualLogSource LogSource;
        internal static ConfigEntry<bool> AutoSave;
        internal static ConfigEntry<bool> ClearBeforeLoad;
        internal static ConfigEntry<bool> Verbose;

        internal static ConfigEntry<bool> SaveOnCrash;

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
