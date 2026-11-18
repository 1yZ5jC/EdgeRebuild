using System;
using System.Collections.Generic;
using System.Linq;

namespace EdgeRebuild.Core
{
    /// <summary>文件夹树的轻量节点，便于脱离数据库做纯逻辑测试。</summary>
    public sealed class FavoriteFolderNode
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>收藏夹文件夹树的纯逻辑辅助（无 WinRT / SQLite 依赖）。</summary>
    public static class FavoriteFolderTree
    {
        private const int MaxDepth = 4096;

        /// <summary>把文件夹移动到 newParentId 是否会形成环（含移动到自身）。</summary>
        public static bool WouldCreateCycle(
            IEnumerable<FavoriteFolderNode> nodes, int folderId, int? newParentId)
        {
            if (!newParentId.HasValue) return false;
            if (newParentId.Value == folderId) return true;

            var byId = new Dictionary<int, FavoriteFolderNode>();
            foreach (var node in nodes)
            {
                if (node != null && !byId.ContainsKey(node.Id)) byId[node.Id] = node;
            }

            int? current = newParentId;
            int guard = 0;
            while (current.HasValue && guard++ < MaxDepth)
            {
                if (current.Value == folderId) return true;
                if (!byId.TryGetValue(current.Value, out var node)) return false;
                current = node.ParentId;
            }
            return false;
        }

        /// <summary>返回 "父 / 子 / 孙" 形式的路径；根目录返回空字符串。</summary>
        public static string GetPath(IEnumerable<FavoriteFolderNode> nodes, int? folderId)
        {
            if (!folderId.HasValue) return "";
            var byId = new Dictionary<int, FavoriteFolderNode>();
            foreach (var node in nodes)
            {
                if (node != null && !byId.ContainsKey(node.Id)) byId[node.Id] = node;
            }

            var names = new List<string>();
            int? current = folderId;
            int guard = 0;
            while (current.HasValue && guard++ < MaxDepth)
            {
                if (!byId.TryGetValue(current.Value, out var node)) break;
                names.Insert(0, node.Name ?? "");
                current = node.ParentId;
            }
            return string.Join(" / ", names.Where(n => n.Length > 0));
        }

        /// <summary>返回该文件夹及其所有后代的 Id 集合（包含 folderId 本身）。</summary>
        public static HashSet<int> CollectSubtreeIds(
            IEnumerable<FavoriteFolderNode> nodes, int folderId)
        {
            var ids = new HashSet<int> { folderId };
            var children = new Dictionary<int, List<int>>();
            foreach (var node in nodes)
            {
                if (node == null || !node.ParentId.HasValue) continue;
                if (!children.TryGetValue(node.ParentId.Value, out var list))
                {
                    list = new List<int>();
                    children[node.ParentId.Value] = list;
                }
                list.Add(node.Id);
            }

            var queue = new Queue<int>();
            queue.Enqueue(folderId);
            while (queue.Count > 0)
            {
                int parent = queue.Dequeue();
                if (!children.TryGetValue(parent, out var list)) continue;
                foreach (int child in list)
                {
                    if (ids.Add(child)) queue.Enqueue(child);
                }
            }
            return ids;
        }
    }
}
