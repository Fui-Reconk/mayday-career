# Mayday Career

给《Mayday Airways》Demo 补一层**跨局进度存档**：每趟航班结束后把公司状态写进文件，下次启动接着上次的钱、座排和客舱器材继续飞。

游戏本身没有这个功能。它只持久化设置和装扮（`settings.json` / `ui_hints.json`），而金钱、欠款、已购升级都挂在 `GameState` 这个 `NetworkBehaviour` 上——随房间建立、随房间销毁，退出即清零。客舱里花钱买的器材更彻底：连会话级状态都算不上，是纯场景对象，换个机场就没了。

不改动游戏任何文件，删除插件即可完全复原。

> **仅房主安装即可。** 存档由主机写入并读取，队友通过游戏原有的状态同步自动看到结果，**其他人不需要装**。
> 本插件改变经济节奏，请在单人或私人房间使用。

---

## 功能

| 存什么 | 说明 |
|---|---|
| 钱 / 欠款 | 走 `ServerAddMoney` / `ServerAddDebt`，账本和 HUD 一起刷新 |
| 第几段 / 当前机场 / 目的地 | 航段与位置 |
| 评级 / 难度 | `Rating` / `Difficulty` |
| 累计收入 / 累计乘客 | 生涯统计 |
| 升级台账 | `Upgrades`（`SyncList<string>`），按 id 逐条写回 |
| **座排** | `rowMask` 位图，按存档原样还原 |
| **客舱物品** | 灭火器、锤子、胶带、油桶、座位套件等实体物品的完整快照（实测 37 件级别） |

**座排为什么要单独存**：升级是"台账"，而座排是买 `seat_kit` 之后**物理装出来**的实体。只把升级列表写回去，座位不会自己长出来。

**客舱物品为什么要单独存**：`UpgradeInfo.deliverItem` 在全部 7 个升级上都是空的——不存在"买升级送实物"这回事。器材全部来自补给站直接购买，游戏对它们零持久化。

**尚未支持**：油量、机体损伤、涂装。它们要么是每帧被模拟覆盖的快照，要么还没有找到可靠的写入点，原因见 [docs/FINDINGS.md](docs/FINDINGS.md)。

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

和游戏的 `settings.json` 放一起。

**每次写新档之前会把上一份复制成 `career.bak.json`。** 所以手滑读档覆盖了进度，把 `career.bak.json` 改名成 `career.json` 就能捞回上一次。直接删掉 `career.json` 就是全新公司。

存档格式带版本号（当前 `version: 2`）。读到旧版存档会打 Warning 提示，并说明只能还原钱/进度/升级——进机舱按一次 `J` 即可升级到 v2。

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
| `SaveItems` | `true` | 存盘时一并快照客舱物品 |
| `LoadItems` | `true` | 读档时把物品补回场景（幂等） |
| `ClearBeforeLoad` | `false` | 读档后钱变成两倍时改成 `true` |
| `Verbose` | `true` | 打印导演机的每次阶段切换，排查用 |

### `[Debug]`

| 项 | 默认 | 说明 |
|---|---|---|
| `DumpOnLoad` | `true` | 读档后把升级目录 / 物品目录 / 座位状态 / 补给站状态打进日志。排查用，稳定后关掉 |

### ⚠️ 座排还原不消耗套件

存档里记的是 `rowMask` 位图（第 i 位 = 第 i 排已装）。读档时对"存档置位、当前没装"的排直接调 `PassengerDirector.ServerInstallRow`，**绕过游戏原有的套件校验**——不需要手持 `seat_kit`，不校验站位，也不走安装进度条，因此**不消耗任何 `seat_kit`**（正常 350 元/排）。

也就是说：**座排数量完全由存档决定**，插件不做"补到 N 排"这类额外补足。想让客舱多几排，改存档里的 `seatRowMask`（位图，`255` = 8 排），或者正常买套件装好再按 `J` 存盘。

补装只加不删：存档里置位少于当前已装的排数时，不会拆排。

