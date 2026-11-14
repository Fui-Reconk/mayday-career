# 逆向记录

面向想继续往下做的人。这里记的是**结论和验证方法**，不是过程流水账。

## 环境

| | |
|---|---|
| 游戏 | Mayday Airways Demo (Steam AppID 5480890) |
| 版本 | 0.14.11 |
| Unity | 6000.3.20f1 |
| 脚本后端 | IL2CPP，metadata v39 |
| 网络层 | Mirror + FizzySteamworks |
| 插件框架 | BepInEx 6.0.0-be.793 (IL2CPP, win-x64) |

## 关键前提：interop 是必需的

Unity 6 / metadata v39 目前主流工具支持都很差：Il2CppDumper 6.7.46 只到 v31，Cpp2IL 正式版只到 v29（pre-release 才到 v39）。

**最省事的路线不是 dumper，而是让 BepInEx 自己生成 interop**：启动一次游戏，`BepInEx\interop\` 会出现 119 个程序集，其中 `Assembly-CSharp.dll` 就是完整的类型结构。之后用 `MetadataReader` 离线解析即可，不需要跑游戏。

`tools/interop-inspector` 就是干这个的。

## 为什么游戏没有存档

- 磁盘上只有 `settings.json`（`settingsVersion: 4`）和 `ui_hints.json`，都是配置类
- `GameSettingsData` 的 34 个字段里没有金钱、没有航段、没有升级
- `Money` / `Leg` / `Upgrades` 全在 `Mayday.GameState : NetworkBehaviour` 上，是会话级对象
- 存在 `ServerResetRun(int, int)` 这个方法，说明设计上“开局即重置”
- 没有 `Http` / `Api` / `Account` / `Login` 类型，`Mayday.Services` 只是 `Dictionary<Type, object>` 注册表，不是后端

## 状态所在

### Mayday.GameState（核心，单例 `Instance`）

```
Money / Debt / Leg / RunSeed            int
CurrentAirport / DestinationAirport     int
TotalEarned / TotalPassengers           int
Rating / Difficulty / TimeOfDay / Weather   float
Phase                                   GamePhase
Upgrades                                SyncList<string>
```

方法（决定了“怎么写回去”）：

```
ServerAddMoney(int delta, string reason)
ServerAddDebt(int delta)
ServerAddUpgrade(string id)
HasUpgrade(string id) / UpgradeLevel(string id)
ServerResetRun(int, int)        // 开局重置 —— 读档必须抢在它之后
ServerShowReport(FlightReport)  // ⚠️ 实测整局都不会被调用，别挂这里（见下）
ServerSetPhase(GamePhase)
```

**存盘时机的坑**：一开始想挂 `GameState.ServerShowReport`，结果跑完一趟日志里一条都没有。真正跑结算的是导演机的 **`GameDirector.ServerReport()`**（无参、private），它在 `Flight -> Arrived` 的转换里触发。所以存盘挂在导演机的阶段机上，`Arrived` / `Turnaround` 两个阶段都是"成功飞行之后"的安全点。

**不要直接写 `Money` 属性**：值是变了，但 `MoneyChanged` 静态事件不触发，HUD 和账本会不同步。

### Mayday.PlaneTelemetry（全静态）

```
FuelLiters            float[]   [get,set]   // 逐油箱绝对升数
FuelCapacityPerTank   float
HullIntegrity         float
EngineHealth          float[]
EngineOil             float[]
CowlOpen              bool[]
```

坑在这里：它是**每帧刷新的快照**，虽然属性有 setter，但写进去下一帧就被模拟覆盖，恢复不了真实状态。要恢复油量和损伤，得找到底层持有者（`Mayday.Systems.HullSystem.jetHp`、`EngineUnit` 之类），这还没做完。

### Mayday.Systems.HullSystem

```
holes        SyncList<Vector4>   // 蒙皮破洞
jetHp        float[]
shownMain / shownAccent  Color[] // 涂装
```

### Mayday.Systems.FuelSystem（燃油：派生值 vs 真实存储）

```csharp
fuelL / fuelR        float   [get,set]  // 派生值，配 NetworkfuelL/NetworkfuelR 同步给客户端
capacity             float   [get,set]  // 单箱容量
tank                 float[] [get,set]  // ★ 模拟的真实存储，实测 2 个元素（左/右）
reserveTank          float   [get,set]  // 本地储备油
reserveSync          float   [get,set]  // 储备油的同步副本
```

**这里踩过一个坑，值得单列**：`fuelL/fuelR` 有 `NetworkfuelL` 之类的 SyncVar 备份字段，看上去像权威数据，于是第一版直接写它们。结果：

```
燃油还原 L=150.9 R=150.9                    ← 写入成功，回读也是 150.9
燃油校验（3 秒后）L=160.0 R=160.0 → 不一致    ← 被满油覆盖
```

原因：模拟每帧从 `tank[]` 重算 `fuelL/fuelR`，SyncVar 同步的是**计算结果**，不是源头。改成先写 `tank[]` 再写 `fuelL/fuelR` 后：

```
燃油快照 tank=[152.2, 152.2]
燃油还原 tank写入=2项
燃油校验（3 秒后）L=152.2 R=152.2 期望 L=152.2 R=152.2 → 一致
```

**通用教训**：判断"能不能写"不能只看有没有 SyncVar 备份字段，要看模拟读的是哪个。**写完之后隔几秒回读**是唯一可靠的验证方式——即时回读一定会读到你刚写的值，看起来永远是对的。

另外 `PlaneTelemetry.FuelLiters` 是每帧刷新的只读快照，写它完全无效，只能用来读。

## 持久化的既有范式

游戏自己在 `%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\` 下写 JSON，缩进 4 空格、camelCase、带版本号（`settingsVersion` / `cosmeticsVersion`），还有 `LegacyDataMigration` 做版本迁移。本插件的 `career.json` 沿用同一套约定。

## 座排：`rowMask` 才是真相

```
Mayday.Passengers.PassengerDirector
  rowMask        Byte   [SyncVar]        // 第 i 位 = 第 i 排已装 ← 权威来源
  HookRows(byte old, byte now)           // 位图变化时的回调
  RowCount       int    [get]            // 语义不明，不要拿来当依据
  IsRowInstalled(int slot)  bool         // 位运算，越界返回 false 而不是抛
  ServerInstallRow(int slot, bool premium)   // public，低层直通
