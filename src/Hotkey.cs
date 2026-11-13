using System;

namespace MaydayCareer
{
    /// <summary>
    /// 手动读档热键。
    ///
    /// 游戏用的是新输入系统（Unity.InputSystem），旧的 UnityEngine.Input
    /// 在启用新系统后调用会抛异常，所以这里直接读 Keyboard.current。
    ///
    /// 轮询挂在 Mirror.NetworkManager.Update 上 —— 这是唯一能确定每帧都会跑的
    /// 现成入口，因为 IL2CPP 下没法自己建 MonoBehaviour 做 Update。
    /// </summary>
    internal static class Hotkey
    {
        static UnityEngine.InputSystem.Key _key = UnityEngine.InputSystem.Key.F9;
        static bool _ready;

        static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            try
            {
                string name = Plugin.LoadKey.Value;
                if (!Enum.TryParse(name, true, out _key))
                {
                    _key = UnityEngine.InputSystem.Key.F9;
                    Plugin.LogSource.LogWarning("[MaydayCareer] 无法识别的热键 " + name + "，回退到 F9");
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 热键解析失败: " + e.Message);
            }
        }

        public static void Poll()
        {
            try
            {
                Ensure();
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return;
                if (kb[_key].wasPressedThisFrame)
                {
                    Plugin.LogSource.LogInfo("[MaydayCareer] 检测到 " + _key + "，手动读档");
                    CareerStore.ApplyForce();
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayCareer] 热键轮询异常: " + e);
            }
        }
    }
}
