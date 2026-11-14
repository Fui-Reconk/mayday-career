using System;
using System.Runtime.InteropServices;

namespace MaydayCareer
{
    /// <summary>
    /// 禁用 BepInEx 控制台窗口的 QuickEdit 模式。
    ///
    /// 背景（用户实测踩坑）：Windows 老式控制台默认开启 QuickEdit —— 在
    /// 控制台内容区点一下鼠标就会进入文本选择状态，此时所有向控制台的
    /// 写入（WriteConsole）都会被阻塞，直到选择结束。游戏主线程每帧都在
    /// 打日志 → 写控制台的调用卡死在内核等待 → 整个游戏未响应。
    /// 之前用户点了一下日志窗口，游戏就直接卡死了。
    ///
    /// 处理：启动时把控制台输出模式的 ENABLE_QUICK_EDIT_MODE 位清掉
    /// （内核 P/Invoke，BepInEx 插件运行在 CoreCLR 上，P/Invoke 可用）。
    /// 代价：日志窗口里不能用鼠标圈选复制文字 —— 用复制日志的场景
    /// （报 issue）建议直接发 LogOutput.log 文件。
    ///
    /// 若控制台不存在（BepInEx 配置为仅文件日志）则静默跳过。
    /// </summary>
    internal static class ConsoleGuard
    {
        const int STD_OUTPUT_HANDLE = -11;
        const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        const uint ENABLE_EXTENDED_FLAGS = 0x0080;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        public static void DisableQuickEdit()
        {
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

                if (!GetConsoleMode(handle, out uint mode)) return; // 没有控制台（仅文件日志）

                if ((mode & ENABLE_QUICK_EDIT_MODE) == 0) return;   // 本来就没开，不折腾

                uint newMode = (mode & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS;
                if (SetConsoleMode(handle, newMode))
                    Plugin.LogSource.LogInfo("[MaydayCareer] 已禁用控制台 QuickEdit（点日志窗口不会再卡死游戏）");
                else
                    Plugin.LogSource.LogWarning("[MaydayCareer] 禁用控制台 QuickEdit 失败: " + Marshal.GetLastWin32Error());
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCareer] 控制台 QuickEdit 处理异常（忽略）: " + e.Message);
            }
        }
    }
}