```

**坑**：`RowCount` 装排前后都读到 3，用"已装数"和"总槽位数"两种语义都能解释得通。按它去补装会**超装**——如果它是总槽位数，遍历 `i < RowCount` 就会把所有空槽都填上，改变座位容量、破坏乘客分配。所以补装一律按 `rowMask` 的置位走。

**另一个坑**：`IsRowInstalled` 对越界 slot **不抛异常、返回 false**。所以仅靠它判断，扫描循环会一路尝试越界 slot。护栏必须是两层：先用几何上限夹取，再加"抛异常就停"。

### 座排上限

上限取 **`min(8, PlaneLayout.SeatRowZ.Length)`**：

- `rowMask` 是 `Byte` → 8 位，硬上限
- **`PlaneLayout.SeatRowZ : Single[]`** 是每个座排位的 Z 坐标数组，`Length` 是机身几何真正能摆几排。超出这个长度的 slot 没有座椅，装出来是空排

`PlaneLayout` 上**没有** `MaxRows` / `RowCapacity` 之类的显式字段（整个类型过了一遍）。同族只有三个相关项：`SeatRowZ`（排位数组）、`InitialSeatRows`（开局装几排，实测 3）、`SeatsPerRow`（每排几个座）。

### 安装链：`ServerInstallRow` 在最底下

```
玩家手持 SeatKitItem、站在 SeatMount 前
  HoldsKit(PlayerNet, Item) / KitNearSlot(Vector3, int) / KitAvailable(int, PlayerNet)
  KitAvailableAny(int) / ScanKits() / SlotForKitPosition(Vector3)
    ↓
