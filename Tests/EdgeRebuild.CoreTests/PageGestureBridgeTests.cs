using EdgeRebuild.Core;
using Xunit;

namespace EdgeRebuild.CoreTests
{
    public class PageGestureBridgeTests
    {
        [Theory]
        [InlineData("swb", PageShortcutKey.SwipeBack)]
        [InlineData("swf", PageShortcutKey.SwipeForward)]
        [InlineData("cr", PageShortcutKey.ReaderMode)]
        [InlineData("cst", PageShortcutKey.RestoreClosedTab)]
        public void Parse_MapsMarkers(string marker, PageShortcutKey expected)
        {
            Assert.Equal(expected, PageKeyMarkers.Parse(marker));
        }

        [Theory]
        [InlineData("")]
        [InlineData("unknown")]
        [InlineData("sw")]
        public void Parse_UnknownMarker_ReturnsNull(string marker)
        {
            Assert.Null(PageKeyMarkers.Parse(marker));
        }

        [Fact]
        public void BuildSwipeScript_ReportsBackAndForward()
        {
            string script = PageGestureBridge.BuildSwipeScript("send");

            Assert.Contains("touchstart", script);
            Assert.Contains("touchmove", script);
            Assert.Contains("touchend", script);
            Assert.Contains("'swb'", script);
            Assert.Contains("'swf'", script);
            Assert.Contains("send(m)", script);
        }

        [Fact]
        public void BuildSwipeScript_DoesNotBindMouseOrPointer()
        {
            string script = PageGestureBridge.BuildSwipeScript("mark");

            Assert.Contains("__edgSwipeBridge", script);
            Assert.DoesNotContain("mousedown", script);
            Assert.DoesNotContain("pointerdown", script);
            Assert.DoesNotContain("postMessage", script);
        }

        [Fact]
        public void BuildSwipeScript_ReportsTouchpadWheelOverscroll()
        {
            string script = PageGestureBridge.BuildSwipeScript("send");

            Assert.Contains("addEventListener('wheel'", script);
            Assert.Contains("deltaX", script);
            Assert.Contains("deltaMode", script);
            Assert.Contains("shiftKey", script);
            Assert.Contains("wc >= 2", script);
        }

        [Fact]
        public void BuildSwipeScript_SkipsDocumentReadersAndHtmlFullscreen()
        {
            string script = PageGestureBridge.BuildSwipeScript("send");

            Assert.Contains("epubreader.local", script);
            Assert.Contains("pdfviewer.local", script);
            Assert.Contains("ms-local-stream", script);
            Assert.Contains("fullscreenElement", script);
            Assert.Contains("window.top", script);
            Assert.Contains("canvas", script);
            Assert.Contains("video", script);
        }
    }
}
