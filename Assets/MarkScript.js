// 网页文本高亮标记脚本（双引擎共用，注入执行）。
// 与宿主通信：WebView2 用 window.chrome.webview.postMessage('edgmark:remove:<id>')；
// EdgeHTML 用 window.__edgKeyMarker（宿主轮询读取，与页面快捷键共用通道）。
(function (global) {
    if (global.EdgMarks) return;
    var EdgMarks = {};

    // 计算节点的 XPath（仅支持 nodeName[index] 链，文本节点为 text()[n]）
    function xpathOf(node) {
        var parts = [];
        while (node && node.nodeType === 3) {
            var p = node.parentNode;
            if (!p) return null;
            var idx = 1, sib = p.firstChild;
            while (sib && sib !== node) { if (sib.nodeType === 3) idx++; sib = sib.nextSibling; }
            parts.unshift('text()[' + idx + ']');
            node = p;
        }
        while (node && node.nodeType === 1) {
            var el = node, idx = 1, sib = el.previousSibling;
            while (sib) {
                if (sib.nodeType === 1 && sib.nodeName === el.nodeName) idx++;
                sib = sib.previousSibling;
            }
            parts.unshift(el.nodeName.toLowerCase() + '[' + idx + ']');
            node = el.parentNode;
        }
        if (parts.length === 0) return null;
        return '/' + parts.join('/');
    }

    // 简易 XPath 求值：/div[2]/p[1]/text()[1]
    function findByXPath(xp) {
        var parts = String(xp).split('/').filter(Boolean);
        var node = document;
        for (var i = 0; i < parts.length; i++) {
            var m = parts[i].match(/^([a-z0-9]+)\[(\d+)\]$/);
            if (!m) return null;
            var name = m[1], idx = parseInt(m[2], 10);
            if (name === 'text') {
                if (!node || node.nodeType !== 1) return null;
                var cnt = 0, found = null;
                for (var c = node.firstChild; c; c = c.nextSibling) {
                    if (c.nodeType === 3) { cnt++; if (cnt === idx) found = c; }
                }
                node = found;
            } else {
                if (!node || node.nodeType !== 1) return null;
                var seen = 0, match = null, kids = node.children;
                for (var j = 0; j < kids.length; j++) {
                    if (kids[j].nodeName.toLowerCase() === name) { seen++; if (seen === idx) { match = kids[j]; break; } }
                }
                node = match;
            }
            if (!node) return null;
        }
        return node;
    }

    // 获取当前选区锚点（起始文本节点 XPath + 偏移 + 文本）
    EdgMarks.getSelectionMark = function () {
        var s = window.getSelection();
        if (!s || s.isCollapsed || s.rangeCount === 0) return 'null';
        var r = s.getRangeAt(0);
        var node = r.startContainer;
        if (!node) return 'null';
        var xp = xpathOf(node);
        if (!xp) return 'null';
        return JSON.stringify({ xpath: xp, offset: r.startOffset, text: s.toString() });
    };

    // 应用单个标记，成功返回 '1'，失败返回 '0'
    EdgMarks.applyMark = function (id, xp, offset, text, color) {
        var tnode = findByXPath(xp);
        if (!tnode || tnode.nodeType !== 3) return '0';
        if (offset < 0 || offset >= tnode.textContent.length) return '0';
        var seg = tnode.textContent.substring(offset, offset + text.length);
        if (seg !== text) return '0';
        var mark = document.createElement('mark');
        mark.className = 'edg-mark';
        mark.setAttribute('data-mark-id', String(id));
        mark.style.background = color;
        mark.style.color = '#000000';
        mark.style.padding = '0 1px';
        mark.style.borderRadius = '2px';
        mark.style.cursor = 'pointer';
        var tail = tnode.splitText(offset);
        tail.splitText(text.length);
        tail.parentNode.insertBefore(mark, tail);
        mark.appendChild(tail);
        return '1';
    };

    // 批量恢复（参数为 JSON 数组字符串），返回成功数量
    EdgMarks.applyMarks = function (json) {
        var list;
        try { list = JSON.parse(json); } catch (e) { return '0'; }
        if (!list) return '0';
        var ok = 0;
        for (var i = 0; i < list.length; i++) {
            var m = list[i];
            if (EdgMarks.applyMark(m.Id, m.XPath, m.Offset, m.Text, m.Color) === '1') ok++;
        }
        return String(ok);
    };

    // 移除页面全部标记
    EdgMarks.clearMarks = function () {
        var marks = document.querySelectorAll('.edg-mark');
        for (var i = marks.length - 1; i >= 0; i--) marks[i].remove();
        return String(marks.length);
    };

    // 点击标记 → 移除 DOM + 通知宿主清理 DB（每页只装一次）
    EdgMarks.initClick = function () {
        if (window.__edgMarkClickInit) return;
        window.__edgMarkClickInit = true;
        document.addEventListener('click', function (e) {
            var t = e.target;
            var m = (t && t.closest) ? t.closest('.edg-mark') : null;
            if (!m) return;
            var id = m.getAttribute('data-mark-id');
            if (id) {
                try { window.chrome.webview.postMessage('edgmark:remove:' + id); }
                catch (err) {
                    try { window.__edgKeyMarker = 'mark:remove:' + id; } catch (err2) { }
                }
                m.remove();
            }
        }, true);
        return '1';
    };

    global.EdgMarks = EdgMarks;
    return '1';
})(window);