长按累积进度
  CmdInstallWork(byte, conn) → InstallTick(float) / InstallProgress01(int)
  InstallWorkers(int,int) / EstimateInstallHold(int) / InstallHeartbeat(float)
    ↓
完成
  CompleteInstall(int slot)
    ↓
  ServerInstallRow(int slot, bool premium)   ← public，链底
```

`ServerInstallRow` 是 public 的低层入口，上面那一整套门槛（持有套件、在位、进度条）全在它之上。**直接调它 = 无校验、不消耗任何 `seat_kit`**。插件按存档 `rowMask` 还原座排时走的就是这条路（`RebuildSeats`），等价于凭存档白送座排，不带任何套件消耗。

## 升级不投递实物（重要负面结论）

`UpgradeInfo` 上确实有 `deliverItem` / `deliverCount` / `deliverTinted` 三个字段，`UpgradeCatalog` 里也有对应的 `Deliver(info, id, count)` 设置方法。看起来"买升级会送实物"完全成立。

**但实测 7 个升级的 `deliverItem` 全是空**：

```
升级 id=seat_premium 价格=500 ... 投递物=- 投递数=0
（其余 6 项同样）
```

所以这条路不存在。客舱器材**全部**来自补给站直接购买（`ItemCatalog` 里 `shop = true` 那批），游戏对世界物品零持久化。

顺带证伪了一条更早的猜想：`GameDirector.ServerBuyUpgrade` 旁路也走不通——`UpgradeCatalog.Check` 会先判 `Owned` 直接返回，既不会扣钱也不会投递。

> 这类"字段存在但永远为空"的结论只有在运行时才能确认：interop 是托管 stub，不含字符串字面量，`deliverItem` 的具体值离线读不到。插件里的 `DumpOnLoad` 就是干这个的。

## 物品快照方案

```
Item.ServerSpawn(string id, Vector3 pos, Quaternion rot, Action<Item> cb, bool flag) → Item
ItemSocket.ServerInsert(Item) → bool
Item.ServerAttachToSocket(ItemSocket)
```

**socket 归属用 `transform` 层级路径，不用世界坐标**。理由：`GroundCrew` 会把整个机场站点重新锚定到下一个机场，世界绝对坐标在存读之间会变，而舱内层级结构不变。路径形如 `Plane#0/Cabin#2/Row0#2/Socket#0`（`name#siblingIndex` 逐级拼接）。

**幂等靠"按 id 计数再补差"**：读档时先扫场景里同 id 物品的数量，只生成缺的那几件。反复读档不会翻倍。

### 过滤规则用分类白名单

```
Mayday.ItemCategory = { Galley, Cabin, Cleaning, Maintenance, Luggage, Crate, Misc }
```

存：`Galley` / `Cabin` / `Cleaning` / `Maintenance` / `Crate`
不存：`Luggage`（行李，结算会自己清）、`Misc`（`pax_carry` / `player_carry` / 炸弹等运行时临时物）

外加排除 `Item.IsHeld`。**白名单比 id 黑名单稳**：游戏以后加新物品不会漏，而列 id 黑名单永远会漏。

### `GroundCrew.ServerReanchor` 优于 `Reanchored` 静态事件

`GroundCrew.Reanchored` 是 `Il2CppSystem.Action<int>` 静态事件，从托管代码挂委托要过 Il2CppInterop 的委托封送，在 IL2CPP 下容易运行时炸。

而 `ServerReanchor(int siteIndex, ReanchorKind kind)` 正是那个事件的发出点（public），用 Harmony postfix 挂在它后面，时机完全等价——所有 `GroundService.ServerOnReanchor(kind)` 都跑完了，且晚于 `GameDirector.CleanupItems()` 的清场。

