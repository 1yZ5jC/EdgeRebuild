using System;
using Windows.Storage;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 调试开关。
    ///
    /// 使用 ApplicationData.LocalSettings（同步 API）而不是 SQLite 设置表：
    /// PlatformCompat 的静态初始化发生在数据库可用之前，必须能同步读取这些开关。
    /// </summary>
    public static class DebugOptions
    {
        private const string ForceLegacyWindowModeKey = "Debug.ForceLegacyWindowMode";

        /// <summary>
        /// 强制使用 17763（1809）兼容窗口路径：
        /// 禁用 AppWindow / XamlRoot 等 18362+ API。
        /// 修改后需要重启应用才能生效。
        /// </summary>
        public static bool ForceLegacyWindowMode
        {
            get
            {
                try
                {
                    object value = ApplicationData.Current.LocalSettings.Values[ForceLegacyWindowModeKey];
                    if (value is bool boolValue) return boolValue;
                    if (value is string text)
                    {
                        return string.Equals(text, "True", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(text, "1", StringComparison.Ordinal);
                    }
                }
                catch
                {
                    // 非打包/单元测试环境没有 ApplicationData，按关闭处理
                }
                return false;
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[ForceLegacyWindowModeKey] = value;
                }
                catch
                {
                    // 无法持久化时至少不影响当前会话
                }
            }
        }
    }
}
