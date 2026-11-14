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
        static Text _subtitle;
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

        // 二次确认状态：_armedSlot = 已点过一次、等待确认的档位（0 = 无）
        static int _armedSlot;

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
            try { if (_root != null) _root.SetActive(false); }
            catch { }
            _armedSlot = 0;
        }

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

            // 面板本体：行数 = 自动存档 1 行 + 手动档 N 行
            float panelH = 340f + (CareerStore.SlotCount + 1) * RowStep;
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

            // 档位行：行 0 = 自动存档，行 1..N = 手动档位
            int n = CareerStore.SlotCount + 1;
            _builtRows = n;
            for (int i = 0; i < n; i++)
            {
                int slot = i; // 行号即档位号：0 = 自动存档，1..N = 手动档
                var rowGo = NewImage(panel.transform, slot == CareerStore.AutoSlot ? "SlotAuto" : "Slot" + slot, new Color(0.20f, 0.23f, 0.31f, 1f));
                var rrt = rowGo.transform.TryCast<UnityEngine.RectTransform>();
                rrt.sizeDelta = new Vector2(900f, RowH);
                rrt.anchoredPosition = new Vector2(0f, panelH / 2f - 180f - i * RowStep);

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
                var lrt = label.transform.TryCast<UnityEngine.RectTransform>();
                Stretch(lrt);
                label.alignment = TextAnchor.MiddleCenter;

                var row = new Row { go = rowGo, label = label, button = btn };
                btn.onClick.AddListener(Wrap(delegate { OnRowClicked(slot); }));
                _rows.Add(row);
            }

            // 关闭按钮
            var closeGo = NewImage(panel.transform, "Close", new Color(0.35f, 0.38f, 0.45f, 1f));
            var crt = closeGo.transform.TryCast<UnityEngine.RectTransform>();
            crt.sizeDelta = new Vector2(220f, 64f);
            crt.anchoredPosition = new Vector2(0f, -(panelH / 2f - 62f));
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeGo.GetComponent<Image>();
            var clabel = NewText(closeGo.transform, "Label", "关闭", font, 30, Color.white);
            Stretch(clabel.transform.TryCast<UnityEngine.RectTransform>());
            clabel.alignment = TextAnchor.MiddleCenter;
            closeBtn.onClick.AddListener(Wrap(Close));

            rootGo.SetActive(false); // 构建完先藏起来，Open 再亮
            Plugin.LogSource.LogInfo("[MaydayCareer] 构建选档界面: 完成（" + n + " 行）");
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
