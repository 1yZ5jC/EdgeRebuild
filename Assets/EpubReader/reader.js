/* EPUB 阅读器：epubjs 渲染 + 目录/书签/搜索/设置/进度记忆/章节圆点 */
(function () {
  "use strict";

  if (!window.ePub) {
    document.getElementById("statusText").textContent = "加载 EPUB 引擎失败";
    return;
  }

  var fileParam = new URLSearchParams(location.search).get("file") || "book.epub";

  var fileName = fileParam.split("/").pop() || "book.epub";
  // EdgeHTML 阅读器始终以虚拟名 book.epub 加载，真实缓存名经 cache 参数传入；
  // 进度/书签按 cache 区分，避免不同 EPUB 互相覆盖（旧 URL 无 cache 时回退文件名）。
  var cacheParam = new URLSearchParams(location.search).get("cache") || "";
  var storageName = cacheParam.split("/").pop() || fileName;
  var book, rendition;
  var currentCfi = null;
  var currentHref = null;
  var spineItems = [];   // [{href, index}]（采样后的章节圆点）
  var fullSpine = [];    // 全部章节 href（locations 未就绪时拖动进度条的兜底）
  var hrefIndex = {};    // href -> spine 索引
  var tocItems = [];
  var locationsReady = false;
  var chaptersLoaded = false;

  var statusText = document.getElementById("statusText");
  var tocPanel = document.getElementById("tocPanel");
  var searchPanel = document.getElementById("searchPanel");
  var bookmarksPanel = document.getElementById("bookmarksPanel");
  var settingsPanel = document.getElementById("settingsPanel");

  // ---- 持久化（localStorage，按文件名区分） ----
  function store(key, value) {
    try { localStorage.setItem(key + "_" + storageName, JSON.stringify(value)); } catch (e) { }
  }
  function loadStore(key) {
    try { return JSON.parse(localStorage.getItem(key + "_" + storageName)) || null; } catch (e) { return null; }
  }

  var settings = loadStore("er_settings") || {};
  settings.fontSize = settings.fontSize || "100%";
  settings.font = settings.font || "serif";
  settings.lineHeight = settings.lineHeight || "1.6";
  settings.theme = settings.theme || "light";
  var bookmarks = loadStore("er_bookmarks") || [];
  var savedProgress = loadStore("er_progress") || null;

  // ---- 主题 ----
  var THEMES = {
    light: { color: "#333333", bg: "#ffffff", viewerBg: "#e6e6e6" },
    sepia: { color: "#5b4636", bg: "#f4ecd8", viewerBg: "#e2d6bf" },
    dark: { color: "#d6d6d6", bg: "#1e1e1e", viewerBg: "#111111" }
  };
  var SERIF_FONT = "Georgia, 'Times New Roman', 'Songti SC', serif";
  var SANS_FONT = "'Segoe UI', 'Microsoft YaHei', sans-serif";

  function applySettings() {
    if (!rendition || !rendition.themes) return;
    var th = THEMES[settings.theme] || THEMES.light;
    rendition.themes.fontSize(settings.fontSize);
    rendition.themes.font(settings.font === "serif" ? SERIF_FONT : SANS_FONT);
    rendition.themes.override("line-height", settings.lineHeight);
    rendition.themes.override("color", th.color);
    rendition.themes.override("background-color", th.bg);
    document.getElementById("viewer").style.background = th.viewerBg;
  }

  function saveSettings() {
    store("er_settings", settings);
    highlightSettings();
  }

  // ---- 面板 ----
  function closePanels() {
    tocPanel.classList.add("hidden");
    searchPanel.classList.add("hidden");
    bookmarksPanel.classList.add("hidden");
    settingsPanel.classList.add("hidden");
  }
  function togglePanel(panel) {
    var willOpen = panel.classList.contains("hidden");
    closePanels();
    panel.classList.toggle("hidden", !willOpen);
  }

  // ---- 目录 ----
  function loadToc() {
    book.loaded.navigation.then(function (nav) {
      tocItems = nav.toc || [];
      var listEl = document.getElementById("tocList");
      listEl.innerHTML = "";
      tocItems.forEach(function (item) {
        listEl.appendChild(makeTocItem(item, 0));
      });
    }).catch(function () { });
  }

  function makeTocItem(item, depth) {
    var li = document.createElement("li");
    li.style.paddingLeft = (4 + depth * 16) + "px";
    li.textContent = item.label;
    li.addEventListener("click", function () {
      rendition.display(item.href);
      tocPanel.classList.add("hidden");
    });
    if (item.subitems) {
      item.subitems.forEach(function (sub) {
        li.appendChild(makeTocItem(sub, depth + 1));
      });
    }
    return li;
  }

  // ---- 章节圆点 ----
  function loadSpineDots() {
    book.loaded.spine.then(function (spine) {
      var n = typeof spine.length === "number" ? spine.length : 0;
      if (!n && typeof spine.get === "function") {
        // spine.get(0) 探测长度
        var i = 0;
        try { while (spine.get(i)) { i++; if (i > 5000) break; } } catch (e) { }
        n = i;
      }
      var dotsEl = document.getElementById("dotsRow");
      dotsEl.innerHTML = "";
      var step = n > 24 ? Math.ceil(n / 24) : 1;
      spineItems = [];
      fullSpine = [];
      for (var k = 0; k < n; k += step) {
        (function (idx) {
          var sec = spine.get(idx);
          if (!sec) return;
          hrefIndex[sec.href] = idx;
          spineItems.push({ href: sec.href, index: idx });
          var dot = document.createElement("button");
          dot.className = "chapter-dot";
          dot.title = "第 " + (idx + 1) + " 章";
          dot.addEventListener("click", function () {
            rendition.display(sec.href);
          });
          dotsEl.appendChild(dot);
        })(k);
      }
      if (n === 1) {
        var sec0 = spine.get(0);
        if (sec0) {
          hrefIndex[sec0.href] = 0;
          spineItems.push({ href: sec0.href, index: 0 });
          var dot = document.createElement("button");
          dot.className = "chapter-dot";
          dot.title = "第 1 章";
          dot.addEventListener("click", function () {
            rendition.display(sec0.href);
          });
          dotsEl.appendChild(dot);
        }
      }
      try {
        for (var fi = 0; fi < n; fi++) {
          var fsec = spine.get(fi);
          if (fsec && fsec.href) fullSpine.push(fsec.href);
        }
      } catch (e) { }
      chaptersLoaded = true;
    }).catch(function () { });
  }

  function updateDots(href) {
    var dots = document.querySelectorAll("#dotsRow .chapter-dot");
    for (var j = 0; j < dots.length; j++) dots[j].classList.remove("active");
    if (!chaptersLoaded || href == null) return;
    var idx = hrefIndex[href];
    if (idx == null) return;
    var best = 0;
    for (var k = 0; k < spineItems.length; k++) {
      if (spineItems[k].index <= idx) best = k; else break;
    }
    if (dots[best]) dots[best].classList.add("active");
  }

  // ---- 进度 ----
  var progressBar = document.getElementById("progressBar");
  var progressFill = document.getElementById("progressFill");
  var percentText = document.getElementById("percentText");

  function updateProgress(pct) {
    pct = Math.max(0, Math.min(100, Math.round(pct)));
    progressFill.style.width = pct + "%";
    if (percentText) percentText.textContent = pct + "%";
  }

  function buildLocations() {
    book.locations.generate().then(function () {
      locationsReady = true;
      if (currentCfi) {
        var pct = book.locations.percentageFromCfi(currentCfi);
        updateProgress(pct * 100);
      }
    }).catch(function () { });
  }

  var saveProgressTimer = null;
  function saveProgress() {
    if (saveProgressTimer) clearTimeout(saveProgressTimer);
    saveProgressTimer = setTimeout(function () {
      if (!currentCfi) return;
      var prog = { cfi: currentCfi, href: currentHref, updated: Date.now() };
      if (locationsReady) prog.percent = book.locations.percentageFromCfi(currentCfi);
      store("er_progress", prog);
    }, 600);
  }

  // ---- 书签 ----
  function chapterLabelFor(href) {
    for (var i = 0; i < tocItems.length; i++) {
      if (tocItems[i].href === href) return tocItems[i].label;
    }
    return href.split("/").pop() || href;
  }

  function updateBookmarkBtn() {
    var active = bookmarks.some(function (b) { return b.cfi === currentCfi; });
    document.getElementById("bookmarkBtn").classList.toggle("active", active);
  }

  function addCurrentBookmark() {
    if (!currentCfi) return;
    for (var i = 0; i < bookmarks.length; i++) {
      if (bookmarks[i].cfi === currentCfi) {
        statusText.textContent = "当前页已在书签中";
        return;
      }
    }
    var label = chapterLabelFor(currentHref);
    var bm = {
      cfi: currentCfi,
      href: currentHref,
      label: label,
      chapter: label,
      created: Date.now()
    };
    if (locationsReady) bm.percent = book.locations.percentageFromCfi(currentCfi);
    bookmarks.unshift(bm);
    store("er_bookmarks", bookmarks);
    updateBookmarkBtn();
    renderBookmarks();
    statusText.textContent = "已添加书签";
  }

  function renderBookmarks() {
    var listEl = document.getElementById("bookmarksList");
    listEl.innerHTML = "";
    if (!bookmarks.length) {
      var empty = document.createElement("div");
      empty.className = "empty-tip";
      empty.textContent = "暂无书签";
      listEl.appendChild(empty);
      return;
    }
    bookmarks.forEach(function (bm, idx) {
      var row = document.createElement("div");
      row.className = "bookmark-row";
      var info = document.createElement("div");
      info.className = "bookmark-info";
      var label = document.createElement("div");
      label.className = "bookmark-label";
      label.textContent = bm.label;
      var sub = document.createElement("div");
      sub.className = "bookmark-sub";
      var pct = bm.percent != null ? " · " + Math.round(bm.percent * 100) + "%" : "";
      sub.textContent = bm.chapter + pct;
      info.appendChild(label);
      info.appendChild(sub);
      var del = document.createElement("button");
      del.className = "bookmark-del";
      del.textContent = "删除";
      del.addEventListener("click", function (e) {
        e.stopPropagation();
        bookmarks.splice(idx, 1);
        store("er_bookmarks", bookmarks);
        renderBookmarks();
        updateBookmarkBtn();
      });
      row.appendChild(info);
      row.appendChild(del);
      row.addEventListener("click", function () {
        rendition.display(bm.cfi);
        bookmarksPanel.classList.add("hidden");
      });
      listEl.appendChild(row);
    });
  }

  // ---- 大声朗读（TTS）：当前章节文本经消息通道交给原生层朗读 ----
  document.getElementById("ttsBtn").addEventListener("click", function () {
    var href = currentHref;
    if (!href) return;
    statusText.textContent = "朗读准备中…";
    window.__ttsAcked = false;
    var finished = false;
    var timer = setTimeout(function () {
      if (finished) return;
      finished = true;
      statusText.textContent = "朗读准备超时：无法读取本章内容";
    }, 4000);
    resolveChapterText(href).then(function (html) {
      if (finished) return;
      var tmp = document.createElement("div");
      tmp.innerHTML = String(html || "");
      var text = (tmp.textContent || "").replace(/\s+/g, " ").trim();
      if (!text) {
        finished = true;
        clearTimeout(timer);
        statusText.textContent = "本章无可朗读内容";
        return;
      }
      finished = true;
      clearTimeout(timer);
      text = text.substring(0, 6000);
      var send = window.chrome && window.chrome.webview && window.chrome.webview.postMessage;
      if (!send) {
        // EdgeHTML 引擎：无 postMessage，经全局 marker 由宿主轮询转发
        try {
          window.__edgTtsMarker = "tts:" + text;
          statusText.textContent = "已交给朗读：本章前 6000 字";
        } catch (e2) {
          statusText.textContent = "朗读不可用：当前引擎不支持";
        }
        return;
      }
      window.chrome.webview.postMessage("tts:" + text);
      statusText.textContent = "已交给朗读：本章前 6000 字";
      setTimeout(function () {
        if (!window.__ttsAcked) statusText.textContent = "朗读未响应：请检查系统语音设置";
      }, 1500);
    }, function () {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      statusText.textContent = "读取本章内容失败";
    });
  });

  // ---- 搜索 ----
  // getText 可能同步返回字符串或返回 promise，统一转成 promise 再使用
  function resolveChapterText(href) {
    var p = null;
    try {
      var absolute = typeof book.resolve === "function" ? book.resolve(href) : href;
      if (book.archive && typeof book.archive.getText === "function") {
        p = book.archive.getText(absolute);
      }
    } catch (e) { }
    if (p !== null && p !== undefined) {
      if (typeof p.then === "function") {
        return p.then(function (v) { return String(v || ""); }, function () { return ""; });
      }
      return Promise.resolve(String(p || ""));
    }
    try {
      var r = book.request(href, "text");
      if (r && typeof r.then === "function") {
        return r.then(function (v) { return String(v || ""); }, function () { return ""; });
      }
      return Promise.resolve(String(r || ""));
    } catch (e) {
      return Promise.resolve("");
    }
  }

  function searchBook(query) {
    query = query.trim().toLowerCase();
    var resultsEl = document.getElementById("searchResults");
    resultsEl.innerHTML = "";
    if (!query) return;
    statusText.textContent = "搜索中…";
    book.loaded.spine.then(function (spine) {
      var n = spine.length;
      var found = 0;
      var tasks = [];
      for (var i = 0; i < n; i++) {
        (function (idx) {
          var sec = spine.get(idx);
          tasks.push(resolveChapterText(sec.href).then(function (html) {
            var text = String(html || "")
              .replace(/<[^>]+>/g, " ")
              .replace(/&amp;/g, "&").replace(/&lt;/g, "<").replace(/&gt;/g, ">")
              .replace(/&nbsp;/g, " ").replace(/\s+/g, " ").toLowerCase();
            var pos = text.indexOf(query);
            if (pos >= 0) {
              var start = Math.max(0, pos - 40);
              var snippet = (start > 0 ? "…" : "") + text.substr(start, 90) + "…";
              found++;
              resultsEl.appendChild(makeSearchResult(idx, sec.href, snippet));
            }
          }).catch(function () { }));
        })(i);
      }
      Promise.all(tasks).then(function () {
        if (!found) {
          var empty = document.createElement("div");
          empty.className = "empty-tip";
          empty.textContent = "没有找到匹配内容";
          resultsEl.appendChild(empty);
        }
        statusText.textContent = found ? "找到 " + found + " 处匹配" : "搜索完成，无匹配";
      });
    }).catch(function () {
      statusText.textContent = "搜索失败";
    });
  }

  function makeSearchResult(idx, href, snippet) {
    var row = document.createElement("div");
    row.className = "search-result";
    var label = document.createElement("div");
    label.className = "search-label";
    label.textContent = "第 " + (idx + 1) + " 章";
    var text = document.createElement("div");
    text.className = "search-snippet";
    text.textContent = snippet;
    row.appendChild(label);
    row.appendChild(text);
    row.addEventListener("click", function () {
      rendition.display(href);
      searchPanel.classList.add("hidden");
    });
    return row;
  }

  // ---- UI 事件 ----
  document.getElementById("tocBtn").addEventListener("click", function () { togglePanel(tocPanel); });
  document.getElementById("searchBtn").addEventListener("click", function () {
    togglePanel(searchPanel);
    if (!searchPanel.classList.contains("hidden")) document.getElementById("searchInput").focus();
  });

  var searchInput = document.getElementById("searchInput");
  var searchTimer = null;
  searchInput.addEventListener("input", function () {
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(function () { searchBook(searchInput.value); }, 300);
  });
  searchInput.addEventListener("keydown", function (e) {
    if (e.key === "Enter") {
      if (searchTimer) clearTimeout(searchTimer);
      searchBook(searchInput.value);
    }
  });
  document.getElementById("settingsBtn").addEventListener("click", function () { togglePanel(settingsPanel); });
  document.getElementById("prevBtn").addEventListener("click", function () { rendition.prev(); });
  document.getElementById("nextBtn").addEventListener("click", function () { rendition.next(); });

  document.getElementById("bookmarkBtn").addEventListener("click", function () {
    togglePanel(bookmarksPanel);
    if (!bookmarksPanel.classList.contains("hidden")) renderBookmarks();
  });
  document.getElementById("addBookmarkBtn").addEventListener("click", addCurrentBookmark);

  // 设置面板
  function bindOpts(id, dataKey, apply) {
    document.getElementById(id).querySelectorAll("button").forEach(function (btn) {
      btn.addEventListener("click", function () {
        apply(btn.getAttribute("data-" + dataKey));
        saveSettings();
      });
    });
  }
  bindOpts("sizeOpts", "size", function (v) { settings.fontSize = v; applySettings(); });
  bindOpts("fontOpts", "font", function (v) { settings.font = v; applySettings(); });
  bindOpts("lhOpts", "lh", function (v) { settings.lineHeight = v; applySettings(); });
  bindOpts("themeOpts", "theme", function (v) { settings.theme = v; applySettings(); });

  function highlightSettings() {
    var map = [
      ["sizeOpts", "size", settings.fontSize],
      ["fontOpts", "font", settings.font],
      ["lhOpts", "lh", settings.lineHeight],
      ["themeOpts", "theme", settings.theme]
    ];
    map.forEach(function (m) {
      document.getElementById(m[0]).querySelectorAll("button").forEach(function (btn) {
        btn.classList.toggle("sel", btn.getAttribute("data-" + m[1]) === m[2]);
      });
    });
  }
  highlightSettings();

  // 进度条拖动（locations 未就绪时降级为章节级跳转，保证拖动始终可用）
  var dragging = false;
  function setProgressByEvent(e) {
    var rect = progressBar.getBoundingClientRect();
    var x = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
    var pct = x / rect.width;
    if (locationsReady) {
      try {
        var cfi = book.locations.cfiFromPercentage(pct);
        if (cfi) rendition.display(cfi);
      } catch (err) { }
      return;
    }
    if (fullSpine.length) {
      var idx = Math.max(0, Math.min(fullSpine.length - 1, Math.floor(pct * fullSpine.length)));
      rendition.display(fullSpine[idx]);
      updateProgress(pct * 100);
    }
  }
  progressBar.addEventListener("pointerdown", function (e) {
    dragging = true;
    progressBar.setPointerCapture(e.pointerId);
    setProgressByEvent(e);
  });
  progressBar.addEventListener("pointermove", function (e) {
    if (dragging) setProgressByEvent(e);
  });
  progressBar.addEventListener("pointerup", function () { dragging = false; });

  // 键盘
  document.addEventListener("keydown", function (e) {
    if ((e.ctrlKey || e.metaKey) && (e.key === "f" || e.key === "F")) {
      e.preventDefault();
      if (searchPanel.classList.contains("hidden")) togglePanel(searchPanel);
      var input = document.getElementById("searchInput");
      input.focus();
      input.select();
      return;
    }
    if (e.key === "Escape") { closePanels(); return; }
    if (e.key === "ArrowLeft") { rendition.prev(); e.preventDefault(); }
    else if (e.key === "ArrowRight") { rendition.next(); e.preventDefault(); }
  });

  // 点击页边翻页（旧版 Edge gutter 交互）
  document.getElementById("gutterLeft").addEventListener("click", function () { rendition.prev(); });
  document.getElementById("gutterRight").addEventListener("click", function () { rendition.next(); });

  // ---- 初始化 ----
  // EdgeHTML 下 epub.js 内部以 XHR(blob) 读取 ms-appdata 会一直无响应；
  // 先自行以 arraybuffer 读取整本书，再把二进制交给 epub.js（ArrayBuffer 走 openEpub）。
  function initBook(data) {
    book = ePub(data);

    book.loaded.metadata.then(function (meta) {
      document.getElementById("bookTitle").textContent = meta.title || "EPUB 阅读器";
    }).catch(function () { });

    rendition = book.renderTo("viewer", {
      width: "100%",
      height: "100%",
      flow: "paginated",
      spread: "none"
    });

    rendition.on("relocated", function (location) {
      if (!location || !location.start) return;
      currentCfi = location.start.cfi;
      currentHref = location.start.href;
      updateDots(currentHref);
      updateBookmarkBtn();
      if (locationsReady) {
        var pct = book.locations.percentageFromCfi(currentCfi);
        updateProgress(pct * 100);
      }
      statusText.textContent = "阅读模式：分页";
      saveProgress();
    });

    var openPromise = savedProgress && savedProgress.cfi
      ? rendition.display(savedProgress.cfi)
      : rendition.display();

    openPromise.then(function () {
      statusText.textContent = "阅读模式：分页";
      loadToc();
      loadSpineDots();
      buildLocations();
      applySettings();
      renderBookmarks();
    }).catch(function (err) {
      statusText.textContent = "打开失败：" + (err && err.message ? err.message : err);
    });
  }

  if (fileParam.indexOf("ms-appdata:") === 0) {
    var bookXhr = new XMLHttpRequest();
    bookXhr.open("GET", fileParam, true);
    bookXhr.responseType = "arraybuffer";
    bookXhr.onreadystatechange = function () {
      if (bookXhr.readyState !== 4) return;
      if ((bookXhr.status === 200 || bookXhr.status === 0)
          && bookXhr.response && bookXhr.response.byteLength > 0) {
        initBook(bookXhr.response);
      } else {
        statusText.textContent = "打开失败：无法读取电子书 (" + bookXhr.status + ")";
      }
    };
    bookXhr.onerror = function () {
      statusText.textContent = "打开失败：无法读取电子书";
    };
    bookXhr.send();
  } else {
    initBook(fileParam);
  }
})();
