## v1.1.1 — 坠机后新公司不再继承座排

用户实测发现：坠机进入新一轮后，新公司凭空多了几排座椅。

原因：座排自愈钩子（`ServerResetForNewRun` / `ServerReanchor` 后补回存档排数）
不知道"当前这局"已经不是读档的那一局——坠机把钱、升级全打回初始，
唯独座排被我们硬拉回旧档的 8 排。

修法与 `GuardSave` 同款判据：读档时记下存档段数（`_savedLeg`），
自愈触发时若当前段数小于它，说明这是新公司，
清掉自愈目标并打日志 `新一轮是第0段（存档是第5段），座排恢复游戏初始，不再自愈`。
之后按 `O` 重新读档会重新记录段数并恢复座排。

## v1.1.0 — 机体损伤与机翼掉漆（翻案）

之前判定"破洞读不到、损伤做不到"的结论**是错的**，起因是分析了一个社区存档模组（LS3213/Mayday-Mod-Manager 的 mayday-save 0.6.52，参考其文档与反编译产物学习字段映射，未复制代码）。真正的结构是：

- `holes` / `damage` / `dirt` 是 **per-panel 列表**，`Count` 必须等于 `HullSystem.Count`。之前看到的 Count 11→12 是初始化在补齐面板数，被误读成"多了一个洞"
- 前置判据是 `HullSystem.serverReady`，不是 `paintInit`
- `breaches` / `jetSync` / `sectSync` 只是汇总位图，`jetHp` 是派生值 —— 之前写它们当然没用
- 掉漆的视觉链是 `shownMain/shownAccent`（渲染读这个）+ `goalMain/goalAccent` + `glideMain/glideAccent` + `paintCover/coverDamage`；`paintCover` 只是覆盖度参数
- 重绘入口是 **`ApplyAllSectionPaint()` + `MarkAllDirty()`**（私有方法，反射调用），之前调的 `TickPaint` 只是每帧颜色插值推进

**新增**

- `SaveHull` / `LoadHull`（替代 `SavePaint` / `LoadPaint`）：存读每块面板的损伤/脏污/破洞/补丁进度、区段维修阶段、喷漆与掉漆完整外观链
- 读档后 3 秒自动回读校验（breaches / 最大损伤 / 洞数 / 最低覆盖）
- `patchLast` 存 age 不存绝对时间，还原时按当前 `Time.time` 转回

**已知边界**

- `holes[i]` 的索引器写入如果失败（个别 interop 版本对 `SyncList<Vector4>` 的支持问题），洞的位置可能不还原，损伤与掉漆不受影响，日志会明确提示
- `mainColor` 是 `SyncList<Color32>`，同样存在索引器风险，逐元素 try 包裹

## v1.0.0 — 首个正式版

给《Mayday Airways》Demo 补的跨局进度存档，覆盖 10 类状态。

### 保存

| 项 | 数据来源 |
|---|---|
| 金钱 / 欠款 | `GameState.Money` / `Debt` |
| 第几段 / 当前机场 / 目的地 / 随机种子 | `GameState.Leg` / `CurrentAirport` / `DestinationAirport` / `RunSeed` |
| 评级 / 难度 / 累计收入 / 累计乘客 | `GameState` |
| 已购升级 | `Upgrades`（`SyncList<string>`），走 `ServerAddUpgrade` |
| 座位排数 | `PassengerDirector.rowMask` 位图，走 `ServerInstallRow` |
| 燃油 | `FuelSystem.tank[]` 左右箱 + 储备油，**绝对升数**不是比例 |
| 发动机 | 每台的 `state` / `faults` / `rpm` / `cowl` / 机油量（`EngineUnit`） |
| 机翼掉漆 | `HullSystem.paintCover`，每块外皮"还剩多少漆" |
| 客舱器材 | 场景里实体物品的完整快照，按 socket 层级路径找回原位 |

### 不保存

| 项 | 原因 |
|---|---|
| 机身破洞 | 洞的几何数据不在托管层：`holes` / `holes.objects` / `holePlaced` / `HoleOpen()` / `holeViews` / `breaches` 在洞明明存在时全部返回零或未激活 |
| 分段受损（`jetHp`） | 是破洞的派生值：直接写当场可读、3 秒后归零；改写 `jetSync` / `sectSync` 位图也无效 |
| 行李 / 手持物 / 炸弹 | 运行时临时对象，结算时游戏自己会清 |
| 火焰、冒烟等特效 | 瞬时视觉，只还原"故障"这个事实 |

### 机制

- **存盘**：成功落地（`Arrived` / `Turnaround`）自动存，或机舱内按 `J`
- **读档**：按 `F9`（默认手动）
- **校验**：读写燃油 / 发动机后 3 秒自动回读，日志给出"一致 / 不一致"
- **备份**：每次写盘前把上一份留成 `career.bak.json`
- **坠机不存**：`GameOver` 默认跳过，存档永远是"最后一次成功落地"
- **回退保护**：当前局比存档更早时拒绝写盘，避免用刚开的新公司覆盖真进度
- **座排自愈**：游戏重置客舱（新一轮 / 站点重锚）后自动补回存档要求的排数

### 开发中确认的四个坑

1. **带 `Network*` 镜像的字段往往是同步副本，不是真源。** `fuelL/fuelR`（真源 `tank[]`）、`EngineUnit.oil`（真源 `lOil`）都是这样：写派生值"当场回读正常、3 秒后被打回原值"。所以必须先写源、再延迟回读验证。
2. **`Upgrades` 只是购买台账，不是效果开关。** 座排这类实体是购买那一刻 spawn 出来的，改列表不会让座位长出来，必须单独补 `rowMask`。
3. **`paintInit = false` 时 `paintCover` 全是零占位**，不代表"整机掉漆"。这时既不读也不写，否则会把好飞机写成掉光漆。
4. **`NetworkManager.singleton` 在 `ApplyDefaults` 阶段是 null**，读档全程用组件单例和 `GameState.Instance`。
