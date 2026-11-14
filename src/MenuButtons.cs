using System;
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
        const float ManualSpacing = 12f; // 父容器没有 LayoutGroup 时按钮间的估算间距

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

            // 父容器有 LayoutGroup 就交给它排；没有就手动往下挪
            bool hasLayout = false;
            try { hasLayout = parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null; }
            catch { }

            if (!hasLayout)
                Plugin.LogSource.LogInfo("[MaydayCareer] 暂停菜单父容器无 LayoutGroup，按钮手动排位");

            CloneButton(anchor, parent, SaveBtnName, "存盘", OnSaveClicked, hasLayout, 0);
            CloneButton(anchor, parent, LoadBtnName, "读档", OnLoadClicked, hasLayout, 1);

            Plugin.LogSource.LogInfo("[MaydayCareer] 暂停菜单按钮已注入（存盘/读档）");
        }

        static void CloneButton(
            global::Mayday.UI.UIButton anchor,
            UnityEngine.Transform parent,
            string name,
            string text,
            Action callback,
            bool hasLayout,
            int slot)
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

            // 无 LayoutGroup 时手动排位：贴着原按钮往下排
            if (!hasLayout)
            {
                try
                {
                    var anchorRt = anchor.transform.TryCast<UnityEngine.RectTransform>();
                    var rt = go.transform.TryCast<UnityEngine.RectTransform>();
                    if (anchorRt != null && rt != null)
                    {
                        float dy = -(anchorRt.rect.height + ManualSpacing) * (slot + 1);
                        rt.anchoredPosition = anchorRt.anchoredPosition + new UnityEngine.Vector2(0f, dy);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning("[MaydayCareer] 按钮排位失败（可能与原按钮重叠）: " + e.Message);
                }
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
                CareerStore.SaveManual();
                var gs = global::Mayday.GameState.Instance;
                Toast(gs != null
                    ? "已存盘：第" + gs.Leg + "段 · " + gs.Money + " 块"
                    : "已存盘", global::Mayday.NotifyKind.Good);
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
