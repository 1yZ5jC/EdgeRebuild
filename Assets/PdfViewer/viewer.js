/* 自研 PDF 查看器逻辑：基于 pdfjs-dist 4.x 的 PDFViewer 组件 */

// 动态加载，失败时状态栏显示原因（启动时已清 WebView2 缓存，无需 ?v=）
var pdfjsLib = await Promise.race([
  import("./lib/pdf.min.js"),
  new Promise(function (r) { setTimeout(function () { r("timeout"); }, 8000); })
]).catch(function (e) {
  document.getElementById("statusText").textContent = "加载核心失败：" + e.message;
  return null;
});
if (pdfjsLib === "timeout") {
  document.getElementById("statusText").textContent = "加载核心超时（8s 未完成）";
  pdfjsLib = null;
}

// web 打包版 pdf_viewer.js 从 globalThis.pdfjsLib 解构核心 API，
// 必须先挂全局再加载组件模块。
globalThis.pdfjsLib = pdfjsLib;
var pdfjsViewer = await Promise.race([
  import("./lib/pdf_viewer.js"),
  new Promise(function (r) { setTimeout(function () { r("timeout"); }, 8000); })
]).catch(function (e) {
  document.getElementById("statusText").textContent = "加载组件失败：" + e.message;
  return null;
});
if (pdfjsViewer === "timeout") {
  document.getElementById("statusText").textContent = "加载组件超时（8s 未完成）";
  pdfjsViewer = null;
}

