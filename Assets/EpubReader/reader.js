/* EPUB 阅读器逻辑：epubjs 渲染 + 翻页 + 目录导航 */
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

  var book, rendition;
  var tocPanel = document.getElementById("tocPanel");
  var statusText = document.getElementById("statusText");

  book = ePub(fileParam);

  book.loaded.metadata.then(function (meta) {
    document.getElementById("bookTitle").textContent = meta.title || "EPUB 阅读器";
  });

  rendition = book.renderTo("viewer", {
    width: "100%",
    height: "100%",
    flow: "paginated",
    spread: "none"
  });
  rendition.display().then(function () {
    statusText.textContent = "阅读模式：分页";
    loadToc();
  }).catch(function (err) {
    statusText.textContent = "打开失败：" + (err && err.message ? err.message : err);
  });

  document.getElementById("prevBtn").addEventListener("click", function () { rendition.prev(); });
  document.getElementById("nextBtn").addEventListener("click", function () { rendition.next(); });

  document.getElementById("tocBtn").addEventListener("click", function () {
    tocPanel.classList.toggle("hidden");
  });

  function loadToc() {
    book.loaded.navigation.then(function (nav) {
      var listEl = document.getElementById("tocList");
      nav.toc.forEach(function (item) {
        var li = document.createElement("li");
        li.textContent = item.label;
        li.addEventListener("click", function () {
          rendition.display(item.href);
          tocPanel.classList.add("hidden");
        });
        listEl.appendChild(li);
        if (item.subitems) {
          var subUl = document.createElement("ul");
          item.subitems.forEach(function (sub) {
            var sli = document.createElement("li");
            sli.textContent = sub.label;
            sli.addEventListener("click", function () {
              rendition.display(sub.href);
              tocPanel.classList.add("hidden");
            });
            subUl.appendChild(sli);
          });
          li.appendChild(subUl);
        }
      });
    });
  }

  document.addEventListener("keydown", function (e) {
    if (e.key === "ArrowLeft") { rendition.prev(); e.preventDefault(); }
    else if (e.key === "ArrowRight") { rendition.next(); e.preventDefault(); }
  });
})();
