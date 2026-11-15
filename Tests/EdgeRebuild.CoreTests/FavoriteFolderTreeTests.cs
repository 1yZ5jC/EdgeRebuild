using System.Collections.Generic;
using System.Linq;
using EdgeRebuild.Core;
using Xunit;

namespace EdgeRebuild.CoreTests
{
    public class FavoriteFolderTreeTests
    {
        private static List<FavoriteFolderNode> Sample() => new List<FavoriteFolderNode>
        {
            new FavoriteFolderNode { Id = 1, ParentId = null, Name = "A" },
            new FavoriteFolderNode { Id = 2, ParentId = 1, Name = "B" },
            new FavoriteFolderNode { Id = 3, ParentId = 2, Name = "C" },
            new FavoriteFolderNode { Id = 4, ParentId = null, Name = "D" }
        };

        [Fact]
        public void WouldCreateCycle_MoveFolderIntoOwnDescendant_ReturnsTrue()
        {
            Assert.True(FavoriteFolderTree.WouldCreateCycle(Sample(), 1, 3));
        }

        [Fact]
        public void WouldCreateCycle_MoveFolderIntoItself_ReturnsTrue()
        {
            Assert.True(FavoriteFolderTree.WouldCreateCycle(Sample(), 2, 2));
        }

        [Fact]
        public void WouldCreateCycle_MoveFolderToRoot_ReturnsFalse()
        {
            Assert.False(FavoriteFolderTree.WouldCreateCycle(Sample(), 3, null));
        }

        [Fact]
        public void WouldCreateCycle_MoveFolderToSibling_ReturnsFalse()
        {
            Assert.False(FavoriteFolderTree.WouldCreateCycle(Sample(), 3, 4));
        }

        [Fact]
        public void GetPath_NestedFolder_ReturnsFullPath()
        {
            Assert.Equal("A / B / C", FavoriteFolderTree.GetPath(Sample(), 3));
        }

        [Fact]
        public void GetPath_Root_ReturnsEmpty()
        {
            Assert.Equal("", FavoriteFolderTree.GetPath(Sample(), null));
        }

        [Fact]
        public void CollectSubtreeIds_ReturnsFolderAndDescendantsOnly()
        {
            var ids = FavoriteFolderTree.CollectSubtreeIds(Sample(), 1);
            Assert.Equal(new[] { 1, 2, 3 }, ids.OrderBy(i => i).ToArray());
        }

        [Fact]
        public void CollectSubtreeIds_LeafFolder_ReturnsOnlyItself()
        {
            var ids = FavoriteFolderTree.CollectSubtreeIds(Sample(), 4);
            Assert.Equal(new[] { 4 }, ids.ToArray());
        }
    }
}
