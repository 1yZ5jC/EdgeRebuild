/* EPUB 阅读器：epubjs 渲染 + 目录/书签/搜索/设置/进度记忆/章节圆点 */
(function () {
  "use strict";

  if (!window.ePub) {
    document.getElementById("statusText").textContent = "加载 EPUB 引擎失败";
    return;
  }

  var fileParam = new URLSearchParams(location.search).get("file");
  if (!fileParam) {
    document.getElementById("statusText").textContent = "缺少 file 参数";
    return;
  }

  var fileName = fileParam.split("/").pop() || "book.epub";
  var book, rendition;
  var currentCfi = null;
  var currentHref = null;
  var spineItems = [];   // [{href, label}]
  var tocItems = [];     // nav.toc
  var locationsReady = false;
  var chaptersLoaded = false;

  var statusText = document.getElementById("statusText");
  var tocPanel = document.getElementById("tocPanel");
  var searchPanel = document.getElementById("searchPanel");
  var bookmarksPanel = document.getElementById("bookmarksPanel");
  var settingsPanel = document.getElementById("settingsPanel");

  // ---- 持久化 ----
  function store(key, value) {
    try { localStorage.setItem(key + "_" + fileName, JSON.stringify(value)); } catch (e) { }
  }
  function loadStore(key) {
    try { return JSON.parse(localStorage.getItem(key + "_" + fileName)) || null; } catch (e) { return null; }
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
    light: { color: "#333333", bg: "#ffffff" },
    sepia: { color: "#5b4636", bg: "#f4ecd8" },
    dark: { color: "#d6d6d6", bg: "#1e1e1e" }
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
    document.body.style.background = th.themeBg || "";
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
    li.style.paddingLeft = (6 + depth * 14) + "px";
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
      spineItems = [];
      var dotsEl = document.getElementById("dotsRow");
      dotsEl.innerHTML = "";
      var max = spine.length;
      var step = max > 24 ? Math.ceil(max / 24) : 1;
      for (var i = 0; i < max; i += step) {
        (function (idx) {
          var href = spine.get(idx).href;
          spineItems.push({ href: href, index: idx });
          var dot = document.createElement("button");
          dot.className = "chapter-dot";
          dot.title = "第 " + (idx + 1) + " 章";
          dot.addEventListener("click", function () {
            rendition.display(spine.get(idx).href);
          });
          dotsEl.appendChild(dot);
        })(i);
      }
      if (max <= step) {
        // 只有 1 章时补一个当前章节圆点
        var dot = document.createElement("button");
        dot.className = "chapter-dot";
        dot.title = "第 1 章";
        dot.addEventListener("click", function () {
          rendition.display(spine.get(0).href);
        });
        dotsEl.appendChild(dot);
      }
      chaptersLoaded = true;
    }).catch(function () { });
  }

  function updateDots(href) {
    var dots = document.querySelectorAll("#dotsRow .chapter-dot");
    var best = -1;
    for (var i = 0; i < spineItems.length; i++) {
      if (spineItems[i].href === href) { best = i; break; }
    }
    for (var j = 0; j < dots.length; j++) {
      dots[j].classList.toggle("active", j === best);
    }
  }

  // ---- 进度 ----
  var progressBar = document.getElementById("progressBar");
  var progressFill = document.getElementById("progressFill");
  var percentLabel = document.getElementById("percentLabel");

  function updateProgress(pct) {
    pct = Math.max(0, Math.min(100, Math.round(pct)));
    progressFill.style.width = pct + "%";
    percentLabel.textContent = pct + "%";
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

  // ---- 搜索 ----
  var searchTimer = null;
  function searchBook(query) {
    query = query.trim().toLowerCase();
    var resultsEl = document.getElementById("searchResults");
    resultsEl.innerHTML = "";
    if (!query) return;
    statusText.textContent = "搜索中…";
    var spineReady = book.loaded.spine.then(function (spine) {
      return spine;
    });
    spineReady.then(function (spine) {
      var n = spine.length;
      var found = 0;
      var tasks = [];
      for (var i = 0; i < n; i++) {
        (function (idx) {
          tasks.push(spine.get(idx).load().then(function (html) {
            var text = html.replace(/<[^>]+>/g, " ").replace(/&amp;/g, "&")
              .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&nbsp;/g, " ").replace(/\s+/g, " ");
            var q = query;
            var pos = text.toLowerCase().indexOf(q);
            if (pos >= 0) {
              var start = Math.max(0, pos - 40);
              var snippet = (start > 0 ? "…" : "") + text.substr(start, 90) + "…";
              found++;
              resultsEl.appendChild(makeSearchResult(idx, spine.get(idx).href, snippet));
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
  document.getElementById("bookmarkBtn").addEventListener("click", function () {
    if (bookmarksPanel.classList.contains("hidden") === false) {
      // 已打开面板时点击按钮 → 在当前页加/删书签
      var existing = bookmarks.findIndex(function (b) { return b.cfi === currentCfi; });
      if (existing >= 0) {
        bookmarks.splice(existing, 1);
        statusText.textContent = "已删除书签";
      } else {
        var bm = {
          cfi: currentCfi,
          href: currentHref,
          label: chapterLabelFor(currentHref),
          chapter: chapterLabelFor(currentHref),
          created: Date.now()
        };
        if (locationsReady) bm.percent = book.locations.percentageFromCfi(currentCfi);
        bookmarks.unshift(bm);
        statusText.textContent = "已添加书签";
      }
      store("er_bookmarks", bookmarks);
      updateBookmarkBtn();
      renderBookmarks();
    } else {
      togglePanel(bookmarksPanel);
      renderBookmarks();
    }
  });
  document.getElementById("settingsBtn").addEventListener("click", function () { togglePanel(settingsPanel); });

  document.getElementById("prevBtn").addEventListener("click", function () { rendition.prev(); });
  document.getElementById("nextBtn").addEventListener("click", function () { rendition.next(); });

  // 设置面板
  function bindOpts(id, key, cb) {
    var opts = document.getElementById(id);
    opts.querySelectorAll("button").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var v = btn.getAttribute("data-" + key);
        settings[key] = v;
        saveSettings();
        cb(v);
      });
    });
  }
  bindOpts("sizeOpts", "size", function (v) { settings.fontSize = v; applySettings(); });
  bindOpts("fontOpts", "font", function () { applySettings(); });
  bindOpts("lhOpts", "lh", function () { applySettings(); });
  bindOpts("themeOpts", "theme", function () { applySettings(); });

  function highlightSettings() {
    var map = {
      sizeOpts: settings.fontSize,
      fontOpts: settings.font,
      lhOpts: settings.lineHeight,
      themeOpts: settings.theme
    };
    Object.keys(map).forEach(function (id) {
      var opts = document.getElementById(id);
      opts.querySelectorAll("button").forEach(function (btn) {
        var attr = btn.getAttribute("data-" + id.replace("Opts", ""));
        var keys = { sizeOpts: "size", fontOpts: "font", lhOpts: "lh", themeOpts: "theme" };
        btn.classList.toggle("sel", btn.getAttribute("data-" + keys[id]) === map[id]);
      });
    });
  }
  highlightSettings();

  // 进度条拖动
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
    if (e.key === "Escape") { closePanels(); return; }
    if (e.key === "ArrowLeft") { rendition.prev(); e.preventDefault(); }
    else if (e.key === "ArrowRight") { rendition.next(); e.preventDefault(); }
  });

  // ---- 初始化 ----
  book = ePub(fileParam);

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
})();
