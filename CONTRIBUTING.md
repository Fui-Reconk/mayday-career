# 参与开发

## 分支策略

`main` 只接受 Pull Request，不接受直接推送（见下方 [关于推送保护](#关于推送保护)）。

```
main                 ← 稳定分支，永远可发布
 └─ fix/xxx          ← 你的改动
 └─ feat/xxx
```

流程：

```bash
git clone https://github.com/Fui-Reconk/mayday-career.git
cd mayday-career
git checkout -b fix/some-bug
# 改代码
git commit -m "fix: 说清楚修了什么"
git push origin fix/some-bug
# 然后在 GitHub 上开 PR
```

## 关于推送保护

GitHub 的分支保护 / Rulesets 对**私有仓库**需要 GitHub Pro 才能启用，所以目前靠两件事约束：

1. **协作者权限设为 `read`**：对方能 clone、能开 PR，但没有推送权限，只能 fork 或走 PR
2. **CI 检查**（`.github/workflows/guard.yml`）：拦住游戏派生物和版本号不一致

维护者自己的账号是 admin，可以直接推。仓库转成 public 后可以立刻开启真正的分支保护。

## 不要提交什么

这是硬性要求，CI 会拦：

| 不要提交 | 原因 |
|---|---|
| `BepInEx/`、`interop/` | 由游戏代码生成的程序集，版权归发行商 |
| `GameAssembly.dll`、`global-metadata.dat` | 游戏本体文件 |
| `bin/`、`obj/`、`dist/` | 构建产物 |
| 任何 `.dll` / `.zip` / `.pdb` | 同上 |

`.gitignore` 已经覆盖了这些路径，别用 `git add -f` 绕过。

## 代码约定

- **C# / .NET 6**，文件编码 UTF-8，缩进 4 空格
- **日志统一带前缀** `[MaydayCareer]`，方便在 `LogOutput.log` 里 grep
- **所有 Harmony 补丁和游戏字段访问都要 try/catch**：游戏更新后字段改名的表现是"静默失效"，不能让它把游戏搞崩
- **注释写"为什么"，不写"做了什么"**：这份代码里每一个奇怪的写法背后都有一条踩过的坑，注释要留下那条坑
- **不引入新依赖**：BepInEx 运行时里没有 `System.Text.Json`，JSON 是手写的（`src/Json.cs`）

## 版本号

三处必须一致，CI 会检查：

```
VERSION                 文件内容就是版本号
src/Plugin.cs           const string Version
CHANGELOG.md            ## v<版本> 标题
```

## 本地验证

CI 跑不了编译 —— 编译需要游戏目录里由 BepInEx 生成的 `interop` 程序集，而那是游戏派生物，不能进仓库。所以**提交前请本地跑一遍**：

```powershell
.\build.ps1 -Deploy
# 启动游戏，看 BepInEx\LogOutput.log 里：
#   [MaydayCareer] 已加载 v1.0.0
#   [MaydayCareer] 补丁数 = 8
```

`补丁数` 小于 8 说明有补丁没挂上，通常是你改了方法名或者游戏更新了。

## 提交信息

用 `type: 摘要` 的形式，正文说清"为什么"：

```
fix: 读档后座排被打回初始值

游戏在新一轮会调 PassengerDirector.ServerResetForNewRun 把客舱重置成 3 排，
读档时机在它之前，所以装好的 8 排会掉回 3 排。
改成挂在这个方法之后二次补装。
```

类型：`feat` / `fix` / `docs` / `refactor` / `chore`。

**一个功能一次提交。** 中间迭代只部署、不提交 —— 一堆 `fix: 修正上一条` 的提交历史读起来是噪音。

### PR 标题就是提交标题

合并方式只开 **Squash** 和 **Rebase**，默认用 Squash，而且 squash 后：

| 取什么 | 来自哪里 |
|---|---|
| 提交标题 | **PR 标题** |
| 提交正文 | **PR 描述** |

所以 **PR 标题请直接用提交信息的格式写**，不要写「修复一些问题」这种。PR 描述里的"为什么"会原样变成主干上那条提交的说明 —— 这是唯一会被长期保留的部分。

分支内你随便提交多少次都行（`fix: 打字错误` 之类），squash 会把它们压成一条。

## 报告问题

开 issue 时请带上：

1. 游戏版本（主菜单右下角或 `MaydayAirways_Data/app.info`）
2. 插件版本（日志第一行 `已加载 v...`）
3. `BepInEx\LogOutput.log` 里带 `MaydayCareer` 的行
4. 现象和你期望的行为

日志比描述有用得多 —— 每条存档相关的操作都会打一行，看日志基本能定位到是哪一步断了。

## 自己想挖字段

游戏是 Unity 6 + IL2CPP，`tools/interop-inspector` 可以离线查任意类型：

```powershell
cd tools\interop-inspector
dotnet run Inspector.cs "<游戏目录>\BepInEx\interop\Assembly-CSharp.dll" list Mayday
dotnet run Inspector.cs "<游戏目录>\BepInEx\interop\Assembly-CSharp.dll" detail "Mayday.GameState"
dotnet run Inspector.cs "<游戏目录>\BepInEx\interop\Assembly-CSharp.dll" find "FuelLiters"
```

`detail` 输出里 `[static]` 是静态成员，`[get,set]` 是可写 —— 判断能不能写回去靠这个。已有的结论都在 [docs/FINDINGS.md](docs/FINDINGS.md) 里，动手前先看一眼，避免重复踩坑。