`ReanchorKind = { Move, Snap, NewRun }`。`SupplyShop` / `CargoLoaders` / `AirStairs` / `BagDrop` / `BaggageCart` / `FuelTruck` / `Casino` 各自实现 `ServerOnReanchor`。

## 升级效果的三种消费方式（决定了读档后要不要补）

| 消费方 | 读取方式 | 读档后自愈？ |
|---|---|---|
| `GalleySystem`（Espresso / Microwave / FridgeBig） | `PollUpgrades(bool)` + `upgradeT`，在 `Update` 里轮询 | ✅ 会 |
| `CargoNets` | `owned` + `ownedCheck` 定时器，`FixedUpdate` | ✅ 会 |
| `CargoLoaders`（BeltLoader） | `BeltOwned` + `lastOwned` + `ownedCheck` | ✅ 会 |
| `PassengerDirector.premiumSeats`（SeatPremium） | 只有 `ServerSetPremium(bool,bool)`，**无定时器** | ❌ 不会 |
| 世界实体（座排、器材） | 购买那一刻生成一次 | ❌ 不会 |

这张表解释了为什么用户只反馈"座位和器材没了"——咖啡机、货舱网、传送带其实自己会恢复。

## 验证 dll 里的中文字面量：UTF-16LE

C# 字符串字面量编译进程序集的 `#US` heap 是 **UTF-16LE**。所以：

- `strings file.dll | grep 中文` → **查不到**
- `grep -a 中文 file.dll`（按 UTF-8）→ **查不到**

正确做法是把待查串转成 UTF-16LE 再做二进制匹配：

```bash
printf '%s' "座位补装" | iconv -f UTF-8 -t UTF-16LE > /tmp/pat.bin
grep -aq -F -f /tmp/pat.bin MaydayCareer.dll
```

（ASCII 标识符如类型名、方法名在 `#Strings` heap 里是 UTF-8，直接 `grep` 就行——这就是为什么"方法名查得到、中文查不到"。）

## 机体损伤与机翼掉漆（v1.1.0 翻案：之前的结论是错的）

> 下面那节"查了九轮，最终整块放弃"的历史结论**已被推翻**，保留下来是为了记录错误推理的过程。
> 翻案的依据是分析社区模组 mayday-save 0.6.52（LS3213/Mayday-Mod-Manager，MelonLoader + Il2CppInterop）。
> 该仓库无 LICENSE，只学字段映射与调用顺序，未复制任何代码。

**之前错在哪：**

| 当时的判断 | 实际情况 |
|---|---|
| `holes.Count` 11→12 是"多了一个洞" | `holes` 是 per-panel 列表，Count 必须 == `HullSystem.Count`；11→12 是初始化在补齐面板数 |
| 洞的数据"不在托管层" | 在：`holes[i]`（Vector4 位置+半径）、`damage[i]`/`lDamage[i]`、`dirt[i]`/`lDirt[i]`、`patchT[i]`/`patchLast[i]` |
| `breaches`/`jetSync`/`sectSync` 是洞的数据 | 只是汇总位图；`jetHp` 是派生值。写位图当然无效 |
| 掉漆 = `paintCover` | `paintCover` 只是覆盖度；渲染读 `shownMain/shownAccent`，还有 `goalMain/goalAccent`、`glideMain/glideAccent`、`coverDamage` |
| 重绘 = `TickPaint` | 重绘 = `ApplyAllSectionPaint()` + `MarkAllDirty()`（私有，反射调用） |
| 就绪判据 = `paintInit` | 就绪判据 = `serverReady` 且 `holes/damage/dirt` 的 Count == `HullSystem.Count` |

**为什么九轮探测全失败：** 探测时机没等 `serverReady`，读到的都是初始化前的占位零；又把 `SyncList` Count 的增长误读成了洞的变化。教训：**探测一个系统的状态前，先找它的"就绪"标记**。

**字段表（interop 全部确认存在）：**

