using System;

namespace MaydayCareer
{
    /// <summary>
    /// 读档 / 存盘两个热键。
    ///
    /// 游戏用的是新输入系统（Unity.InputSystem），旧的 UnityEngine.Input
    /// 在启用新系统后调用会抛异常，所以这里直接读 Keyboard.current。
    ///
    /// 轮询挂在 Mirror.NetworkManager.Update 上 —— 这是唯一能确定每帧都会跑
    /// 的现成入口，因为 IL2CPP 下没法自己建 MonoBehaviour 做 Update。
    ///
    /// 两个键刻意不共用解析状态：各自 _resolved 标记、各自 try。
    /// 一个键在配置里写错了不该把另一个键也拖下水。
    /// </summary>
    internal static class Hotkey
    {
        const UnityEngine.InputSystem.Key LoadFallback = UnityEngine.InputSystem.Key.F9;
        const UnityEngine.InputSystem.Key SaveFallback = UnityEngine.InputSystem.Key.J;

        static UnityEngine.InputSystem.Key _loadKey = LoadFallback;
        static UnityEngine.InputSystem.Key _saveKey = SaveFallback;
        static bool _loadResolved;
        static bool _saveResolved;

        /// <summary>解析一个热键名。失败打 warn 并返回 fallback，绝不抛。</summary>
        static UnityEngine.InputSystem.Key Resolve(string raw, UnityEngine.InputSystem.Key fallback, string what)
        {
            try
            {
                if (!string.IsNullOrEmpty(raw))
                {
                    string name = raw.Trim();
                    if (Enum.TryParse(name, true, out UnityEngine.InputSystem.Key parsed))
                        return parsed;
                    Plugin.LogSource.LogWarning(
                        "[MaydayCareer] 无法识别的" + what + "热键 " + name + "，回退到 " + fallback);
                    return fallback;
                }
                Plugin.LogSource.LogWarning("[MaydayCareer] " + what + "热键为空，回退到 " + fallback);
                return fallback;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] " + what + "热键解析失败: " + e.Message);
                return fallback;
            }
        }

        static void EnsureLoad()
        {
            if (_loadResolved) return;
            _loadResolved = true;
            _loadKey = Resolve(Plugin.LoadKey.Value, LoadFallback, "读档");
        }

        static void EnsureSave()
        {
            if (_saveResolved) return;
            _saveResolved = true;
            _saveKey = Resolve(Plugin.SaveKey.Value, SaveFallback, "存盘");
        }

        public static void Poll()
        {
            UnityEngine.InputSystem.Keyboard kb;
            try
            {
                kb = UnityEngine.InputSystem.Keyboard.current;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 取 Keyboard.current 失败: " + e.Message);
                return;
            }
            if (kb == null) return;

            PollLoad(kb);
            PollSave(kb);
        }

        static void PollLoad(UnityEngine.InputSystem.Keyboard kb)
        {
            try
            {
                EnsureLoad();
                if (!kb[_loadKey].wasPressedThisFrame) return;
                Plugin.LogSource.LogInfo("[MaydayCareer] 检测到 " + _loadKey + "，手动读档");
                CareerStore.ApplyForce();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 读档热键异常: " + e);
            }
        }

        static void PollSave(UnityEngine.InputSystem.Keyboard kb)
        {
            try
            {
                EnsureSave();
                if (!kb[_saveKey].wasPressedThisFrame) return;
                Plugin.LogSource.LogInfo("[MaydayCareer] 检测到 " + _saveKey + "，手动存盘");
                CareerStore.SaveManual();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 存盘热键异常: " + e);
            }
        }

    }
}
