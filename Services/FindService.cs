using System.Text.Json;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 页内查找（双引擎共用）：生成查找注入脚本 + 解析返回结果。
    /// 逻辑与主窗口原实现一致（MainPage 已改为委托本服务）。
    /// </summary>
    internal static class FindService
    {
        public static EdgeRebuild.FindResult ParseFindResult(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string json = raw.Trim();
            if (json.StartsWith("\""))
            {
                try { json = JsonSerializer.Deserialize(json, AppJsonContext.Default.String); }
                catch { return null; }
            }
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonSerializer.Deserialize(json, AppJsonContext.Default.FindResult); }
            catch { return null; }
        }

        public static string BuildFindScript(string query, string mode)
        {
            string q = JsonSerializer.Serialize(query, AppJsonContext.Default.String);
            string m = JsonSerializer.Serialize(mode, AppJsonContext.Default.String);
            return $@"
;(function(){{
    if (typeof document === 'undefined') return '{{""count"":0,""index"":-1}}';
    if (!document.getElementById('edg-find-style')) {{
        var st = document.createElement('style');
        st.id = 'edg-find-style';
        st.textContent = 'mark.edg-find{{background:#ffe58f;color:inherit;padding:0;}} mark.edg-find-current{{background:#ff9632;}}';
        (document.head || document.documentElement).appendChild(st);
    }}
    var query = {q};
    var mode = {m};
    var state = window.__edgFindState || null;
    function clearMarks(ms) {{
        for (var i = ms.length - 1; i >= 0; i--) {{
            var m = ms[i];
            if (m.parentNode) m.parentNode.replaceChild(document.createTextNode(m.textContent), m);
        }}
    }}
    if (!query) {{
        if (state) {{ clearMarks(state.marks); window.__edgFindState = null; }}
        return '{{""count"":0,""index"":-1}}';
    }}
    // 同 query 的上/下一个：不重扫 DOM，只移动当前高亮（页面 SPA 重渲染不会破坏导航）
    if (state && state.query === query && mode !== 'new') {{
        var live = [];
        for (var a = 0; a < state.marks.length; a++) if (state.marks[a].parentNode) live.push(state.marks[a]);
        if (live.length > 0) {{
            var idx = (state.index + (mode === 'next' ? 1 : live.length - 1)) % live.length;
            for (var b = 0; b < live.length; b++) live[b].className = b === idx ? 'edg-find edg-find-current' : 'edg-find';
            var cur = live[idx];
            try {{ cur.scrollIntoView({{ block: 'center' }}); }} catch (e) {{ cur.scrollIntoView(true); }}
            state.index = idx;
            state.marks = live;
            return '{{""count"":' + live.length + ',""index"":' + idx + '}}';
        }}
    }}
    // 全量重扫（query 变化或状态失效）
    if (state) clearMarks(state.marks);
    window.__edgFindState = null;
    var olds = document.querySelectorAll('mark.edg-find');
    for (var i = olds.length - 1; i >= 0; i--) {{
        var p = olds[i].parentNode;
        p.replaceChild(document.createTextNode(olds[i].textContent), olds[i]);
    }}
    if (document.body) document.body.normalize();
    var rx;
    try {{ rx = new RegExp(query.replace(/[.*+?^${{}}()|[\]\\]/g, '\\$&'), 'gi'); }}
    catch (e) {{ return '{{""count"":0,""index"":-1}}'; }}
    var hits = [];
    var visited = 0;
    function scan(root) {{
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {{
            acceptNode: function (node) {{
                var tag = node.parentNode ? node.parentNode.tagName : '';
                if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT' || tag === 'MARK') return NodeFilter.FILTER_REJECT;
                return NodeFilter.FILTER_ACCEPT;
            }}
        }});
        var nd;
        while ((nd = walker.nextNode()) !== null) {{
            visited++;
            var text = nd.nodeValue;
            if (!text || !text.trim()) continue;
            rx.lastIndex = 0;
            var mm;
            while ((mm = rx.exec(text)) !== null) {{
                hits.push({{ node: nd, start: mm.index, end: mm.index + mm[0].length }});
                if (mm[0].length === 0) {{ rx.lastIndex = mm.index + 1; break; }}
            }}
        }}
    }}
    scan(document.documentElement);
    var all = document.querySelectorAll('*');
    for (var j = 0; j < all.length; j++) {{
        if (all[j].shadowRoot) scan(all[j].shadowRoot);
    }}
    var marks = [];
    for (var k = hits.length - 1; k >= 0; k--) {{
        var h = hits[k];
        try {{
            var range = document.createRange();
            range.setStart(h.node, h.start);
            range.setEnd(h.node, h.end);
            var mark = document.createElement('mark');
            mark.className = 'edg-find';
            range.surroundContents(mark);
            marks.unshift(mark);
        }} catch (e) {{}}
    }}
    var count = marks.length;
    if (count === 0) {{
        var bt = document.body && document.body.innerText ? document.body.innerText : '';
        var sample = bt.replace(/\s+/g, ' ').substr(0, 60).replace(/\\/g, '\\\\').replace(/""/g, '\\""');
        return '{{""count"":0,""index"":-1,""nodes"":' + visited + ',""textLen"":' + bt.length + ',""sample"":""' + sample + '""}}';
    }}
    var idx2 = 0;
    if (mode === 'next') idx2 = 1 % count;
    else if (mode === 'prev') idx2 = (count - 1) % count;
    window.__edgFindState = {{ query: query, marks: marks, index: idx2 }};
    var cur2 = marks[idx2];
    try {{ cur2.scrollIntoView({{ block: 'center' }}); }} catch (e) {{ cur2.scrollIntoView(true); }}
    cur2.className = 'edg-find edg-find-current';
    return '{{""count"":' + count + ',""index"":' + idx2 + '}}';
}})();";
        }
    }
}
