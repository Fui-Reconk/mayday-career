using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayCareer
{
    /// <summary>
    /// galgame 式存读档界面（T-MOD-011 的进阶形态）。
    ///
    /// 点暂停菜单的「存盘 / 读档」后弹出全屏选档面板：
    ///   - 列出档 1..SaveSlots，每行显示该档的存档时间 / 段数 / 金钱（空档标注）
    ///   - 存盘模式：点行 = 把当前进度写入该档（GuardSave 照常生效）
    ///   - 读档模式：点行 = 把该档读入当前进度
    ///   - 底部「关闭」或点击四周暗幕关闭
    ///
    /// 实现说明（IL2CPP 下的约束与对策）：
    ///   - 不能自定义 MonoBehaviour 子类，所以整棵 UI 树全用现成的 il2cpp
    ///     组件拼出来：Canvas / CanvasScaler / GraphicRaycaster / Image /
    ///     Button / Text，全部 AddComponent 到我们自己的 GameObject 上
    ///   - 游戏文字走 TMP，其字体资产不好从外部取；这里用系统字体
    ///     （微软雅黑，Font.CreateDynamicFontFromOSFont），Windows 上必有，
    ///     中文渲染零风险
    ///   - 行点击用 uGUI Button.onClick（UnityEvent），
    ///     AddListener 经 DelegateSupport 封送托管委托
    ///   - 独立 Canvas（sortOrder 3000）悬浮在游戏 UI 之上，
    ///     不参与游戏的 UIScreen 栈，开闭就是 SetActive
    /// </summary>
    internal static class SaveLoadScreen
    {
        const float RefW = 1920f, RefH = 1080f;
        const float RowH = 96f, RowStep = 108f, RowGap = 40f;

        static GameObject _root;        // Canvas 根（场景切换会被销毁，销毁后重建）
        static Text _title;
        static readonly List<Row> _rows = new List<Row>();
        static bool _saveMode;
        static int _builtRows = -1;

        sealed class Row
        {
            public GameObject go;
            public Text label;
            public Button button;
        }

        public static bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>打开选档面板。saveMode=true 存盘模式，false 读档模式。</summary>
        public static void Open(bool saveMode)
        {
            try
            {
                EnsureBuilt();
                _saveMode = saveMode;
                _title.text = saveMode
                    ? "存档 —— 选择要写入的档位（写入会覆盖该档，上一份自动备份）"
                    : "读档 —— 选择要读取的档位（读取会覆盖当前进度）";

                int slots = CareerStore.SlotCount;
                for (int i = 0; i < _rows.Count; i++)
                {
                    bool active = i < slots;
                    _rows[i].go.SetActive(active);
                    if (active) FillRow(_rows[i], i + 1);
                }
                _root.SetActive(true);
                Plugin.LogSource.LogInfo("[MaydayCareer] 打开" + (saveMode ? "存" : "读") + "档选档界面（" + slots + " 档）");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 打开选档界面失败: " + e);
            }
        }

        public static void Close()
        {
            try { if (_root != null) _root.SetActive(false); }
            catch { }
        }

        static void FillRow(Row row, int slot)
        {
            string info = CareerStore.SlotInfo(slot);
            row.label.text = info == null
                ? "档位 " + slot + "　　（空）"
                : "档位 " + slot + "　　" + info;
        }

        // ---------- 构建 ----------

        static void EnsureBuilt()
        {
            if (_root != null) { try { return; } catch { } _root = null; } // Unity 假 null（场景切换销毁）→ 重建

            _rows.Clear();
            _builtRows = -1;

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

            var rt = rootGo.transform as RectTransform;
            Stretch(rt);

            // 四周暗幕：点击即关闭
            var dim = NewImage(rt, "Dim", new Color(0f, 0f, 0f, 0.78f));
            Stretch(dim.transform as RectTransform);
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.onClick.AddListener(Wrap(Close));

            // 面板本体
            float panelH = 300f + CareerStore.SlotCount * RowStep;
            var panel = NewImage(rt, "Panel", new Color(0.12f, 0.14f, 0.19f, 0.985f));
            var prt = panel.transform as RectTransform;
            prt.sizeDelta = new Vector2(980f, panelH);

            Font font = GetCjkFont();

            // 标题
            _title = NewText(panel.transform, "Title", "", font, 34, Color.white);
            var trt = _title.transform as RectTransform;
            trt.anchoredPosition = new Vector2(0f, panelH / 2f - 75f);
            trt.sizeDelta = new Vector2(940f, 60f);

            // 档位行
            int n = CareerStore.SlotCount;
            _builtRows = n;
            for (int i = 0; i < n; i++)
            {
                int slot = i + 1; // 闭包捕获：必须是局部变量
                var rowGo = NewImage(panel.transform, "Slot" + slot, new Color(0.20f, 0.23f, 0.31f, 1f));
                var rrt = rowGo.transform as RectTransform;
                rrt.sizeDelta = new Vector2(900f, RowH);
                rrt.anchoredPosition = new Vector2(0f, panelH / 2f - 145f - i * RowStep);

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
                var lrt = label.transform as RectTransform;
                Stretch(lrt);
                label.alignment = TextAnchor.MiddleCenter;

                var row = new Row { go = rowGo, label = label, button = btn };
                btn.onClick.AddListener(Wrap(delegate { OnRowClicked(slot); }));
                _rows.Add(row);
            }

            // 关闭按钮
            var closeGo = NewImage(panel.transform, "Close", new Color(0.35f, 0.38f, 0.45f, 1f));
            var crt = closeGo.transform as RectTransform;
            crt.sizeDelta = new Vector2(220f, 64f);
            crt.anchoredPosition = new Vector2(0f, -(panelH / 2f - 62f));
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeGo.GetComponent<Image>();
            var clabel = NewText(closeGo.transform, "Label", "关闭", font, 30, Color.white);
            Stretch(clabel.transform as RectTransform);
            clabel.alignment = TextAnchor.MiddleCenter;
            closeBtn.onClick.AddListener(Wrap(Close));

            rootGo.SetActive(false); // 构建完先藏起来，Open 再亮
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
                    string reason = CareerStore.TrySave();
                    if (reason == null)
                    {
                        var gs = global::Mayday.GameState.Instance;
                        MenuButtons2Toast(gs != null
                            ? "已存盘[档" + slot + "]：第" + gs.Leg + "段 · " + gs.Money + " 块"
                            : "已存盘", global::Mayday.NotifyKind.Good);
                    }
                    else
                    {
                        MenuButtons2Toast(reason, global::Mayday.NotifyKind.Warning);
                        return; // 没存上，留在界面里让用户换行重试
                    }
                }
                else
                {
                    CareerStore.ApplyForce();
                    MenuButtons2Toast("已读档[档" + slot + "]", global::Mayday.NotifyKind.Good);
                }
                Close();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 选档操作异常: " + e);
                MenuButtons2Toast("操作失败，详见日志", global::Mayday.NotifyKind.Danger);
            }
        }

        /// <summary>复用通知条：逻辑和暂停菜单按钮一致，直接调用那边的方法。</summary>
        static void MenuButtons2Toast(string msg, global::Mayday.NotifyKind kind)
        {
            MenuButtons.Toast(msg, kind);
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
            var rt = go.transform as RectTransform;
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
            var rt = go.transform as RectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return t;
        }

        /// <summary>
        /// 系统字体（微软雅黑）。Windows 上必有，中文渲染零风险；
        /// 万一创建失败退回 Arial，再退回 Text 的默认字体（反正能点，字丑而已）。
        /// </summary>
        static Font GetCjkFont()
        {
            try
            {
                var f = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei" }, 30);
                if (f != null) return f;
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("[MaydayCareer] 创建系统字体失败: " + e.Message); }
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
