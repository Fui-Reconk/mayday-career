# Mayday Career

给《Mayday Airways》Demo 补一层**跨局进度存档**：每趟航班结束后把公司状态写进文件，下次启动接着上次的钱、座排、燃油、发动机和客舱器材继续飞。

游戏本身没有这个功能。它只持久化设置和装扮（`settings.json` / `ui_hints.json`），而金钱、欠款、已购升级都挂在 `GameState` 这个 `NetworkBehaviour` 上——随房间建立、随房间销毁，退出即清零。客舱里花钱买的器材更彻底：连会话级状态都算不上，是纯场景对象，换个机场就没了。

不改动游戏任何文件，删除插件即可完全复原。

> **仅房主安装即可。** 存档由主机写入并读取，队友通过游戏原有的状态同步自动看到结果，**其他人不需要装**。
> 本插件改变经济节奏，请在单人或私人房间使用。

---

## v1.0.1 保存了什么

| 项 | 数据来源 | 状态 |
|---|---|---|
| 金钱 / 欠款 | `GameState.Money` / `Debt` | ✅ |
| 第几段 / 当前机场 / 目的地 | `GameState.Leg` / `CurrentAirport` / `DestinationAirport` | ✅ |
| 评级 / 难度 | `GameState.Rating` / `Difficulty` | ✅ |
| 累计收入 / 累计乘客 | `GameState.TotalEarned` / `TotalPassengers` | ✅ |
| 已购升级 | `GameState.Upgrades`（`SyncList<string>`） | ✅ |
| 座位排数 | `PassengerDirector.rowMask` 位图 | ✅ |
| 燃油 | `FuelSystem.tank[]` 左右箱 + 储备油（**绝对升数**） | ✅ |
| 发动机 | 每台的 `state` / `faults` / `rpm` / `cowl` / 机油量 | ✅ |
| **机翼掉漆** | `HullSystem.paintCover`（每块外皮"还剩多少漆"） | ✅ 数据层，视觉见下 |
| 客舱器材 | 灭火器、锤子、胶带、油桶、座位套件等实体的完整快照 | ✅ |

### 不保存什么，以及为什么

| 项 | 原因 |
|---|---|
| **机身破洞** | 游戏不把洞的坐标暴露给托管层——`holes`、`holePlaced`、`HoleOpen()`、`holeViews`、`breaches`、`jetHp`、`jetSync` 七个入口实测在洞明明存在时全部返回零或未激活。况且洞在飞行途中玩家就会补掉，留着反而怪 |
| **分段受损（`jetHp`）** | 它是洞的派生值：直接写当场可读、3 秒后被重算归零；改写成 `jetSync` / `sectSync` 位图再试，`jetHp` 依然是零。没有洞可依附，"哪一段受损"就是个空状态 |
| **行李** | 每趟航班按合同生成，结算时游戏自己会清 |
| **手持物品、乘客携带物、炸弹** | 运行时临时对象（`Misc` 分类），存了没意义 |
| **燃烧 / 冒烟等特效** | 瞬时视觉，只还原"故障"这个事实 |

### 掉漆这一项的特殊说明

机翼掉漆**不会自己恢复**——玩家得拿喷漆去补，而不少人玩到通关都没搞清怎么补。所以它是真状态，必须存。

数据层已经验证：存档写 `[0.55, 0.60, 1.00, ...]`，读档 3 秒后原样还在。**唯一的缺口是材质重绘**——数组改了，游戏不一定立刻重画外皮，读档后可能要先飞一段、或者等它自己 tick 一次才看得出来。功能逻辑（要不要补漆）是按数据走的，所以不受影响。

`paintInit = false` 时插件**不读也不写**这个数组——那时 12 个零只是占位，硬写会把好飞机写成"整机掉漆"。

详细逆向过程见 [docs/FINDINGS.md](docs/FINDINGS.md)。

---

## 快速上手

