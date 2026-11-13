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
ServerShowReport(FlightReport)  // 航班结算 —— 存盘时机
ServerSetPhase(GamePhase)
```

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

## 待解决

1. `ServerResetRun(int, int)` 两个参数的含义（seed？难度？）—— 影响能否连 `Leg`、`RunSeed` 一起还原
2. 油量的真实写入点（telemetry 只是快照）
3. 机体损伤与涂装的还原路径
4. 多人下队友重连后的状态是否完整（`SyncList` 全量同步应该没问题，未验证）
