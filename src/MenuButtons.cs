using System;
using System.Text;
using HarmonyLib;

namespace MaydayCareer
{
    /// <summary>
    /// 暂停菜单注入「存盘 / 读档」按钮（T-MOD-011）。
    ///
    /// 实现方式：Harmony postfix 挂在 PauseScreen.Build() 后面，
    /// 把游戏自己的 resumeBtn 整个 GameObject 克隆两份。
    /// 克隆而不是从零拼 UI 的原因：
    ///   - 字体、底色、hover 动画、点击音效、布局参数全部与原按钮一致，
    ///     不用碰游戏的 UITheme / UISprites / SDF 资源
    ///   - 游戏的 UIButton 是纯代码构建（Build 里的 lambda b__16_x），
    ///     interop 里没有公开的工厂方法可用
    ///
    /// 位置策略：插到可视最底部的原按钮（通常是「退出」）的正上方，
    /// 「退出」保持在菜单最底下。有 LayoutGroup 的容器用兄弟序号排位；
    /// 手动锚定的容器用世界坐标排位（不依赖各自的锚点设置）。
    ///
    /// 回调通过 DelegateSupport 把托管 System.Action 封送成 Il2CppSystem.Action
    /// 赋给 UIButton.onClick（单播字段，直接整体赋值）。
    /// 委托目标是静态方法，加上 Il2CppInterop 的 Il2CppToMonoDelegateReference
    /// 注册表持有引用，不会被 GC 掉。
    /// </summary>
    [HarmonyPatch]
    internal static class PauseMenuHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.UI.PauseScreen), "Build")]
        private static void Postfix(global::Mayday.UI.PauseScreen __instance)
        {
            if (!Plugin.MenuButtons.Value) return;
            try { MenuButtons.Inject(__instance); }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 暂停菜单按钮注入失败: " + e);
            }
        }
    }

    internal static class MenuButtons
    {
        const string SaveBtnName = "MaydayCareer_Save";
        const string LoadBtnName = "MaydayCareer_Load";
        const float ManualGap = 12f; // 手动排位时按钮之间的间距（世界单位）

        public static void Inject(global::Mayday.UI.PauseScreen pause)
        {
            var anchor = pause.resumeBtn;
            if (anchor == null)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 暂停菜单注入跳过：resumeBtn 为空");
                return;
            }

            var parent = anchor.transform.parent;
            if (parent == null)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 暂停菜单注入跳过：resumeBtn 没有父容器");
                return;
            }

            // Build 可能被调用多次（重开屏幕等），按名字防重复
            if (parent.Find(SaveBtnName) != null) return;

            // 父容器有 LayoutGroup 就用兄弟序号排；没有就按世界坐标排
            bool hasLayout = false;
            try { hasLayout = parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null; }
            catch { }

            DumpHierarchy(parent, hasLayout);

            // 可视最底部的原按钮（世界 Y 最小）：我们的按钮插它正上方，退出保持最底
            var bottom = BottomMostSibling(parent);
            Plugin.LogSource.LogInfo("[MaydayCareer] 按钮定位基准: " +
                (bottom != null ? bottom.name + " worldY=" + bottom.position.y.ToString("F1") : "无（退回 resumeBtn 下方）"));

            if (hasLayout)
            {
                // 布局容器：先克隆（追加在末尾），再插到底部按钮的序号处，
                // 把底部按钮往下顶 —— 它仍然是最后一个
                int idx = bottom != null ? bottom.GetSiblingIndex() : parent.childCount;
                CloneButton(anchor, parent, SaveBtnName, "存盘", OnSaveClicked, true, idx);
                CloneButton(anchor, parent, LoadBtnName, "读档", OnLoadClicked, true, idx + 1);
            }
            else if (bottom != null)
            {
                // 手动锚定：按世界坐标贴着底部按钮往上叠（存盘在上，读档在下）
                CloneButton(anchor, parent, SaveBtnName, "存盘", OnSaveClicked, false, -1, bottom, 0);
                CloneButton(anchor, parent, LoadBtnName, "读档", OnLoadClicked, false, -1, bottom, 1);
            }
            else
            {
                // 兜底：找不到基准就贴着 resumeBtn 往下排（v1.3.0 首版行为）
                CloneButton(anchor, parent, SaveBtnName, "存盘", OnSaveClicked, false, -1, null, 0);
                CloneButton(anchor, parent, LoadBtnName, "读档", OnLoadClicked, false, -1, null, 1);
            }

            Plugin.LogSource.LogInfo("[MaydayCareer] 暂停菜单按钮已注入（存盘/读档）");
        }

        /// <summary>可视最底部的原按钮（排除我们的克隆）。比较用世界坐标，锚点差异不影响。</summary>
        static UnityEngine.RectTransform BottomMostSibling(UnityEngine.Transform parent)
        {
            UnityEngine.RectTransform bottom = null;
            float minY = float.MaxValue;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == SaveBtnName || child.name == LoadBtnName) continue;
                if (!child.gameObject.activeInHierarchy) continue;
                var rt = child.TryCast<UnityEngine.RectTransform>();
                if (rt == null) continue;
                if (rt.position.y < minY) { minY = rt.position.y; bottom = rt; }
            }
            return bottom;
        }

        /// <summary>
        /// 把父容器的子物体清单打进日志（名字/激活/世界Y/高度/文案），
        /// 排位不合适时照着这份清单调。
        /// </summary>
        static void DumpHierarchy(UnityEngine.Transform parent, bool hasLayout)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("[MaydayCareer] 暂停菜单容器 ").Append(parent.name)
                  .Append("（").Append(hasLayout ? "有LayoutGroup" : "手动锚定").Append("）共 ")
                  .Append(parent.childCount).Append(" 个子物体:");
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    var rt = child.TryCast<UnityEngine.RectTransform>();
                    string label = "";
                    try
                    {
                        var btn = child.GetComponent<global::Mayday.UI.UIButton>();
                        if (btn != null && btn.Label != null) label = "「" + btn.Label.current + "」";
                    }
                    catch { }
                    sb.Append("\n  [").Append(i).Append("] ").Append(child.name)
                      .Append(child.gameObject.activeInHierarchy ? "" : " (隐藏)")
                      .Append(rt != null
                          ? " worldY=" + rt.position.y.ToString("F1") + " h=" + rt.rect.height.ToString("F1")
                          : " (非RectTransform)")
                      .Append(label);
                }
                Plugin.LogSource.LogInfo(sb.ToString());
            }
            catch { }
        }

        /// <summary>
        /// 克隆一个按钮。两种排位模式：
        ///   siblingIndex >= 0 —— LayoutGroup 容器，克隆后插到该序号；
        ///   siblingIndex < 0  —— 手动锚定，按 bottom 基准 + slot 从下往上叠。
        /// </summary>
        static void CloneButton(
            global::Mayday.UI.UIButton anchor,
            UnityEngine.Transform parent,
            string name,
            string text,
            Action callback,
            bool hasLayout,
            int siblingIndex,
            UnityEngine.RectTransform bottom = null,
            int slot = 0)
        {
            var go = UnityEngine.Object.Instantiate(anchor.gameObject, parent);
            go.name = name;

            var btn = go.GetComponent<global::Mayday.UI.UIButton>();
            if (btn == null)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 克隆按钮 " + name + " 上没有 UIButton，丢弃");
                UnityEngine.Object.Destroy(go);
                return;
            }

            // 改文案。UIText 的 key 是本地化键 —— 克隆会带上原按钮的 key，
            // 语言切换或 Awake 的 ApplyKey 会把我们的字盖回去，先清掉再 Set。
            // （keyed 是全局静态注册表 List<UIText>，不是实例标志，绝不能动。）
            var label = btn.Label;
            if (label != null)
            {
                try { label.key = null; } catch { }
                try { label.Set(text); } catch (Exception e)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 设置按钮文字失败（" + name + "）: " + e.Message);
                }
                // 兜底：如果 Build 时屏幕还没激活，克隆体的 Awake 会在打开菜单时才跑，
                // 可能从 current/shown 恢复旧文案 —— 三个状态字段一起写死。
                try { label.current = text; } catch { }
                try { label.shown = text; } catch { }
            }

            // 排位
            try
            {
                if (hasLayout && siblingIndex >= 0)
                {
                    go.transform.SetSiblingIndex(siblingIndex);
                }
                else if (bottom != null)
                {
                    var anchorRt = anchor.transform.TryCast<UnityEngine.RectTransform>();
                    var rt = go.transform.TryCast<UnityEngine.RectTransform>();
                    if (anchorRt != null && rt != null)
                    {
                        // 底部按钮中心 → 它的半高 + 我们半高 + 间距 = 我们最下面那颗的中心；
                        // slot 越大越靠上？约定 slot0=存盘（最上）、slot1=读档（贴着底部按钮）
                        float hBottom = bottom.rect.height;
                        float hOurs = anchorRt.rect.height;
                        float d0 = hBottom / 2f + hOurs / 2f + ManualGap; // 贴底那颗离底部按钮中心的距离
                        float dy = d0 + (1 - slot) * (hOurs + ManualGap);
                        var p = rt.position;
                        p.x = bottom.position.x;
                        p.y = bottom.position.y + dy;
                        rt.position = p;
                    }
                }
                else
                {
                    // 兜底：贴着 resumeBtn 往下排
                    var anchorRt = anchor.transform.TryCast<UnityEngine.RectTransform>();
                    var rt = go.transform.TryCast<UnityEngine.RectTransform>();
                    if (anchorRt != null && rt != null)
                    {
                        float dy = -(anchorRt.rect.height + ManualGap) * (slot + 1);
                        rt.position = anchorRt.position + new UnityEngine.Vector3(0f, dy, 0f);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 按钮排位失败（可能与原按钮重叠）: " + e.Message);
            }

            // 挂回调。委托目标都是静态方法，不会被 GC。
            btn.onClick = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                new System.Action(callback));
        }

        // ---------- 按钮动作 ----------

        static void OnSaveClicked()
        {
            try
            {
                string reason = CareerStore.SaveManual();
                if (reason == null)
                {
                    var gs = global::Mayday.GameState.Instance;
                    Toast(gs != null
                        ? "已存盘：第" + gs.Leg + "段 · " + gs.Money + " 块"
                        : "已存盘", global::Mayday.NotifyKind.Good);
                }
                else
                {
                    // 被守卫拦下（新公司/非房主等）—— 如实告知，别让玩家以为存上了
                    Toast(reason, global::Mayday.NotifyKind.Warning);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 菜单存盘异常: " + e);
                Toast("存盘失败，详见日志", global::Mayday.NotifyKind.Danger);
            }
        }

        static void OnLoadClicked()
        {
            try
            {
                CareerStore.ApplyForce();
                Toast("已读档", global::Mayday.NotifyKind.Good);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 菜单读档异常: " + e);
                Toast("读档失败，详见日志", global::Mayday.NotifyKind.Danger);
            }
        }

        /// <summary>游戏自带的通知条（右上角 toast）。</summary>
        static void Toast(string msg, global::Mayday.NotifyKind kind)
        {
            try
            {
                var feed = global::Mayday.UI.NotificationFeed.instance;
                if (feed != null) feed.Push(msg, kind);
            }
            catch { /* 通知只是锦上添花，失败不打扰主流程 */ }
        }
    }
}
