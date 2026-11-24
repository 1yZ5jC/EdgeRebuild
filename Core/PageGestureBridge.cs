namespace EdgeRebuild.Core
{
    /// <summary>
    /// 页面内左右滑动桥（触摸 + 触摸板）：右滑后退、左滑前进。
    /// EdgeHTML 与 WebView2 共用同一段检测逻辑，只有上报函数不同——
    /// WebView2 用 postMessage，EdgeHTML 写入 marker 由宿主轮询。
    /// 触摸用 touch 事件；触摸板横向滚动用 wheel 事件（过滚到阈值才触发）。
    /// 鼠标/触控笔不参与；横向可滚动区域、输入框、页面本身横向溢出时跳过，
    /// 避免与轮播、横向表格和文本选择冲突。
    /// </summary>
    public static class PageGestureBridge
    {
        /// <summary>生成滑动检测脚本；sendFunction 为页面内已有的上报函数名。</summary>
        public static string BuildSwipeScript(string sendFunction)
        {
            return "(function(){ if (window.__edgSwipeBridge) return; window.__edgSwipeBridge = true;"
                + " var docReader = false;"
                + " try {"
                + "   var h = (location.hostname || '').toLowerCase();"
                + "   var p = location.protocol || '';"
                + "   docReader = p === 'ms-local-stream:' || h === 'epubreader.local' || h === 'pdfviewer.local';"
                + " } catch(e){}"
                + " if (docReader) return;"   // 本地 EPUB/PDF 阅读器有自己的翻页交互
                + " try { if (window.top !== window) return; } catch(e){}"   // 仅顶层文档，避免 iframe 重复
                + " var sx=0, sy=0, st=0, active=false;"
                + " var wx=0, wc=0, wt=0, wlock=0;"   // 触摸板轮子：累计位移/事件数/时间/冷却
                + " function report(m){ try { " + sendFunction + "(m); } catch(e){} }"
                + " function skipTarget(el){"
                + "   var n = el && el.nodeType === 3 ? el.parentElement : el;"
                + "   for (var i=0; n && n !== document.body && i<8; i++, n=n.parentElement){"
                + "     var tag = (n.tagName || '').toLowerCase();"
                + "     if (tag === 'input' || tag === 'textarea' || tag === 'select'"
                + "         || tag === 'canvas' || tag === 'video' || n.isContentEditable) return true;"
                + "   }"
                + "   return false;"
                + " }"
                + " function canScrollX(el){"
                + "   var n = el && el.nodeType === 3 ? el.parentElement : el;"
                + "   for (var i=0; n && i<8; i++, n=n.parentElement){"
                + "     var st = null; try { st = getComputedStyle(n); } catch(e){}"
                + "     if (!st) continue;"
                + "     if ((st.overflowX === 'auto' || st.overflowX === 'scroll')"
                + "         && n.scrollWidth > n.clientWidth + 4) return true;"
                + "   }"
                + "   return false;"
                + " }"
                + " function docScrollableX(){"
                + "   try {"
                + "     var d = document.documentElement, b = document.body;"
                + "     if (d && d.scrollWidth > window.innerWidth + 8"
                + "         && getComputedStyle(d).overflowX !== 'hidden') return true;"
                + "     if (b && b.scrollWidth > window.innerWidth + 8"
                + "         && getComputedStyle(b).overflowX !== 'hidden') return true;"
                + "   } catch(e){}"
                + "   return false;"
                + " }"
                + " document.addEventListener('touchstart', function(e){"
                + "   if (e.touches.length !== 1) { active = false; return; }"
                + "   if (document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement) { active = false; return; }"
                + "   var t = e.touches[0];"
                + "   if (t.clientX < 24 || t.clientX > (window.innerWidth - 24)) { active = false; return; }"
                + "   if (skipTarget(e.target) || canScrollX(e.target) || docScrollableX()) { active = false; return; }"
                + "   sx = t.clientX; sy = t.clientY; st = Date.now(); active = true;"
                + " }, { passive: true, capture: true });"
                + " document.addEventListener('touchmove', function(e){"
                + "   if (!active) return;"
                + "   if (e.touches.length !== 1) { active = false; return; }"
                + "   var t = e.touches[0], dx = t.clientX - sx, dy = t.clientY - sy;"
                + "   if (Math.abs(dy) > 40 && Math.abs(dy) > Math.abs(dx)) active = false;"
                + " }, { passive: true, capture: true });"
                + " document.addEventListener('touchend', function(e){"
                + "   if (!active) return;"
                + "   active = false;"
                + "   var t = e.changedTouches && e.changedTouches[0];"
                + "   if (!t) return;"
                + "   var dx = t.clientX - sx, dy = t.clientY - sy;"
                + "   if (Date.now() - st > 900) return;"
                + "   if (Math.abs(dx) < 80 || Math.abs(dy) > 40) return;"
                + "   report(dx > 0 ? 'swb' : 'swf');"
                + " }, { passive: true, capture: true });"
                + " document.addEventListener('touchcancel', function(){ active = false; },"
                + "   { passive: true, capture: true });"
                + " document.addEventListener('wheel', function(e){"
                + "   var now = Date.now();"
                + "   if (now < wlock) return;"
                + "   if (document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement) { wx=0; wc=0; return; }"
                + "   if (e.ctrlKey || e.shiftKey || e.altKey || e.metaKey) { wx=0; wc=0; return; }"
                + "   if ((e.deltaMode || 0) !== 0) { wx=0; wc=0; return; }"
                + "   var dx = e.deltaX || 0, dy = e.deltaY || 0;"
                + "   if (Math.abs(dx) < 2 || Math.abs(dx) <= Math.abs(dy) * 1.2) { wx=0; wc=0; return; }"
                + "   if (skipTarget(e.target) || canScrollX(e.target) || docScrollableX()) { wx=0; wc=0; return; }"
                + "   if (now - wt > 350) { wx=0; wc=0; }"
                + "   if ((wx > 0 && dx < 0) || (wx < 0 && dx > 0)) { wx=0; wc=0; }"
                + "   wt = now; wx += dx; wc++;"
                + "   if (Math.abs(wx) >= 120 && wc >= 2) {"
                + "     var back = wx < 0;"   // Chromium 横向过滚：负 deltaX 为后退
                + "     wx=0; wc=0; wlock = now + 700;"
                + "     report(back ? 'swb' : 'swf');"
                + "   }"
                + " }, { passive: true, capture: true });"
                + "})();";
        }
    }
}