(function () {
  "use strict";

  var statusText = document.getElementById("statusText");

  // ---- 调试日志：全部阶段写入状态栏与调试面板（双击状态栏展开） ----
  function dbg(msg) {
    var t = new Date().toTimeString().split(" ")[0];
    var el = document.getElementById("debugLog");
    if (el) {
      el.textContent = (el.textContent ? el.textContent + "\n" : "") + t + " " + msg;
      if (el.textContent.split("\n").length > 60) {
        var lines = el.textContent.split("\n");
        lines.shift();
        el.textContent = lines.join("\n");
      }
    }
    if (statusText) statusText.textContent = msg;
  }
  document.getElementById("statusBar").addEventListener("dblclick", function () {
    var p = document.getElementById("debugPanel");
    p.classList.toggle("hidden");
  });
  document.getElementById("debugCopyBtn").addEventListener("click", function () {
    var text = document.getElementById("debugLog").textContent;
    var ta = document.createElement("textarea");
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand("copy"); } catch (e) { }
    document.body.removeChild(ta);
    statusText.textContent = "日志已复制";
  });

  // ---- 复制兜底（WebView2 禁用浏览器加速键时 Ctrl+C 不复制选区） ----
  document.addEventListener("keydown", function (e) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "c") {
      var sel = document.getSelection();
      var txt = sel ? sel.toString() : "";
      if (txt) {
        e.preventDefault();
        var ta = document.createElement("textarea");
        ta.value = txt;
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        try { document.execCommand("copy"); } catch (err) { }
        document.body.removeChild(ta);
      }
    }
  }, true);
  document.addEventListener("copy", function (e) {
    var sel = document.getSelection();
    var txt = sel ? sel.toString() : "";
    if (txt) {
      e.clipboardData.setData("text/plain", txt);
      e.preventDefault();
    }
  }, true);

  dbg("viewer.js 执行");

  window.onerror = function (msg, src, line, col, err) {
    var extra = "";
    if (err && err.stack) {
      var frames = err.stack.split("\n");
      extra = " @" + (frames[1] || frames[0] || "").trim();
    }
    dbg("脚本错误: " + msg + " [src=" + src + " 行" + line + "]" + extra);
  };

  window.addEventListener("unhandledrejection", function (e) {
    dbg("未处理拒绝: " + (e.reason && e.reason.message ? e.reason.message : e.reason));
  });

  if (!pdfjsLib || !pdfjsViewer) return;

  pdfjsLib.GlobalWorkerOptions.workerSrc = "lib/pdf.worker.min.js";

  var fileParam = new URLSearchParams(location.search).get("file");
  if (!fileParam) {
    document.getElementById("statusText").textContent = "缺少 file 参数";
    return;
  }

  var container = document.getElementById("mainContainer");
  var viewerEl = document.getElementById("pdfViewer");
  var pageNumInput = document.getElementById("pageNum");
  var numPagesLabel = document.getElementById("numPages");
  var zoomLabel = document.getElementById("zoomLabel");
  var sidebar = document.getElementById("sidebar");
  var thumbnailsEl = document.getElementById("thumbnails");
  var outlineEl = document.getElementById("outline");
  var outlineHeader = document.getElementById("outlineHeader");

  var eventBus = null, linkService = null, pdfViewer = null;
  try {
    eventBus = new pdfjsViewer.EventBus();
    linkService = new pdfjsViewer.PDFLinkService({ eventBus: eventBus });
    pdfViewer = new pdfjsViewer.PDFViewer({
      container: container,
      viewer: viewerEl,
      linkService: linkService,
      eventBus: eventBus,
      annotationEditorMode: pdfjsLib.AnnotationEditorType.HIGHLIGHT,
      annotationEditorHighlightColors: "yellow=#FFFF00,green=#008000,blue=#0000FF,pink=#FF00FF,red=#FF0000"
    });
    linkService.setViewer(pdfViewer);
    dbg("初始化完成");
  } catch (e) {
    dbg("初始化失败: " + (e && e.message ? e.message : e)
      + " @" + (e && e.stack ? e.stack.split("\n")[1] : "").trim());
    return;
  }

  var pdfDocument = null;
  var currentPage = 1;
  var currentScale = 1.0;
  var findController = null;

  // ---- 事件 ----
  eventBus.on("pagesinit", function () {
    dbg("pagesinit: 页面结构就绪");
    pdfViewer.currentScale = 1;
    updateZoomLabel();
    if (pdfDocument) {
      loadOutline();
      loadThumbnails();
      statusText.textContent = pdfDocument.numPages + " 页";
    }
  });

  eventBus.on("pagechanging", function (evt) {
    currentPage = evt.pageNumber;
    pageNumInput.value = currentPage;
  });

  eventBus.on("updatefindmatchescount", function (evt) {
    var total = evt.matchesCount.total;
    var current = evt.matchesCount.current;
    document.getElementById("searchCount").textContent =
      total > 0 ? (current + 1) + " / " + total : "无结果";
  });

  function updateZoomLabel() {
    zoomLabel.textContent = Math.round(currentScale * 100) + "%";
  }

  function zoomBy(factor) {
    currentScale = Math.min(4, Math.max(0.25, currentScale * factor));
    pdfViewer.currentScale = currentScale;
    updateZoomLabel();
  }

  function goToPage(n) {
    if (!pdfDocument) return;
    n = Math.max(1, Math.min(pdfDocument.numPages, n | 0));
    pdfViewer.currentPageNumber = n;
  }

  // ---- 加载文档 ----
  var docLoading = true;
  setTimeout(function () {
    if (docLoading) statusText.textContent = "加载文档超时（网络或渲染 worker 问题）";
  }, 20000);

  dbg("getDocument 开始: " + fileParam);

  pdfjsLib.getDocument({
    url: fileParam,
    cMapUrl: "lib/cmaps/",
    cMapPacked: true
  }).promise.then(function (doc) {
    docLoading = false;
    pdfDocument = doc;
    dbg("getDocument 成功: " + doc.numPages + " 页");
    pdfViewer.setDocument(doc);
    linkService.setDocument(doc, null);
    numPagesLabel.textContent = "/ " + doc.numPages;
    document.title = doc.title || "PDF 查看器";
    statusText.textContent = "第 " + 1 + " / " + doc.numPages + " 页";

    pdfViewer.onePageRendered.then(function () {
      try {
        pdfViewer.annotationEditorMode = { mode: pdfjsLib.AnnotationEditorType.NONE };
      } catch (e) { }
      for (var k in annotBtns) annotBtns[k].disabled = false;
    });

    if (pdfjsViewer.PDFFindController) {
      findController = new pdfjsViewer.PDFFindController({
        linkService: linkService,
        eventBus: eventBus
      });
      pdfViewer.findController = findController;
      try { findController.setDocument(doc); } catch (e) { }
    }
  }).catch(function (err) {
    dbg("打开文档失败: " + (err && err.message ? err.message : err));
    statusText.textContent = "打开文档失败：" + (err && err.message ? err.message : err);
  });

  // ---- 工具栏 ----
  document.getElementById("prevBtn").addEventListener("click", function () {
    eventBus.dispatch("previouspage");
  });
  document.getElementById("nextBtn").addEventListener("click", function () {
    eventBus.dispatch("nextpage");
  });
  pageNumInput.addEventListener("keydown", function (e) {
    if (e.key === "Enter") goToPage(parseInt(pageNumInput.value, 10));
  });
  pageNumInput.addEventListener("blur", function () {
    pageNumInput.value = currentPage;
  });
  document.getElementById("zoomInBtn").addEventListener("click", function () { zoomBy(1.25); });
  document.getElementById("zoomOutBtn").addEventListener("click", function () { zoomBy(0.8); });

  document.getElementById("rotateBtn").addEventListener("click", function () {
    pdfViewer.pagesRotation = (pdfViewer.pagesRotation + 90) % 360;
    dbg("旋转: " + pdfViewer.pagesRotation + "°");
  });
  document.getElementById("fitPageBtn").addEventListener("click", function () {
    pdfViewer.currentScaleValue = "page-fit";
    currentScale = pdfViewer.currentScale;
    updateZoomLabel();
  });

  document.getElementById("saveBtn").addEventListener("click", function () {
    if (!pdfDocument || !pdfDocument.saveDocument) {
      statusText.textContent = "保存失败：文档未就绪";
      return;
    }
    statusText.textContent = "正在保存…";
    pdfDocument.saveDocument().then(function (data) {
      var blob = new Blob([data], { type: "application/pdf" });
      var a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = "annotated.pdf";
      a.click();
      statusText.textContent = "已保存（含批注）";
    }).catch(function (err) {
      statusText.textContent = "保存失败：" + (err && err.message ? err.message : err);
    });
  });
  document.getElementById("fullscreenBtn").addEventListener("click", function () {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen();
  });

  // ---- 批注（annotation editor） ----
  var annotBtns = {
    cursor: document.getElementById("cursorBtn"),
    highlight: document.getElementById("highlightBtn"),
    freetext: document.getElementById("freetextBtn"),
    ink: document.getElementById("inkBtn")
  };
  for (var k in annotBtns) annotBtns[k].disabled = true;

  // ---- 画笔颜色/粗细选择（经 switchannotationeditorparams 事件） ----
  var colorBar = document.getElementById("colorBar");
  var colorDots = document.querySelectorAll(".color-dot");
  var thickDots = document.querySelectorAll(".thick-dot");
  var currentInkColor = null;
  var currentInkThickness = null;
  function dispatchInkParam(type, value) {
    if (pdfDocument) {
      eventBus.dispatch("switchannotationeditorparams", { type: type, value: value });
    }
  }
  function selectInkColor(color) {
    currentInkColor = color;
    for (var i = 0; i < colorDots.length; i++) {
      colorDots[i].classList.toggle("selected", colorDots[i].getAttribute("data-color") === color);
    }
    dispatchInkParam(pdfjsLib.AnnotationEditorParamsType.INK_COLOR, color);
  }
  function selectInkThickness(t) {
    currentInkThickness = t;
    for (var i = 0; i < thickDots.length; i++) {
      thickDots[i].classList.toggle("selected", parseInt(thickDots[i].getAttribute("data-thick"), 10) === t);
    }
    dispatchInkParam(pdfjsLib.AnnotationEditorParamsType.INK_THICKNESS, t);
  }
  for (var i = 0; i < colorDots.length; i++) {
    colorDots[i].style.background = colorDots[i].getAttribute("data-color");
    (function (dot) {
      dot.addEventListener("click", function () { selectInkColor(dot.getAttribute("data-color")); });
    })(colorDots[i]);
  }
  for (var i = 0; i < thickDots.length; i++) {
    (function (dot) {
      dot.addEventListener("click", function () { selectInkThickness(parseInt(dot.getAttribute("data-thick"), 10)); });
    })(thickDots[i]);
  }

  function setAnnotateTool(mode, activeBtn) {
    if (!pdfDocument) return;
    try {
      pdfViewer.annotationEditorMode = { mode: mode };
      dbg("模式切换成功: mode=" + mode);
      for (var k in annotBtns) annotBtns[k].classList.remove("active");
      if (activeBtn) activeBtn.classList.add("active");
      colorBar.classList.toggle("hidden", mode !== pdfjsLib.AnnotationEditorType.INK);
      if (mode === pdfjsLib.AnnotationEditorType.INK) {
        if (!currentInkColor) selectInkColor("#E11D48");
        if (!currentInkThickness) selectInkThickness(2);
      }
    } catch (e) {
      var errMsg = "批注不可用：" + (e && e.message ? e.message : e)
        + " | type=" + JSON.stringify(pdfjsLib.AnnotationEditorType)
        + " | mode=" + mode;
      dbg(errMsg);
      statusText.textContent = errMsg;
    }
  }

  annotBtns.cursor.addEventListener("click", function () { setAnnotateTool(pdfjsLib.AnnotationEditorType.NONE, annotBtns.cursor); });
  annotBtns.highlight.addEventListener("click", function () { setAnnotateTool(pdfjsLib.AnnotationEditorType.HIGHLIGHT, annotBtns.highlight); });
  annotBtns.freetext.addEventListener("click", function () { setAnnotateTool(pdfjsLib.AnnotationEditorType.FREETEXT, annotBtns.freetext); });
  annotBtns.ink.addEventListener("click", function () { setAnnotateTool(pdfjsLib.AnnotationEditorType.INK, annotBtns.ink); });

  // ---- 搜索 ----
  var searchBox = document.getElementById("searchBox");
  var searchInput = document.getElementById("searchInput");
  var lastFind = null;
  function findInPage(extra) {
    if (!findController) return;
    if (!lastFind) {
      var q = searchInput.value;
      if (!q) return;
      lastFind = { query: q, phraseSearch: true, caseSensitive: false, entireWord: false, highlightAll: true };
    }
    eventBus.dispatch("find", Object.assign({}, lastFind, extra));
  }
  document.getElementById("searchBtn").addEventListener("click", function () {
    searchBox.classList.toggle("hidden");
    if (!searchBox.classList.contains("hidden")) searchInput.focus();
  });
  searchInput.addEventListener("keydown", function (e) {
    if (e.key === "Enter") {
      lastFind = null;
      findInPage({ findPrevious: e.shiftKey });
    }
  });
  document.getElementById("searchNext").addEventListener("click", function () {
    findInPage({ type: "again", findPrevious: false });
  });
  document.getElementById("searchPrev").addEventListener("click", function () {
    findInPage({ type: "again", findPrevious: true });
  });

  // ---- 侧栏：缩略图 ----
  document.getElementById("sidebarToggle").addEventListener("click", function () {
    sidebar.classList.toggle("hidden");
  });

  function loadThumbnails() {
    thumbnailsEl.innerHTML = "";
    for (var p = 1; p <= pdfDocument.numPages; p++) {
      (function (pageNum) {
        pdfDocument.getPage(pageNum).then(function (page) {
          var vp = page.getViewport({ scale: 1 });
          var div = document.createElement("div");
          div.className = "thumbnail";
          div.innerHTML = '<div class="page"></div><div class="tnum">' + pageNum + "</div>";
          var pageDiv = div.querySelector(".page");
          var ratio = Math.min(1, 180 / vp.width);
          pageDiv.style.height = (vp.height * ratio) + "px";
          var canvas = document.createElement("canvas");
          pageDiv.appendChild(canvas);
          var cw = vp.width * ratio, ch = vp.height * ratio;
          canvas.width = cw; canvas.height = ch;
          page.render({
            canvasContext: canvas.getContext("2d"),
            viewport: page.getViewport({ scale: ratio }),
            intent: "print"
          }).promise.then(function () {
            dbg("缩略图 " + pageNum + " 渲染完成");
          }).catch(function (err) {
            if (err && err.name === "RenderingCancelledException") return;
            dbg("缩略图 " + pageNum + " 失败: " + (err && err.message ? err.message : err));
          });
          div.addEventListener("click", function () { goToPage(pageNum); });
          thumbnailsEl.appendChild(div);
        });
      })(p);
    }
  }

  // ---- 侧栏：目录 ----
  function loadOutline() {
    pdfDocument.getOutline().then(function (outline) {
      if (!outline || outline.length === 0) return;
      outlineHeader.classList.remove("hidden");
      outlineEl.appendChild(buildOutlineTree(outline));
    });
  }

  function buildOutlineTree(items) {
    var ul = document.createElement("ul");
    items.forEach(function (item) {
      var li = document.createElement("li");
      li.textContent = item.title;
      li.addEventListener("click", function () {
        linkService.navigateTo(item.dest);
      });
      if (item.items && item.items.length) li.appendChild(buildOutlineTree(item.items));
      ul.appendChild(li);
    });
    return ul;
  }

  // ---- 键盘 ----
  document.addEventListener("keydown", function (e) {
    if (e.target === searchInput) return;
    if (e.key === "ArrowLeft" || e.key === "PageUp") { eventBus.dispatch("previouspage"); e.preventDefault(); }
    else if (e.key === "ArrowRight" || e.key === "PageDown") { eventBus.dispatch("nextpage"); e.preventDefault(); }
    else if (e.key === "Home") goToPage(1);
    else if (e.key === "End") goToPage(pdfDocument ? pdfDocument.numPages : 1);
  });

  window.goToPage = goToPage;
})();
