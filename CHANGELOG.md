# 更新记录

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