```
per-panel：damage/dirt (SyncList<float>)、lDamage/lDirt/patchT/patchLast (float[])
           holes (SyncList<Vector4>：x,y,z,半径)、sStage (byte[])、sProg (float[])
汇总：     Networkbreaches (ushort)、NetworksectSync (ulong)
掉漆：     mainColor/accentColor (SyncList<Color32>)、lMain/lAccent (Color[])、
           shownMain/shownAccent/goalMain/goalAccent (Color[])、glideMain/glideAccent (float[])、
           paintCover/coverDamage (float[])
方法：     ApplyAllSectionPaint()（private）、MarkAllDirty()（private）
常量：     HullSystem.Count（面板数）、HullSystem.StagePaint（区段维修的喷漆阶段）
判据：     serverReady、paintInit+shownInit（掉漆链）
```

**还原顺序（顺序错了会被下一帧重算盖掉）：**

```
1. lDamage/damage、lDirt/dirt、holes[i]、patchT/patchLast（逐面板）
2. Networkbreaches
3. sStage/sProg → NetworksectSync
4. mainColor/accentColor → lMain/lAccent
5. shown*/goal*/glide*/paintCover/coverDamage → shownInit = true
6. ApplyAllSectionPaint() → MarkAllDirty()
```

### 实测：喷漆与区段修复的状态机（v1.1.2 后，用户实机操作得出）

**喷漆（spray can）：**

- `paintCover`（cover）= 漆面完好度，**喷漆会涨**：全机 0.16→0.35，重点喷的面板到 0.55
- `dirt` = 脏污，**喷罐顺带清零**：0.66→0、0.41→0
- `damage` / `coverDamage` 完全不受喷漆影响 —— 结构伤归锤子/补丁，外观归喷罐
- `paintCover` 同时是**派生值**：存档存 0，读档后 3 秒回读变 0.41、再变 0.56（模拟朝当前应有状态回归）。视觉由 damage/dirt/颜色驱动，所以不影响 —— 但别指望把它当权威数据存

**区段修复（三段状态机）：**

```
sStage = 0      健康，无需修理
sStage = 1/2/3  三段各自"待做"的档位（对应 HullSectTuning.FrameSeconds / PlateSeconds / PaintSeconds）
sProg           当前段的累积进度（长按工作累积）
```

- 完成一段工作 → `sStage` 前进一位；三段全做完 → 回到 0（健康）
- **部分修复不减少面板 `damage`** —— 修到一半时，进度只存在于区段状态机里，
  面板 damage 要等整段结算。这就是 `sStage`/`sProg` 必须单独存的原因：修一半的进度也能跨局保留
- 实测记录：面板 #2 做完第 1 段 → `stage 1→2`；面板 #4 做完第 1、2 段 → `stage 1→3`（progress 0.10）；
  未动的 #0 停在 1。面板 damage/dirt/patchT 全程未变

## 【历史，已推翻】机体损伤与机翼掉漆：查了九轮，最终整块放弃

结论先写在最前面：**机体损伤（破洞 / 分段）和机翼掉漆在 v1.0.0 里都不保存**，代码也删干净了。下面是把这条路走死的过程，留给以后想接着挖的人。

### 破洞：七个入口全读不到

打洞本身是通的——`HullPanels.ServerBlowAt` 就是随机事件用的那个入口，按一下就能在指定面板开洞。但**读不出洞在哪**。

实测条件：打洞后画面上出现明显破洞，日志 `打洞结果 成功=1`。

| 入口 | 结果 |
|---|---|
| `holes[i]`（`SyncList<Vector4>` 索引器） | `Count` 会变（11→12），元素**全 (0,0,0,0)** |
| `holes.objects[i]`（绕过索引器读内部列表） | **同样全零** → 不是 interop marshalling 问题，数据真不在托管层 |
| `holePlaced[i]`（`Vector4[]`） | 长度 12，非空 0 |
| `HoleOpen(slot)`（public 查询） | 计数 0 |
| `holeViews[i].key` + `root.activeSelf` | 长度 12，活跃 0 |
| `breaches`（`UInt16`，配 `Networkbreaches`） | 恒为 `0x0000`，与画面不符（16 位宽和 12 个槽位只是巧合） |

