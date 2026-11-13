using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MaydayCareer
{
    /// <summary>career.json 的模型。</summary>
    internal sealed class CareerState
    {
        public int version = 1;
        public string savedAt = "";

        public int money;
        public int debt;
        public int leg;
        public int runSeed;
        public int currentAirport;
        public int totalEarned;
        public int totalPassengers;
        public float rating;
        public float difficulty;
        public List<string> upgrades = new List<string>();

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("    \"version\": ").Append(version).Append(",\n");
            sb.Append("    \"savedAt\": "); Json.Escape(sb, savedAt ?? ""); sb.Append(",\n");
            sb.Append("    \"money\": ").Append(money).Append(",\n");
            sb.Append("    \"debt\": ").Append(debt).Append(",\n");
            sb.Append("    \"leg\": ").Append(leg).Append(",\n");
            sb.Append("    \"runSeed\": ").Append(runSeed).Append(",\n");
            sb.Append("    \"currentAirport\": ").Append(currentAirport).Append(",\n");
            sb.Append("    \"totalEarned\": ").Append(totalEarned).Append(",\n");
            sb.Append("    \"totalPassengers\": ").Append(totalPassengers).Append(",\n");
            sb.Append("    \"rating\": ").Append(Json.Num(rating)).Append(",\n");
            sb.Append("    \"difficulty\": ").Append(Json.Num(difficulty)).Append(",\n");
            sb.Append("    \"upgrades\": [");
            for (int i = 0; i < upgrades.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                Json.Escape(sb, upgrades[i]);
            }
            sb.Append("]\n}\n");
            return sb.ToString();
        }

        public static CareerState FromJson(string text)
        {
            var d = Json.Parse(text) as Dictionary<string, object>;
            if (d == null) return null;
            var s = new CareerState
            {
                version = Json.GetInt(d, "version", 1),
                savedAt = Json.GetStr(d, "savedAt", ""),
                money = Json.GetInt(d, "money", 0),
                debt = Json.GetInt(d, "debt", 0),
                leg = Json.GetInt(d, "leg", 0),
                runSeed = Json.GetInt(d, "runSeed", 0),
                currentAirport = Json.GetInt(d, "currentAirport", 0),
                totalEarned = Json.GetInt(d, "totalEarned", 0),
                totalPassengers = Json.GetInt(d, "totalPassengers", 0),
                rating = Json.GetFloat(d, "rating", 0f),
                difficulty = Json.GetFloat(d, "difficulty", 1f),
                upgrades = Json.GetStrList(d, "upgrades"),
            };
            return s;
        }
    }

    internal static class CareerStore
    {
        static string _dir;
        static string _file;

        public static string FilePath => _file;

        public static void Init()
        {
            _dir = ResolveDir();
            _file = Path.Combine(_dir, "career.json");
            Plugin.LogSource.LogInfo("[MaydayCareer] 存档路径 = " + _file);
        }

        static string ResolveDir()
        {
            // Unity 在 Windows 上是 %USERPROFILE%\AppData\LocalLow\<Company>\<Product>，
            // 公司名取自 app.info 第一行：Lyngo Games
            string profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
                return Path.Combine(profile, "AppData", "LocalLow", "Lyngo Games", "Mayday Airways");

            string low = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "..", "LocalLow", "Lyngo Games", "Mayday Airways");
            return Path.GetFullPath(low);
        }

        // ---------- 存 ----------

        public static void Save()
        {
            var gs = global::Mayday.GameState.Instance;
            if (gs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] GameState 为空，跳过存盘"); return; }

            var s = new CareerState
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                money = gs.Money,
                debt = gs.Debt,
                leg = gs.Leg,
                runSeed = gs.RunSeed,
                currentAirport = gs.CurrentAirport,
                totalEarned = gs.TotalEarned,
                totalPassengers = gs.TotalPassengers,
                rating = gs.Rating,
                difficulty = gs.Difficulty,
                upgrades = ReadUpgrades(gs),
            };

            Directory.CreateDirectory(_dir);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, s.ToJson(), new UTF8Encoding(false));
            if (File.Exists(_file)) File.Delete(_file);
            File.Move(tmp, _file);

            Plugin.LogSource.LogInfo(string.Format(
                "[MaydayCareer] 已保存 钱={0} 债={1} 第{2}段 升级={3} -> {4}",
                s.money, s.debt, s.leg, s.upgrades.Count, _file));
        }

        static List<string> ReadUpgrades(global::Mayday.GameState gs)
        {
            var outList = new List<string>();
            try
            {
                var list = gs.Upgrades;
                if (list == null) return outList;
                int n = list.Count;
                for (int i = 0; i < n; i++)
                {
                    string id = list[i]?.ToString();
                    if (!string.IsNullOrEmpty(id)) outList.Add(id);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 读取升级列表失败: " + e.Message);
            }
            return outList;
        }

        // ---------- 读 ----------

        public static CareerState Load()
        {
            try
            {
                if (!File.Exists(_file)) { Plugin.LogSource.LogInfo("[MaydayCareer] 没有存档，按新公司开局"); return null; }
                var s = CareerState.FromJson(File.ReadAllText(_file, Encoding.UTF8));
                if (s == null) { Plugin.LogSource.LogWarning("[MaydayCareer] 存档解析失败"); return null; }
                return s;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 读档失败: " + e);
                return null;
            }
        }

        static bool _applied;

        /// <summary>读档只做一次，避免阶段机每次回到 PreShift 都重新加钱。</summary>
        public static void ApplyOnce()
        {
            if (_applied) return;
            _applied = true;
            Apply();
        }

        public static void ResetApplyFlag()
        {
            _applied = false;
        }

        public static void Apply()
        {
            var s = Load();
            if (s == null) return;

            var gs = global::Mayday.GameState.Instance;
            if (gs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] GameState 为空，无法读档"); return; }

            int before = gs.Money;

            // 走游戏自己的 API，而不是直接写字段：
            // ServerAddMoney 会触发 MoneyChanged 事件、刷新 UI、计入账本。
            if (Plugin.ClearBeforeLoad.Value)
                gs.ServerAddMoney(-gs.Money, "career.load.reset");

            int delta = s.money - gs.Money;
            if (delta != 0) gs.ServerAddMoney(delta, "career.load");

            int debtDelta = s.debt - gs.Debt;
            if (debtDelta != 0) gs.ServerAddDebt(debtDelta);

            int added = 0;
            foreach (var id in s.upgrades)
            {
                if (!gs.HasUpgrade(id)) { gs.ServerAddUpgrade(id); added++; }
            }

            Plugin.LogSource.LogInfo(string.Format(
                "[MaydayCareer] 已读档 钱 {0} -> {1}（存档 {2}）升级 +{3}",
                before, gs.Money, s.money, added));
        }
    }
}
