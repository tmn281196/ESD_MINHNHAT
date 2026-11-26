// A diagram drawn wider than the page is shrunk to fit it, often past reading. Every figure.diagram
// gets a link (and the drawing itself a click) that opens it at its own size in a full-window view:
// scrollable, zoomable with - / 100% / + (or Ctrl + wheel), closed with Esc, the X or a click on the
// dark edge. The drawing is copied from the page, so a Mermaid diagram is shown once it has been drawn
// and keeps the page's fonts - a new tab opened from file:// would not get them.
(function () {
  var vi = (document.documentElement.lang || "vi").indexOf("vi") === 0;
  var T = vi
    ? { open: "Xem sơ đồ cỡ thật", close: "Đóng (Esc)", fit: "Vừa màn hình", wait: "Sơ đồ đang được vẽ, thử lại sau giây lát." }
    : { open: "View full size", close: "Close (Esc)", fit: "Fit", wait: "The diagram is still being drawn - try again in a moment." };

  function naturalWidth(svg) {
    var vb = svg.viewBox && svg.viewBox.baseVal;
    if (vb && vb.width) return vb.width;
    return svg.getBoundingClientRect().width;
  }

  function show(figure) {
    var svg = figure.querySelector("svg");
    if (!svg) { alert(T.wait); return; }
    var title = "";
    var lede = figure.previousElementSibling;
    if (lede && lede.classList.contains("lede")) title = lede.textContent.trim();

    var overlay = document.createElement("div");
    overlay.className = "dv-overlay";
    overlay.innerHTML =
      '<div class="dv-bar"><span class="dv-title"></span>' +
      '<button type="button" data-z="-">−</button><button type="button" data-z="1">100%</button>' +
      '<button type="button" data-z="+">+</button><button type="button" data-z="fit"></button>' +
      '<button type="button" class="dv-close"></button></div><div class="dv-stage"><div class="dv-sheet"></div></div>';
    overlay.querySelector(".dv-title").textContent = title;
    overlay.querySelector('[data-z="fit"]').textContent = T.fit;
    overlay.querySelector(".dv-close").textContent = T.close;

    var copy = svg.cloneNode(true);
    copy.removeAttribute("style");
    copy.removeAttribute("width");
    copy.removeAttribute("height");
    var sheet = overlay.querySelector(".dv-sheet");
    var stage = overlay.querySelector(".dv-stage");
    sheet.appendChild(copy);

    var base = naturalWidth(svg), zoom = 1;
    function apply() {
      copy.style.width = Math.round(base * zoom) + "px";
      overlay.querySelector('[data-z="1"]').textContent = Math.round(zoom * 100) + "%";
    }
    function fit() { zoom = Math.min(1, (stage.clientWidth - 48) / base); apply(); }
    function setZoom(z) { zoom = Math.max(0.2, Math.min(4, z)); apply(); }

    function close() {
      document.removeEventListener("keydown", onKey);
      overlay.remove();
      document.body.style.overflow = "";
    }
    function onKey(e) {
      if (e.key === "Escape") close();
      else if (e.key === "+" || e.key === "=") setZoom(zoom * 1.25);
      else if (e.key === "-") setZoom(zoom / 1.25);
      else if (e.key === "0") setZoom(1);
    }
    overlay.addEventListener("click", function (e) {
      var b = e.target.closest("button");
      if (b) {
        var z = b.getAttribute("data-z");
        if (z === "+") setZoom(zoom * 1.25);
        else if (z === "-") setZoom(zoom / 1.25);
        else if (z === "1") setZoom(1);
        else if (z === "fit") fit();
        else if (b.classList.contains("dv-close")) close();
        return;
      }
      if (e.target === stage) close();
    });
    stage.addEventListener("wheel", function (e) {
      if (!e.ctrlKey) return;
      e.preventDefault();
      setZoom(e.deltaY < 0 ? zoom * 1.1 : zoom / 1.1);
    }, { passive: false });
    document.addEventListener("keydown", onKey);
    document.body.appendChild(overlay);
    document.body.style.overflow = "hidden";
    apply();
  }

  function init() {
    var figures = document.querySelectorAll("figure.diagram");
    for (var i = 0; i < figures.length; i++) {
      (function (figure) {
        var link = document.createElement("a");
        link.href = "#";
        link.className = "dv-open";
        link.textContent = T.open + " ↗";
        link.addEventListener("click", function (e) { e.preventDefault(); show(figure); });
        figure.insertBefore(link, figure.firstChild);
        figure.addEventListener("click", function (e) {
          if (e.target.closest("a, button")) return;
          if (e.target.closest("svg")) show(figure);
        });
      })(figures[i]);
    }
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