### 分段受损（`jetHp`）：写进去留不住

`jetHp : float[]`（7 段）是唯一跟着破洞变化的字段，所以先尝试直接存它：

```
损伤还原 jetHp写入=7项
损伤立即回读 jetHp=[0.00, 0.50, 0.00, 0.30, ...]   ← 当场写进去了
损伤校验（3 秒后）jetHp=[0.00, 0.00, 0.00, ...]     ← 3 秒后归零
```

**当场可读、几秒后被重算** —— 和燃油的 `fuelL/fuelR` 一个套路，说明它是派生值。于是去找源头：`jetSync` / `sectSync` 都是 `UInt64` SyncVar，从真实存档反推出编码像是「每段 1 字节，受损段填 `0xFF` / `0x01`」：

| jetHp | jetSync | sectSync |
|---|---|---|
| `[0,0,0,0,1,0,0]` | `0xFF00000000` | `0x100000000` |
| `[0,0,0,0,0,1,0]` | `0xFF0000000000` | — |

按这个编码写 `jetSync=0xFF00FF00`、`sectSync=0x1000100` 之后，`jetHp` **依然是全零**。所以它不是从这两个位图算出来的，剩下的唯一解释是：`jetHp` 由破洞本身推导，而破洞读不到。

### 机翼掉漆（`paintCover`）：数据能存，视觉不动

`paintCover : float[]`（12 项）配 `HullPaintTuning.CoverMax / CoverSeconds / UncoverPerDamage`，语义是「还剩多少漆」。它是真存储：

```
存档写 [0.55, 0.60, 1.00, ...]
漆面校验（3 秒后）[0.55, 0.60, 1.00, ...]   ← 原样留住
```

但**改了数组材质不重绘**。试过反射调私有方法强制刷新：

```
漆面刷新：TickPaint(0.05)
漆面刷新：TickPaintCover 逐块调用 N 次
```

调得到、不报错，画面上依然看不出掉漆。说明视觉那条链上还有一环（很可能是 `BuildPaintGroups` / `SplitPlatesByPaint` 把外皮拆成若干组后按组换材质的流程），我们没有可靠的触发点。

### 为什么最终决定整块不做

1. **破洞在飞行途中就会被玩家用胶带补掉**，一轮结束时几乎不会留着洞。还原一个「本来就不该存在」的状态没有意义（这是用户提的，判断是对的）
2. **掉漆同理**，机翼掉漆的修法就是刷漆，飞行中就会被补上
3. 分段受损依附于破洞，没有洞就没有状态可还原
4. 硬写 SyncVar（`breaches` / `jetSync`）会让客舱显示一个并不存在的受损状态，还会同步给队友——比不存更糟

所以 v1.0.0 把这块代码整个删掉了，只保留「发动机故障位」这种真正的持久状态。

## 待解决

1. `ServerResetRun(int, int)` 两个参数的含义（seed？难度？）—— 不影响现在的结果，`Leg` / `RunSeed` 已能直接写属性
2. 机翼掉漆的视觉刷新链（`BuildPaintGroups` / `SplitPlatesByPaint` 之后按组换材质那一步），数据层已通
3. 涂装（livery）持久化未做
4. 多人下队友重连后的状态是否完整（`SyncList` 全量同步理论上没问题，未实测）
5. `CompleteInstall` 内部是否就是调 `ServerInstallRow`、是否会顺手用 `FreeKitAt` 销毁附近套件 —— 没有 IL 无法确认
6. `RowCount` 到底是"已装数"还是"总槽位数" —— 运行时把 `RowCount` 和 `rowMask` 的 popcount 一起打出来即可判定
