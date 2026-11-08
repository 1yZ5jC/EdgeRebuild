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

  // ---- 调试日志：只写调试面板（双击状态栏展开），避免覆盖页码等状态 ----
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
    if (statusText) statusText.textContent = "脚本错误：" + msg;
  };

  window.addEventListener("unhandledrejection", function (e) {
    dbg("未处理拒绝: " + (e.reason && e.reason.message ? e.reason.message : e.reason));
  });

  if (!pdfjsLib || !pdfjsViewer) return;

  pdfjsLib.GlobalWorkerOptions.workerSrc = "lib/pdf.worker.min.js";

  var params = new URLSearchParams(location.search);
  var fileParam = params.get("file");
  if (!fileParam) {
    document.getElementById("statusText").textContent = "缺少 file 参数";
    return;
  }

  var SpreadMode = pdfjsViewer.SpreadMode;
  var ScrollMode = pdfjsViewer.ScrollMode;
  var FindState = pdfjsViewer.FindState;

  // ---- DOM 引用 ----
  var container = document.getElementById("mainContainer");
  var viewerEl = document.getElementById("pdfViewer");
  var pageNumInput = document.getElementById("pageNum");
  var numPagesLabel = document.getElementById("numPages");
  var sidebar = document.getElementById("sidebar");
  var thumbnailsEl = document.getElementById("thumbnails");
  var outlineEl = document.getElementById("outline");
  var outlineHeader = document.getElementById("outlineHeader");
  var sidebarToggle = document.getElementById("sidebarToggle");
  var searchBtn = document.getElementById("searchBtn");

  var findBar = document.getElementById("findBar");
  var searchInput = document.getElementById("searchInput");
  var searchCount = document.getElementById("searchCount");
  var searchPrevBtn = document.getElementById("searchPrev");
  var searchNextBtn = document.getElementById("searchNext");
  var searchCloseBtn = document.getElementById("searchClose");

  var normalTools = document.getElementById("normalTools");
  var inkTools = document.getElementById("inkTools");
  var addNotesBtn = document.getElementById("addNotesBtn");
  var saveBtn = document.getElementById("saveBtn");
  var saveAsBtn = document.getElementById("saveAsBtn");
  var annotSaveBtn = document.getElementById("annotSaveBtn");
  var pinBtn = document.getElementById("pinBtn");
  var zoomInBtn = document.getElementById("zoomInBtn");
  var zoomOutBtn = document.getElementById("zoomOutBtn");
  var rotateBtn = document.getElementById("rotateBtn");
  var fullscreenBtn = document.getElementById("fullscreenBtn");
  var fitToggleBtn = document.getElementById("fitToggleBtn");
  var fitLabel = document.getElementById("fitLabel");
  var fitIconPage = document.getElementById("fitIconPage");
  var fitIconWidth = document.getElementById("fitIconWidth");

  var layoutBtn = document.getElementById("layoutBtn");
  var layoutFlyout = document.getElementById("layoutFlyout");
  var layoutSingle = document.getElementById("layoutSingle");
  var layoutDouble = document.getElementById("layoutDouble");
  var continuousToggle = document.getElementById("continuousToggle");
  var continuousState = document.getElementById("continuousState");

  var penBtn = document.getElementById("penBtn");
  var penPalette = document.getElementById("penPalette");
  var highlightBtn = document.getElementById("highlightBtn");
  var freetextBtn = document.getElementById("freetextBtn");
  var eraserBtn = document.getElementById("eraserBtn");
  var exitInkBtn = document.getElementById("exitInkBtn");
  var thickSlider = document.getElementById("thickSlider");
  var thickPreview = document.getElementById("thickPreview");
  var colorDots = document.querySelectorAll(".color-dot");

  // ---- 全局布局与工具栏状态（跨文档持久化） ----
  var LAYOUT_KEY = "pdf_layout";
  var layout = loadLayout();
  var fitMode = layout.fit || "page-fit"; // "page-fit" | "page-width" | null（手动缩放）
  var spreadMode = layout.spread;
  var scrollMode = layout.scroll;
  var pinned = layout.pinned;

  function loadLayout() {
    var defaults = {
      fit: "page-fit",
      spread: SpreadMode.NONE,
      scroll: ScrollMode.VERTICAL,
      pinned: true
    };
    try {
      var raw = JSON.parse(localStorage.getItem(LAYOUT_KEY) || "null");
      if (!raw) return defaults;
      if (raw.fit === "page-fit" || raw.fit === "page-width") defaults.fit = raw.fit;
      // 旧版本可能存过 EVEN；UI 只提供“双页”，统一归一化为 ODD
      if (raw.spread === SpreadMode.NONE || raw.spread === SpreadMode.ODD || raw.spread === SpreadMode.EVEN) {
        defaults.spread = raw.spread === SpreadMode.EVEN ? SpreadMode.ODD : raw.spread;
      }
      if (raw.scroll === ScrollMode.VERTICAL || raw.scroll === ScrollMode.PAGE) {
        defaults.scroll = raw.scroll;
      }
      if (typeof raw.pinned === "boolean") defaults.pinned = raw.pinned;
    } catch (e) { }
    return defaults;
  }

  function saveLayout() {
    try {
      localStorage.setItem(LAYOUT_KEY, JSON.stringify({
        fit: fitMode,
        spread: spreadMode,
        scroll: scrollMode,
        pinned: pinned
      }));
    } catch (e) { }
  }

  // ---- 阅读位置与画笔设置记忆（按来源 URL 关联，与批注缓存回写配套） ----
  var posSrcUrl = null;
  try { posSrcUrl = params.get("src") || fileParam; } catch (e) { posSrcUrl = fileParam; }
  var posKey = "pdf_pos_" + posSrcUrl;
  var posSaveTimer = null;

  function loadPos() {
    try { return JSON.parse(localStorage.getItem(posKey) || "null"); } catch (e) { return null; }
  }

  function persistPos() {
    if (posSaveTimer) clearTimeout(posSaveTimer);
    posSaveTimer = setTimeout(function () {
      try {
        localStorage.setItem(posKey, JSON.stringify({
          page: pdfViewer ? pdfViewer.currentPageNumber : null,
          scale: pdfViewer ? pdfViewer.currentScale : null,
          fit: fitMode,
          color: currentInkColor || null,
          thickness: currentInkThickness || null,
          updated: Date.now()
        }));
      } catch (e) { }
    }, 800);
  }

  // ---- 创建 PDFViewer ----
  var eventBus = null, linkService = null, pdfViewer = null, findController = null;
  try {
    eventBus = new pdfjsViewer.EventBus();
    linkService = new pdfjsViewer.PDFLinkService({ eventBus: eventBus });
    if (pdfjsViewer.PDFFindController) {
      findController = new pdfjsViewer.PDFFindController({
        linkService: linkService,
        eventBus: eventBus
      });
    }
    pdfViewer = new pdfjsViewer.PDFViewer({
      container: container,
      viewer: viewerEl,
      linkService: linkService,
      eventBus: eventBus,
      findController: findController,
      annotationEditorMode: pdfjsLib.AnnotationEditorType.NONE,
      annotationEditorHighlightColors: "yellow=#FFFF00,green=#008000,blue=#0000FF,pink=#FF00FF,red=#FF0000",
      removePageBorders: true
    });
    linkService.setViewer(pdfViewer);
    dbg("初始化完成");
  } catch (e) {
    var initErr = "初始化失败: " + (e && e.message ? e.message : e)
      + " @" + (e && e.stack ? e.stack.split("\n")[1] : "").trim();
    dbg(initErr);
    statusText.textContent = initErr;
    return;
  }

  var pdfDocument = null;
  var currentPage = 1;
  var currentScale = 1.0;
  var findTimer = null;
  var lastFind = null;

  // AnnotationEditorUIManager 实例通过 eventBus 事件获取（PDFViewer 无公开 getter）
  var uiManager = null;
  eventBus.on("annotationeditoruimanager", function (evt) {
    uiManager = evt.uiManager;
    dbg("uiManager 就绪");
  });
  function getUiManager() {
    if (uiManager) return uiManager;
    try {
      return (pdfViewer && pdfViewer._layerProperties && pdfViewer._layerProperties.annotationEditorUIManager) || null;
    } catch (e) { return null; }
  }

  // ---- 文档按钮可用性 ----
  function setDocButtonsEnabled(on) {
    var list = [
      pageNumInput, searchBtn, sidebarToggle, fitToggleBtn, layoutBtn,
      zoomInBtn, zoomOutBtn, rotateBtn,
      saveBtn, saveAsBtn, annotSaveBtn,
      addNotesBtn, penBtn, highlightBtn, freetextBtn, eraserBtn, exitInkBtn
    ];
    for (var i = 0; i < list.length; i++) {
      if (list[i]) list[i].disabled = !on;
    }
  }
  setDocButtonsEnabled(false);

  // ---- 适应页面大小 / 适应宽度（两态循环，显示当前状态） ----
  function updateFitButton() {
    var isWidth = fitMode === "page-width";
    fitLabel.textContent = isWidth ? "适应宽度" : "适应页面大小";
    fitIconPage.classList.toggle("hidden", isWidth);
    fitIconWidth.classList.toggle("hidden", !isWidth);
    fitToggleBtn.title = isWidth
      ? "适应宽度（点击切换到适应页面大小，Ctrl+Shift+A）"
      : "适应页面大小（点击切换到适应宽度，Ctrl+Shift+A）";
    fitToggleBtn.setAttribute("aria-label", fitLabel.textContent + "，点击切换");
    document.body.classList.toggle("fit-page", fitMode === "page-fit");
  }

  function setFit(mode) {
    if (!pdfDocument) return;
    fitMode = mode;
    pdfViewer.currentScaleValue = mode;
    updateFitButton();
    saveLayout();
    persistPos();
  }

  function toggleFit() {
    if (!pdfDocument) return;
    setFit(fitMode === "page-fit" ? "page-width" : "page-fit");
  }

  // ---- 页面视图（单页/双页 + 连续滚动） ----
  function updateLayoutUI() {
    var isDouble = spreadMode === SpreadMode.ODD;
    layoutSingle.classList.toggle("active", !isDouble);
    layoutSingle.setAttribute("aria-pressed", isDouble ? "false" : "true");
    layoutDouble.classList.toggle("active", isDouble);
    layoutDouble.setAttribute("aria-pressed", isDouble ? "true" : "false");

    var continuous = scrollMode === ScrollMode.VERTICAL;
    continuousToggle.classList.toggle("on", continuous);
    continuousToggle.setAttribute("aria-checked", continuous ? "true" : "false");
    continuousState.textContent = continuous ? "开" : "关";
    document.body.classList.toggle("scroll-page", scrollMode === ScrollMode.PAGE);
  }

  function applyLayout() {
    if (!pdfDocument) return;
    try {
      pdfViewer.spreadMode = spreadMode;
      pdfViewer.scrollMode = scrollMode;
    } catch (e) {
      dbg("应用布局失败: " + (e && e.message ? e.message : e));
    }
    // 文档过大时 pdf.js 会强制单页滚动，回读真实值
    spreadMode = pdfViewer.spreadMode;
    scrollMode = pdfViewer.scrollMode;
    updateLayoutUI();
  }

  function setSpread(mode) {
    if (!pdfDocument) return;
    spreadMode = mode;
    pdfViewer.spreadMode = mode;
    spreadMode = pdfViewer.spreadMode;
    updateLayoutUI();
    saveLayout();
    persistPos();
  }

  function setContinuous(on) {
    if (!pdfDocument) return;
    var mode = on ? ScrollMode.VERTICAL : ScrollMode.PAGE;
    pdfViewer.scrollMode = mode;
    scrollMode = pdfViewer.scrollMode;
    updateLayoutUI();
    saveLayout();
    persistPos();
    if (scrollMode !== mode) {
      statusText.textContent = "文档页数过多，已自动改为单页滚动";
    }
  }

  // ---- 缩放 ----
  function setZoom(scale) {
    if (!pdfDocument) return;
    scale = Math.max(0.25, Math.min(5, Math.round(scale * 100) / 100));
    fitMode = null;
    pdfViewer.currentScale = scale;
    updateFitButton();
    persistPos();
  }

  function zoomBy(delta) {
    if (!pdfDocument) return;
    setZoom(pdfViewer.currentScale + delta);
  }

  // ---- 旋转 / 全屏 ----
  function rotate() {
    if (!pdfDocument) return;
    pdfViewer.pagesRotation = (pdfViewer.pagesRotation + 90) % 360;
    dbg("旋转: " + pdfViewer.pagesRotation + "°");
  }

  function toggleFullscreen() {
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(function () { });
    } else {
      document.documentElement.requestFullscreen().catch(function (e) {
        statusText.textContent = "无法进入全屏：" + (e && e.message ? e.message : e);
      });
    }
  }

  function updateFullscreenButton() {
    var fs = !!document.fullscreenElement;
    fullscreenBtn.textContent = fs ? "\uE73F" : "\uE740";
    fullscreenBtn.title = fs ? "退出全屏 (F11)" : "全屏 (F11)";
  }
  document.addEventListener("fullscreenchange", updateFullscreenButton);

  // ---- 工具栏固定 / 自动隐藏 ----
  function applyPinned() {
    document.body.classList.toggle("toolbar-unpinned", !pinned);
    pinBtn.textContent = pinned ? "\uE77A" : "\uE840";
    pinBtn.title = pinned ? "解锁工具栏（自动隐藏）" : "固定工具栏";
    pinBtn.classList.toggle("active", !pinned);
    pinBtn.setAttribute("aria-pressed", pinned ? "true" : "false");
  }
  applyPinned();

  // ---- 页码 ----
  function goToPage(n) {
    if (!pdfDocument) return;
    n = Math.max(1, Math.min(pdfDocument.numPages, n | 0));
    pdfViewer.currentPageNumber = n;
  }

  // ---- 事件 ----
  eventBus.on("pagesinit", function () {
    dbg("pagesinit: 页面结构就绪");
    applyLayout();
    restoreView();
    loadOutline();
    loadThumbnails();
    setDocButtonsEnabled(true);
    updateLayoutUI();
    updateFitButton();
  });

  eventBus.on("pagechanging", function (evt) {
    currentPage = evt.pageNumber;
    if (document.activeElement !== pageNumInput) pageNumInput.value = currentPage;
    if (!document.body.classList.contains("annotating") && !eraserMode) {
      statusText.textContent = "第 " + currentPage + " / " + (pdfDocument ? pdfDocument.numPages : "?") + " 页";
    }
    updateThumbnailSelection();
    persistPos();
  });

  eventBus.on("scalechanging", function (evt) {
    currentScale = evt.scale;
    if (evt.presetValue === "page-fit" || evt.presetValue === "page-width") {
      fitMode = evt.presetValue;
    } else if (evt.presetValue === undefined) {
      fitMode = null;
    }
    updateFitButton();
    persistPos();
  });

  eventBus.on("updatefindmatchescount", function (evt) {
    var total = evt.matchesCount.total;
    var current = evt.matchesCount.current;
    searchCount.textContent = total > 0 ? current + " / " + total : "无结果";
  });

  eventBus.on("updatefindcontrolstate", function (evt) {
    if (evt.state === FindState.NOT_FOUND) {
      searchCount.textContent = "无结果";
    }
  });

  function restoreView() {
    var saved = loadPos();
    if (saved && (saved.fit === "page-fit" || saved.fit === "page-width")) {
      fitMode = saved.fit;
    } else if (saved && saved.scale > 0) {
      fitMode = null;
    } else {
      fitMode = fitMode || "page-fit";
    }

    // 页码必须先于缩放：pdf.js 的 page-fit / page-width 按当前页尺寸计算，
    // 混合尺寸 PDF 若先算第 1 页再跳页，恢复后的贴合会不准。
    var hasPage = saved && saved.page >= 1 && pdfDocument && saved.page <= pdfDocument.numPages;
    pdfViewer.currentPageNumber = hasPage ? saved.page : 1;

    if (fitMode) {
      pdfViewer.currentScaleValue = fitMode;
    } else if (saved && saved.scale > 0) {
      pdfViewer.currentScale = saved.scale;
    } else {
      pdfViewer.currentScaleValue = "page-fit";
    }

    if (hasPage) {
      statusText.textContent = "已恢复上次位置：第 " + saved.page + " 页";
      dbg("恢复位置: page=" + saved.page + " scale=" + (saved.scale || 1) + " fit=" + (saved.fit || "custom"));
    } else if (pdfDocument) {
      statusText.textContent = "第 1 / " + pdfDocument.numPages + " 页";
    }

    if (saved && saved.color) selectInkColor(saved.color);
    if (saved && saved.thickness) selectInkThickness(saved.thickness);
    updateFitButton();
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
    numPagesLabel.textContent = "(共 " + doc.numPages + " 页)";
    document.title = doc.title || "PDF 查看器";
    statusText.textContent = "第 1 / " + doc.numPages + " 页";

    pdfViewer.onePageRendered.then(function () {
      if (!document.body.classList.contains("annotating")) {
        try {
          pdfViewer.annotationEditorMode = { mode: pdfjsLib.AnnotationEditorType.NONE };
        } catch (e) { }
      }
    });

  }).catch(function (err) {
    dbg("打开文档失败: " + (err && err.message ? err.message : err));
    statusText.textContent = "打开文档失败：" + (err && err.message ? err.message : err);
  });

  // ---- 阅读栏按钮 ----
  pageNumInput.addEventListener("keydown", function (e) {
    if (e.key === "Enter") {
      e.preventDefault();
      goToPage(parseInt(pageNumInput.value, 10));
      pageNumInput.blur();
    } else if (e.key === "Escape") {
      pageNumInput.value = currentPage;
      pageNumInput.blur();
    }
  });
  pageNumInput.addEventListener("focus", function () { pageNumInput.select(); });
  pageNumInput.addEventListener("blur", function () { pageNumInput.value = currentPage; });

  zoomInBtn.addEventListener("click", function () { zoomBy(0.1); });
  zoomOutBtn.addEventListener("click", function () { zoomBy(-0.1); });
  rotateBtn.addEventListener("click", rotate);
  fullscreenBtn.addEventListener("click", toggleFullscreen);
  fitToggleBtn.addEventListener("click", toggleFit);

  pinBtn.addEventListener("click", function () {
    pinned = !pinned;
    applyPinned();
    saveLayout();
  });

  saveBtn.addEventListener("click", function () { savePdf(false); });
  annotSaveBtn.addEventListener("click", function () { savePdf(false); });
  saveAsBtn.addEventListener("click", function () { savePdf(true); });

  // Ctrl+滚轮缩放（旧版 Edge 手感）
  container.addEventListener("wheel", function (e) {
    if (!e.ctrlKey) return;
    e.preventDefault();
    zoomBy(e.deltaY < 0 ? 0.1 : -0.1);
  }, { passive: false });

  // 窗口尺寸变化后重算适应模式
  var resizeTimer = null;
  window.addEventListener("resize", function () {
    if (resizeTimer) clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      if (pdfDocument && fitMode) pdfViewer.currentScaleValue = fitMode;
    }, 160);
  });

  // ---- 页面视图浮层 ----
  function closeFlyouts() {
    layoutFlyout.classList.add("hidden");
    layoutBtn.setAttribute("aria-expanded", "false");
    penPalette.classList.add("hidden");
  }

  layoutBtn.addEventListener("click", function (e) {
    e.stopPropagation();
    var opening = layoutFlyout.classList.contains("hidden");
    penPalette.classList.add("hidden");
    layoutFlyout.classList.toggle("hidden", !opening);
    layoutBtn.setAttribute("aria-expanded", opening ? "true" : "false");
  });

  layoutSingle.addEventListener("click", function () { setSpread(SpreadMode.NONE); });
  layoutDouble.addEventListener("click", function () { setSpread(SpreadMode.ODD); });
  continuousToggle.addEventListener("click", function () {
    setContinuous(scrollMode !== ScrollMode.VERTICAL);
  });

  document.addEventListener("pointerdown", function (e) {
    if (!e.target || !e.target.closest) return;
    if (e.target.closest(".flyout-wrap")) return;
    closeFlyouts();
  });

  // ---- 侧栏 ----
  sidebarToggle.addEventListener("click", function () {
    sidebar.classList.toggle("hidden");
    sidebarToggle.classList.toggle("active", !sidebar.classList.contains("hidden"));
    if (pdfDocument && fitMode) {
      // 侧栏改变可用宽度，重算适应页面/宽度
      setTimeout(function () { pdfViewer.currentScaleValue = fitMode; }, 0);
    }
  });

  // ---- 批注（annotation editor） ----
  var annotBtns = {
    addNotes: addNotesBtn,
    pen: penBtn,
    highlight: highlightBtn,
    freetext: freetextBtn,
    eraser: eraserBtn
  };
  var eraserMode = false;
  var annotMode = pdfjsLib.AnnotationEditorType.NONE;

  // ---- 画笔颜色/粗细选择（经 switchannotationeditorparams 事件，UWP InkToolbar 风格） ----
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
    if (thickPreview) thickPreview.style.background = color;
    dispatchInkParam(pdfjsLib.AnnotationEditorParamsType.INK_COLOR, color);
  }

  function selectInkThickness(t) {
    t = parseInt(t, 10);
    if (isNaN(t)) t = 2;
    currentInkThickness = t;
    thickSlider.value = t;
    thickSlider.style.setProperty("--fill", ((t - 1) / 9 * 100) + "%");
    var d = Math.max(4, t * 1.6);
    thickPreview.style.width = d + "px";
    thickPreview.style.height = d + "px";
    thickPreview.style.background = currentInkColor || "#333";
    dispatchInkParam(pdfjsLib.AnnotationEditorParamsType.INK_THICKNESS, t);
  }

  for (var ci = 0; ci < colorDots.length; ci++) {
    colorDots[ci].style.background = colorDots[ci].getAttribute("data-color");
    (function (dot) {
      dot.addEventListener("click", function () { selectInkColor(dot.getAttribute("data-color")); });
    })(colorDots[ci]);
  }
  thickSlider.addEventListener("input", function () { selectInkThickness(thickSlider.value); });

  function clearAnnotActive() {
    for (var k in annotBtns) {
      if (annotBtns[k]) annotBtns[k].classList.remove("active");
    }
  }

  function setAnnotateTool(mode, activeBtn) {
    if (!pdfDocument) return;
    if (eraserMode) disableEraser();
    try {
      pdfViewer.annotationEditorMode = { mode: mode };
      annotMode = mode;
      clearAnnotActive();
      if (activeBtn) activeBtn.classList.add("active");
      penPalette.classList.toggle("hidden", mode !== pdfjsLib.AnnotationEditorType.INK);
      if (mode === pdfjsLib.AnnotationEditorType.INK) {
        if (!currentInkColor) selectInkColor("#E11D48");
        if (!currentInkThickness) selectInkThickness(2);
      }
      dbg("批注模式: mode=" + mode);
    } catch (e) {
      var errMsg = "批注不可用：" + (e && e.message ? e.message : e);
      dbg(errMsg);
      statusText.textContent = errMsg;
    }
  }

  function enterAnnotateMode() {
    if (!pdfDocument) return;
    document.body.classList.add("annotating");
    normalTools.classList.add("hidden");
    inkTools.classList.remove("hidden");
    closeFindBar();
    closeFlyouts();
    setAnnotateTool(pdfjsLib.AnnotationEditorType.INK, penBtn);
    penPalette.classList.remove("hidden");
    statusText.textContent = "批注模式：选择工具后在页面上书写";
  }

  function exitAnnotateMode() {
    if (!pdfDocument) return;
    if (eraserMode) disableEraser();
    try { pdfViewer.annotationEditorMode = { mode: pdfjsLib.AnnotationEditorType.NONE }; } catch (e) { }
    annotMode = pdfjsLib.AnnotationEditorType.NONE;
    document.body.classList.remove("annotating");
    normalTools.classList.remove("hidden");
    inkTools.classList.add("hidden");
    penPalette.classList.add("hidden");
    clearAnnotActive();
    statusText.textContent = "第 " + currentPage + " / " + pdfDocument.numPages + " 页";
  }

  addNotesBtn.addEventListener("click", enterAnnotateMode);
  exitInkBtn.addEventListener("click", exitAnnotateMode);

  penBtn.addEventListener("click", function (e) {
    e.stopPropagation();
    if (annotMode !== pdfjsLib.AnnotationEditorType.INK || eraserMode) {
      setAnnotateTool(pdfjsLib.AnnotationEditorType.INK, penBtn);
      penPalette.classList.remove("hidden");
    } else {
      penPalette.classList.toggle("hidden");
    }
  });

  highlightBtn.addEventListener("click", function () {
    setAnnotateTool(pdfjsLib.AnnotationEditorType.HIGHLIGHT, highlightBtn);
  });
  freetextBtn.addEventListener("click", function () {
    setAnnotateTool(pdfjsLib.AnnotationEditorType.FREETEXT, freetextBtn);
  });

  // ---- 橡皮擦：点击批注即删除（pdf.js 4.x 无内置擦除工具） ----
  function disableEraser() {
    eraserMode = false;
    eraserBtn.classList.remove("active");
    container.classList.remove("eraser");
  }

  function setEraserMode(on) {
    if (!pdfDocument) return;
    if (on) {
      try { pdfViewer.annotationEditorMode = { mode: pdfjsLib.AnnotationEditorType.NONE }; } catch (e) { }
      annotMode = pdfjsLib.AnnotationEditorType.NONE;
      clearAnnotActive();
      eraserBtn.classList.add("active");
      penPalette.classList.add("hidden");
      statusText.textContent = "橡皮擦：点击批注将其删除";
    } else {
      eraserBtn.classList.remove("active");
      statusText.textContent = "批注模式：选择工具后在页面上书写";
    }
    eraserMode = on;
    container.classList.toggle("eraser", on);
    dbg("[ERASE] 橡皮擦模式=" + on + " | ui=" + (getUiManager() ? "存在" : "NULL"));
  }

  eraserBtn.addEventListener("click", function () {
    dbg("[ERASE] 橡皮擦按钮点击，当前 eraserMode=" + eraserMode);
    setEraserMode(!eraserMode);
  });

  // 捕获阶段处理：pointerdown 早于 pdf.js 的任何批注监听，点击即删。
  // 定位顺序：div.id -> getEditor(id)  |  包含匹配  |  坐标包围盒兜底
  container.addEventListener("pointerdown", function (e) {
    if (!eraserMode) return;
    var tgt = e.target;
    var cls = tgt && (typeof tgt.className === "string" ? tgt.className : (tgt.className && tgt.className.baseVal) || "");
    dbg("[ERASE] pointerdown target=" + (tgt ? tgt.tagName : "null") + "." + cls + " @(" + Math.round(e.clientX) + "," + Math.round(e.clientY) + ")");
    var ui = getUiManager();
    if (!ui) {
      dbg("[ERASE] FAIL: annotationEditorUIManager 为 NULL");
      return;
    }
    var el = tgt && tgt.closest ? tgt.closest(".inkEditor, .freeTextEditor, .highlightEditor, .stampEditor") : null;
    var editor = null;
    if (el && el.id) {
      editor = ui.getEditor(el.id);
      dbg("[ERASE] closest=" + (el ? el.tagName + "." + (typeof el.className === "string" ? el.className : el.className && el.className.baseVal) + " id=" + el.id : "null") + " -> getEditor=" + (editor ? "hit" : "miss"));
    }
    if (!editor && el) {
      var pageEl = el.closest(".page");
      var pageIndex = pageEl ? parseInt(pageEl.getAttribute("data-page-number") || "0", 10) - 1 : -1;
      dbg("[ERASE] contains 匹配 pageIndex=" + pageIndex + " (pagesCount=" + pdfViewer.pagesCount + ")");
      if (pageIndex >= 0) {
        var eds = ui.getEditors(pageIndex);
        dbg("[ERASE] 该页 editors=" + eds.length);
        for (var i = 0; i < eds.length; i++) {
          if (eds[i].div === el || eds[i].div.contains(el)) { editor = eds[i]; dbg("[ERASE] contains 命中 id=" + eds[i].div.id); break; }
        }
      }
    }
    if (!editor) {
      // 坐标兜底：批注可能被 annotationLayer 只读元素盖住，按包围盒命中
      for (var p = 0; p < pdfViewer.pagesCount; p++) {
        var list = ui.getEditors(p);
        for (var j = 0; j < list.length; j++) {
          var r = list[j].div.getBoundingClientRect();
          var inside = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
          dbg("[ERASE] coord p" + p + " #" + j + " id=" + list[j].div.id + " rect=(" + Math.round(r.left) + "," + Math.round(r.top) + ")-(" + Math.round(r.right) + "," + Math.round(r.bottom) + ") inside=" + inside);
          if (inside) editor = list[j];
        }
      }
      dbg("[ERASE] 坐标命中=" + (editor ? "YES " + editor.div.id : "NO"));
    }
    if (editor) {
      try {
        editor.remove();
        dbg("[ERASE] 已删除批注 id=" + editor.div.id);
      } catch (err2) {
        dbg("[ERASE] 删除抛异常: " + (err2 && err2.stack ? err2.stack : err2));
      }
      return;
    }
    dbg("[ERASE] 未命中任何批注");
  }, true);

  // ---- 保存 ----
  function buildSaveName() {
    var title = (document.title || "annotated").replace(/[\\/:*?"<>|]+/g, "_").trim();
    if (!title) title = "annotated";
    if (!/\.pdf$/i.test(title)) title += ".pdf";
    return title;
  }

  function downloadBlob(blob, name) {
    var a = document.createElement("a");
    var url = URL.createObjectURL(blob);
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
  }

  function savePdf(asCopy) {
    if (!pdfDocument || !pdfDocument.saveDocument) {
      statusText.textContent = "保存失败：文档未就绪";
      return;
    }
    statusText.textContent = asCopy ? "正在另存为…" : "正在保存…";
    pdfDocument.saveDocument().then(function (data) {
      var blob = new Blob([data], { type: "application/pdf" });
      if (asCopy) {
        downloadBlob(blob, buildSaveName());
        statusText.textContent = "已另存为副本";
        persistPos();
        return;
      }

      var hasBridge = window.chrome && window.chrome.webview && window.chrome.webview.postMessage;
      if (!hasBridge) {
        downloadBlob(blob, buildSaveName());
        statusText.textContent = "已另存为副本（未连接缓存）";
        persistPos();
        return;
      }

      var fr = new FileReader();
      fr.onload = function () {
        var b64 = String(fr.result).split(",")[1];
        if (b64) {
          window.chrome.webview.postMessage("savepdf:" + b64);
          statusText.textContent = "已保存（批注已写入缓存）";
        } else {
          statusText.textContent = "保存失败：编码结果为空";
        }
      };
      fr.onerror = function () {
        statusText.textContent = "保存失败：读取结果出错";
      };
      fr.readAsDataURL(blob);
      persistPos();
    }).catch(function (err) {
      statusText.textContent = "保存失败：" + (err && err.message ? err.message : err);
    });
  }

  // ---- 查找栏 ----
  function openFindBar() {
    document.body.classList.add("find-open");
    findBar.classList.remove("hidden");
    searchBtn.classList.add("active");
    searchInput.focus();
    searchInput.select();
    if (searchInput.value) findInPage({ type: "again", findPrevious: false });
  }

  function closeFindBar() {
    if (findBar.classList.contains("hidden")) return;
    document.body.classList.remove("find-open");
    findBar.classList.add("hidden");
    searchBtn.classList.remove("active");
    searchCount.textContent = "";
    lastFind = null;
    if (findTimer) { clearTimeout(findTimer); findTimer = null; }
    if (findController) eventBus.dispatch("findbarclose", { source: findController });
    if (document.activeElement === searchInput) searchInput.blur();
  }

  function findInPage(extra) {
    if (!findController) return;
    var q = searchInput.value;
    if (!q) return;
    var state = Object.assign({
      query: q,
      phraseSearch: true,
      caseSensitive: false,
      entireWord: false,
      highlightAll: true
    }, extra || {});
    lastFind = state;
    eventBus.dispatch("find", state);
  }

  searchBtn.addEventListener("click", function () {
    if (findBar.classList.contains("hidden")) openFindBar();
    else closeFindBar();
  });
  searchCloseBtn.addEventListener("click", closeFindBar);

  searchInput.addEventListener("input", function () {
    if (findTimer) { clearTimeout(findTimer); findTimer = null; }
    lastFind = null;
    var q = searchInput.value;
    if (!q) {
      searchCount.textContent = "";
      if (findController) eventBus.dispatch("findbarclose", { source: findController });
      return;
    }
    findTimer = setTimeout(function () {
      findTimer = null;
      findInPage({ type: "new", findPrevious: false });
    }, 200);
  });

  searchInput.addEventListener("keydown", function (e) {
    if (e.key === "Enter") {
      e.preventDefault();
      if (lastFind) findInPage({ type: "again", findPrevious: e.shiftKey });
      else findInPage({ type: "new", findPrevious: e.shiftKey });
    } else if (e.key === "Escape") {
      e.preventDefault();
      closeFindBar();
    }
  });

  searchNextBtn.addEventListener("click", function () {
    if (!searchInput.value) { searchInput.focus(); return; }
    findInPage({ type: "again", findPrevious: false });
  });
  searchPrevBtn.addEventListener("click", function () {
    if (!searchInput.value) { searchInput.focus(); return; }
    findInPage({ type: "again", findPrevious: true });
  });

  // ---- 侧栏：缩略图 ----
  function updateThumbnailSelection() {
    var nodes = thumbnailsEl.children;
    for (var i = 0; i < nodes.length; i++) {
      var n = nodes[i];
      if (n.classList && n.classList.contains("thumbnail")) {
        n.classList.toggle("selected", parseInt(n.getAttribute("data-page"), 10) === currentPage);
      }
    }
  }

  function loadThumbnails() {
    thumbnailsEl.innerHTML = "";
    for (var p = 1; p <= pdfDocument.numPages; p++) {
      (function (pageNum) {
        pdfDocument.getPage(pageNum).then(function (page) {
          var vp = page.getViewport({ scale: 1 });
          var div = document.createElement("div");
          div.className = "thumbnail";
          div.setAttribute("data-page", pageNum);
          div.innerHTML = '<div class="page"></div><div class="tnum">' + pageNum + "</div>";
          var pageDiv = div.querySelector(".page");
          var ratio = Math.min(1, 180 / vp.width);
          pageDiv.style.height = (vp.height * ratio) + "px";
          var canvas = document.createElement("canvas");
          pageDiv.appendChild(canvas);
          var cw = Math.max(1, Math.floor(vp.width * ratio));
          var ch = Math.max(1, Math.floor(vp.height * ratio));
          canvas.width = cw;
          canvas.height = ch;
          page.render({
            canvasContext: canvas.getContext("2d"),
            viewport: page.getViewport({ scale: ratio })
          }).promise.catch(function (err) {
            if (err && err.name === "RenderingCancelledException") return;
            dbg("缩略图 " + pageNum + " 失败: " + (err && err.message ? err.message : err));
          });
          div.addEventListener("click", function () { goToPage(pageNum); });
          thumbnailsEl.appendChild(div);
          updateThumbnailSelection();
        });
      })(p);
    }
  }

  // ---- 侧栏：目录 ----
  function loadOutline() {
    outlineEl.innerHTML = "";
    outlineHeader.classList.add("hidden");
    pdfDocument.getOutline().then(function (outline) {
      if (!outline || outline.length === 0) return;
      outlineHeader.classList.remove("hidden");
      outlineEl.appendChild(buildOutlineTree(outline));
    }).catch(function (err) {
      dbg("目录加载失败: " + (err && err.message ? err.message : err));
    });
  }

  function buildOutlineTree(items) {
    var ul = document.createElement("ul");
    items.forEach(function (item) {
      var li = document.createElement("li");
      li.textContent = item.title;
      li.title = item.title;
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
    var tag = (e.target && e.target.tagName) || "";
    var inField = tag === "INPUT" || tag === "TEXTAREA" || (e.target && e.target.isContentEditable);

    if (e.ctrlKey || e.metaKey) {
      var k = (e.key || "").toLowerCase();
      if (k === "f") { e.preventDefault(); openFindBar(); return; }
      if (k === "g") { e.preventDefault(); pageNumInput.focus(); pageNumInput.select(); return; }
      if (k === "s") { e.preventDefault(); savePdf(!!e.shiftKey); return; }
      if (k === "a" && e.shiftKey) { e.preventDefault(); toggleFit(); return; }
      if (k === "+" || k === "=") { e.preventDefault(); zoomBy(0.1); return; }
      if (k === "-" || k === "_") { e.preventDefault(); zoomBy(-0.1); return; }
      if (k === "0") { e.preventDefault(); setZoom(1); return; }
    }

    if (e.key === "F3") {
      e.preventDefault();
      if (e.shiftKey) searchPrevBtn.click();
      else searchNextBtn.click();
      return;
    }
    if (e.key === "F9") { e.preventDefault(); rotate(); return; }
    if (e.key === "F11") { e.preventDefault(); toggleFullscreen(); return; }

    if (e.key === "Escape") {
      if (!findBar.classList.contains("hidden")) { closeFindBar(); return; }
      if (!layoutFlyout.classList.contains("hidden") || !penPalette.classList.contains("hidden")) {
        closeFlyouts();
        return;
      }
      if (document.body.classList.contains("annotating")) { exitAnnotateMode(); return; }
      return;
    }

    if (inField || document.body.classList.contains("annotating")) return;

    if (e.key === "ArrowLeft" || e.key === "PageUp") {
      e.preventDefault();
      eventBus.dispatch("previouspage");
    } else if (e.key === "ArrowRight" || e.key === "PageDown") {
      e.preventDefault();
      eventBus.dispatch("nextpage");
    } else if (e.key === "Home") {
      e.preventDefault();
      goToPage(1);
    } else if (e.key === "End") {
      e.preventDefault();
      goToPage(pdfDocument ? pdfDocument.numPages : 1);
    }
  });

  updateFitButton();
  updateLayoutUI();
  updateFullscreenButton();

  window.goToPage = goToPage;
})();
