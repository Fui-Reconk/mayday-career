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

        // 机体：损伤（per-panel 数组）+ 掉漆（shown*/coverDamage 外观链）。
        // 关键认知（v1.1.0 翻案）：
        //   - holes/damage/dirt 是 per-panel 列表，Count 必须等于 HullSystem.Count；
        //     初始化没完成时 Count 会从 11 涨到 12 —— 那不是"多了一个洞"
        //   - breaches/jetSync/sectSync 只是汇总位图，jetHp 是派生值
        //   - 渲染读的是 shownMain/shownAccent，不是 paintCover；
        //     重绘入口是 ApplyAllSectionPaint + MarkAllDirty，不是 TickPaint
        //   - 前置判据是 serverReady，不是 paintInit
        public HullRec hull;


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
            public float lOil;      // 本地机油量：怀疑才是真存储，oil 只是同步副本
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
                    lOil = Json.GetFloat(o, "lOil", Json.GetFloat(o, "oil", 0f)),
                    temp = Json.GetFloat(o, "temp", 0f),
                    fire = Json.GetFloat(o, "fire", 0f),
                    batt = Json.GetFloat(o, "batt", 0f),
                    swapP = Json.GetFloat(o, "swapP", 0f),
                });
            }
            return list;
        }

        // ---------- 机体存档结构 ----------

        public sealed class HullPanelRec
        {
            public float damage;       // lDamage[i]  本地面板损伤
            public float netDamage;    // damage[i]   同步面板损伤
            public float dirt;         // lDirt[i]    本地脏污
            public float netDirt;      // dirt[i]     同步脏污
            public bool hasHole;
            public float hx, hy, hz, hr;   // holes[i] = (x, y, z, 半径)
            public float patchT;           // 补丁进度
            public bool hasPatchAge;
            public float patchAge;         // Time.time - patchLast，存 age 不存绝对时间
        }

        public sealed class HullSectionRec
        {
            public int stage;          // sStage[j]，HullSystem.StagePaint 是"待喷漆"那一档
            public float progress;     // sProg[j]
        }

        public sealed class HullPaintRec
        {
            // 每个颜色都是 [r,g,b,a]。mainColor/accentColor 是 SyncList（网络），
            // lMain/lAccent 是本地；shown* 才是渲染读的 —— 之前只写 paintCover 所以看不见
            public List<float> main = new List<float>();
            public List<float> accent = new List<float>();
            public List<float> netMain = new List<float>();
            public List<float> netAccent = new List<float>();
            public List<float> shownMain = new List<float>();
            public List<float> shownAccent = new List<float>();
            public List<float> goalMain = new List<float>();
            public List<float> goalAccent = new List<float>();
            public float glideMain;
            public float glideAccent;
            public float cover;        // paintCover[i]
            public float coverDamage;  // coverDamage[i] —— 这才是"掉漆量"
        }

        public sealed class HullRec
        {
            public int breaches;
            public string sectSync = "0";
            public List<HullPanelRec> panels = new List<HullPanelRec>();
            public List<HullSectionRec> sections = new List<HullSectionRec>();
            public List<HullPaintRec> paint = new List<HullPaintRec>();
        static void PutColorArr(StringBuilder sb, string name, List<float> v)
        {
            sb.Append('\"').Append(name).Append("\": [");
            for (int i = 0; i < v.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Json.Num(v[i])); }
            sb.Append(']');
        }

        public void HullToJson(StringBuilder sb)
        {
            sb.Append("{\n");
            sb.Append("        \"breaches\": ").Append(breaches).Append(",\n");
            sb.Append("        \"sectSync\": "); Json.Escape(sb, sectSync ?? "0"); sb.Append(",\n");
            sb.Append("        \"panels\": [");
            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (i > 0) sb.Append(',');
                sb.Append("\n            {\"damage\": ").Append(Json.Num(p.damage));
                sb.Append(", \"netDamage\": ").Append(Json.Num(p.netDamage));
                sb.Append(", \"dirt\": ").Append(Json.Num(p.dirt));
                sb.Append(", \"netDirt\": ").Append(Json.Num(p.netDirt));
                sb.Append(", \"hole\": [").Append(Json.Num(p.hx)).Append(", ").Append(Json.Num(p.hy))
                   .Append(", ").Append(Json.Num(p.hz)).Append(", ").Append(Json.Num(p.hr)).Append(']');
                sb.Append(", \"patchT\": ").Append(Json.Num(p.patchT));
                if (p.hasPatchAge) sb.Append(", \"patchAge\": ").Append(Json.Num(p.patchAge));
                sb.Append('}');
            }
            sb.Append("\n        ],\n");
            sb.Append("        \"sections\": [");
            for (int i = 0; i < sections.Count; i++)
            {
                var q = sections[i];
                if (i > 0) sb.Append(", ");
                sb.Append("\n            {\"stage\": ").Append(q.stage)
                   .Append(", \"progress\": ").Append(Json.Num(q.progress)).Append('}');
            }
            sb.Append("\n        ],\n");
            sb.Append("        \"paint\": [");
            for (int i = 0; i < paint.Count; i++)
            {
                var q = paint[i];
                if (i > 0) sb.Append(',');
                sb.Append("\n            {");
                PutColorArr(sb, "main", q.main); sb.Append(", ");
                PutColorArr(sb, "accent", q.accent); sb.Append(", ");
                PutColorArr(sb, "netMain", q.netMain); sb.Append(", ");
                PutColorArr(sb, "netAccent", q.netAccent); sb.Append(", ");
                PutColorArr(sb, "shownMain", q.shownMain); sb.Append(", ");
                PutColorArr(sb, "shownAccent", q.shownAccent); sb.Append(", ");
                PutColorArr(sb, "goalMain", q.goalMain); sb.Append(", ");
                PutColorArr(sb, "goalAccent", q.goalAccent); sb.Append(", ");
                sb.Append("\"glideMain\": ").Append(Json.Num(q.glideMain));
                sb.Append(", \"glideAccent\": ").Append(Json.Num(q.glideAccent));
                sb.Append(", \"cover\": ").Append(Json.Num(q.cover));
                sb.Append(", \"coverDamage\": ").Append(Json.Num(q.coverDamage));
                sb.Append('}');
            }
            sb.Append("\n        ]\n    }");
        }
        }

        public static HullRec ParseHull(Dictionary<string, object> d)
        {
            if (d == null || !d.TryGetValue("hull", out var hv) || !(hv is Dictionary<string, object> hd)) return null;
            var rec = new HullRec
            {
                breaches = Json.GetInt(hd, "breaches", 0),
                sectSync = Json.GetStr(hd, "sectSync", "0"),
            };
            foreach (var po in Json.GetObjList(hd, "panels"))
            {
                var p = new HullPanelRec
                {
                    damage = Json.GetFloat(po, "damage", 0f),
                    netDamage = Json.GetFloat(po, "netDamage", 0f),
                    dirt = Json.GetFloat(po, "dirt", 0f),
                    netDirt = Json.GetFloat(po, "netDirt", 0f),
                    patchT = Json.GetFloat(po, "patchT", 0f),
                };
                var hole = Json.GetFloatList(po, "hole");
                if (hole.Count >= 4) { p.hx = hole[0]; p.hy = hole[1]; p.hz = hole[2]; p.hr = hole[3]; p.hasHole = hole[3] > 0f; }
                if (po.TryGetValue("patchAge", out var pa) && pa is double pd) { p.hasPatchAge = true; p.patchAge = (float)pd; }
                rec.panels.Add(p);
            }
            foreach (var so in Json.GetObjList(hd, "sections"))
                rec.sections.Add(new HullSectionRec { stage = Json.GetInt(so, "stage", 0), progress = Json.GetFloat(so, "progress", 0f) });
            foreach (var qo in Json.GetObjList(hd, "paint"))
            {
                rec.paint.Add(new HullPaintRec
                {
                    main = Json.GetFloatList(qo, "main"),
                    accent = Json.GetFloatList(qo, "accent"),
                    netMain = Json.GetFloatList(qo, "netMain"),
                    netAccent = Json.GetFloatList(qo, "netAccent"),
                    shownMain = Json.GetFloatList(qo, "shownMain"),
                    shownAccent = Json.GetFloatList(qo, "shownAccent"),
                    goalMain = Json.GetFloatList(qo, "goalMain"),
                    goalAccent = Json.GetFloatList(qo, "goalAccent"),
                    glideMain = Json.GetFloat(qo, "glideMain", 0f),
                    glideAccent = Json.GetFloat(qo, "glideAccent", 0f),
                    cover = Json.GetFloat(qo, "cover", 1f),
                    coverDamage = Json.GetFloat(qo, "coverDamage", 0f),
                });
            }
            return rec;
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
            if (hull != null)
            {
                sb.Append("    \"hull\": ");
                hull.HullToJson(sb);
                sb.Append(",\n");
            }
            // engines 的 JSON 序列化在 v1.0.0 清理 breaches 块时被整段误删了
            // （当时 engines 恰好排在 breaches 和 upgrades 之间）——
            // 采集一直在跑，但从来没写进文件，读档只好一直跳过。v1.1.2 找回。
            sb.Append("    \"engines\": [");
            for (int ei = 0; ei < engines.Count; ei++)
            {
                var e = engines[ei];
                if (ei > 0) sb.Append(", ");
                sb.Append("{\"index\": ").Append(e.index);
                sb.Append(", \"state\": ").Append(e.state);
                sb.Append(", \"faults\": ").Append(e.faults);
                sb.Append(", \"cowl\": ").Append(e.cowl);
                sb.Append(", \"bat\": ").Append(e.bat);
                sb.Append(", \"swap\": ").Append(e.swap);
                sb.Append(", \"swapSerial\": ").Append(e.swapSerial);
                sb.Append(", \"rpm\": ").Append(Json.Num(e.rpm));
                sb.Append(", \"oil\": ").Append(Json.Num(e.oil));
                sb.Append(", \"lOil\": ").Append(Json.Num(e.lOil));
                sb.Append(", \"temp\": ").Append(Json.Num(e.temp));
                sb.Append(", \"fire\": ").Append(Json.Num(e.fire));
                sb.Append(", \"batt\": ").Append(Json.Num(e.batt));
                sb.Append(", \"swapP\": ").Append(Json.Num(e.swapP));
                sb.Append('}');
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
                hull = ParseHull(d),
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

        // ---------- 多档位 ----------
        //
        // 档1 = career.json（主档，兼容旧档、旧流程与已发布的说明）；
        // 档2+ = career.slotN.json（独立文件，备份同理 career.slotN.bak.json）。
        // 所有读写（含热键）都作用在"当前档位"上；档位由暂停菜单的
        // 「档位」按钮循环切换，选择持久化在 BepInEx 配置的 ActiveSlot 里。
        //
        // GuardSave 的"存档是第几段"缓存按档位各存一份：切档后首次
        // 存盘会对新档位做一次文件预读，之后走内存。
        static int _activeSlot = 1;
        public static int ActiveSlot => _activeSlot;

        public static int SlotCount => Math.Max(1, Math.Min(6, Plugin.SaveSlots?.Value ?? 3));

        public static void SetActiveSlot(int slot)
        {
            _activeSlot = Math.Max(1, Math.Min(SlotCount, slot));
        }

        /// <summary>
        /// 档位的展示信息（选档界面用）：存档时间 + 段数 + 金钱。
        /// 文件不存在返回 null（界面显示"空"）。
        /// </summary>
        public static string SlotInfo(int slot)
        {
            try
            {
                string path = FileFor(slot);
                if (!File.Exists(path)) return null;
                var s = LoadFrom(path);
                if (s == null) return null;
                return s.savedAt.Replace('T', ' ') + " ｜ 第" + s.leg + "段 · " + s.money + "块";
            }
            catch { return null; }
        }

        static string FileFor(int slot)
        {
            return slot <= 1
                ? Path.Combine(_dir, "career.json")
                : Path.Combine(_dir, "career.slot" + slot + ".json");
        }

        static string BakFor(int slot)
        {
            return slot <= 1
                ? Path.Combine(_dir, "career.bak.json")
                : Path.Combine(_dir, "career.slot" + slot + ".bak.json");
        }

        public static string FilePath => FileFor(_activeSlot);

        public static void Init()
        {
            _dir = ResolveDir();
            Plugin.LogSource.LogInfo("[MaydayCareer] 存档路径 = " + FileFor(1)
                + "（档位数 = " + SlotCount + "）");
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

        // GuardSave 用的"存档里是第几段"内存缓存，按档位各一份。
        // 之前每次存盘都重新 Load() 整份 JSON 来对比 leg —— 纯粹为了读一个 int
        // 却在主线程做了整文件 IO + 解析，存盘时机又全在地面（Arrived/Turnaround），
        // 是落地瞬间卡顿的嫌疑之一。
        //   -2 = 还没读过盘（首次存盘时补读一次）
        //   -1 = 读过盘，没有存档文件
        //   >=0 = 缓存的存档 leg
        static int[] _cachedLegs; // 下标即档位（1 起），按需分配
        static readonly object _ioLock = new object();

        static int[] EnsureLegCache()
        {
            if (_cachedLegs == null)
                _cachedLegs = new int[8]; // 档位数上限 6，留些余量
            for (int i = 0; i < _cachedLegs.Length && i <= SlotCount; i++)
                if (i > 0 && _cachedLegs[i] == 0) _cachedLegs[i] = -2;
            return _cachedLegs;
        }

        static int GetCachedSavedLeg(int slot)
        {
            var cache = EnsureLegCache();
            if (slot >= cache.Length) return -2;
            if (cache[slot] != -2) return cache[slot];
            try
            {
                var old = LoadFrom(FileFor(slot));
                cache[slot] = old != null ? old.leg : -1;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 回退保护预读失败（按无存档处理）: " + e.Message);
                cache[slot] = -1;
            }
            return cache[slot];
        }

        public static void Save()
        {
            // 自动存盘的调用点（阶段机）不关心结果；返回值给手动入口用
            TrySave();
        }

        /// <summary>
        /// 尝试存盘。返回 null = 已提交写盘（后台线程完成落盘）；
        /// 返回字符串 = 跳过原因（GameState 未就绪 / GuardSave 拦截）。
        /// 菜单按钮用返回值在 toast 里如实告知用户，不能静默返回——
        /// 之前按钮误报"已存盘"，实际被 GuardSave 拦了，用户以为存的逻辑没生效。
        ///
        /// manual=true 跳过 GuardSave：选档界面里点行覆盖是带二次确认的明确操作，
        /// 不该被自动拦截（v1.3.0 用户反馈"低档覆盖的去了吧"）。
        /// 拦截保留给没有确认环节的路径：自动存盘（Arrived/Turnaround）与热键 ——
        /// 新公司落地自动存盘仍然不能抹掉真档。
        /// </summary>
        public static string TrySave(bool manual = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var gs = global::Mayday.GameState.Instance;
            if (gs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] GameState 为空，跳过存盘"); return "GameState 未就绪，跳过存盘"; }

            int slot = _activeSlot;

            // 档位回退保护：刚启动时游戏总是从「第 0 段、600 块」的新公司开始，
            // 这时候按 J 就会把真进度抹成初始值（实测踩过：钱 1300 -> 600）。
            // 当前局比存档更早，就跳过并提示，让用户先读档。
            // leg 从内存缓存取（按档位），不再整文件重读。
            if (!manual && Plugin.GuardSave.Value)
            {
                int savedLeg = GetCachedSavedLeg(slot);
                if (savedLeg > gs.Leg)
                {
                    string reason = string.Format(
                        "已跳过：当前第{0}段，档{2}已是第{1}段（更靠后），不覆盖",
                        gs.Leg, savedLeg, slot);
                    Plugin.LogSource.LogWarning(string.Format(
                        "[MaydayCareer] 存盘已跳过（档{2}）：当前是第{0}段，档{2}是第{1}段（更靠后）。"
                        + "刚开的新公司别急着存盘，先按读档键把进度拿回来。",
                        gs.Leg, savedLeg, slot));
                    return reason;
                }
            }

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

            sw.Stop();

            // 主线程只负责"读状态 + 序列化 JSON"，写盘（含备份复制）全部转后台。
            // 磁盘 I/O 的耗时不稳定（机械盘/杀毒扫描时能到几十毫秒），
            // 放主线程就是落地结算那一帧的卡顿源。
            //
            // 为什么不用 ThreadPool/Task：interop 引用的 Il2Cppmscorlib 里也有
            // System.Threading 的委托类型，编译器把 WaitCallback 解析到那边会报
            // CS0656（NullableAttribute 构造器不可见）。改用自定义委托 +
            // 单消费者写盘线程，类型绑定完全落在自己程序集里。
            string json = s.ToJson();
            var cache = EnsureLegCache();
            if (slot < cache.Length) cache[slot] = gs.Leg;
            string snapshot =
                "[档" + slot + "] 钱=" + s.money + " 债=" + s.debt + " 第" + s.leg + "段 升级=" + s.upgrades.Count +
                " 座排=" + s.seatRows.Count + " 物品=" + s.items.Count;

            lock (_ioLock)
            {
                _pendingSlot = slot;
                _pendingJson = json;
                _pendingSnapshot = snapshot;
                _pendingSerializeMs = sw.Elapsed.TotalMilliseconds;
                if (!_writeThreadAlive)
                {
                    _writeThreadAlive = true;
                    var t = new System.Threading.Thread(_WriteWorker);
                    t.IsBackground = true;
                    t.Name = "MaydayCareer.SaveIO";
                    t.Start();
                }
            }
            return null; // 已提交后台写盘
        }

        // 后台写盘的单消费者模型。_pendingJson 单槽：连续两次存盘时
        // 后一份覆盖前一份排队中的内容（写盘期间前一份已经开始写，互不影响，
        // _ioLock 保证 Delete/Move 不会交错出半新半旧的文件）。
        static int _pendingSlot = 1;
        static string _pendingJson;
        static string _pendingSnapshot;
        static double _pendingSerializeMs;
        static bool _writeThreadAlive;

        static void _WriteWorker()
        {
            while (true)
            {
                int slot;
                string json, snapshot;
                double serializeMs;
                lock (_ioLock)
                {
                    json = _pendingJson;
                    if (json == null) { _writeThreadAlive = false; return; }
                    _pendingJson = null;
                    slot = _pendingSlot;
                    snapshot = _pendingSnapshot;
                    _pendingSnapshot = null;
                    serializeMs = _pendingSerializeMs;
                    _pendingSerializeMs = 0;
                }

                try
                {
                    var swIo = System.Diagnostics.Stopwatch.StartNew();
                    string file = FileFor(slot);
                    WriteSaveFiles(slot, json);
                    swIo.Stop();
                    Plugin.LogSource.LogInfo(string.Format(
                        "[MaydayCareer] 已保存 {0} -> {1}（序列化 {2:F1}ms，写盘后台 {3:F1}ms，{4:0.0}KB）",
                        snapshot, file, serializeMs, swIo.Elapsed.TotalMilliseconds, json.Length / 1024.0));
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError("[MaydayCareer] 存盘写文件失败: " + e);
                }
            }
        }

        /// <summary>
        /// 实际落盘：备份上一份 -> 写 tmp -> 原子替换。只在后台线程调用（见 Save）。
        /// 加锁串行化：连续两次存盘（比如落地结算 + 手动 J）时后一次等前一次写完，
        /// 不然 Delete/Move 交错会出现半新半旧的文件。
        /// </summary>
        static void WriteSaveFiles(int slot, string json)
        {
            string file = FileFor(slot);
            string bak = BakFor(slot);
            lock (_ioLock)
            {
                Directory.CreateDirectory(_dir);

                // 写盘前先把上一份留一份。
                // 用户有过连按读档键把进度按回旧值的经历 —— 读档是覆盖操作，
                // 至少让被覆盖的那份还能手工捞回来。
                if (File.Exists(file)) BackupExisting(slot);

                string tmp = file + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
        }

        /// <summary>
        /// 把当前档位的存档复制成对应 .bak（覆盖旧 bak）。
        ///
        /// 失败只 warn，绝不允许挡住正常存盘 —— 备份是兜底，不是存盘的前置条件。
        /// 只做"写档前备份上一份"，不做"读档前备份"：语义简单，
        /// 用户随时能手工把 bak 改名回主档名捞回上一次。
        /// </summary>
        static void BackupExisting(int slot)
        {
            try
            {
                string file = FileFor(slot);
                string bak = BakFor(slot);
                File.Copy(file, bak, true);
                Plugin.LogSource.LogInfo(
                    "[MaydayCareer] 已备份上一份存档 -> " + Path.GetFileName(bak));
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
            return LoadFrom(FileFor(_activeSlot));
        }

        /// <summary>读指定文件（档位缓存预读用，不打扰当前档位语义）。</summary>
        static CareerState LoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) { Plugin.LogSource.LogInfo("[MaydayCareer] 没有存档，按新公司开局"); return null; }
                var s = CareerState.FromJson(File.ReadAllText(path, Encoding.UTF8));
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

            var legCache = EnsureLegCache();
            if (_activeSlot < legCache.Length) legCache[_activeSlot] = s.leg; // GuardSave 直接用内存值，不再重读盘

            var gs = global::Mayday.GameState.Instance;
            if (gs == null) { Plugin.LogSource.LogWarning("[MaydayCareer] GameState 为空，无法读档"); return; }
            _savedLeg = s.leg;

            Plugin.LogSource.LogInfo("[MaydayCareer] 本次读的是档" + _activeSlot + "（" + Path.GetFileName(FilePath) + "）");

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

        static float SafeFloat(Func<float> f, float fallback)
        {
            try { return f(); } catch { return fallback; }
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
                        lOil = SafeFloat(delegate { return u.lOil; }, u.oil),
                        temp = u.temp,
                        fire = u.fire,
                        batt = u.batt,
                        swapP = u.swapP,
                    });
                }
                var sbE = new StringBuilder();
                foreach (var e in s.engines)
                    sbE.Append(" #" + e.index + " state=" + e.state + " faults=0x" + e.faults.ToString("X2")
                        + " rpm=" + e.rpm.ToString("F0") + " oil=" + e.oil.ToString("F3")
                        + " lOil=" + e.lOil.ToString("F3") + " temp=" + e.temp.ToString("F1"));
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
                            // oil 有 Networkoil 镜像，看着像权威值，但实测 3 秒后被拉回 1.00；
                            // lOil 是本地那份，一起写才有机会留住。
                            try { u.lOil = rec.lOil; } catch { }
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
                // 比较键只含 state / faults（这两个是 SyncVar 权威值）；
                // oil / temp 是模拟量，只展示不参与判定 —— 否则永远误报"不一致"。
                var sbKey = new StringBuilder();
                foreach (var u in units)
                {
                    sbKey.Append(" #" + u.index + " state=" + (int)u.state + " faults=0x" + u.faults.ToString("X2"));
                    sbE.Append(" #" + u.index + " state=" + (int)u.state + " faults=0x" + u.faults.ToString("X2")
                        + " oil=" + u.oil.ToString("F3") + " lOil=" + SafeFloat(delegate { return u.lOil; }, -1f).ToString("F3")
                        + " Oil=" + SafeFloat(delegate { return u.Oil; }, -1f).ToString("F3"));
                }
                bool ok = sbKey.ToString() == _expectEngines;
                Plugin.LogSource.LogInfo("[MaydayCareer] 发动机校验（3 秒后）" + sbE + " → " + (ok ? "一致" : "不一致（期望" + _expectEngines + "）"));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 发动机校验失败: " + e.Message); }
        }

        // ---------- 机体：损伤 + 掉漆 ----------

        // 见 FindFuelSystem 的注释：同款缓存，理由相同。
        static global::Mayday.Systems.HullSystem _hullSystem;

        static global::Mayday.Systems.HullSystem FindHull()
        {
            try
            {
                var hs = _hullSystem;
                if (hs != null) return hs;
                hs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.HullSystem>();
                _hullSystem = hs;
                return hs;
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 找不到 HullSystem: " + e.Message); return null; }
        }

        // serverReady 是机体系统的总开关。没就绪时 holes/damage 的 Count 会小于面板数，
        // 这时候读到的全零只是占位 —— 硬写等于把好飞机写成残骸。
        // 之前九轮探测失败就是没等这个标记：Count 从 11 涨到 12 是在补齐面板数，
        // 不是"多了一个洞"。
        static bool HullReady(global::Mayday.Systems.HullSystem hs, out int panels, out string why)
        {
            panels = 0; why = "";
            try
            {
                if (hs == null) { why = "HullSystem 未就绪"; return false; }
                if (!hs.serverReady) { why = "serverReady=false"; return false; }
                int count = global::Mayday.Systems.HullSystem.Count;
                if (hs.holes == null || hs.damage == null || hs.dirt == null) { why = "per-panel 列表为空"; return false; }
                if (hs.holes.Count != count || hs.damage.Count != count || hs.dirt.Count != count)
                {
                    why = "面板数不匹配 holes=" + hs.holes.Count + " damage=" + hs.damage.Count
                        + " dirt=" + hs.dirt.Count + " 应为 " + count;
                    return false;
                }
                if (hs.sStage == null || hs.sProg == null || hs.sStage.Length != hs.sProg.Length)
                { why = "区段维修数组不完整"; return false; }
                panels = count;
                return true;
            }
            catch (Exception e) { why = e.Message; return false; }
        }

        static void PutColor(List<float> dst, UnityEngine.Color32 c)
        { dst.Add(c.r); dst.Add(c.g); dst.Add(c.b); dst.Add(c.a); }

        static void PutColor(List<float> dst, UnityEngine.Color c)
        { dst.Add(c.r); dst.Add(c.g); dst.Add(c.b); dst.Add(c.a); }

        static UnityEngine.Color GetColor(List<float> v)
        {
            if (v == null || v.Count < 4) return new UnityEngine.Color(1f, 1f, 1f, 1f);
            return new UnityEngine.Color(v[0], v[1], v[2], v[3]);
        }

        static UnityEngine.Color32 GetColor32(List<float> v)
        {
            var c = GetColor(v);
            return new UnityEngine.Color32(
                (byte)Math.Round(Math.Clamp(c.r, 0f, 1f) * 255f),
                (byte)Math.Round(Math.Clamp(c.g, 0f, 1f) * 255f),
                (byte)Math.Round(Math.Clamp(c.b, 0f, 1f) * 255f),
                (byte)Math.Round(Math.Clamp(c.a, 0f, 1f) * 255f));
        }

        static void ReadHull(CareerState s)
        {
            s.hull = null;
            try
            {
                var hs = FindHull();
                if (!HullReady(hs, out int n, out string why))
                { Plugin.LogSource.LogInfo("[MaydayCareer] 机体快照跳过：" + why); return; }

                var rec = new CareerState.HullRec { breaches = hs.breaches };
                try { rec.sectSync = hs.sectSync.ToString(); } catch { }

                float now = UnityEngine.Time.time;
                for (int i = 0; i < n; i++)
                {
                    var p = new CareerState.HullPanelRec();
                    try { p.damage = hs.lDamage[i]; } catch { }
                    try { p.netDamage = hs.damage[i]; } catch { }
                    try { p.dirt = hs.lDirt[i]; } catch { }
                    try { p.netDirt = hs.dirt[i]; } catch { }
                    try
                    {
                        var v = hs.holes[i];
                        p.hx = v.x; p.hy = v.y; p.hz = v.z; p.hr = v.w; p.hasHole = v.w > 0f;
                    }
                    catch { }
                    try { p.patchT = hs.patchT[i]; } catch { }
                    try
                    {
                        float last = hs.patchLast[i];
                        if (last > 0f) { p.hasPatchAge = true; p.patchAge = Math.Max(0f, now - last); }
                    }
                    catch { }
                    rec.panels.Add(p);
                }

                for (int j = 0; j < hs.sStage.Length; j++)
                    rec.sections.Add(new CareerState.HullSectionRec { stage = hs.sStage[j], progress = hs.sProg[j] });

                // 掉漆：paintInit/shownInit 都为 true、数组长度对上面板数，外观链才有效
                bool paintReady = false;
                try
                {
                    paintReady = hs.paintInit && hs.shownInit
                        && hs.paintCover != null && hs.paintCover.Length == n
                        && hs.shownMain != null && hs.shownMain.Length == n
                        && hs.coverDamage != null && hs.coverDamage.Length == n;
                }
                catch { }

                if (paintReady)
                {
                    int ok = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var q = new CareerState.HullPaintRec();
                        bool good = true;
                        try { PutColor(q.main, hs.mainColor[i]); } catch { good = false; }
                        try { PutColor(q.accent, hs.accentColor[i]); } catch { good = false; }
                        try { PutColor(q.netMain, hs.lMain[i]); } catch { good = false; }
                        try { PutColor(q.netAccent, hs.lAccent[i]); } catch { good = false; }
                        try { PutColor(q.shownMain, hs.shownMain[i]); } catch { good = false; }
                        try { PutColor(q.shownAccent, hs.shownAccent[i]); } catch { good = false; }
                        try { PutColor(q.goalMain, hs.goalMain[i]); } catch { good = false; }
                        try { PutColor(q.goalAccent, hs.goalAccent[i]); } catch { good = false; }
                        try { q.glideMain = hs.glideMain[i]; } catch { good = false; }
                        try { q.glideAccent = hs.glideAccent[i]; } catch { good = false; }
                        try { q.cover = hs.paintCover[i]; } catch { good = false; }
                        try { q.coverDamage = hs.coverDamage[i]; } catch { good = false; }
                        if (good) { rec.paint.Add(q); ok++; }
                    }
                    float minCover = 1f, maxCd = 0f;
                    foreach (var q in rec.paint)
                    {
                        if (q.cover < minCover) minCover = q.cover;
                        if (q.coverDamage > maxCd) maxCd = q.coverDamage;
                    }
                    Plugin.LogSource.LogInfo(string.Format(
                        "[MaydayCareer] 掉漆快照 {0}/{1} 块 最低覆盖={2:F2} 最大掉漆={3:F2}",
                        ok, n, minCover, maxCd));
                }
                else
                {
                    Plugin.LogSource.LogInfo("[MaydayCareer] 掉漆快照跳过：漆面外观还没初始化");
                }

                s.hull = rec;
                float maxDmg = 0f;
                foreach (var p in rec.panels) maxDmg = Math.Max(maxDmg, p.netDamage);
                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 机体快照 {0} 块面板 breaches=0x{1:X4} 最大损伤={2:F2}",
                    n, rec.breaches, maxDmg));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 机体快照失败: " + e.Message); }
        }

        static DateTime _hullCheckAt;

        static void RestoreHull(CareerState s)
        {
            if (s.hull == null || s.hull.panels.Count == 0)
            {
                Plugin.LogSource.LogInfo("[MaydayCareer] 机体还原跳过：存档没有机体数据（进机舱按 J 存一次即可记录）");
                return;
            }
            try
            {
                var hs = FindHull();
                if (!HullReady(hs, out int n, out string why))
                { Plugin.LogSource.LogWarning("[MaydayCareer] 机体还原跳过：" + why); return; }

                var rec = s.hull;

                // 顺序对齐游戏自己的状态链，倒着写会被下一帧重算盖掉：
                // 1) per-panel 损伤/脏污/洞/补丁 → 2) breaches → 3) 区段 →
                // 4) 网络色+本地色 → 5) 外观链 shown*/goal*/glide*/cover →
                // 6) shownInit=true → ApplyAllSectionPaint → MarkAllDirty
                int np = Math.Min(n, rec.panels.Count);
                float now = UnityEngine.Time.time;
                int holesBack = 0;
                bool holeWriteFailed = false;
                for (int i = 0; i < np; i++)
                {
                    var p = rec.panels[i];
                    try { hs.lDamage[i] = p.damage; } catch { }
                    try { hs.damage[i] = p.netDamage; } catch { }
                    try { hs.lDirt[i] = p.dirt; } catch { }
                    try { hs.dirt[i] = p.netDirt; } catch { }
                    try
                    {
                        hs.holes[i] = new UnityEngine.Vector4(p.hx, p.hy, p.hz, p.hr);
                        if (p.hasHole) holesBack++;
                    }
                    catch (Exception he)
                    {
                        // SyncList<Vector4> 的索引器在个别 interop 版本下可能有 marshalling 问题。
                        // 只报一次，别让它连累损伤和掉漆
                        if (!holeWriteFailed)
                        {
                            holeWriteFailed = true;
                            Plugin.LogSource.LogWarning("[MaydayCareer] 写 holes 失败（洞位可能不还原）: " + he.Message);
                        }
                    }
                    try { hs.patchT[i] = p.patchT; } catch { }
                    try { hs.patchLast[i] = p.hasPatchAge ? now - p.patchAge : 0f; } catch { }
                }

                try { hs.Networkbreaches = (ushort)rec.breaches; }
                catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 写 breaches 失败: " + e.Message); }

                int ns = Math.Min(hs.sStage.Length, rec.sections.Count);
                for (int j = 0; j < ns; j++)
                {
                    try { hs.sStage[j] = (byte)rec.sections[j].stage; } catch { }
                    try { hs.sProg[j] = rec.sections[j].progress; } catch { }
                }
                try { hs.NetworksectSync = ulong.Parse(rec.sectSync ?? "0"); } catch { }

                int paintOk = 0;
                int nq = rec.paint.Count;
                if (nq > 0)
                {
                    // 4) 网络色 + 本地色
                    for (int i = 0; i < Math.Min(n, nq); i++)
                    {
                        var q = rec.paint[i];
                        bool good = true;
                        try { hs.mainColor[i] = GetColor32(q.main); } catch { good = false; }
                        try { hs.accentColor[i] = GetColor32(q.accent); } catch { good = false; }
                        try { hs.lMain[i] = GetColor(q.netMain); } catch { good = false; }
                        try { hs.lAccent[i] = GetColor(q.netAccent); } catch { good = false; }
                        if (good) paintOk++;
                    }
                    // 5) 外观链：shown* 才是渲染读的；cover/coverDamage 是掉漆量
                    for (int i = 0; i < Math.Min(n, nq); i++)
                    {
                        var q = rec.paint[i];
                        try { hs.shownMain[i] = GetColor(q.shownMain); } catch { }
                        try { hs.shownAccent[i] = GetColor(q.shownAccent); } catch { }
                        try { hs.goalMain[i] = GetColor(q.goalMain); } catch { }
                        try { hs.goalAccent[i] = GetColor(q.goalAccent); } catch { }
                        try { hs.glideMain[i] = q.glideMain; } catch { }
                        try { hs.glideAccent[i] = q.glideAccent; } catch { }
                        try { hs.paintCover[i] = q.cover; } catch { }
                        try { hs.coverDamage[i] = q.coverDamage; } catch { }
                    }
                    try { hs.shownInit = true; } catch { }
                }

                // 6) 重绘：这才是让画面跟上的入口，TickPaint 只是每帧的颜色插值推进
                bool repainted = InvokeHull(hs, "ApplyAllSectionPaint") && InvokeHull(hs, "MarkAllDirty");

                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 机体还原 {0} 块面板（洞 {1} 处） breaches=0x{2:X4} 区段={3} 掉漆 {4}/{5} 块 重绘={6}",
                    np, holesBack, rec.breaches, ns, paintOk, nq, repainted ? "已调用" : "失败"));

                _hullCheckAt = DateTime.UtcNow.AddSeconds(3);
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 机体还原失败: " + e.Message); }
        }

        static bool InvokeHull(global::Mayday.Systems.HullSystem hs, string name)
        {
            try
            {
                var mi = hs.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (mi == null)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 找不到 HullSystem." + name + "（私有方法未生成托管桩）");
                    return false;
                }
                mi.Invoke(hs, null);
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 调用 HullSystem." + name + " 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>3 秒后回读损伤与掉漆 —— 写进去不算，留得住才算（燃油/发动机同一套规矩）。</summary>
        public static void TickVerifyHull()
        {
            if (_hullCheckAt == default) return;
            if (DateTime.UtcNow < _hullCheckAt) return;
            _hullCheckAt = default;
            try
            {
                var hs = FindHull();
                if (hs == null) return;
                float maxDmg = 0f, minCover = 1f;
                int holes = 0;
                try { for (int i = 0; i < hs.damage.Count; i++) maxDmg = Math.Max(maxDmg, hs.damage[i]); } catch { }
                try { if (hs.paintCover != null) for (int i = 0; i < hs.paintCover.Length; i++) minCover = Math.Min(minCover, hs.paintCover[i]); } catch { }
                try { for (int i = 0; i < hs.holes.Count; i++) if (hs.holes[i].w > 0f) holes++; } catch { }
                Plugin.LogSource.LogInfo(string.Format(
                    "[MaydayCareer] 机体校验（3 秒后）breaches=0x{0:X4} 最大损伤={1:F2} 洞={2} 最低覆盖={3:F2}",
                    hs.breaches, maxDmg, holes, minCover));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 机体校验失败: " + e.Message); }
        }

        // ---------- 燃油 ----------

        // 真值在 FuelSystem 上：fuelL / fuelR 是 SyncVar（NetworkfuelL / NetworkfuelR），
        // reserveTank 是本地模拟值。PlaneTelemetry.FuelLiters 只是每帧刷新的快照，
        // 写它没有意义 —— 下一帧就被模拟覆盖。
        // 缓存单例引用：FindObjectOfType 是全场景扫描（原生调用 + 场景越大越慢），
        // 存盘快照、读档还原、延迟校验都会用到。对象销毁后 Unity 包装器 == null，
        // 缓存自动失效、下次重扫 —— 换场景/退房不会拿到死引用。
        static global::Mayday.Systems.FuelSystem _fuelSystem;

        static global::Mayday.Systems.FuelSystem FindFuelSystem()
        {
            try
            {
                var fs = _fuelSystem;
                if (fs != null) return fs;
                fs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.FuelSystem>();
                _fuelSystem = fs;
                return fs;
            }
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
        /// 手动存盘入口。返回 null = 已提交写盘；否则 = 跳过原因（菜单按钮会显示给玩家）。
        ///
        /// skipLegGuard=true 跳过 GuardSave —— 选档界面专用：那里的覆盖
        /// 已经过了二次确认，是明确操作；热键 J 没有确认环节，保持拦截。
        /// </summary>
        public static string SaveManual(bool skipLegGuard = false)
        {
            try
            {
                bool server = false;
                try { server = global::Mirror.NetworkServer.active; } catch { }

                if (!server)
                {
                    Plugin.LogSource.LogWarning(
                        "[MaydayCareer] 手动存盘跳过：当前不是房主/服务端（纯客户端不写盘）");
                    return "只有房主能存盘（纯客户端不写盘）";
                }

                if (global::Mayday.GameState.Instance == null)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 手动存盘跳过：GameState 还没就绪");
                    return "GameState 还没就绪";
                }

                return TrySave(skipLegGuard);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 手动存盘异常: " + e);
                return "存盘异常：" + e.Message;
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
        // 读档时记下存档要求的座排位图。座排会在若干时机被游戏重置
        // （新一轮 ServerResetForNewRun、站点重锚），所以要能随时复查补回。
        static int _expectedRowMask = -1;

        // 读档那一局的段数。坠机或手动重开会把整局打回新公司，
        // 此时当前段数会小于它 —— 座排自愈必须就此罢手，
        // 否则新公司会凭空继承旧档的座椅（用户实测踩过：坠机后新档自动补齐 8 排）。
        static int _savedLeg = -1;

        /// <summary>
        /// 复查座排还在不在，缺了就补回来。
        /// 调用点：读档后 3 秒、以及每次 GroundCrew.ServerReanchor 之后。
        /// </summary>
        public static void EnsureSavedSeats(string why)
        {
            if (_expectedRowMask <= 0) return;
            try
            {
                // 当前局比读档那局更早 = 坠机/重开后进入的新公司。
                // 座排跟钱、升级一样归游戏重置，我们不再硬拉回存档值。
                try
                {
                    var gsNow = global::Mayday.GameState.Instance;
                    if (gsNow != null && gsNow.Leg < _savedLeg)
                    {
                        _expectedRowMask = -1;
                        Plugin.LogSource.LogInfo(string.Format(
                            "[MaydayCareer] 新一轮是第{0}段（存档是第{1}段），座排恢复游戏初始，不再自愈",
                            gsNow.Leg, _savedLeg));
                        return;
                    }
                }
                catch { }

                var pd = global::Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return;

                int mask = 0;
                try { mask = pd.rowMask; } catch { return; }
                int missing = _expectedRowMask & ~mask;
                if (missing == 0)
                {
                    Plugin.LogSource.LogInfo(string.Format(
                        "[MaydayCareer] 座排复查（{0}）：仍在 0x{1:X2}，无缺失", why, mask));
                    return;
                }

                bool want = false;
                try { var gs = global::Mayday.GameState.Instance; if (gs != null) want = gs.HasUpgrade(global::Mayday.Airport.UpgradeCatalog.SeatPremium); } catch { }

                int fixedCount = 0;
                for (int i = 0; i < 8; i++)
                {
                    if ((missing & (1 << i)) == 0) continue;
                    try { pd.ServerInstallRow(i, want); fixedCount++; }
                    catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 座排复查补装第" + i + "排失败: " + e.Message); break; }
                }
                int after = 0;
                try { after = pd.rowMask; } catch { }
                Plugin.LogSource.LogWarning(string.Format(
                    "[MaydayCareer] 座排被重置（{0}）：0x{1:X2} 缺 0x{2:X2} → 补装{3}排，现在 0x{4:X2}（目标 0x{5:X2}）",
                    why, mask, missing, fixedCount, after, _expectedRowMask));
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 座排复查异常: " + e.Message); }
        }

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

                _expectedRowMask = targetMask;   // 供 3 秒后与站点重建后复查用

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