## 存档语义

- **存**：成功落地后（`Arrived` / `Turnaround`），或在机舱里按 `SaveKey`
- **不存**：坠机（`GameOver`），除非把 `SaveOnCrash` 打开
- **读**：默认手动，游戏内按 `LoadKey`。**这是覆盖操作**
- **备份**：每次写盘前把上一份留成 `career.bak.json`

之所以坠机不存：游戏的设计是失败即整轮作废（`GameOver` 之后立刻回 `PreShift` 重开）。在这里存盘等于把 roguelike 改成检查点，而且会当场把罚完的余额固化进下一轮。

**读档是幂等的**：物品按 id 计数、只补差额；座排跳过已装的排。连按 `F9` 不会让物品翻倍（实测连按 7 次，每次都是 `37 / 37`）。

## 已知限制

- **物品只存"客舱/货舱里的实体"**。行李（`Luggage` 分类，游戏结算会自己清）和手持物品不存；`Misc` 分类的运行时临时物（乘客携带、炸弹等）也不存
- 物品的 `tag` **只记录、不还原**——它是运行时网络句柄，重新生成必然拿到新值
- 物品按 `transform` 层级路径找回原插槽。站点会被重新锚定到下一个机场，所以世界坐标不可靠；万一某个插槽对不上，物品会落到存档记录的世界坐标处，日志里 `socket 未命中` 会计数
- **多人时只有房主存读**。纯客户端按热键只打日志、不写盘，避免用不完整的状态覆盖房主存档
- 油量 / 机体损伤 / 涂装未支持
- 游戏更新后如果方法名变化，Harmony 补丁会失败。排查入口：`BepInEx\LogOutput.log` 搜 `MaydayCareer`

## 验证装好了

游戏启动后窗口标题会带 `BepInEx` 字样。日志在 `BepInEx\LogOutput.log`，正常应该能看到：

```
[MaydayCareer] 已加载 v0.9.0
[MaydayCareer] 存档路径 = ...\career.json
[MaydayCareer] 补丁数 = 7
```

进机舱按 `J` 存盘：

```
[MaydayCareer] 检测到 J，手动存盘
[MaydayCareer] 已备份上一份存档 -> career.bak.json
[MaydayCareer] 物品快照 已记录 37 件（跳过 1 件）
[MaydayCareer] 已保存 钱=1311 债=1200 第3段 升级=1 座排=5 物品=37 -> ...\career.json
```

再按 `F9` 读档：

```
[MaydayCareer] 检测到 F9，手动读档
[MaydayCareer] 已读档 钱 600 -> 1311（存档 1311） 债 1200 第3段 机场3 升级 +1
[MaydayCareer] 座位重建 补装0排 已装5排 premium=True（RowCount=5，视觉兜底0处）
[MaydayCareer] 座位补装 目标=5 当前=5 → 无需补装
[MaydayCareer] 物品重建 成功 37 / 目标 37，socket 未命中 0（生成失败 0，第 1 次）
```

只要**没有任何 `[Warning]`**、且 `物品重建` 的分子等于分母，就是健康状态。

## 卸载

删掉 `BepInEx\plugins\MaydayCareer.dll`。存档文件留着不影响，删掉就是全新开局。

## 版本兼容性

针对 **游戏 0.14.11 / Unity 6000.3.20f1 / IL2CPP metadata v39** 开发。

游戏更新后如果 `ServerReport`、`ServerResetRun`、`ServerReanchor` 等被改名，Harmony 会打补丁失败，日志里搜 `MaydayCareer` 就能看到。届时需要重新编译。

## 风险提示

修改游戏运行行为可能违反服务条款。本插件会改变游戏的经济设计意图（原作是"一趟航班就是一段完整经历"），请自行判断是否使用，风险自负。

仓库内不包含任何游戏文件或由其生成的派生物，详见 `.gitignore`。

## 许可证

MIT。BepInEx 为 LGPL-2.1，若随发布包分发其运行时请一并附上其许可证。
