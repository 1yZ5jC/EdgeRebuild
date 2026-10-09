using SQLite;
using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    [Table("WindowGeometry")]
    public class WindowGeometryRecord
    {
        [PrimaryKey]
        public int Slot { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }
    }

    /// <summary>
    /// 窗口几何持久化（原型）：按槽位保存/恢复窗口位置和尺寸。
    /// 主窗口槽位 0，副窗口槽位 1..n。
    /// UWP AppWindow 不暴露 Position/MoveAndResize（那是 WinUI API），
    /// 位置/尺寸通过 ApplicationView.SetPreferredSurfaceSize + CoreWindow 路径获取；
    /// 原型阶段先存储数据，恢复逻辑后续补充。
    /// </summary>
    public static class WindowGeometryManager
    {
        public static async Task SaveAsync(int slot, double x, double y, double width, double height, bool maximized = false)
        {
            if (slot < 0) return;
            try
            {
                await DatabaseService.Database.InsertOrReplaceAsync(new WindowGeometryRecord
                {
                    Slot = slot,
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    IsMaximized = maximized
                });
            }
            catch { }
        }

        public static async Task<WindowGeometryRecord> LoadAsync(int slot)
        {
            if (slot < 0) return null;
            try
            {
                return await DatabaseService.Database.Table<WindowGeometryRecord>()
                    .Where(r => r.Slot == slot).FirstOrDefaultAsync();
            }
            catch { return null; }
        }

        public static async Task RemoveAsync(int slot)
        {
            if (slot < 0) return;
            try
            {
                await DatabaseService.Database.ExecuteAsync(
                    "DELETE FROM WindowGeometry WHERE Slot = ?", slot);
            }
            catch { }
        }
    }
}
