# Mayday Career

《Mayday Airways》的跨局进度存档插件 —— 把公司状态落成多份 JSON 存档（主档 + 手动档 + 自动存档），下次启动接着上次飞。暂停菜单里有 galgame 式选档界面，也有热键可用。

[快速开始](#快速开始) · [安装](#安装) · [配置](#配置) · [存档](#存档文件) · [从源码构建](#从源码构建) · [常见问题](#常见问题) · [逆向笔记](docs/FINDINGS.md) · [参与开发](CONTRIBUTING.md) · [更新记录](CHANGELOG.md) · [发布包](https://github.com/Fui-Reconk/mayday-career/releases/latest)

---

## 为什么需要它

游戏本身不保存任何进度。钱、欠款、已购升级全部挂在 `GameState` 这个 `NetworkBehaviour` 上 —— 随房间建立、随房间销毁，退出即清零。客舱里花钱买的器材更彻底：连会话级状态都算不上，是纯场景对象，换个机场就没了。

游戏唯一持久化的是设置和装扮（`settings.json` / `ui_hints.json`）。

这个插件把上面那些状态写进存档文件（多档位 + 独立自动存档），下次进游戏从暂停菜单的选档界面读回来，或者按热键。

**不改动游戏任何文件**，删掉插件即完全复原。

## 功能

### 保存

| 项 | 数据来源 |
|---|---|
| 金钱 / 欠款 | `GameState.Money` / `Debt` |
| 第几段 / 当前机场 / 目的地 / 随机种子 | `GameState.Leg` / `CurrentAirport` / `DestinationAirport` / `RunSeed` |
| 评级 / 难度 | `GameState.Rating` / `Difficulty` |
| 累计收入 / 累计乘客 | `GameState.TotalEarned` / `TotalPassengers` |
| 已购升级 | `GameState.Upgrades`（`SyncList<string>`） |
| 座位排数 | `PassengerDirector.rowMask` 位图 |
| 燃油 | `FuelSystem.tank[]` 左右箱 + 储备油 |
| 发动机 | 每台的 `state` / `faults` / `rpm` / `cowl` / 机油量 |
| 机体损伤 | `HullSystem` per-panel：每块面板的损伤/脏污/破洞/补丁进度 + 区段维修阶段（`sStage`/`sProg`） |
| 机翼掉漆 | `shownMain/shownAccent` 外观链 + `paintCover`/`coverDamage`，写回后调 `ApplyAllSectionPaint` 重绘 |
| 客舱器材 | 场景里实体物品的完整快照，按 socket 层级路径找回原位；读档为**精确还原**（先清空再按存档重建，多余的物品会消失） |

### 不保存

| 项 | 原因 |
|---|---|
| 行李 / 手持物 / 炸弹 | 运行时临时对象，结算时游戏自己会清 |
| 火焰、冒烟等特效 | 瞬时视觉，只还原"故障"这个事实 |

想了解每一项是怎么确认的，看 [docs/FINDINGS.md](docs/FINDINGS.md)。

## 环境要求

| 项 | 版本 |
|---|---|
| 游戏 | Mayday Airways Demo（**0.14.11** 验证通过） |
| 引擎 | Unity 6000.3.20f1 / IL2CPP metadata v39 |
| 运行时 | [BepInEx 6.0.0-be.793](https://builds.bepinex.dev/projects/bepinex_be)（IL2CPP, win-x64） |
| 系统 | Windows 10/11 x64 |

> 插件依赖 BepInEx 在首次启动时生成的 interop 程序集。没有它，任何 IL2CPP 插件都无法编译或运行。

## 安装

### 1. 装 BepInEx

从 [BepInEx BE 构建页](https://builds.bepinex.dev/projects/bepinex_be) 下载 `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793`，解压到游戏根目录（与 `MaydayAirways.exe` 同层）。

Steam 用户：右键游戏 → 管理 → 浏览本地文件，就能打开游戏目录。

解压后目录应该长这样：

```
Mayday Airways Demo\
├─ MaydayAirways.exe
├─ GameAssembly.dll
├─ winhttp.dll              ← BepInEx 注入器
├─ doorstop_config.ini      ← 注入配置
├─ dotnet\                  ← 运行时
└─ BepInEx\
   ├─ core\
   ├─ plugins\              ← 插件放这里
   └─ interop\              ← 首次启动后生成
```

### 2. 首次启动，生成 interop

直接启动游戏。会先弹一个黑色控制台窗口刷 `Generating interop assemblies...`，**这一步要等 1–3 分钟**，别以为卡死。

进到主菜单（或机库）后再退出。这时 `BepInEx\interop\` 里应该有约 119 个程序集。

### 3. 装插件

把 `MaydayCareer.dll` 放进 `BepInEx\plugins\`：

```
BepInEx\plugins\MaydayCareer.dll
```

就这一个文件，不需要改任何游戏文件。

### 4. 验证

重新启动游戏。日志（`BepInEx\LogOutput.log`）里应该出现：

```
[MaydayCareer] 已加载 v1.3.2
[MaydayCareer] 存档路径 = C:\Users\<你>\AppData\LocalLow\Lyngo Games\Mayday Airways\career.json
[MaydayCareer] 补丁数 = 13
```

补丁数随版本增加（v1.3.2 为 13），关键是**和最近一次启动的日志一致** —— 对不上说明 Harmony 有补丁没挂上，通常是游戏版本变了。

## 快速开始

1. 进游戏，**走进机舱**（主菜单里 `GameState` 还没建）
2. 按 `ESC` 打开暂停菜单 → 点「存盘」→ 选档界面里点一个档位
3. 下次进来：ESC →「读档」→ 点同一个档位；或直接按 `F9` 读"当前档位"

也可以用热键：`J` 存 / `F9` 读，见 [配置](#配置)。

## 使用

### 暂停菜单与选档界面

按 `ESC` 打开暂停菜单，底部有「存盘 / 读档」两个按钮（样式与游戏原按钮一致）：

- 点「存盘」→ 弹出选档界面，列出行：**自动存档**（落地时自动更新，不可手动写）+ 档位 1~N
- 每行显示该档的存档时间 / 段数 / 金钱
- 点行即写入 / 读取；**覆盖已有存档会要求二次确认**（行变橙色，再点一次执行，上一份自动留成 `.bak`）
- `ESC` / 底部「关闭」/ 点击暗幕 退出选档界面（ESC 只关选档界面，暂停菜单保留）
- 结果用游戏自带的通知条反馈

### 热键

| 键 | 配置项 | 默认 | 说明 |
|---|---|---|---|
| 存盘 | `SaveKey` | `J` | 立刻写一份到**当前档位**，不用飞完一趟 |
| 读档 | `LoadKey` | `F9` | 把**当前档位**覆盖到当前进度 |

键名取 `UnityEngine.InputSystem.Key` 的枚举名（`F9` / `F10` / `K` / `L` …）。
"当前档位" = 选档界面里最后点过的那个手动档。

> `J` 是"现在这样就挺好，记下来"，`F9` 是"回到上次记下来的那一步"。
> **读档是覆盖操作**，按之前想清楚 —— 好在每次写盘都会把上一份留成备份。

### 自动存盘的时机

不开热键也会存，挂在游戏的阶段机上。**自动存盘写入独立的 `career.autosave.json`**，
不占用手动档位 1~N：

| 阶段 | 动作 | 说明 |
|---|---|---|
| `Arrived` | 自动存档 | 落地 |
| `Turnaround` | 自动存档 | 周转，此时账已结清 |
| `GameOver` | **不存** | 坠机不覆盖（可配置 `SaveOnCrash`） |
| `PreShift` | 只打日志 | 新一轮开始，绝不在这里存 |

所以**自动存档永远是"最后一次成功落地"的状态**，坠机后从选档界面读"自动存档"
就能回到摔之前。手动档位（档 1~N）只受你自己的存档操作影响，自动流程碰不到。

### 多人游戏

**只有房主需要装。** 存档由主机读写，队友通过游戏自带的 SyncVar 同步看到结果。

带密码的房间、Steam 大厅这些照常工作 —— 插件不碰网络层。

建议只在单人或私人房间使用：它会改变原作的经济节奏（原作设计是"一趟航班就是一段完整经历"）。

## 配置

首次启动后生成：

```
BepInEx\config\local.mayday.career.cfg
```

改完**重启游戏**生效。

### `[General]`

| 项 | 默认 | 说明 |
|---|---|---|
| `AutoSave` | `true` | 落地 / 周转时自动存盘 |
| `SaveOnCrash` | `false` | 坠机（`GameOver`）是否也存盘。默认不存 |
| `AutoLoadOnStart` | `false` | 进入新一轮时自动读档。默认关闭，改为手动按热键 |
| `LoadKey` | `F9` | 读档热键 |
| `SaveKey` | `J` | 存盘热键 |
| `GuardSave` | `true` | 存盘回退保护：热键存盘时当前局比存档更早则拒绝。选档界面里的覆盖有二次确认，不受此限 |
| `MenuButtons` | `true` | 暂停菜单（ESC）里显示「存盘 / 读档」按钮，点击弹出选档界面 |
| `SaveSlots` | `3` | 手动存档档位数（1-12）。档1 = 主档 `career.json`；档2+ = `career.slotN.json`。档多时选档界面自动出现滚动条 |
| `ActiveSlot` | `1` | 上次使用的档位（选档界面点行时自动写回，一般不用手改） |
| `ClearItemsOnNewRun` | `true` | 新一轮重置/手动重开时清空上一局残留物品并补全新的开局 kit（官方开新档不清场） |
| `PerfWatch` | `true` | 帧监测：出现卡顿时在日志里汇总卡顿帧数、当时阶段与插件自身耗时，用于区分"游戏卡"还是"插件卡" |
| `ClearBeforeLoad` | `false` | 读档后钱变成两倍时改成 `true` |
| `SaveItems` / `LoadItems` | `true` | 客舱器材的存 / 读 |
| `SaveFuel` / `LoadFuel` | `true` | 燃油的存 / 读 |
| `SaveEngine` / `LoadEngine` | `true` | 发动机的存 / 读 |
| `SaveHull` / `LoadHull` | `true` | 机体损伤 + 掉漆的存 / 读（写入后 3 秒自动回读校验） |
| `Verbose` | `true` | 打印导演机的每次阶段切换，排查用 |

### `[Debug]`

| 项 | 默认 | 说明 |
|---|---|---|
| `DumpOnLoad` | `true` | 读档后把升级目录、物品目录、座位状态打进日志。稳定后可关掉 |

### 几个配置的细节

**`GuardSave`** —— 游戏每次启动都从"第 0 段、600 块"的新公司开始。要是这时候按了 `J`（热键），存档就会被初始值覆盖。开了这个开关，插件发现当前段数比存档早，会拒绝写盘：

```
[Warning] 存盘已跳过（档1）：当前是第0段，档1是第3段（更靠后）。
          刚开的新公司别急着存盘，先按读档键把进度拿回来。
```

拦截只对**没有确认环节的路径**生效（热键 J 与自动存档；自动存档写独立文件，另有一层结构隔离）。选档界面里的覆盖有二次确认，不受此限。想连热键也放开，把它设成 `false`。

**`ClearBeforeLoad`** —— 正常情况下不用动。如果发现读档后钱正好是"存档金额 + 开局资金"，把它改成 `true`。

**`AutoLoadOnStart`** —— 打开后，进新一轮会自动读档。适合单人玩法；多人时建议保持关闭，让房主自己决定什么时候读。

## 存档文件

### 位置

```
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\
├─ career.json             主档（手动档 1）
├─ career.bak.json         主档的上一份（每次写盘前自动生成）
├─ career.autosave.json    自动存档（落地 / 周转时自动写入）
├─ career.slot2.json       手动档 2（SaveSlots ≥ 2 时存在）
├─ career.slot2.bak.json   档 2 的上一份
├─ career.slotN.json       … 依 SaveSlots 而定（最多 12）
├─ settings.json           游戏自己的设置，与本插件无关
└─ ui_hints.json           游戏自己的教程记录
```

和游戏的用户数据放在一起，换机迁移、Steam 云备份都走同一套逻辑。

### 格式

4 空格缩进、camelCase、带 `version` 字段，风格与游戏自己的 `settings.json` 一致。所有档位文件（含自动存档）共用同一格式：

```json
{
    "version": 2,
    "savedAt": "2026-11-14T00:31:00",
    "money": 1311,
    "debt": 1200,
    "leg": 3,
    "runSeed": -482,
    "currentAirport": 3,
    "destinationAirport": 1,
    "totalEarned": 5200,
    "totalPassengers": 14,
    "rating": 2.8,
    "difficulty": 1,
    "fuelL": 152.2,
    "fuelR": 152.2,
    "reserveTank": 0,
    "reserveSync": 0,
    "fuelCapacity": 160,
    "tanks": [152.2, 152.2],
    "hull": {
        "breaches": 0,
        "sectSync": "0",
        "panels": [
            { "damage": 0, "netDamage": 0, "dirt": 0.07, "netDirt": 0.07, "hole": [0, 0, 0, 0], "patchT": 0 }
        ],
        "sections": [
            { "stage": 0, "progress": 1 }
        ],
        "paint": [
            {
                "main": [255, 255, 255, 255], "accent": [40, 90, 160, 255],
                "netMain": [255, 255, 255, 255], "netAccent": [40, 90, 160, 255],
                "shownMain": [255, 255, 255, 255], "shownAccent": [40, 90, 160, 255],
                "goalMain": [255, 255, 255, 255], "goalAccent": [40, 90, 160, 255],
                "glideMain": 1, "glideAccent": 1,
                "cover": 1, "coverDamage": 0
            }
        ]
    },
    "engines": [
        { "index": 0, "state": 0, "faults": 0, "cowl": 0, "bat": 1, "swap": 0, "swapSerial": 0,
          "rpm": 0, "oil": 1, "lOil": 1, "temp": 0.08, "fire": 0, "batt": 1, "swapP": 0 }
    ],
    "upgrades": ["seat_premium", "espresso"],
    "seatRowMask": 255,
    "seatRowCount": 8,
    "seatRows": [0, 1, 2, 3, 4, 5, 6, 7],
    "items": [
        {
            "id": "fuse", "tint": -1, "amount": 0, "count": 0, "tag": 0,
            "socket": "Plane#0/PlaneSystems#1/Sys#2/CargoBay#0/BreakerPanel#1/FuseSocket_0#0",
            "px": -1.93, "py": 2.9, "pz": -6.5,
            "rx": 0, "ry": 0.707, "rz": 0, "rw": 0.707
        }
    ]
}
```

各数组的含义：`tanks` 是按索引的油箱模拟值；`hull.panels` 是逐面板的损伤/脏污/破洞；`hull.sections` 是三段修复的阶段与进度；`hull.paint` 是每块面板的喷漆外观链（`shown*` 才是渲染读取的）；`seatRows` 是已装座排的编号列表；`items` 里 `socket` 是插槽的层级路径（找不回插槽时按 `px/py/pz` 世界坐标摆放）。

### 常用操作

| 想做的事 | 怎么做 |
|---|---|
| 回到上次落地状态（坠机救回） | 选档界面 → 读档 → 点「自动存档」行 |
| 切换 / 新建档位 | 选档界面里点对应行存一份 |
| 回到某档的上一份 | 把 `career.slotN.bak.json` 改名成 `career.slotN.json` |
| 重开新公司 | 删掉 `career.json` |
| 手动改钱 | 编辑档文件里的 `"money"`，进游戏读该档 |
| 看内容 | `notepad "%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.json"` |

改 JSON 时保持缩进和逗号格式即可，插件用的是标准解析器。

## 从源码构建

### 前置

- .NET SDK 6.0+
- 游戏目录里已经生成好 `BepInEx\interop`（见 [安装](#安装) 第 2 步）

### 构建

```powershell
.\build.ps1                              # 通过 Steam 注册表自动定位游戏目录
.\build.ps1 -Game "D:\Games\Mayday Airways Demo"   # 手动指定
.\build.ps1 -Deploy                      # 构建完直接装进游戏
```

产物在 `src\bin\Release\net6.0\MaydayCareer.dll`。

### 仓库结构

```
mayday-career\
├─ src\
│  ├─ MaydayCareer.csproj   引用 BepInEx 与 interop
│  ├─ Plugin.cs             入口、配置项
│  ├─ Hooks.cs              Harmony 补丁（阶段机、重置、重锚、每帧）
│  ├─ CareerStore.cs        多档位存档读写、物品精确还原、座排维护
│  ├─ MenuButtons.cs        暂停菜单存读按钮（克隆原按钮注入）
│  ├─ SaveLoadScreen.cs     galgame 式选档界面（自研滚动 + ESC 拦截）
│  ├─ PerfWatch.cs          帧时间监测
│  ├─ Hotkey.cs             热键轮询
│  └─ Json.cs               手写的极简 JSON（BepInEx 运行时没有 System.Text.Json）
├─ tools\interop-inspector\ 离线解析 interop 程序集的字段表
├─ docs\FINDINGS.md         逆向结论
├─ build.ps1
└─ CHANGELOG.md
```

### 为什么手写 JSON

BepInEx 的运行时里没有 `System.Text.Json`，也没有 `Newtonsoft.Json`。拖一个序列化库进来会让发布包变大、还得处理 IL2CPP 下的裁剪问题。存档结构简单（一层对象 + 两个数组），手写 200 行更省事。

### 排查工具

`tools\interop-inspector` 可以离线查字段：

```powershell
cd tools\interop-inspector
dotnet run Inspector.cs "C:\...\BepInEx\interop\Assembly-CSharp.dll" detail "Mayday.GameState"
dotnet run Inspector.cs "C:\...\BepInEx\interop\Assembly-CSharp.dll" find "FuelLiters"
```

游戏更新后想确认某个字段是否改名，用它比翻 `global-metadata.dat` 快得多。

## 常见问题

**Q：`补丁数` 和说明对不上**

说明有补丁没挂上。游戏更新改了方法名，需要重新编译（见 [从源码构建](#从源码构建)）。日志里搜 `MaydayCareer` 能看到具体的失败信息。

**Q：按热键没反应**

- 是不是在主菜单按的？`GameState` 要进机舱才存在
- 键位是不是和游戏内操作撞车？改配置里的 `LoadKey` / `SaveKey`
- 纯客户端按存盘键只会打一条警告，不写盘 —— 这是有意的

**Q：读档后钱没变**

看日志有没有 `已读档 钱 A -> B`。有的话说明写进去了，只是 UI 没刷新；没有的话可能 `GameState` 还没就绪，进机舱后再按一次。

**Q：钱变成两倍**

开局资金没被清掉。把 `ClearBeforeLoad` 设成 `true`。

**Q：物品没回来**

看 `物品重建 成功 X / 目标 Y` 这行：

- `X = Y`：成功了
- `socket 未命中 > 0`：有物品找不到原插槽，落到了存档记录的世界坐标处（换机场后那个坐标可能是陈的）

**Q：座位数没变**

座排会按存档的 `rowMask` 补装。如果游戏又跑了一次新一轮重置，插件会自动补回 —— 日志里会打 `座排被重置（…）→ 补装N排`。

**Q：地面操作有粘滞感 / 掉帧，是插件的问题吗**

先看日志里的 `帧监测` 行（`PerfWatch` 默认开）。它每 10 秒汇总一次出现过的卡顿帧，并附上两个关键数字：

```
[Warning] [MaydayCareer] 帧监测（近 10s）：3 个 >100ms 卡顿帧，最长 210ms（阶段=Turnaround），插件自身每帧 ≤0.05ms
```

- **插件自身每帧 ≤0.05ms 左右**：插件的每帧开销可以忽略，卡顿来自游戏本身的地面物理与 AI（地面阶段相机、拖车、乘客、地上物品都在做物理模拟），与插件无关
- **插件自身每帧明显偏大**（比如 >1ms）：把日志发到 issue，附上卡顿发生时的场景

另外插件的两类重活都已移出关键路径：存盘写文件在后台线程（主线程只剩状态读取与 JSON 序列化，日志会打 `序列化 Xms，写盘后台 Xms`）；`FuelSystem` / `HullSystem` 引用有缓存，不做重复的全场景扫描。确认过原因后可以把 `PerfWatch` 关掉。

**Q：能用在盗版上吗**

技术上都行 —— 插件只依赖 `GameAssembly.dll` 和 interop 程序集。但请支持开发者。

## 已知限制

- 物品只存客舱 / 货舱里的**实体**，行李和手持物不存
- 读档为**精确还原**：先清空场景里的可持久化物品再按存档重建，所以读档会清掉你本局新买的、存档里没有的器材（读档前想保留就先存一份）
- 多人时其他玩家**手里拿着的**物品不会被清（避免从手中抽走），读档后可能出现同 id 的手上物 + 存档物并存
- 升级台账会精确回退（本局多买的会被移除）；座排也会随存档对齐——读"排数更少"的档时多装的排会正常消失
- 物品的 `tag` 只记录不还原 —— 那是运行时网络句柄，重新生成必然是新值
- 发动机保证 `state` / `faults` 准确；机油量已验证能留住，温度等纯模拟量会在几秒内回归正常值
- 机翼掉漆走的是完整外观链（`shownMain/shownAccent` + `paintCover/coverDamage`），写回后调 `ApplyAllSectionPaint` + `MarkAllDirty` 重绘。若日志出现 `写 holes 失败`，说明当前 interop 版本对 `SyncList<Vector4>` 索引器支持有问题 —— 损伤与掉漆仍会还原，只是洞的位置可能不在原处
- 机体状态有一道前置门：`HullSystem.serverReady` 且 `holes/damage/dirt` 的 Count 等于面板数。没就绪时读档会跳过机体部分（日志会说明原因），等一轮结算后再读即可
- 跨机场读档（存档机场 ≠ 当前机场）会触发游戏重建世界，物品还原自动等重建完成后补一次 —— 日志里能看到"等重锚后再还原一次"
- 多人时只有房主存读
- 游戏更新后如果方法名或字段结构变化，需要重新编译

## 卸载

删掉 `BepInEx\plugins\MaydayCareer.dll`。

想连带清掉配置和存档：

```
BepInEx\config\local.mayday.career.cfg
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.json
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.bak.json
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.autosave.json
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.slot*.json（含 .bak）
```

游戏本体从未被修改，所以不需要"还原"。

## 兼容性

针对 **游戏 0.14.11 / Unity 6000.3.20f1 / IL2CPP metadata v39** 开发，运行时为 **BepInEx 6.0.0-be.793**。

游戏更新后可能失效的挂载点：

```
GameState.ServerResetRun          读档时机
GameDirector.SetStage             阶段机（自动存档 / 读档）
GameDirector.ServerRestartRun     重开一轮
GroundCrew.ServerReanchor         站点重锚（跨机场读档后的物品补还原）
PassengerDirector.ServerResetForNewRun   座排自愈 / 新公司清场
PassengerDirector.rowMask         座排位图
Mirror.NetworkManager.Update      热键与 ESC 轮询
UIRoot.HandleBack / Update        ESC 拦截（选档界面开着时）
UIScreen.Back / Close / CloseInstant  ESC 拦截（暂停菜单保持）
ServiceDirector.ServerSpawnKit    新公司开局 kit 重生成
FuelSystem.tank / EngineUnit.lOil 燃油与发动机
HullSystem.serverReady / .Count   机体快照的前置判据
HullSystem.damage/dirt/holes      机体损伤（per-panel）
HullSystem.shownMain/shownAccent  机翼掉漆（外观链）
HullSystem.ApplyAllSectionPaint   掉漆重绘入口（私有，反射调用）
HullSystem.MarkAllDirty           材质脏标记（私有，反射调用）
GameState.Upgrades                升级台账（SyncList，回退用 RemoveAt）
Item.ServerSpawn / ServerDestroy  物品重建与精确还原
```

任何一个改名都会让对应的功能静默失效（有 try/catch 兜底，不会崩游戏），日志里能看到。

## 风险提示

修改游戏运行行为可能违反服务条款，请自行判断，风险自负。

游戏没有反作弊机制，也不会检测到本插件 —— 写入走的是游戏自己的 `ServerAddMoney` / `ServerAddUpgrade` 等 API，产生的同步流量和正常玩法一样。但反过来也要清楚：**队友无法验证你的存档是不是"诚实"的**。这就是它只适合私人房间的原因。

仓库内不包含任何游戏文件或由其生成的派生物。

## 致谢

- [BepInEx](https://github.com/BepInEx/BepInEx) —— IL2CPP 插件框架
- [HarmonyX](https://github.com/BepInEx/HarmonyX) —— 运行时方法补丁
- [CHmadoka/more_players](https://github.com/CHmadoka/more_players) —— 同款游戏的人数扩展 mod，本项目参考了它的工程结构和风险提示写法
- [LS3213/Mayday-Mod-Manager](https://github.com/LS3213/Mayday-Mod-Manager) —— 同游戏的模组管理器与存档模组

## 许可证

[MIT](LICENSE)。

BepInEx 与 HarmonyX 均为 LGPL-2.1；本项目不随发布包分发它们的运行时，请自行安装。
