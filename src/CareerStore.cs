using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        public int destinationAirport;
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
            sb.Append("    \"destinationAirport\": ").Append(destinationAirport).Append(",\n");
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
                destinationAirport = Json.GetInt(d, "destinationAirport", 0),
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
                destinationAirport = gs.DestinationAirport,
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

        /// <summary>
        /// 把当前网络身份打进日志。
        /// ServerReport / SetStage 都是服务端方法，只有房主才会执行；
        /// 如果玩家是纯客户端，这里会显示 server=False，那就说明补丁本来就不该触发。
        /// </summary>
        public static void LogNetworkMode()
        {
            try
            {
                bool server = global::Mirror.NetworkServer.active;
                bool client = global::Mirror.NetworkClient.active;
                string mode = server && client ? "host（房主）"
                            : server ? "dedicated server"
                            : client ? "client（客户端，存档不会触发）"
                            : "offline（没有任何网络会话）";
                Plugin.LogSource.LogInfo("[MaydayCareer] 网络模式: " + mode
                    + " | GameState=" + (global::Mayday.GameState.Instance != null ? "有" : "空"));
            }
            catch (System.Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 探测网络模式失败: " + e.Message);
            }
        }

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

        /// <summary>手动读档：无视一次性标记，立即应用。</summary>
        public static void ApplyForce()
        {
            _applied = true;
            Apply();
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

            // 进度字段：都是 SyncVar 属性，直接写会标脏并同步给客户端。
            // 逐个 try，某个失败不影响其他字段。
            Try("RunSeed", () => gs.RunSeed = s.runSeed);
            Try("Leg", () => gs.Leg = s.leg);
            Try("CurrentAirport", () => gs.CurrentAirport = s.currentAirport);
            Try("DestinationAirport", () => gs.DestinationAirport = s.destinationAirport);
            Try("Rating", () => gs.Rating = s.rating);
            Try("Difficulty", () => gs.Difficulty = s.difficulty);
            Try("TotalEarned", () => gs.TotalEarned = s.totalEarned);
            Try("TotalPassengers", () => gs.TotalPassengers = s.totalPassengers);

            Plugin.LogSource.LogInfo(string.Format(
                "[MaydayCareer] 已读档 钱 {0} -> {1}（存档 {2}） 债 {3} 第{4}段 机场{5} 升级 +{6}",
                before, gs.Money, s.money, gs.Debt, gs.Leg, gs.CurrentAirport, added));
            RebuildSeats();
            DumpCatalog();
        }

        // ---------- 实体重建 ----------

        /// <summary>
        /// 把"已购升级"重新落到实体上。
        ///
        /// 为什么必须有这一步：GameState.Upgrades 只是一本购买台账，
        /// 升级的真实效果是购买那一刻由别的系统生成的世界对象。
        /// 多数系统自己有定时轮询会自愈（GalleySystem.PollUpgrades、
        /// CargoNets.ownedCheck、CargoLoaders.ownedCheck），
        /// 但座位高级化没有任何轮询 —— 只有玩家买下升级时才会调一次
        /// PassengerDirector.ServerSetPremium。所以只把列表写回去，
        /// 座位这一路永远没人读，表现就是"升级买了、座位没变"。
        ///
        /// 这里全程用 public API，不反射私有方法：
        ///   PassengerDirector.Instance              public static get
        ///   PassengerDirector.premiumSeats          SyncVar 属性
        ///   PassengerDirector.RowCount              public get
        ///   PassengerDirector.IsRowInstalled(int)   public
        ///   SeatRow.Get(int)                        public static
        ///   SeatRow.ServerSetPremium(bool)          public
        ///
        /// 只标记"已经装好的排"，绝不新增排 —— 新增会改变座位容量，
        /// 破坏乘客分配和已经结算过的座位数。
        /// </summary>
        public static void RebuildSeats()
        {
            try
            {
                var pd = global::Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null)
                {
                    Plugin.LogSource.LogWarning(
                        "[MaydayCareer] 座位重建跳过：PassengerDirector 还没就绪（站点/机舱未建），进机舱后再按一次读档键");
                    return;
                }

                var gs = global::Mayday.GameState.Instance;
                if (gs == null)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 座位重建跳过：GameState 为空");
                    return;
                }

                // 升级 id 取游戏自己的静态常量，不硬编码 "seat_premium"
                bool want = false;
                try { want = gs.HasUpgrade(global::Mayday.Airport.UpgradeCatalog.SeatPremium); }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 读取 SeatPremium 升级状态失败: " + e.Message); }

                // SyncVar：写下去会标脏并同步给客户端，队友看得到
                try { pd.premiumSeats = want; }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 写 premiumSeats 失败: " + e.Message); }

                int rows = 0;
                try { rows = pd.RowCount; }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 读取 RowCount 失败: " + e.Message); }

                // 兜底上限：万一 RowCount 返回到离谱的值，别在这里死循环
                if (rows < 0) rows = 0;
                if (rows > 64) rows = 64;

                int done = 0;
                for (int i = 0; i < rows; i++)
                {
                    // 每排单独 try：某一排坏了不影响其余排
                    try
                    {
                        if (!pd.IsRowInstalled(i)) continue;
                        var row = global::Mayday.Passengers.SeatRow.Get(i);
                        if (row == null) continue;
                        row.ServerSetPremium(want);
                        done++;
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning(string.Format(
                            "[MaydayCareer] 第 {0} 排座位设置失败: {1}", i, e.Message));
                    }
                }

                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 座位重建 已装{0}排 premium={1}", done, want));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 座位重建异常: " + e);
            }
        }

        // ---------- 诊断导出 ----------

        const string DumpTag = "[MaydayCareer][dump] ";

        /// <summary>
        /// 排查用：把升级目录 / 物品目录 / 座位状态 / 补给站状态整个打一遍。
        ///
        /// 为什么需要它：interop 是托管 stub，不含字符串字面量，
        /// 所以 UpgradeInfo.deliverItem 之类"哪个升级会送什么实物"的值
        /// 离线根本读不到，只能运行时打出来。这份日志是后续做
        /// "实物投递重放"的唯一数据来源。
        ///
        /// 稳定之后把 DumpOnLoad 关掉即可。整段全包在 try/catch 里，
        /// 任何一步失败都只打 warn，绝不会把读档搞崩。
        /// </summary>
        public static void DumpCatalog()
        {
            if (!Plugin.DumpOnLoad.Value) return;
            try { Plugin.LogSource.LogInfo(DumpTag + "===== 开始导出目录 ====="); }
            catch { return; }

            DumpUpgradeCatalog();
            DumpItemCatalog();
            DumpSeatState();
            DumpSupplyShop();

            Plugin.LogSource.LogInfo(DumpTag + "===== 导出结束 =====");
        }

        static void DumpUpgradeCatalog()
        {
            try
            {
                var list = AsList<global::Mayday.Airport.UpgradeInfo>(global::Mayday.Airport.UpgradeCatalog.All);
                if (list == null)
                {
                    Plugin.LogSource.LogWarning(DumpTag + "升级目录为空（UpgradeCatalog.All 拿不到 List），跳过");
                    return;
                }

                int n = list.Count;
                Plugin.LogSource.LogInfo(DumpTag + "升级目录 共 " + n + " 项");
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var u = list[i];
                        if (u == null) { Plugin.LogSource.LogInfo(DumpTag + "升级 [null]"); continue; }

                        string deliver = null;
                        try { deliver = u.deliverItem; } catch { }
                        if (string.IsNullOrEmpty(deliver)) deliver = "-";

                        string req = null;
                        try { req = u.requires; } catch { }
                        if (string.IsNullOrEmpty(req)) req = "-";

                        Plugin.LogSource.LogInfo(string.Format(
                            "{0}升级 id={1} 价格={2} 分类={3} 上限={4} 可重复={5} 消耗品={6} 投递物={7} 投递数={8} 染色={9} 前置={10}",
                            DumpTag,
                            u.id, u.price, u.category, u.maxLevel, u.Repeatable,
                            u.consumable, deliver, u.deliverCount, u.deliverTinted, req));
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning(DumpTag + "第 " + i + " 项升级读不出来: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "升级目录导出失败: " + e.Message);
            }
        }

        static void DumpItemCatalog()
        {
            try
            {
                var list = AsList<global::Mayday.ItemInfo>(global::Mayday.ItemCatalog.All);
                if (list == null)
                {
                    Plugin.LogSource.LogWarning(DumpTag + "物品目录为空（ItemCatalog.All 拿不到 List），跳过");
                    return;
                }

                int n = list.Count;
                Plugin.LogSource.LogInfo(DumpTag + "物品目录 共 " + n + " 项");
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var it = list[i];
                        if (it == null) { Plugin.LogSource.LogInfo(DumpTag + "物品 [null]"); continue; }
                        Plugin.LogSource.LogInfo(string.Format(
                            "{0}物品 id={1} 价格={2} 商店={3} 分类={4}",
                            DumpTag, it.id, it.price, it.shop, it.category));
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning(DumpTag + "第 " + i + " 项物品读不出来: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "物品目录导出失败: " + e.Message);
            }
        }

        static void DumpSeatState()
        {
            try
            {
                var pd = global::Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null)
                {
                    Plugin.LogSource.LogInfo(DumpTag + "座位 状态：PassengerDirector 为空（机舱未就绪）");
                    return;
                }

                int rows = 0;
                try { rows = pd.RowCount; } catch { }
                bool prem = false;
                try { prem = pd.PremiumSeats; } catch { }
                Plugin.LogSource.LogInfo(string.Format(DumpTag + "座位 排数={0} 高级座={1}", rows, prem));

                if (rows < 0) rows = 0;
                if (rows > 64) rows = 64;
                for (int i = 0; i < rows; i++)
                {
                    try
                    {
                        Plugin.LogSource.LogInfo(string.Format(
                            DumpTag + "座位 第{0}排 已装={1}", i, pd.IsRowInstalled(i)));
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning(DumpTag + "第 " + i + " 排状态读不出来: " + e.Message);
                    }
                }

                // ServerAll 是 interop 里的静态列表，可见性不确定 → 走反射兜底
                int kits = TryCountStatic(typeof(global::Mayday.Passengers.SeatKitItem), "ServerAll");
                Plugin.LogSource.LogInfo(DumpTag + "座位 套件在场数量=" + (kits < 0 ? "读不到" : kits.ToString()));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "座位状态导出失败: " + e.Message);
            }
        }

        static void DumpSupplyShop()
        {
            try
            {
                var shop = global::Mayday.Airport.SupplyShop.Instance;
                if (shop == null)
                {
                    Plugin.LogSource.LogInfo(DumpTag + "补给站 为空（机场站点未就绪）");
                    return;
                }

                // backlog 大概率是 private [SerializeField]，编译期访问不到 → 反射兜底
                int backlog = TryCount(shop, typeof(global::Mayday.Airport.SupplyShop), "backlog");
                Plugin.LogSource.LogInfo(DumpTag + "补给站 有 投递积压=" + (backlog < 0 ? "读不到" : backlog.ToString()));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "补给站状态导出失败: " + e.Message);
            }
        }

        /// <summary>
        /// 把 Catalog.All 这类 IReadOnlyList&lt;T&gt; 转成真正带 Count/索引器的 List&lt;T&gt;。
        /// interop 生成的 IReadOnlyList 接口桩上没有 Count，只能转回 List 再用。
        /// 转不了返回 null（调用方打 warn 跳过）。
        /// </summary>
        static Il2CppSystem.Collections.Generic.List<T> AsList<T>(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase src)
            where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if (src == null) return null;
            try
            {
                var direct = src as Il2CppSystem.Collections.Generic.List<T>;
                if (direct != null) return direct;
                return src.TryCast<Il2CppSystem.Collections.Generic.List<T>>();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "List 转换失败: " + e.Message);
                return null;
            }
        }

        /// <summary>反射读某个实例/静态成员里的 Count，读不到返回 -1。</summary>
        static int TryCount(object target, Type type, string name)
        {
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic |
                            (target == null ? BindingFlags.Static : BindingFlags.Instance);

                object v = null;
                var pi = type.GetProperty(name, flags);
                if (pi != null) { try { v = pi.GetValue(target); } catch { } }
                if (v == null)
                {
                    var fi = type.GetField(name, flags);
                    if (fi != null) { try { v = fi.GetValue(target); } catch { } }
                }
                if (v == null) return -1;

                var cp = v.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                if (cp == null) return -1;
                return Convert.ToInt32(cp.GetValue(v));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "反射读 " + name + " 失败: " + e.Message);
                return -1;
            }
        }

        static int TryCountStatic(Type type, string name) => TryCount(null, type, name);

        static void Try(string what, Action act)
        {
            try { act(); }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 还原 " + what + " 失败: " + e.Message); }
        }
    }
}
