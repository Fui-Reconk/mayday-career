# interop-inspector

离线读取 BepInEx 生成的 interop 程序集，列出类型、字段、属性的名称和类型。不需要启动游戏。

用 `System.Reflection.Metadata` 直接解析 ECMA-335 元数据，不加载程序集，所以缺依赖也不会崩。

## 用法

```powershell
dotnet run Inspector.cs "<游戏目录>\BepInEx\interop\Assembly-CSharp.dll" list Mayday
dotnet run Inspector.cs "...\Assembly-CSharp.dll" detail GameState
dotnet run Inspector.cs "...\Assembly-CSharp.dll" find Fuel
```

| 模式 | 作用 |
|---|---|
| `list <关键字>` | 列出类型全名 |
| `detail <关键字>` | 展开类型：属性、字段、方法签名指针 |
| `find <字段名>` | 反查：哪些类型含有这个属性，最常用 |

输出里的 `[static]` 和 `[get,set]` 是访问器信息——判断某个值能不能写回去，全靠这个。

## 为什么不用 Il2CppDumper

Unity 6（metadata v39）超出了 Il2CppDumper 和 Cpp2IL 正式版的支持范围。让 BepInEx 生成 interop 是最省事的替代路线，代价是首启动慢 1–3 分钟，生成约 95 MB。

## 注意

`BepInEx\interop\` 是由游戏代码生成的派生物，**不要提交到仓库**，也不要随发布包分发。
