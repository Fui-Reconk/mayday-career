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

`ServerInstallRow` 是 public 的低层入口，上面那一整套门槛（持有套件、在位、进度条）全在它之上。**直接调它 = 无校验、不消耗任何 `seat_kit`**。插件里的 `SeatRows` 就是这么干的，属于作弊性质，README 里已明示。

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

## 待解决

1. `ServerResetRun(int, int)` 两个参数的含义（seed？难度？）—— 影响能否连 `Leg`、`RunSeed` 一起还原
2. 油量的真实写入点（telemetry 只是快照）
3. 机体损伤与涂装的还原路径
4. 多人下队友重连后的状态是否完整（`SyncList` 全量同步应该没问题，未验证）
5. `CompleteInstall` 内部是否就是调 `ServerInstallRow`、是否会顺手用 `FreeKitAt` 销毁附近套件 —— 没有 IL 无法确认
6. `RowCount` 到底是"已装数"还是"总槽位数" —— 运行时把 `RowCount` 和 `rowMask` 的 popcount 一起打出来即可判定
