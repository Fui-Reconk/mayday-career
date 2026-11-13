# Mayday Career

给《Mayday Airways》Demo 补一层**跨局进度存档**：每趟航班结束后把公司状态写进文件，下次启动接着上次的钱和升级继续飞。

游戏本身没有这个功能。它只持久化设置和装扮（`settings.json` / `ui_hints.json`），而金钱、欠款、已购升级都挂在 `GameState` 这个 `NetworkBehaviour` 上——随房间建立、随房间销毁，退出即清零。

不改动游戏任何文件，删除插件即可完全复原。

> **仅房主安装即可。** 存档由主机写入并读取，队友通过游戏原有的状态同步自动看到结果，**其他人不需要装**。
> 本插件改变经济节奏，请在单人或私人房间使用。

---

## 当前进度

| 版本 | 状态 |
|---|---|
| v0.1.0 | 计划中：金钱、欠款、已购升级 |

已确认的字段（来自 interop 程序集）：

- `Mayday.GameState`（`NetworkBehaviour` 单例）：`Money`、`Debt`、`Leg`、`RunSeed`、`CurrentAirport`、`TotalEarned`、`TotalPassengers`、`Rating`、`Difficulty`、`Upgrades`（`SyncList<string>`）
- 读写都走游戏自己的 API（`ServerAddMoney` / `ServerAddDebt` / `ServerAddUpgrade` / `HasUpgrade`），不直接写字段——否则 `MoneyChanged` 不触发，UI 和账本会不同步

**尚未支持**：油量、机体损伤、涂装。原因见 [docs/FINDINGS.md](docs/FINDINGS.md)。

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
%USERPROFILE%\AppData\LocalLow\Lyngo Games\Mayday Airways\career.json
```

和游戏的 `settings.json` 放一起。想重开可以从备份恢复，直接删掉就是全新公司。

## 配置

```
BepInEx\config\local.mayday.career.cfg
```

| 项 | 默认 | 说明 |
|---|---|---|
| `AutoSave` | `true` | 每趟航班结算后自动存盘 |
| `ClearBeforeLoad` | `false` | 读档后钱变成两倍时改成 `true` |

## 验证装好了

游戏启动后窗口标题会带 `BepInEx` 字样。日志在 `BepInEx\LogOutput.log`，正常应该能看到：

```
[MaydayCareer] 已加载 v0.1.0
[MaydayCareer] 存档路径 = ...\career.json
[MaydayCareer] 补丁数 = 2
```

飞完一趟后：

```
[MaydayCareer] 已保存 钱=4210 债=500 第4段 升级=2 -> ...\career.json
```

## 卸载

删掉 `BepInEx\plugins\MaydayCareer.dll`。存档文件留着不影响，删掉就是全新开局。

## 版本兼容性

针对 **游戏 0.14.11 / Unity 6000.3.20f1 / IL2CPP metadata v39** 开发。

游戏更新后如果 `ServerShowReport` 或 `ServerResetRun` 被改名，Harmony 会打补丁失败，日志里搜 `MaydayCareer` 就能看到。届时需要重新编译。

## 风险提示

修改游戏运行行为可能违反服务条款。本插件会改变游戏的经济设计意图（原作是“一趟航班就是一段完整经历”），请自行判断是否使用，风险自负。

仓库内不包含任何游戏文件或由其生成的派生物，详见 `.gitignore`。

## 许可证

MIT。BepInEx 为 LGPL-2.1，若随发布包分发其运行时请一并附上其许可证。
