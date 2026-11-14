using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayCareer
{
    /// <summary>
    /// 选档界面开着时拦截游戏的返回/关闭路径（ESC 的多个可能入口）：
    ///   1. UIRoot.HandleBack —— 游戏返回按钮逻辑
    ///   2. UIScreen.Back / Close / CloseInstant —— ESC 关暂停菜单最终落到这里
    ///   3. UIRoot.Update —— 整个跳过（游戏的输入轮询在里面，轮询不到 ESC 就不会反应）
    /// 再配合 SaveLoadScreen 自己的 ESC 轮询（关选档界面）+ 帧抑制
    /// （Close 记录 frameCount，当帧所有前缀都拦截，杜绝执行顺序竞态）。
    /// </summary>
    [HarmonyPatch]
    internal static class EscapeHook
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIRoot), "HandleBack")]
        private static bool HandleBackPrefix()
        {
            if (!SaveLoadScreen.ShouldBlockGameBack) return true;
            SaveLoadScreen.Close();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIRoot), "Update")]
        private static bool UIRootUpdatePrefix()
        {
            // 选档界面是模态：冻结 UIRoot 的输入轮询，游戏的 ESC 反应不过来。
            // 通知条/界面动画也走这里，界面开着时会暂停刷新 —— 可接受。
            return !SaveLoadScreen.IsOpen;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIScreen), "Back")]
        private static bool ScreenBackPrefix() => !SaveLoadScreen.ShouldBlockGameBack;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIScreen), "Close")]
        private static bool ScreenClosePrefix() => !SaveLoadScreen.ShouldBlockGameBack;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIScreen), "CloseInstant")]
        private static bool ScreenCloseInstantPrefix() => !SaveLoadScreen.ShouldBlockGameBack;
    }

    /// <summary>
    /// galgame 式存读档界面（T-MOD-011 的进阶形态）。
    ///
    /// 点暂停菜单的「存盘 / 读档」后弹出全屏选档面板：
    ///   - 第一行 = 自动存档（读档可选，存盘灰显不可写）
    ///   - 档 1..SaveSlots 每行显示存档时间 / 段数 / 金钱（空档标注）
    ///   - 存盘模式点已有数据的档位 → 二次确认（行变橙再点执行）
    ///   - 档位多时行区滚动（ScrollRect + 布局组），滚轮/拖拽/滚动条都可用
    ///   - ESC / 底部「关闭」/ 点击暗幕 关闭；ESC 只关本界面，暂停菜单不动
    ///
    /// 实现说明（IL2CPP 下的约束与对策）：
    ///   - 不能自定义 MonoBehaviour，整棵 UI 树用现成 il2cpp 组件拼：
    ///     Canvas / CanvasScaler / GraphicRaycaster / Image / Button / Text /
    ///     ScrollRect / Scrollbar / VerticalLayoutGroup / ContentSizeFitter
    ///   - 文字用系统字体（微软雅黑，单名重载的 CreateDynamicFontFromOSFont
    ///     —— string[] 重载在部分 interop 里缺方法），Windows 中文零风险
    ///   - 行点击：uGUI Button.onClick，AddListener 经 DelegateSupport 封送
    ///   - 独立 Canvas（sortOrder 3000），不参与游戏的 UIScreen 栈
    /// </summary>
    internal static class SaveLoadScreen
    {
        const float RefW = 1920f, RefH = 1080f;
        const float RowH = 96f, RowGap = 12f;
        const float RowStep = RowH + RowGap;      // 布局组的行距（行高 + 间距）
        const float PanelMaxH = 980f; // 面板最大高度：超出部分靠滚动，不撑爆屏幕

        static GameObject _root;        // Canvas 根（场景切换会被销毁，销毁后重建）
        static Text _title;
        static Text _subtitle;
        static readonly List<Row> _rows = new List<Row>();
        static bool _saveMode;
        static int _builtRows = -1;

        // ESC 帧抑制：Close 记录当前帧号，该帧内游戏的所有返回/关闭路径都拦截，
        // 无论游戏输入轮询和插件轮询谁先跑（见 EscapeHook.ShouldBlockGameBack）
        static int _lastCloseFrame = -1;

        // 自研滚动状态：content 位移、可视高、可滚距离、滚动条把手视觉
        static float _scrollOffset;
        static float _viewportH, _contentH, _maxScroll, _sbHandleH;
        static UnityEngine.RectTransform _contentRect;
        static UnityEngine.RectTransform _sbHandle;

        sealed class Row
        {
            public GameObject go;
            public Text label;
            public Button button;
        }

        public static bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>游戏的返回/关闭逻辑要不要拦截（界面开着，或 ESC 刚关掉界面的那一帧）。</summary>
        public static bool ShouldBlockGameBack
        {
            get
            {
                try { return IsOpen || UnityEngine.Time.frameCount == _lastCloseFrame; }
                catch { return false; }
            }
        }

        // 鼠标滚轮诊断：只打一次，确认 InputSystem 的 Mouse 路径是否可用
        static bool _mouseDiagLogged;

        /// <summary>每帧轮询：ESC 关界面；滚轮/方向键/PageUp·Down 滚行区（自研滚动，不依赖 ScrollRect）。</summary>
        public static void PollEsc()
        {
            if (!IsOpen) return;

            // ESC 关界面（独立 try：某个键位枚举缺失不能连累它）
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            }
            catch { }

            // 键盘滚动兜底：Keyboard.current 路径实测可靠（热键就在用）
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    float keyStep = 0f;
                    try { if (kb.pageUpKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) keyStep = -RowStep * 2f; } catch { }
                    try { if (kb.pageDownKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) keyStep = RowStep * 2f; } catch { }
                    if (keyStep != 0f) ScrollBy(keyStep);
                }
            }
            catch { }

            // 鼠标滚轮
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (!_mouseDiagLogged)
                {
                    _mouseDiagLogged = true;
                    Plugin.LogSource.LogInfo("[MaydayCareer] 滚轮诊断：Mouse.current=" + (mouse != null ? "有" : "null"));
                }
                if (mouse != null)
                {
                    Vector2 delta = mouse.scroll.ReadValue();
                    // 一格滚轮 ≈ 一行（Windows 滚轮一格 120，行距 108）
                    if (delta.y != 0f) ScrollBy(-delta.y * 0.9f);
                }
            }
            catch (Exception e)
            {
                if (!_mouseDiagLogged)
                {
                    _mouseDiagLogged = true;
                    Plugin.LogSource.LogWarning("[MaydayCareer] 滚轮路径不可用（可用方向键/PageUp·Down 滚动）: " + e.Message);
                }
            }
        }

        /// <summary>滚动行区：位移钳制在 [0, 最大可滚距离]，同步行可见性（虚拟化裁剪）与滚动条把手。</summary>
        static void ScrollBy(float delta)
        {
            if (_maxScroll <= 0f) return;
            _scrollOffset = Math.Max(0f, Math.Min(_maxScroll, _scrollOffset + delta));
            ApplyScroll();
        }

        static void ApplyScroll()
        {
            if (_contentRect == null) return;
            try
            {
                _scrollOffset = Math.Max(0f, Math.Min(_maxScroll, _scrollOffset));
                _contentRect.anchoredPosition = new Vector2(0f, _scrollOffset);

                // 虚拟化裁剪：视口外的行直接隐藏（没有 RectMask2D 也不会溢出面板）。
                // 行 i 的顶边距视口顶部 = i*RowStep - offset（offset 增大 = 内容上移露出下面的行）。
                // 超出 SaveSlots 的行（档位数收缩隐藏的）不参与。
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (i > CareerStore.SlotCount) continue;
                    float rowTop = i * RowStep - _scrollOffset;
                    bool vis = rowTop < _viewportH && rowTop + RowH > 0f;
                    if (_rows[i].go.activeSelf != vis) _rows[i].go.SetActive(vis);
                }

                // 滚动条把手：纯视觉，位置按滚动进度手算。
                // 把手锚点在轨道底边（anchorMin/Max.y = 0），anchoredPosition 从锚点起算。
                if (_sbHandle != null && _maxScroll > 0f)
                {
                    float progress = _scrollOffset / _maxScroll;
                    float travel = _viewportH - _sbHandleH;
                    _sbHandle.anchoredPosition = new Vector2(0f, _viewportH - _sbHandleH / 2f - progress * travel);
                }
            }
            catch { }
        }

        /// <summary>打开选档面板。saveMode=true 存盘模式，false 读档模式。</summary>
        public static void Open(bool saveMode)
        {
            try
            {
                EnsureBuilt();
                _saveMode = saveMode;
                _armedSlot = 0;
                _title.text = saveMode ? "存档 —— 选择要写入的档位" : "读档 —— 选择要读取的档位";
                _subtitle.text = saveMode
                    ? "写入会覆盖所选档位（上一份自动留成 .bak）；自动存档行由游戏落地时更新"
                    : "读取会覆盖当前进度（档位文件本身不受影响）";

                int slots = CareerStore.SlotCount;
                for (int i = 0; i < _rows.Count; i++)
                {
                    // 行 0 = 自动存档，行 1..N = 手动档位
                    bool active = i <= slots;
                    _rows[i].go.SetActive(active);
                    if (active) FillRow(_rows[i], i, saveMode);
                }
                _scrollOffset = 0f;
                ApplyScroll(); // 回到顶部 + 刷新行可见性

                // 把游戏的 UI 导航焦点锁到我们的行上：否则方向键会同时移动
                // 暂停菜单里游戏自己的候选高亮（EventSystem 导航，拦不住轮询只能改焦点）
                try
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null && _rows.Count > 0) es.SetSelectedGameObject(_rows[0].go);
                }
                catch { }

                _root.SetActive(true);
                Plugin.LogSource.LogInfo("[MaydayCareer] 打开" + (saveMode ? "存" : "读") + "档选档界面（自动 + " + slots + " 手动档）");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 打开选档界面失败: " + e);
            }
        }

        public static void Close()
        {
            try
            {
                if (_root != null && _root.activeSelf)
                {
                    _lastCloseFrame = UnityEngine.Time.frameCount;
                    _root.SetActive(false);
                }
                // 归还 UI 导航焦点，让游戏的 EventSystem 恢复自己的候选逻辑
                try
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null) es.SetSelectedGameObject(null);
                }
                catch { }
            }
            catch { }
            _armedSlot = 0;
        }

        // 二次确认状态：_armedSlot = 已点过一次、等待确认的档位（0 = 无）
        static int _armedSlot;

        static void FillRow(Row row, int slot, bool saveMode)
        {
            if (slot == CareerStore.AutoSlot)
            {
                // 自动存档行：存盘模式灰显禁用（不可手动写），
                // 读档模式可选（回滚到"上次落地状态"）
                if (saveMode)
                {
                    row.label.text = "自动存档　　（落地时自动写入，不可手动覆盖）";
                    row.label.color = new Color(0.45f, 0.48f, 0.55f, 1f);
                    row.button.interactable = false;
                }
                else
                {
                    string info = CareerStore.SlotInfo(CareerStore.AutoSlot);
                    row.label.text = "自动存档　　" + (info ?? "（空 —— 落地后自动生成）");
                    row.label.color = new Color(0.92f, 0.94f, 0.98f, 1f);
                    row.button.interactable = true;
                }
                return;
            }

            string s = CareerStore.SlotInfo(slot);
            row.label.text = s == null
                ? "档位 " + slot + "　　（空）"
                : "档位 " + slot + "　　" + s;
            row.label.color = new Color(0.92f, 0.94f, 0.98f, 1f);
            row.button.interactable = true;
        }

        /// <summary>解除确认状态，把行文字恢复成档位信息。</summary>
        static void Disarm()
        {
            if (_armedSlot <= 0) return;
            int slot = _armedSlot;
            _armedSlot = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i == slot && _rows[i].go.activeSelf) { FillRow(_rows[i], slot, _saveMode); return; }
            }
        }

        // ---------- 构建 ----------

        static void EnsureBuilt()
        {
            if (_root != null)
            {
                // 档位数（SaveSlots）变了也要重建
                if (_builtRows == CareerStore.SlotCount + 1) return;
                try { UnityEngine.Object.Destroy(_root); } catch { }
                _root = null;
                _title = null;
                _subtitle = null;
            }

            try
            {
                BuildNow();
            }
            catch (Exception e)
            {
                // 半途而废的残骸必须清干净：_root 若留着，下次 Open 会拿
                // "有 Canvas 没 Title"的半成品去 NRE。清掉后下次完整重建。
                try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
                _root = null;
                _title = null;
                _subtitle = null;
                _rows.Clear();
                _builtRows = -1;
                throw;
            }
        }

        static void BuildNow()
        {
            _rows.Clear();
            _builtRows = -1;
            Plugin.LogSource.LogInfo("[MaydayCareer] 构建选档界面…");

            var rootGo = new GameObject("MaydayCareerSaveLoad");
            _root = rootGo;

            var canvas = rootGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            var scaler = rootGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;
            rootGo.AddComponent<GraphicRaycaster>();
            Plugin.LogSource.LogInfo("[MaydayCareer] 构建选档界面: canvas ok");

            var rt = rootGo.transform.TryCast<UnityEngine.RectTransform>();
            Stretch(rt);

            // 四周暗幕：点击即关闭
            var dim = NewImage(rt, "Dim", new Color(0f, 0f, 0f, 0.78f));
            Stretch(dim.transform.TryCast<UnityEngine.RectTransform>());
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.onClick.AddListener(Wrap(Close));

            // 面板本体：行多时高度封顶，靠滚动条看剩下的行
            int manual = CareerStore.SlotCount;
            float naturalH = 340f + (manual + 1) * RowStep;
            float panelH = Math.Min(naturalH, PanelMaxH);
            var panel = NewImage(rt, "Panel", new Color(0.12f, 0.14f, 0.19f, 0.985f));
            var prt = panel.transform.TryCast<UnityEngine.RectTransform>();
            prt.sizeDelta = new Vector2(980f, panelH);

            Font font = GetCjkFont();
            Plugin.LogSource.LogInfo("[MaydayCareer] 构建选档界面: 面板+字体 ok（font=" + (font != null ? font.name : "null") + "）");

            // 标题（一行短句，别让长文案溢出面板）
            _title = NewText(panel.transform, "Title", "", font, 34, Color.white);
            var trt = _title.transform.TryCast<UnityEngine.RectTransform>();
            trt.anchoredPosition = new Vector2(0f, panelH / 2f - 55f);
            trt.sizeDelta = new Vector2(940f, 44f);

            // 副标题：灰色小字说明覆盖后果
            _subtitle = NewText(panel.transform, "Subtitle", "", font, 22, new Color(0.62f, 0.66f, 0.72f, 1f));
            var srt = _subtitle.transform.TryCast<UnityEngine.RectTransform>();
            srt.anchoredPosition = new Vector2(0f, panelH / 2f - 103f);
            srt.sizeDelta = new Vector2(940f, 30f);

            // 行滚动区：自研滚动 —— 不用 RectMask2D/ScrollRect/Scrollbar
            //（实测这游戏的 interop 对它们不可靠，AddComponent 直接 NRE）。
            // 滚动 = 滚轮驱动 content 位移 + 视口外的行隐藏（虚拟化裁剪）+
            // 纯视觉滚动条（两张 Image，把手位置手算）。全是验证过的基础组件。
            float viewportH = panelH - 240f; // 上下留出标题区和关闭按钮区
            int n = manual + 1;
            float contentH = n * RowH + (n - 1) * RowGap;
            _viewportH = viewportH;
            _contentH = contentH;
            _maxScroll = Math.Max(0f, contentH - viewportH);
            _scrollOffset = 0f;

            var viewport = NewImage(panel.transform, "Viewport", new Color(0f, 0f, 0f, 0f));
            var vrt = viewport.transform.TryCast<UnityEngine.RectTransform>();
            vrt.sizeDelta = new Vector2(880f, viewportH);
            vrt.anchoredPosition = Vector2.zero; // 居中（不再给滚动条留偏移）

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            // 裸 GameObject 的 transform 是普通 Transform 不是 RectTransform
            //（之前靠紧跟的布局组组件隐式升级，删掉布局组后这里 NRE）—— 显式补上
            content.AddComponent<UnityEngine.RectTransform>();
            _contentRect = content.transform.TryCast<UnityEngine.RectTransform>();
            _contentRect.anchorMin = new Vector2(0f, 1f);   // 顶部锚定，向下生长
            _contentRect.anchorMax = new Vector2(1f, 1f);
            _contentRect.pivot = new Vector2(0.5f, 1f);

            // 档位行：行 0 = 自动存档，行 1..N = 手动档位（手动坐标，久经考验）
            _builtRows = n;
            for (int i = 0; i < n; i++)
            {
                int slot = i; // 闭包捕获：行号即档位号（0 = 自动存档）
                var rowGo = NewImage(content.transform, slot == CareerStore.AutoSlot ? "SlotAuto" : "Slot" + slot, new Color(0.20f, 0.23f, 0.31f, 1f));
                var rrt = rowGo.transform.TryCast<UnityEngine.RectTransform>();
                rrt.anchorMin = new Vector2(0.5f, 1f);
                rrt.anchorMax = new Vector2(0.5f, 1f);
                rrt.pivot = new Vector2(0.5f, 1f);
                rrt.sizeDelta = new Vector2(880f, RowH);
                rrt.anchoredPosition = new Vector2(0f, -(RowH / 2f + i * RowStep));

                var btn = rowGo.AddComponent<Button>();
                btn.targetGraphic = rowGo.GetComponent<Image>();
                btn.colors = new ColorBlock
                {
                    normalColor = new Color(0.20f, 0.23f, 0.31f, 1f),
                    highlightedColor = new Color(0.27f, 0.31f, 0.42f, 1f),
                    pressedColor = new Color(0.16f, 0.18f, 0.25f, 1f),
                    selectedColor = new Color(0.20f, 0.23f, 0.31f, 1f),
                    disabledColor = new Color(0.15f, 0.15f, 0.15f, 1f),
                    colorMultiplier = 1f,
                    fadeDuration = 0.08f,
                };

                var label = NewText(rowGo.transform, "Label", "", font, 30, new Color(0.92f, 0.94f, 0.98f, 1f));
                Stretch(label.transform.TryCast<UnityEngine.RectTransform>());
                label.alignment = TextAnchor.MiddleCenter;

                var row = new Row { go = rowGo, label = label, button = btn };
                btn.onClick.AddListener(Wrap(delegate { OnRowClicked(slot); }));
                _rows.Add(row);
            }
            Plugin.LogSource.LogInfo("[MaydayCareer] 构建选档界面: rows ok（" + _rows.Count + " 行，可视高 " + viewportH.ToString("F0") + "）");

            // 滚动条：纯视觉（轨道 + 把手两张 Image），位置在 ApplyScroll 里手算。
            // 行数放不下滚动区时隐藏。
            try
            {
                var track = NewImage(panel.transform, "ScrollbarTrack", new Color(0.25f, 0.28f, 0.35f, 0.9f));
                var trt2 = track.transform.TryCast<UnityEngine.RectTransform>();
                trt2.sizeDelta = new Vector2(14f, viewportH);
                trt2.anchoredPosition = new Vector2(490f - 24f, 0f);
                var handle = NewImage(track.transform, "Handle", new Color(0.55f, 0.60f, 0.70f, 1f));
                _sbHandle = handle.transform.TryCast<UnityEngine.RectTransform>();
                _sbHandle.anchorMin = new Vector2(0f, 0f);
                _sbHandle.anchorMax = new Vector2(1f, 0f);
                _sbHandle.pivot = new Vector2(0.5f, 0.5f);
                _sbHandleH = Math.Max(40f, viewportH * viewportH / Math.Max(1f, contentH));
                _sbHandle.sizeDelta = new Vector2(0f, _sbHandleH);
                track.SetActive(_maxScroll > 0f);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 滚动条构建失败（不影响滚轮滚动）: " + e.Message);
            }

            // 关闭按钮
            var closeGo = NewImage(panel.transform, "Close", new Color(0.35f, 0.38f, 0.45f, 1f));
            var clrt = closeGo.transform.TryCast<UnityEngine.RectTransform>();
            clrt.sizeDelta = new Vector2(220f, 64f);
            clrt.anchoredPosition = new Vector2(0f, -(panelH / 2f - 62f));
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeGo.GetComponent<Image>();
            var clabel = NewText(closeGo.transform, "Label", "关闭", font, 30, Color.white);
            Stretch(clabel.transform.TryCast<UnityEngine.RectTransform>());
            clabel.alignment = TextAnchor.MiddleCenter;
            closeBtn.onClick.AddListener(Wrap(Close));

            rootGo.SetActive(false); // 构建完先藏起来，Open 再亮
            Plugin.LogSource.LogInfo("[MaydayCareer] 构建选档界面: 完成（自动 + " + manual + " 手动档，可视高 " + viewportH.ToString("F0") + "）");
        }

        /// <summary>行为委托 → UnityAction（UnityEvent.AddListener 的参数类型）。</summary>
        static UnityEngine.Events.UnityAction Wrap(Action a)
        {
            return Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(
                new Action(a));
        }

        static void OnRowClicked(int slot)
        {
            try
            {
                // 自动存档行：只在读档模式可达（存盘模式按钮已禁用）。
                // 读它 = 回滚到"上次落地状态"，不改变当前档位选择。
                if (slot == CareerStore.AutoSlot)
                {
                    CareerStore.ApplySlot(CareerStore.AutoSlot);
                    MenuButtons.Toast("已读档[自动存档]", global::Mayday.NotifyKind.Good);
                    Close();
                    return;
                }

                // 二次确认：存盘模式下点"已有数据"的档位，第一次点先确认，再点才执行。
                // 空档直接写入；读档不确认（档位文件不会因读取而变化）。
                if (_saveMode && _armedSlot != slot && CareerStore.SlotInfo(slot) != null)
                {
                    Disarm();
                    _armedSlot = slot;
                    var row = _rows[slot];
                    row.label.text = "确认覆盖档位 " + slot + "？再点一次执行（现有内容会自动备份）";
                    row.label.color = new Color(1f, 0.72f, 0.30f, 1f);
                    return;
                }
                Disarm();

                CareerStore.SetActiveSlot(slot);
                try
                {
                    Plugin.ActiveSlot.Value = slot; // 记住选择
                    var cfg = Plugin.ConfigRef;
                    if (cfg != null) cfg.Save();
                }
                catch { }

                if (_saveMode)
                {
                    // 选档界面里的覆盖是带二次确认的明确操作，跳过 GuardSave 的段数拦截
                    string reason = CareerStore.SaveManual(skipLegGuard: true);
                    if (reason == null)
                    {
                        var gs = global::Mayday.GameState.Instance;
                        MenuButtons.Toast(gs != null
                            ? "已存盘[档" + slot + "]：第" + gs.Leg + "段 · " + gs.Money + " 块"
                            : "已存盘", global::Mayday.NotifyKind.Good);
                    }
                    else
                    {
                        MenuButtons.Toast(reason, global::Mayday.NotifyKind.Warning);
                        FillRow(_rows[slot], slot, _saveMode); // 恢复行文字，留在界面里让用户换行重试
                        return;
                    }
                }
                else
                {
                    CareerStore.ApplySlot(slot);
                    MenuButtons.Toast("已读档[档" + slot + "]", global::Mayday.NotifyKind.Good);
                }
                Close();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 选档操作异常: " + e);
                MenuButtons.Toast("操作失败，详见日志", global::Mayday.NotifyKind.Danger);
            }
        }

        // ---------- UI 件工厂 ----------

        static void Stretch(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static GameObject NewImage(UnityEngine.Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = go.transform.TryCast<UnityEngine.RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        static Text NewText(UnityEngine.Transform parent, string name, string text, Font font, int size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = go.transform.TryCast<UnityEngine.RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return t;
        }

        /// <summary>
        /// 系统字体（微软雅黑）。必须用单名字符串重载 —— string[] 重载在
        /// 部分 interop 里缺方法（实测 Method not found）。Windows 上必有，
        /// 中文渲染零风险；万一创建失败退回 Arial，再退回 Text 的内置字体。
        /// </summary>
        static Font GetCjkFont()
        {
            string[] names = { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei" };
            foreach (var nm in names)
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(nm, 30);
                    if (f != null) return f;
                }
                catch { }
            }
            try
            {
                var f = Font.CreateDynamicFontFromOSFont("Arial", 30);
                if (f != null) return f;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
