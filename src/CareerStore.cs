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
        public int version = 2;
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

        // 燃油：fuelL/fuelR 是 SyncVar（NetworkfuelL/NetworkfuelR），
        // 记绝对升数而不是比例 —— 油箱容量会随 upgrades 变化。
        public float fuelL;
        public float fuelR;
        public float reserveTank;
        public float reserveSync;
        public float fuelCapacity;
        public List<float> tanks = new List<float>();
        public bool hasFuelField;

        // 机体损伤：breaches 是 16 位掩码，第 i 位 = 第 i 个破洞槽位开着。
        // jetHp 是分段血量，jetSync/sectSync 是游戏内部的同步位图。
        public int breaches;
        public List<float> jetHp = new List<float>();
        public string jetSync = "0";
        public string sectSync = "0";
        public bool hasHullField;

        // 发动机：每台一条记录。state/faults/rpm/cowl 都是 SyncVar，
        // oil/temp/fire 是本地模拟值（写它们才留得住，写派生值没用 —— 燃油那轮的教训）。
        public List<EngineRec> engines = new List<EngineRec>();
        public bool hasEngineField;

        public List<string> upgrades = new List<string>();

        // 座排：rowMask 是权威位图（PassengerDirector.rowMask 的 SyncVar），
        // seatRows 是逐位解出来的结果，留着当兜底和排查用。
        // seatRowCount 只用来打日志 —— 它的语义（已装数 or 总槽位数）还没验死，
        // 绝不能拿它当"该装几排"的依据。
        public int seatRowMask;
        public int seatRowCount;
        public List<int> seatRows = new List<int>();

        // 世界物品快照（不含行李和手持物）
        public List<ItemRecord> items = new List<ItemRecord>();

        // 下面两个只是"这份 JSON 里到底有没有这两个字段"的标记，不参与序列化。
        // 用途：区分"v1 旧存档压根没这些数据"和"有数据但恰好是空的" ——
        // 前者要显式警告，后者是正常状态，不能混为一谈。
        public bool hasSeatRowField;
        public bool hasItemsField;

        public sealed class EngineRec
        {
            public int index;
            public int state;
            public int faults;
            public int cowl;
            public int bat;
            public int swap;
            public int swapSerial;
            public float rpm;
            public float oil;
            public float temp;
            public float fire;
            public float batt;
            public float swapP;
        }

        public static List<EngineRec> ParseEngines(Dictionary<string, object> d)
        {
            var list = new List<EngineRec>();
            foreach (var o in Json.GetObjList(d, "engines"))
            {
                list.Add(new EngineRec
                {
                    index = Json.GetInt(o, "index", 0),
                    state = Json.GetInt(o, "state", 0),
                    faults = Json.GetInt(o, "faults", 0),
                    cowl = Json.GetInt(o, "cowl", 0),
                    bat = Json.GetInt(o, "bat", 0),
                    swap = Json.GetInt(o, "swap", 0),
                    swapSerial = Json.GetInt(o, "swapSerial", 0),
                    rpm = Json.GetFloat(o, "rpm", 0f),
                    oil = Json.GetFloat(o, "oil", 0f),
                    temp = Json.GetFloat(o, "temp", 0f),
                    fire = Json.GetFloat(o, "fire", 0f),
                    batt = Json.GetFloat(o, "batt", 0f),
                    swapP = Json.GetFloat(o, "swapP", 0f),
                });
            }
            return list;
        }

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
            sb.Append("    \"fuelL\": ").Append(Json.Num(fuelL)).Append(",\n");
            sb.Append("    \"fuelR\": ").Append(Json.Num(fuelR)).Append(",\n");
            sb.Append("    \"reserveTank\": ").Append(Json.Num(reserveTank)).Append(",\n");
            sb.Append("    \"fuelCapacity\": ").Append(Json.Num(fuelCapacity)).Append(",\n");
            sb.Append("    \"reserveSync\": ").Append(Json.Num(reserveSync)).Append(",\n");
            sb.Append("    \"tanks\": [");
            for (int ti = 0; ti < tanks.Count; ti++)
            {
                if (ti > 0) sb.Append(", ");
                sb.Append(Json.Num(tanks[ti]));
            }
            sb.Append("],\n");
            sb.Append("    \"breaches\": ").Append(breaches).Append(",\n");
            sb.Append("    \"jetHp\": [");
            for (int hi2 = 0; hi2 < jetHp.Count; hi2++)
            {
                if (hi2 > 0) sb.Append(", ");
                sb.Append(Json.Num(jetHp[hi2]));
            }
            sb.Append("],\n");
            sb.Append("    \"jetSync\": "); Json.Escape(sb, jetSync ?? "0"); sb.Append(",\n");
            sb.Append("    \"sectSync\": "); Json.Escape(sb, sectSync ?? "0"); sb.Append(",\n");
            sb.Append("    \"engines\": [");
            for (int ei = 0; ei < engines.Count; ei++)
            {
                if (ei > 0) sb.Append(", ");
                var e = engines[ei];
                sb.Append("{\"index\": ").Append(e.index);
                sb.Append(", \"state\": ").Append(e.state);
                sb.Append(", \"faults\": ").Append(e.faults);
                sb.Append(", \"cowl\": ").Append(e.cowl);
                sb.Append(", \"bat\": ").Append(e.bat);
                sb.Append(", \"rpm\": ").Append(Json.Num(e.rpm));
                sb.Append(", \"oil\": ").Append(Json.Num(e.oil));
                sb.Append(", \"temp\": ").Append(Json.Num(e.temp));
                sb.Append(", \"fire\": ").Append(Json.Num(e.fire));
                sb.Append(", \"batt\": ").Append(Json.Num(e.batt));
                sb.Append("}");
            }
            sb.Append("],\n");
            sb.Append("    \"upgrades\": [");
            for (int i = 0; i < upgrades.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                Json.Escape(sb, upgrades[i]);
            }
            sb.Append("],\n");
            sb.Append("    \"seatRowMask\": ").Append(seatRowMask).Append(",\n");
            sb.Append("    \"seatRowCount\": ").Append(seatRowCount).Append(",\n");
            sb.Append("    \"seatRows\": [");
            for (int i = 0; i < seatRows.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(seatRows[i]);
            }
            sb.Append("],\n");
            sb.Append("    \"items\": [");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n');
                sb.Append("        ");
                ItemToJson(sb, items[i]);
            }
            sb.Append('\n');
            sb.Append("    ]\n}\n");
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
                fuelL = Json.GetFloat(d, "fuelL", 0f),
                fuelR = Json.GetFloat(d, "fuelR", 0f),
                reserveTank = Json.GetFloat(d, "reserveTank", 0f),
                fuelCapacity = Json.GetFloat(d, "fuelCapacity", 0f),
                reserveSync = Json.GetFloat(d, "reserveSync", 0f),
                tanks = Json.GetFloatList(d, "tanks"),
                breaches = Json.GetInt(d, "breaches", 0),
                jetHp = Json.GetFloatList(d, "jetHp"),
                jetSync = Json.GetStr(d, "jetSync", "0"),
                sectSync = Json.GetStr(d, "sectSync", "0"),
                hasHullField = d.ContainsKey("breaches"),
                hasFuelField = d.ContainsKey("fuelL"),
                upgrades = Json.GetStrList(d, "upgrades"),
                hasEngineField = d.ContainsKey("engines"),
                engines = ParseEngines(d),
                seatRowMask = Json.GetInt(d, "seatRowMask", 0),
                seatRowCount = Json.GetInt(d, "seatRowCount", 0),
                seatRows = Json.GetIntList(d, "seatRows"),
                items = ParseItems(d),
                hasSeatRowField = d.ContainsKey("seatRowMask") || d.ContainsKey("seatRows"),
                hasItemsField = d.ContainsKey("items"),
            };
            return s;
        }

        /// <summary>把一条物品记录写成一行 JSON 对象。</summary>
        static void ItemToJson(StringBuilder sb, ItemRecord r)
        {
            if (r == null) return;
            sb.Append("{\"id\": ");
            Json.Escape(sb, r.id ?? "");
            sb.Append(", \"tint\": ").Append(r.tint);
            sb.Append(", \"amount\": ").Append(Json.Num(r.amount));
            sb.Append(", \"count\": ").Append(r.count);
            sb.Append(", \"tag\": ").Append(r.tag);
            sb.Append(", \"socket\": ");
            Json.Escape(sb, r.socket ?? "");
            sb.Append(", \"px\": ").Append(Json.Num(r.px));
            sb.Append(", \"py\": ").Append(Json.Num(r.py));
            sb.Append(", \"pz\": ").Append(Json.Num(r.pz));
            sb.Append(", \"rx\": ").Append(Json.Num(r.rx));
            sb.Append(", \"ry\": ").Append(Json.Num(r.ry));
            sb.Append(", \"rz\": ").Append(Json.Num(r.rz));
            sb.Append(", \"rw\": ").Append(Json.Num(r.rw));
            sb.Append('}');
        }

        static List<ItemRecord> ParseItems(Dictionary<string, object> d)
        {
            var list = new List<ItemRecord>();
            foreach (var od in Json.GetObjList(d, "items"))
            {
                if (od == null) continue;
                var r = new ItemRecord
                {
                    id = Json.GetStr(od, "id", ""),
                    tint = Json.GetInt(od, "tint", 0),
                    amount = Json.GetFloat(od, "amount", 0f),
                    count = Json.GetInt(od, "count", 0),
                    tag = (uint)Json.GetInt(od, "tag", 0),
                    socket = Json.GetStr(od, "socket", ""),
                    px = Json.GetFloat(od, "px", 0f),
                    py = Json.GetFloat(od, "py", 0f),
                    pz = Json.GetFloat(od, "pz", 0f),
                    rx = Json.GetFloat(od, "rx", 0f),
                    ry = Json.GetFloat(od, "ry", 0f),
                    rz = Json.GetFloat(od, "rz", 0f),
                    rw = Json.GetFloat(od, "rw", 1f),
                };
                if (!string.IsNullOrEmpty(r.id)) list.Add(r);
            }
            return list;
        }
    }

    /// <summary>
    /// 一件需要持久化的世界物品。
    ///
    /// 只记"重新造出来还需要什么"：
    ///   - socket 非空 → 靠层级路径找回原来的插槽
    ///   - socket 为空 → 按存下来的世界位姿直接摆
    ///
    /// tag 只记录、不还原：它是运行时网络句柄，重新生成一定拿到新值，
    /// 硬塞回去会让手持/同步对不上。
    /// </summary>
    internal sealed class ItemRecord
    {
        public string id = "";
        public int tint;            // Color32 的 rgba 打包成 int
        public float amount;
        public int count;
        public uint tag;            // 仅记录，不还原
        public string socket = "";  // socket 的层级路径；空 = 自由摆放
        public float px, py, pz;
        public float rx, ry, rz, rw;
    }

    internal static class CareerStore
    {
        static string _dir;
        static string _file;
        static string _bakFile;

        public static string FilePath => _file;

        public static void Init()
        {
            _dir = ResolveDir();
            _file = Path.Combine(_dir, "career.json");
            _bakFile = Path.Combine(_dir, "career.bak.json");
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

            // 座排：rowMask 是权威位图，seatRows 是逐位解出来的（兜底 + 排查用）
            s.seatRowMask = ReadRowMask();
            s.seatRows = ReadInstalledRows();
            s.seatRowCount = ReadRowCount();

            // 世界物品快照：座排之外，"客舱里的器材"全部来自补给站购买，
            // 游戏自身完全不持久化，只能我们自己扫场景存下来。
            if (Plugin.SaveFuel.Value) ReadFuel(s);
            if (Plugin.SaveHull.Value) ReadHull(s);
            if (Plugin.SaveEngine.Value) ReadEngines(s);
            int skipped = 0;
            if (Plugin.SaveItems.Value) s.items = RecordItems(out skipped);
            else Plugin.LogSource.LogInfo("[MaydayCareer] 物品快照未启用（SaveItems=false）");

            Directory.CreateDirectory(_dir);

            // 写盘前先把上一份留一份。
            // 用户有过连按读档键把进度按回旧值的经历 —— 读档是覆盖操作，
            // 至少让被覆盖的那份还能手工捞回来。
            if (File.Exists(_file)) BackupExisting();

            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, s.ToJson(), new UTF8Encoding(false));
            if (File.Exists(_file)) File.Delete(_file);
            File.Move(tmp, _file);

            Plugin.LogSource.LogInfo(string.Format(
                "[MaydayCareer] 已保存 钱={0} 债={1} 第{2}段 升级={3} 座排={4} 物品={5} -> {6}",
                s.money, s.debt, s.leg, s.upgrades.Count, s.seatRows.Count, s.items.Count, _file));
        }

        /// <summary>
        /// 把当前 career.json 复制成 career.bak.json（覆盖旧 bak）。
        ///
        /// 失败只 warn，绝不允许挡住正常存盘 —— 备份是兜底，不是存盘的前置条件。
        /// 只做"写档前备份上一份"，不做"读档前备份"：语义简单，
        /// 用户随时能手工把 bak 改名回 career.json 捞回上一次。
        /// </summary>
        static void BackupExisting()
        {
            try
            {
                File.Copy(_file, _bakFile, true);
                Plugin.LogSource.LogInfo(
                    "[MaydayCareer] 已备份上一份存档 -> " + Path.GetFileName(_bakFile));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(
                    "[MaydayCareer] 备份上一份存档失败（不影响本次存盘）: " + e.Message);
            }
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

            // 旧存档必须显式喊出来。
            // 否则表现是"读档成功、座位重建补装0排、物品重建目标0件" ——
            // 用户看到的是一串成功日志却什么都没变，纯静默空转。
            if (IsLegacySave(s))
            {
                Plugin.LogSource.LogWarning(
                    "[MaydayCareer] 存档是 v1 旧格式：没有座排位图与物品快照，本次只能还原钱/进度/升级。");
                Plugin.LogSource.LogWarning(
                    "[MaydayCareer] 进机舱后按 " + Plugin.SaveKey.Value + " 存一次盘即可升级到 v2。");
            }

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
            RebuildSeats(s.seatRowMask, s.seatRows, s.hasSeatRowField);
            if (Plugin.LoadFuel.Value) RestoreFuel(s);
            if (Plugin.LoadHull.Value) RestoreHull(s);
            if (Plugin.LoadEngine.Value) RestoreEngines(s);
            RestoreItemsOnLoad(s.items);
            DumpCatalog(s);
        }


        /// <summary>
        /// 判断这份存档是不是"根本没有 v2 数据"。
        ///
        /// 依据是 JSON 里有没有这两个字段，而不是值是不是空 ——
        /// 一个正常 v2 存档完全可能物品数为 0（客舱就是空的），
        /// 那不该被当成旧档。
        /// </summary>
        static bool IsLegacySave(CareerState s)
        {
            if (s == null) return false;
            if (s.version < 2) return true;
            return !s.hasSeatRowField && !s.hasItemsField;
        }

        // ---------- 发动机 ----------

        static global::Mayday.Systems.EngineUnit[] FindEngines()
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.Systems.EngineUnit>();
                if (arr == null) return new global::Mayday.Systems.EngineUnit[0];
                var list = new List<global::Mayday.Systems.EngineUnit>();
                for (int i = 0; i < arr.Length; i++) if (arr[i] != null) list.Add(arr[i]);
                list.Sort((a, b) => a.index.CompareTo(b.index));
                return list.ToArray();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 找不到 EngineUnit: " + e.Message);
                return new global::Mayday.Systems.EngineUnit[0];
            }
        }

        static void ReadEngines(CareerState s)
        {
            try
            {
                var units = FindEngines();
                if (units.Length == 0) { Plugin.LogSource.LogWarning("[MaydayCareer] 发动机快照跳过：一台都没找到"); return; }
                s.engines.Clear();
                foreach (var u in units)
                {
                    s.engines.Add(new CareerState.EngineRec
                    {
                        index = u.index,
                        state = (int)u.state,
                        faults = u.faults,
                        cowl = (int)u.cowl,
                        bat = (int)u.bat,
                        swap = u.swap,
                        swapSerial = u.swapSerial,
                        rpm = u.rpm,
                        oil = u.oil,
                        temp = u.temp,
                        fire = u.fire,
                        batt = u.batt,
                        swapP = u.swapP,
                    });
                }
                var sbE = new StringBuilder();
                foreach (var e in s.engines)
                    sbE.Append(" #" + e.index + " state=" + e.state + " faults=0x" + e.faults.ToString("X2") + " rpm=" + e.rpm.ToString("F0") + " oil=" + e.oil.ToString("F2") + " temp=" + e.temp.ToString("F1"));
                Plugin.LogSource.LogInfo("[MaydayCareer] 发动机快照" + units.Length + "台:" + sbE);
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 发动机快照失败: " + e.Message); }
        }

        static DateTime _engineCheckAt;
        static string _expectEngines = "";

        static void RestoreEngines(CareerState s)
        {
            if (!s.hasEngineField || s.engines.Count == 0)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 发动机还原跳过：存档没有该字段或为空");
                return;
            }
            try
            {
                var units = FindEngines();
                if (units.Length == 0) { Plugin.LogSource.LogWarning("[MaydayCareer] 发动机还原跳过：一台都没找到"); return; }

                var sbE = new StringBuilder();
                int matched = 0;
                foreach (var rec in s.engines)
                {
                    foreach (var u in units)
                    {
                        if (u.index != rec.index) continue;
                        matched++;
                        try
                        {
                            u.state = (global::Mayday.Systems.EngineState)rec.state;
                            u.faults = (ushort)rec.faults;
                            u.cowl = (global::Mayday.Systems.CowlState)rec.cowl;
                            u.bat = (global::Mayday.Systems.BatteryState)rec.bat;
                            u.rpm = rec.rpm;
                            u.oil = rec.oil;
                            u.temp = rec.temp;
                            u.fire = rec.fire;
                            u.batt = rec.batt;
                            u.swap = (byte)rec.swap;
                        }
                        catch (Exception ee) { Plugin.LogSource.LogWarning("[MaydayCareer] 写引擎 #" + rec.index + " 失败: " + ee.Message); }
                        sbE.Append(" #" + rec.index + " state=" + rec.state + " faults=0x" + rec.faults.ToString("X2"));
                        break;
                    }
                }
                Plugin.LogSource.LogInfo("[MaydayCareer] 发动机还原 " + matched + "/" + s.engines.Count + " 台:" + sbE);

                _expectEngines = sbE.ToString();
                _engineCheckAt = DateTime.UtcNow.AddSeconds(3);
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 发动机还原失败: " + e.Message); }
        }

        /// <summary>3 秒后回读发动机状态，确认真留住了（派生值会被模拟覆盖）。</summary>
        public static void TickVerifyEngines()
        {
            if (_engineCheckAt == default) return;
            if (DateTime.UtcNow < _engineCheckAt) return;
            _engineCheckAt = default;
            try
            {
                var units = FindEngines();
                var sbE = new StringBuilder();
                foreach (var u in units)
                    sbE.Append(" #" + u.index + " state=" + (int)u.state + " faults=0x" + u.faults.ToString("X2"));
                bool ok = sbE.ToString() == _expectEngines;
                Plugin.LogSource.LogInfo("[MaydayCareer] 发动机校验（3 秒后）" + sbE + " → " + (ok ? "一致" : "不一致（期望" + _expectEngines + "）"));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 发动机校验失败: " + e.Message); }
        }

        // ---------- 机体损伤 ----------

        static global::Mayday.Systems.HullSystem FindHull()
        {
            try { return UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.HullSystem>(); }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 找不到 HullSystem: " + e.Message);
                return null;
            }
        }

        static void ReadHull(CareerState s)
        {
            try
            {
                var hs = FindHull();
                if (hs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] 损伤快照跳过：HullSystem 未就绪"); return; }
                s.breaches = hs.breaches;
                s.jetSync = hs.jetSync.ToString();
                s.sectSync = hs.sectSync.ToString();
                s.jetHp.Clear();
                var jh = hs.jetHp;
                if (jh != null) for (int i = 0; i < jh.Length; i++) s.jetHp.Add(jh[i]);

                // holes 的索引器读出来全是零（Count 却会变），像是 interop 对
                // SyncList<struct> 的取值问题。改读它内部的 objects 列表试试
                // （IList<T> 的桩上没有 Count，数量仍从 SyncList.Count 拿）。
                try
                {
                    int n = hs.holes.Count;
                    var inner = hs.holes.objects;
                    if (inner != null)
                    {
                        var sbh = new StringBuilder();
                        int nz = 0;
                        for (int i = 0; i < n; i++)
                        {
                            var v = inner[i];
                            bool zero = v.x == 0f && v.y == 0f && v.z == 0f && v.w == 0f;
                            if (!zero)
                            {
                                nz++;
                                if (sbh.Length > 0) sbh.Append(" | ");
                                sbh.Append("#" + i + " " + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ",r" + v.w.ToString("F3"));
                            }
                        }
                        Plugin.LogSource.LogInfo("[MaydayCareer] holes.objects 共" + n + "项 非空=" + nz
                            + (sbh.Length > 0 ? " → " + sbh : ""));
                    }
                    else Plugin.LogSource.LogWarning("[MaydayCareer] holes.objects 为空");
                }
                catch (Exception he) { Plugin.LogSource.LogWarning("[MaydayCareer] 读 holes.objects 失败: " + he.Message); }
                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 损伤快照 breaches=0x{0:X4} jetHp=[{1}]",
                    s.breaches, string.Join(", ", s.jetHp.ConvertAll(v => v.ToString("F2")).ToArray())));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 损伤快照失败: " + e.Message); }
        }

        static DateTime _hullCheckAt;
        static int _expectBreaches = -1;

        static void RestoreHull(CareerState s)
        {
            if (!s.hasHullField)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 损伤还原跳过：存档没有 breaches 字段（旧格式）");
                return;
            }
            try
            {
                var hs = FindHull();
                if (hs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] 损伤还原跳过：HullSystem 未就绪"); return; }

                int before = hs.breaches;
                hs.breaches = (ushort)s.breaches;          // SyncVar，客户端会跟着同步
                try { hs.jetSync = ulong.Parse(s.jetSync); } catch { }
                try { hs.sectSync = ulong.Parse(s.sectSync); } catch { }

                int wrote = 0;
                try
                {
                    var jh = hs.jetHp;
                    if (jh != null && s.jetHp.Count > 0)
                    {
                        int n = Math.Min(jh.Length, s.jetHp.Count);
                        for (int i = 0; i < n; i++) { jh[i] = s.jetHp[i]; wrote++; }
                    }
                }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 写 jetHp 失败: " + e.Message); }

                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 损伤还原 breaches 0x{0:X4} -> 0x{1:X4}（存档 0x{2:X4}） jetHp写入={3}项",
                    before, hs.breaches, s.breaches, wrote));

                _expectBreaches = s.breaches;
                _hullCheckAt = DateTime.UtcNow.AddSeconds(3);
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 损伤还原失败: " + e.Message); }
        }

        /// <summary>3 秒后回读 breaches —— 和燃油同一个道理：写进去不算，留得住才算。</summary>
        public static void TickVerifyHull()
        {
            if (_hullCheckAt == default) return;
            if (DateTime.UtcNow < _hullCheckAt) return;
            _hullCheckAt = default;
            try
            {
                var hs = FindHull();
                if (hs == null) return;
                bool ok = hs.breaches == _expectBreaches;
                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 损伤校验（3 秒后）breaches=0x{0:X4} 期望 0x{1:X4} → {2}",
                    hs.breaches, _expectBreaches, ok ? "一致" : "不一致（被清回去了）"));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 损伤校验失败: " + e.Message); }
        }

        // ---------- 机体损伤（调试探针） ----------

        /// <summary>
        /// 主动打洞。创伤来自随机事件、手工等不到，这个键把"不可复现"变成"按一下就有"。
        /// 走的是游戏自己的 HullPanels.ServerBlowAt —— 随机事件调的就是它。
        /// </summary>
        static int _blowTurn;

        public static void DebugBreach()
        {
            try
            {
                var hs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.HullSystem>();
                if (hs == null) { Plugin.LogSource.LogWarning("[MaydayCareer][hull] 找不到 HullSystem"); return; }

                DumpHull("打洞前", hs);

                var blow = global::Mayday.HullPanels.ServerBlowAt;
                if (blow == null) { Plugin.LogSource.LogWarning("[MaydayCareer][hull] ServerBlowAt 为空，无法打洞"); return; }

                UnityEngine.Vector3 origin = hs.transform.position + UnityEngine.Vector3.up * 1.2f;
                // 每次按键换一个绕机身的角度，否则每次都打在同一块板上
                // （之前连按三次都是「右后」，就是因为偏移量固定）。
                _blowTurn++;
                float baseAng = _blowTurn * 1.7f;
                int ok = 0, fail = 0;
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        float ang = baseAng + i * 2.1f;
                        var pt = origin + new UnityEngine.Vector3(
                            (float)Math.Cos(ang) * 3.5f, 0.4f * i, (float)Math.Sin(ang) * 3.5f);
                        int idx = blow.Invoke(pt, 0.25f);
                        if (idx >= 0) ok++; else fail++;
                        Plugin.LogSource.LogInfo("[MaydayCareer][hull] ServerBlowAt #" + i + " -> 面板=" + idx);
                    }
                    catch (Exception e)
                    {
                        fail++;
                        Plugin.LogSource.LogWarning("[MaydayCareer][hull] 打洞失败: " + e.Message);
                    }
                }
                Plugin.LogSource.LogInfo("[MaydayCareer][hull] 打洞结果 成功=" + ok + " 失败=" + fail);
                DumpHull("打洞后", hs);
            }
            catch (Exception e) { Plugin.LogSource.LogError("[MaydayCareer] 调试打洞异常: " + e); }
        }

        static void DumpHull(string tag, global::Mayday.Systems.HullSystem hs)
        {
            try
            {
                var holes = hs.holes;
                int n = holes == null ? 0 : holes.Count;
                Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " 洞数=" + n);
                try
                {
                    var placed = hs.holePlaced;
                    var spots = hs.holeBuildSpot;
                    var sb2 = new StringBuilder();
                    int nz = 0;
                    if (placed != null)
                    {
                        for (int i = 0; i < placed.Length; i++)
                        {
                            var v = placed[i];
                            bool zero = v.x == 0f && v.y == 0f && v.z == 0f && v.w == 0f;
                            if (!zero) nz++;
                            if (i < 10)
                            {
                                if (i > 0) sb2.Append(" | ");
                                sb2.Append(zero ? "-" : (v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ",r" + v.w.ToString("F2")));
                            }
                        }
                        Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " holePlaced 长度=" + placed.Length
                            + " 非空=" + nz + " 候选洞位=" + (spots == null ? -1 : spots.Length));
                        Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " 槽位: " + sb2);
                    }
                }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer][hull] holePlaced dump 失败: " + e.Message); }

                try
                {
                    // holePlaced 实测全零（打洞后仍然非空=0），说明它不是活的存储。
                    // 改问游戏自己的查询接口：HoleOpen(slot) / HoleAt(slot)。
                    var sb3 = new StringBuilder();
                    int openCount = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        bool open = false;
                        UnityEngine.Vector4 v = default;
                        try { open = hs.HoleOpen(i); } catch { break; }
                        if (open)
                        {
                            openCount++;
                            try { v = hs.HoleAt(i); } catch { }
                            if (sb3.Length > 0) sb3.Append(" | ");
                            sb3.Append("#" + i + " " + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ",w" + v.w.ToString("F3"));
                        }
                    }
                    Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " HoleOpen 计数=" + openCount);
                    if (sb3.Length > 0) Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " 开口槽位: " + sb3);
                }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer][hull] HoleOpen dump 失败: " + e.Message); }

                try
                {
                    // holeViews[i].key 才是洞的真实数据，root.activeSelf 表示该槽位是否在用。
                    // holes / holePlaced / HoleOpen 实测都读不出东西（全零），只有这里对得上。
                    var views = hs.holeViews;
                    var sb4 = new StringBuilder();
                    int live = 0;
                    if (views != null)
                    {
                        for (int i = 0; i < views.Length; i++)
                        {
                            var v = views[i];
                            if (v == null) continue;
                            bool on = false;
                            try { on = v.root != null && v.root.activeSelf; } catch { }
                            if (!on) continue;
                            live++;
                            var k = v.key;
                            if (sb4.Length > 0) sb4.Append(" | ");
                            sb4.Append("#" + i + " " + k.x.ToString("F2") + "," + k.y.ToString("F2") + "," + k.z.ToString("F2") + ",r" + k.w.ToString("F3"));
                        }
                        Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " holeViews 长度=" + views.Length + " 活跃=" + live);
                        if (sb4.Length > 0) Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " 洞键: " + sb4);
                    }
                }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer][hull] holeViews dump 失败: " + e.Message); }

                var jh = hs.jetHp;
                if (jh != null && jh.Length > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < jh.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(jh[i].ToString("F2")); }
                    Plugin.LogSource.LogInfo("[MaydayCareer][hull] " + tag + " jetHp=[" + sb + "] jetSync=" + hs.jetSync);
                }
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer][hull] dump 失败: " + e.Message); }
        }

        // ---------- 燃油 ----------

        // 真值在 FuelSystem 上：fuelL / fuelR 是 SyncVar（NetworkfuelL / NetworkfuelR），
        // reserveTank 是本地模拟值。PlaneTelemetry.FuelLiters 只是每帧刷新的快照，
        // 写它没有意义 —— 下一帧就被模拟覆盖。
        static global::Mayday.Systems.FuelSystem FindFuelSystem()
        {
            try { return UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.FuelSystem>(); }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 找不到 FuelSystem: " + e.Message);
                return null;
            }
        }

        static void ReadFuel(CareerState s)
        {
            try
            {
                var fs = FindFuelSystem();
                if (fs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] 燃油快照跳过：FuelSystem 未就绪"); return; }
                s.fuelL = fs.fuelL;
                s.fuelR = fs.fuelR;
                s.reserveTank = fs.reserveTank;
                s.fuelCapacity = fs.capacity;
                try { s.reserveSync = fs.reserveSync; } catch { }
                s.tanks.Clear();
                try
                {
                    var arr = fs.tank;
                    if (arr != null)
                        for (int i = 0; i < arr.Length; i++) s.tanks.Add(arr[i]);
                }
                catch (Exception te) { Plugin.LogSource.LogWarning("[MaydayCareer] 读取 tank 数组失败: " + te.Message); }
                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 燃油快照 L={0:F1} R={1:F1} 储备={2:F1} 容量={3:F1} tank=[{4}]",
                    s.fuelL, s.fuelR, s.reserveTank, s.fuelCapacity,
                    string.Join(", ", s.tanks.ConvertAll(v => v.ToString("F1")).ToArray())));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 燃油快照失败: " + e.Message); }
        }

        static DateTime _fuelCheckAt;
        static float _expectL, _expectR;

        static float ClampFuel(float v, float cap)
        {
            if (v < 0f) return 0f;
            if (cap > 0f && v > cap) return cap;
            return v;
        }

        static void RestoreFuel(CareerState s)
        {
            if (!s.hasFuelField)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 燃油还原跳过：存档没有燃油字段（旧格式），按 J 存一次盘即可记录");
                return;
            }
            try
            {
                var fs = FindFuelSystem();
                if (fs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] 燃油还原跳过：FuelSystem 未就绪"); return; }

                float cap = fs.capacity;
                float l = ClampFuel(s.fuelL, cap);
                float r = ClampFuel(s.fuelR, cap);

                // tank[] 才是模拟的真实存储 —— 只写 fuelL/fuelR 会被
                // 下一帧从 tank 重算覆盖（实测 3 秒内就退回满油）。
                int wrote = 0;
                try
                {
                    var arr = fs.tank;
                    if (arr != null && s.tanks.Count > 0)
                    {
                        int n = Math.Min(arr.Length, s.tanks.Count);
                        for (int i = 0; i < n; i++)
                        {
                            float v = ClampFuel(s.tanks[i], cap);
                            arr[i] = v;
                            wrote++;
                        }
                    }
                }
                catch (Exception te) { Plugin.LogSource.LogWarning("[MaydayCareer] 写入 tank 数组失败: " + te.Message); }

                fs.fuelL = l;
                fs.fuelR = r;
                try { fs.reserveSync = s.reserveSync; } catch { }
                try { fs.reserveTank = ClampFuel(s.reserveTank, Math.Max(cap, s.reserveTank)); } catch { }
                try { fs.reserveSync = fs.reserveTank; } catch { }

                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 燃油还原 L={0:F1} R={1:F1} 储备={2:F1} tank写入={3}项（存档 L={4:F1} R={5:F1}，容量 {6:F1}）",
                    fs.fuelL, fs.fuelR, fs.reserveTank, wrote, s.fuelL, s.fuelR, cap));

                _expectL = l; _expectR = r;
                _fuelCheckAt = DateTime.UtcNow.AddSeconds(3);
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 燃油还原失败: " + e.Message); }
        }

        /// <summary>
        /// 写入后 3 秒回读一次。燃油是模拟量，如果写错了源对象，
        /// 下一帧就会被模拟覆盖 —— 只有延迟回读才能证明真的写进去了。
        /// </summary>
        public static void TickVerify()
        {
            if (_fuelCheckAt == default) return;
            if (DateTime.UtcNow < _fuelCheckAt) return;
            _fuelCheckAt = default;
            try
            {
                var fs = FindFuelSystem();
                if (fs == null) return;
                bool ok = Math.Abs(fs.fuelL - _expectL) < 1f && Math.Abs(fs.fuelR - _expectR) < 1f;
                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 燃油校验（3 秒后）L={0:F1} R={1:F1} 期望 L={2:F1} R={3:F1} → {4}",
                    fs.fuelL, fs.fuelR, _expectL, _expectR, ok ? "一致" : "不一致（模拟把值改回去了）"));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 燃油校验失败: " + e.Message); }
        }

        /// <summary>
        /// 手动存盘入口（热键用）。
        ///
        /// 和自动存盘的区别只有两处守卫：必须处在服务端，且 GameState 已就绪。
        /// 纯客户端按热键时只打警告，绝不写盘 —— 否则客户端会用自己那份
        /// 不完整的状态覆盖房主的存档。
        /// </summary>
        public static void SaveManual()
        {
            try
            {
                bool server = false;
                try { server = global::Mirror.NetworkServer.active; } catch { }

                if (!server)
                {
                    Plugin.LogSource.LogWarning(
                        "[MaydayCareer] 手动存盘跳过：当前不是房主/服务端（纯客户端不写盘）");
                    return;
                }

                if (global::Mayday.GameState.Instance == null)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 手动存盘跳过：GameState 还没就绪");
                    return;
                }

                Save();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 手动存盘异常: " + e);
            }
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
        public static void RebuildSeats(int savedRowMask, List<int> savedInstalled, bool hadSeatData)
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

                // ---- 座排补装 ----
                // rowMask 是权威位图。存档里置位、而现在没装的排，
                // 用游戏自己的 ServerInstallRow 补回去。
                // 只补、绝不超装：新增排会改变座位容量，破坏乘客分配。
                int targetMask = savedRowMask;
                if (targetMask <= 0 && savedInstalled != null)
                {
                    // 兼容没有 seatRowMask 的旧存档（v1）：按 slot 列表拼出位图
                    foreach (int slot in savedInstalled)
                        if (slot >= 0 && slot < 8) targetMask |= 1 << slot;
                }

                int installed = 0;
                for (int i = 0; i < 8; i++)
                {
                    if ((targetMask & (1 << i)) == 0) continue;
                    try
                    {
                        if (pd.IsRowInstalled(i)) continue;
                        pd.ServerInstallRow(i, want);
                        installed++;
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning(string.Format(
                            "[MaydayCareer] 第 {0} 排补装失败: {1}", i, e.Message));
                    }
                }

                int done = 0;
                int visFixed = 0;
                // rowMask 只有 8 位，逐位判断比依赖 RowCount 可靠
                // （RowCount 的语义还没验死：已装数还是总槽位数）
                for (int i = 0; i < 8; i++)
                {
                    // 每排单独 try：某一排坏了不影响其余排
                    try
                    {
                        if (!pd.IsRowInstalled(i)) continue;
                        var row = global::Mayday.Passengers.SeatRow.Get(i);
                        if (row == null) continue;
                        row.ServerSetPremium(want);

                        // 视觉兜底：ServerSetPremium 依赖 SyncVar hook -> ApplyPremium。
                        // host 本地理论上会立刻生效，但万一 hook 没触发、或 ApplyPremium
                        // 被改成延迟执行，这里直接把 premiumVis 的开关掰到位。
                        try
                        {
                            var vis = row.premiumVis;
                            if (vis != null)
                            {
                                for (int k = 0; k < vis.Length; k++)
                                {
                                    var go = vis[k];
                                    if (go == null) continue;
                                    if (go.activeSelf != want)
                                    {
                                        go.SetActive(want);
                                        visFixed++;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Plugin.LogSource.LogWarning(string.Format(
                                "[MaydayCareer] 第 {0} 排视觉兜底失败: {1}", i, e.Message));
                        }
                        done++;
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning(string.Format(
                            "[MaydayCareer] 第 {0} 排座位设置失败: {1}", i, e.Message));
                    }
                }

                // 两种"什么都没补"必须分开说清楚：
                //   有座排数据、补装 0 排 = 都装好了，正常
                //   没有座排数据          = 存档太旧，补不了
                // 混在一起用户会以为是 bug。
                if (hadSeatData)
                    Plugin.LogSource.LogInfo(string.Format(
                        "[MaydayCareer] 座位重建 补装{0}排 已装{1}排 premium={2}（RowCount={3}，视觉兜底{4}处）",
                        installed, done, want, rows, visFixed));
                else
                    Plugin.LogSource.LogInfo(string.Format(
                        "[MaydayCareer] 座位重建 已装{0}排 premium={1}（存档无座排数据，跳过补装；RowCount={2}，视觉兜底{3}处）",
                        done, want, rows, visFixed));
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
        public static void DumpCatalog(CareerState s)
        {
            if (!Plugin.DumpOnLoad.Value) return;
            try { Plugin.LogSource.LogInfo(DumpTag + "===== 开始导出目录 ====="); }
            catch { return; }

            // 先报"这份存档带了什么"，再看场景 —— 顺序上更符合排查直觉
            DumpSavedSnapshot(s);
            DumpUpgradeCatalog();
            DumpItemCatalog();
            DumpSeatState();
            DumpSupplyShop();

            Plugin.LogSource.LogInfo(DumpTag + "===== 导出结束 =====");
        }

        /// <summary>这份存档本身带了什么数据。区分"字段不存在"和"值是 0"。</summary>
        static void DumpSavedSnapshot(CareerState s)
        {
            try
            {
                if (s == null) { Plugin.LogSource.LogInfo(DumpTag + "存档快照 读取失败"); return; }

                Plugin.LogSource.LogInfo(string.Format(
                    DumpTag + "存档快照 version={0} 升级={1} 座排={2}",
                    s.version, s.upgrades == null ? 0 : s.upgrades.Count,
                    s.hasSeatRowField ? s.seatRows.Count + "排(mask=" + s.seatRowMask + ")" : "无字段"));

                int n = s.items == null ? 0 : s.items.Count;
                if (!s.hasItemsField)
                    Plugin.LogSource.LogInfo(DumpTag + "物品快照 存档记录=0 件（存档无 items 字段）");
                else
                    Plugin.LogSource.LogInfo(DumpTag + "物品快照 存档记录=" + n + " 件");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning(DumpTag + "存档快照导出失败: " + e.Message);
            }
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
                int mask = 0;
                try { mask = pd.rowMask; } catch { }
                Plugin.LogSource.LogInfo(string.Format(DumpTag + "座位 排数={0} rowMask={1} 高级座={2}", rows, mask, prem));

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

        // ---------- 座排读数 ----------

        /// <summary>
        /// rowMask 是 PassengerDirector 的 SyncVar 位图，第 i 位 = 第 i 排已装。
        /// 这是"装了几排"唯一无歧义的来源 —— RowCount 到底是已装数还是总槽位数
        /// 还没验死，不能当依据。
        /// </summary>
        static int ReadRowMask()
        {
            try
            {
                var pd = global::Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return 0;
                return pd.rowMask;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 读 rowMask 失败: " + e.Message);
                return 0;
            }
        }

        static int ReadRowCount()
        {
            try
            {
                var pd = global::Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return 0;
                return pd.RowCount;
            }
            catch { return 0; }
        }

        /// <summary>逐位解出已装排的 slot 列表（位图的冗余副本，用于排查和旧存档兜底）。</summary>
        static List<int> ReadInstalledRows()
        {
            var list = new List<int>();
            try
            {
                var pd = global::Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return list;
                for (int i = 0; i < 8; i++)
                {
                    try { if (pd.IsRowInstalled(i)) list.Add(i); }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 读座排状态失败: " + e.Message);
            }
            return list;
        }

        // ---------- 世界物品：存 ----------

        /// <summary>
        /// 扫场景里所有 Item，把"值得跨局保留"的记下来。
        ///
        /// 为什么只存这些：游戏对世界物品完全没有持久化，落地/换机场时
        /// 客舱里花钱买的器材全都会消失。行李则相反 —— 结算会主动清，
        /// 存了反而会把上一趟的包带到下一趟。
        /// </summary>
        static List<ItemRecord> RecordItems(out int skipped)
        {
            skipped = 0;
            var list = new List<ItemRecord>();
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.Item>();
                if (arr == null)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 物品快照 扫描失败（FindObjectsOfType 返回空）");
                    return list;
                }

                for (int i = 0; i < arr.Length; i++)
                {
                    try
                    {
                        var it = arr[i];
                        if (it == null) { continue; }
                        if (!IsPersistable(it)) { skipped++; continue; }
                        list.Add(CaptureItem(it));
                    }
                    catch (Exception e)
                    {
                        skipped++;
                        Plugin.LogSource.LogWarning("[MaydayCareer] 第 " + i + " 件物品读取失败: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 物品快照异常: " + e.Message);
            }

            Plugin.LogSource.LogInfo(string.Format(
                "[MaydayCareer] 物品快照 已记录 {0} 件（跳过 {1} 件）", list.Count, skipped));
            return list;
        }

        /// <summary>
        /// 是否值得存。白名单按分类走：
        ///   Galley / Cabin / Cleaning / Maintenance / Crate 存
        ///   Luggage（行李，结算会清）、Misc（pax_carry / player_carry / 炸弹等运行时临时物）不存
        /// 另外排除手持物。
        /// </summary>
        static bool IsPersistable(global::Mayday.Item it)
        {
            try { if (it.IsHeld) return false; }
            catch { return false; }

            global::Mayday.ItemInfo info = null;
            try { info = it.Info; }
            catch { return false; }
            if (info == null) return false;

            try
            {
                switch (info.category)
                {
                    case global::Mayday.ItemCategory.Galley:
                    case global::Mayday.ItemCategory.Cabin:
                    case global::Mayday.ItemCategory.Cleaning:
                    case global::Mayday.ItemCategory.Maintenance:
                    case global::Mayday.ItemCategory.Crate:
                        return true;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        static ItemRecord CaptureItem(global::Mayday.Item it)
        {
            var r = new ItemRecord();
            r.id = it.itemId ?? "";
            try { r.amount = it.Amount; } catch { }
            try { r.count = it.Count; } catch { }
            try { r.tag = it.Tag; } catch { }
            try { r.tint = PackTint(it.Tint); } catch { }

            // 归属：优先记插槽路径，找不到插槽才退回世界位姿
            try
            {
                var sock = it.Socket;
                if (sock != null) r.socket = PathOf(sock.transform) ?? "";
            }
            catch { }

            try
            {
                var t = it.transform;
                var p = t.position;
                var q = t.rotation;
                r.px = p.x; r.py = p.y; r.pz = p.z;
                r.rx = q.x; r.ry = q.y; r.rz = q.z; r.rw = q.w;
            }
            catch
            {
                try
                {
                    var p = it.syncPos;
                    var q = it.syncRot;
                    r.px = p.x; r.py = p.y; r.pz = p.z;
                    r.rx = q.x; r.ry = q.y; r.rz = q.z; r.rw = q.w;
                }
                catch { }
            }

            return r;
        }

        /// <summary>
        /// transform 的层级路径，形如 "Plane/Cabin/Row0#2/Socket#0"。
        ///
        /// 为什么不用世界坐标：站点会被 GroundCrew 重新锚定到下一个机场，
        /// 世界的绝对位置在存读之间会变，但舱内层级结构不变。
        /// </summary>
        static string PathOf(UnityEngine.Transform t)
        {
            if (t == null) return "";
            var parts = new List<string>();
            int guard = 0;
            while (t != null && guard++ < 64)
            {
                int idx = 0;
                try { idx = t.GetSiblingIndex(); } catch { }
                string nm = "";
                try { nm = t.name; } catch { }
                parts.Add((string.IsNullOrEmpty(nm) ? "?" : nm) + "#" + idx);
                try { t = t.parent; } catch { t = null; }
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        // ---------- 世界物品：读 ----------

        const int MaxRestoreTries = 3;

        static List<ItemRecord> _pendingItems;
        static bool _restorePending;
        static int _restoreTries;

        /// <summary>
        /// 读档后立刻尝试补物品；同时把"待补"标记挂上，
        /// 让 GroundCrew 重新锚定站点之后再补一次。
        ///
        /// 为什么两处都补：手动热键通常发生在站点已经就绪之后，立刻就能补上；
        /// 自动模式下读档发生在 ServerResetRun，那时机舱/站点还没建，
        /// 必须等 GroundCrew.ServerReanchor 之后。
        /// </summary>
        static void RestoreItemsOnLoad(List<ItemRecord> saved)
        {
            _pendingItems = saved;
            _restoreTries = 0;

            if (saved == null || saved.Count == 0)
            {
                _restorePending = false;
                return;
            }

            if (!Plugin.LoadItems.Value)
            {
                _restorePending = false;
                Plugin.LogSource.LogInfo(
                    "[MaydayCareer] 物品重建未启用（LoadItems=false），存档里有 " + saved.Count + " 件");
                return;
            }

            _restorePending = true;
            RestoreItems(saved);
        }

        /// <summary>GroundCrew 把站点重建好之后调用（见 ReanchorHook）。</summary>
        public static void OnSiteReanchored()
        {
            if (!_restorePending) return;
            if (_restoreTries >= MaxRestoreTries) return;
            RestoreItems(_pendingItems);
        }

        /// <summary>
        /// 按存档补物品。幂等：先数场景里已有的同 id 物品，只补差额，
        /// 所以反复读档不会刷物品。
        /// </summary>
        static void RestoreItems(List<ItemRecord> want)
        {
            _restoreTries++;
            try
            {
                if (want == null || want.Count == 0) { _restorePending = false; return; }

                var sockets = IndexSockets();
                var have = CountItemsById();

                int ok = 0, spawnFail = 0, socketMiss = 0;

                foreach (var r in want)
                {
                    if (r == null || string.IsNullOrEmpty(r.id)) { continue; }

                    // 已经有一件了 → 不重复生成
                    if (have.TryGetValue(r.id, out var n) && n > 0)
                    {
                        have[r.id] = n - 1;
                        ok++;
                        continue;
                    }

                    try
                    {
                        var item = SpawnOne(r);
                        if (item == null) { spawnFail++; continue; }

                        if (!string.IsNullOrEmpty(r.socket))
                        {
                            global::Mayday.ItemSocket sock = null;
                            if (!sockets.TryGetValue(r.socket, out sock) || sock == null || !sock.IsEmpty)
                            {
                                // 插槽找不到 / 已被占 → 退回按存的世界位姿摆，并计数
                                socketMiss++;
                                TryPlaceRaw(item, r);
                            }
                            else
                            {
                                bool inserted = false;
                                try { inserted = sock.ServerInsert(item); }
                                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 插入插槽失败: " + e.Message); }
                                if (!inserted)
                                {
                                    try { item.ServerAttachToSocket(sock); inserted = true; }
                                    catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 挂接插槽失败: " + e.Message); }
                                }
                                if (!inserted) { socketMiss++; TryPlaceRaw(item, r); }
                            }
                        }

                        ok++;
                    }
                    catch (Exception e)
                    {
                        spawnFail++;
                        Plugin.LogSource.LogWarning(string.Format(
                            "[MaydayCareer] 重建物品 {0} 失败: {1}", r.id, e.Message));
                    }
                }

                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 物品重建 成功 {0} / 目标 {1}，socket 未命中 {2}（生成失败 {3}，第 {4} 次）",
                    ok, want.Count, socketMiss, spawnFail, _restoreTries));

                // 全齐了就不再重试；否则留给下一次 GroundCrew 重锚再补
                if (ok >= want.Count) _restorePending = false;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 物品重建异常: " + e);
            }
        }

        static Dictionary<string, global::Mayday.ItemSocket> IndexSockets()
        {
            var map = new Dictionary<string, global::Mayday.ItemSocket>(StringComparer.Ordinal);
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.ItemSocket>();
                if (arr == null) return map;
                for (int i = 0; i < arr.Length; i++)
                {
                    var s = arr[i];
                    if (s == null) continue;
                    try
                    {
                        string p = PathOf(s.transform);
                        if (!string.IsNullOrEmpty(p) && !map.ContainsKey(p)) map[p] = s;
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 扫描插槽失败: " + e.Message);
            }
            return map;
        }

        /// <summary>场景里现有物品按 itemId 计数（不做过滤，宁可少补也不要重复）。</summary>
        static Dictionary<string, int> CountItemsById()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.Item>();
                if (arr == null) return map;
                for (int i = 0; i < arr.Length; i++)
                {
                    var it = arr[i];
                    if (it == null) continue;
                    string id = null;
                    try { id = it.itemId; } catch { }
                    if (string.IsNullOrEmpty(id)) continue;
                    map.TryGetValue(id, out var c);
                    map[id] = c + 1;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 统计现有物品失败: " + e.Message);
            }
            return map;
        }

        static global::Mayday.Item SpawnOne(ItemRecord r)
        {
            var pos = new UnityEngine.Vector3(r.px, r.py, r.pz);
            var rot = new UnityEngine.Quaternion(r.rx, r.ry, r.rz, r.rw);
            var item = global::Mayday.Item.ServerSpawn(r.id, pos, rot, null, false);
            if (item == null) return null;

            // 染色/数量尽力还原；Tag 是运行时网络句柄，故意不还原
            try { if (r.tint != 0) item.Tint = UnpackTint(r.tint); }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 还原染色失败: " + e.Message); }
            try { if (r.amount > 0f) item.Amount = r.amount; } catch { }
            try { if (r.count > 0) item.Count = r.count; } catch { }

            return item;
        }

        static void TryPlaceRaw(global::Mayday.Item item, ItemRecord r)
        {
            try { item.transform.position = new UnityEngine.Vector3(r.px, r.py, r.pz); } catch { }
            try { item.transform.rotation = new UnityEngine.Quaternion(r.rx, r.ry, r.rz, r.rw); } catch { }
        }

        // Color32 有 rgba 联合体，打包成 int 存，读回来用四参构造
        static int PackTint(UnityEngine.Color32 c)
            => (c.a << 24) | (c.b << 16) | (c.g << 8) | c.r;

        static UnityEngine.Color32 UnpackTint(int v)
            => new UnityEngine.Color32(
                (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF),
                (byte)((v >> 16) & 0xFF), (byte)((v >> 24) & 0xFF));

        static void Try(string what, Action act)
        {
            try { act(); }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 还原 " + what + " 失败: " + e.Message); }
        }
    }
}