1. 装好 BepInEx（见下方*安装*）
2. 把 `MaydayCareer.dll` 放进 `BepInEx\plugins\`
3. 进游戏、**走进机舱**，按 `J` 存一次档；下次按 `F9` 读回来

## 热键

| 键 | 配置项 | 默认 | 说明 |
|---|---|---|---|
| 读档 | `LoadKey` | `F9` | 把存档覆盖到当前进度 |
| 存盘 | `SaveKey` | `J` | 立刻写一份存档，不用飞完一趟 |

两个键都**在机舱里按**（主菜单里 `GameState` 还没建，按了只会打一条 warn 日志）。键名取 `UnityEngine.InputSystem.Key` 的枚举名，例如 `F9` / `F10` / `J` / `K` / `L`。

> 更简单地理解：`J` 是"现在这样就挺好，记下来"，`F9` 是"回到上次记下来的那一步"。**读档是覆盖操作**，按之前想清楚。

---

## 安装

1. 安装 **BepInEx 6.0.0-be.793**（IL2CPP, win-x64）到游戏根目录，与 `MaydayAirways.exe` 同层
2. 启动一次游戏，等控制台生成完 119 个 interop 程序集，进主菜单后退出
3. 把 `MaydayCareer.dll` 放进 `BepInEx\plugins\`
4. 重新启动游戏

Steam 用户可以右键游戏 → 管理 → 浏览本地文件，直接打开游戏目录。

## 编译

```powershell
.\build.ps1                  # 自动从 Steam 注册表定位游戏目录
.\build.ps1 -Game "路径"     # 手动指定
.\build.ps1 -Deploy          # 编译并装进游戏
```

依赖 .NET SDK 6.0+，以及游戏目录里已经生成好的 `BepInEx\interop`。

## 存档在哪

```
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.json        主存档
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.bak.json    上一份
```

和游戏的 `settings.json` 放一起。每次写新档之前会把上一份复制成 `career.bak.json` —— 手滑读档覆盖了进度，把 `career.bak.json` 改名成 `career.json` 就能捞回上一次。直接删掉 `career.json` 就是全新公司。

## 配置

```
BepInEx\config\local.mayday.career.cfg
```

### `[General]`

| 项 | 默认 | 说明 |
|---|---|---|
| `AutoSave` | `true` | 成功落地 / 周转阶段自动存盘 |
| `SaveOnCrash` | `false` | 坠机（`GameOver`）是否也存盘。默认不存，存档停在最后一次成功落地 |
| `AutoLoadOnStart` | `false` | 进入新一轮时自动读档。默认关闭，改为手动按热键 |
| `LoadKey` | `F9` | 读档热键 |
| `SaveKey` | `J` | 存盘热键 |
| `GuardSave` | `true` | 存盘回退保护：当前局比存档更早（如刚启动的第 0 段新公司）时跳过存盘 |
| `SaveItems` / `LoadItems` | `true` | 客舱物品的存 / 读 |
| `SaveFuel` / `LoadFuel` | `true` | 燃油的存 / 读（写入后 3 秒自动回读校验） |
| `SaveEngine` / `LoadEngine` | `true` | 发动机的存 / 读（写入后 3 秒自动回读校验） |
| `SavePaint` / `LoadPaint` | `true` | 机翼掉漆的存 / 读 |
| `ClearBeforeLoad` | `false` | 读档后钱变成两倍时改成 `true` |
| `Verbose` | `true` | 打印导演机的每次阶段切换，排查用 |

### `[Debug]`

| 项 | 默认 | 说明 |
|---|---|---|
| `DumpOnLoad` | `true` | 读档后把升级目录 / 物品目录 / 座位状态打进日志。稳定后关掉 |

## 存档语义

- **存**：成功落地后（`Arrived` / `Turnaround`），或在机舱里按 `SaveKey`
- **不存**：坠机（`GameOver`），除非把 `SaveOnCrash` 打开
- **读**：默认手动，游戏内按 `LoadKey`。**这是覆盖操作**
- **备份**：每次写盘前把上一份留成 `career.bak.json`

之所以坠机不存：游戏的设计是失败即整轮作废（`GameOver` 之后立刻回 `PreShift` 重开）。在这里存盘等于把 roguelike 改成检查点，而且会当场把罚完的余额固化进下一轮。

**读档是幂等的**：物品按 id 计数、只补差额；座排跳过已装的排。连按读档键不会让物品翻倍（实测连按 7 次，每次都是 `37 / 37`）。

## 三个实现上的坑（值得知道）

1. **派生值不能写。** `FuelSystem.fuelL/fuelR` 有 `NetworkfuelL` 之类的同步镜像，看着像权威数据，其实是模拟每帧从 `tank[]` 重算的；`EngineUnit.oil` 同理，真源是 `lOil`。只写派生值时"当场回读一切正常、3 秒后被打回原值"。所以插件写完 3 秒后会再回读一次并打日志。

2. **座排会被游戏重置。** 读档装好 8 排之后，游戏可能又跑一次新一轮重置打回 3 排。所以除了 3 秒后复查，还挂在 `PassengerDirector.ServerResetForNewRun` 和 `GroundCrew.ServerReanchor` 上，缺了就补回来。

3. **不能用 `NetworkManager.singleton`。** `ApplyDefaults` 执行时它还是 null；插件读档全程用组件单例和 `GameState.Instance`，不依赖 `singleton`。

## 已知限制

- **物品只存"客舱/货舱里的实体"**。行李和手持物品不存；`Misc` 分类的运行时临时物也不存
- 物品的 `tag` **只记录、不还原**——它是运行时网络句柄，重新生成必然拿到新值
- 物品按 `transform` 层级路径找回原插槽。站点会被重新锚定到下一个机场，世界坐标不可靠；万一某个插槽对不上，物品会落到存档记录的世界坐标处，日志里 `socket 未命中` 会计数
- **发动机只保证 `state` / `faults` 准确**。机油量（`lOil`）已验证能留住，温度等纯模拟量会在几秒内回归正常值
- **多人时只有房主存读**。纯客户端按热键只打日志、不写盘，避免用不完整的状态覆盖房主存档
- 按键冲突：默认 `F9` / `J`，如果和游戏内操作撞车，改配置即可，不用重新编译

## 验证装好了

游戏启动后窗口标题会带 `BepInEx` 字样。日志在 `BepInEx\LogOutput.log`，正常应该能看到：

```
[MaydayCareer] 已加载 v1.0.1
[MaydayCareer] 存档路径 = ...\career.json
[MaydayCareer] 补丁数 = 7
```

进机舱按 `J` 存盘：

```
[MaydayCareer] 检测到 J，手动存盘
[MaydayCareer] 燃油快照 L=152.2 R=152.2 储备=0.0 容量=160.0 tank=[152.2, 152.2]
[MaydayCareer] 发动机快照2台: #0 state=1 faults=0x00 rpm=2100 oil=1.000 lOil=1.000 temp=78.5
[MaydayCareer] 已备份上一份存档 -> career.bak.json
[MaydayCareer] 已保存 钱=1311 债=1200 第3段 升级=1 座排=8 物品=37 -> ...\career.json
```

再按 `F9` 读档（3 秒后自动校验）：

```
[MaydayCareer] 已读档 钱 600 -> 1311（存档 1311） 债 1200 第3段 机场3 升级 +1
[MaydayCareer] 座位重建 补装5排 已装8排 premium=True（RowCount=8，视觉兜底0处）
[MaydayCareer] 燃油还原 L=152.2 R=152.2 储备=0.0 tank写入=2项
[MaydayCareer] 发动机还原 2/2 台: #0 state=1 faults=0x00 #1 state=1 faults=0x00
[MaydayCareer] 物品重建 成功 37 / 目标 37，socket 未命中 0（生成失败 0，第 1 次）
[MaydayCareer] 燃油校验（3 秒后）L=152.2 R=152.2 期望 L=152.2 R=152.2 → 一致
[MaydayCareer] 发动机校验（3 秒后）#0 state=1 faults=0x00 oil=1.000
```

健康的判据：**没有任何 `[Warning]`**、`物品重建` 的分子等于分母、校验行写"一致"。

## 卸载

删掉 `BepInEx\plugins\MaydayCareer.dll`。存档文件留着不影响，删掉就是全新开局。

## 版本兼容性

v1.0.1 针对 **游戏 0.14.11 / Unity 6000.3.20f1 / IL2CPP metadata v39** 开发，运行时为 **BepInEx 6.0.0-be.793**。

游戏更新后如果 `ServerResetRun`、`SetStage`、`ServerReanchor`、`FuelSystem`、`EngineUnit` 等被改名或改结构，Harmony 会打补丁失败或字段读不到。排查入口：`BepInEx\LogOutput.log` 搜 `MaydayCareer`。

## 风险提示

修改游戏运行行为可能违反服务条款。本插件会改变游戏的经济设计意图（原作是"一趟航班就是一段完整经历"），请自行判断是否使用，风险自负。

游戏没有反作弊机制，也不会检测到本插件——写入走的是游戏自己的 `ServerAddMoney` / `ServerAddUpgrade` 等 API，产生的同步流量与正常玩法完全一致。但反过来也要清楚：**队友无法验证你的存档是不是"诚实"的**。这就是它只适合私人房间的原因。

仓库内不包含任何游戏文件或由其生成的派生物，详见 `.gitignore`。

## 许可证

MIT。BepInEx 为 LGPL-2.1，若随发布包分发其运行时请一并附上其许可证。
