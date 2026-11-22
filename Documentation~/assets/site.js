/* Shared behaviour for every page. Plain script: works from file:// and offline. */
(function () {
  "use strict";
  var doc = document, root = doc.documentElement;
  var reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var finePointer = window.matchMedia("(pointer: fine)").matches;
  var isFile = location.protocol === "file:";
  function $(s, el) { return (el || doc).querySelector(s); }
  function $$(s, el) { return Array.prototype.slice.call((el || doc).querySelectorAll(s)); }
  function fold(s) { return (s || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase(); }

  /* ---------- reveal on scroll (content stays visible without JS: .rv only hides under .js) ---------- */
  function reveals() {
    var items = $$(".rv");
    if (reduced || !("IntersectionObserver" in window)) { items.forEach(function (e) { e.classList.add("in"); }); return; }
    var io = new IntersectionObserver(function (list) {
      list.forEach(function (en) { if (en.isIntersecting) { en.target.classList.add("in"); io.unobserve(en.target); } });
    }, { rootMargin: "0px 0px -8% 0px" });
    items.forEach(function (e) { io.observe(e); });
    setTimeout(function () { items.forEach(function (e) { e.classList.add("in"); }); }, 3000);
  }

  /* ---------- copy buttons on every code block ---------- */
  function copyButtons() {
    $$("pre").forEach(function (pre) {
      if (pre.closest(".tr")) return;
      var wrap = pre.parentElement;
      if (!wrap.classList.contains("code-wrap")) {
        wrap = doc.createElement("div");
        wrap.className = "code-wrap";
        pre.parentNode.insertBefore(wrap, pre);
        wrap.appendChild(pre);
      }
      var b = doc.createElement("button");
      b.type = "button"; b.className = "copy"; b.textContent = "Copy";
      b.setAttribute("aria-label", "Copy code");
      b.addEventListener("click", function () {
        var text = pre.innerText.replace(/\n$/, "");
        var done = function () { b.textContent = "Copied"; b.classList.add("done"); setTimeout(function () { b.textContent = "Copy"; b.classList.remove("done"); }, 1400); };
        if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback);
        else fallback();
        function fallback() {
          var t = doc.createElement("textarea"); t.value = text; t.style.position = "fixed"; t.style.opacity = "0";
          doc.body.appendChild(t); t.select();
          try { doc.execCommand("copy"); done(); } catch (e) { /* ignore */ }
          t.remove();
        }
      });
      wrap.appendChild(b);
    });
  }

  /* ---------- lightbox for screenshots ---------- */
  function lightbox() {
    $$("a.shot-frame").forEach(function (a) {
      a.addEventListener("click", function (ev) {
        ev.preventDefault();
        var box = doc.createElement("div");
        box.className = "lightbox"; box.setAttribute("role", "dialog"); box.setAttribute("aria-label", "Screenshot");
        box.tabIndex = -1;
        var img = doc.createElement("img"); img.src = a.getAttribute("href"); img.alt = ($("img", a) || {}).alt || "";
        box.appendChild(img); doc.body.appendChild(box);
        requestAnimationFrame(function () { box.classList.add("open"); });
        box.focus();
        function close() { box.remove(); doc.removeEventListener("keydown", key); a.focus(); }
        function key(e) { if (e.key === "Escape") close(); }
        box.addEventListener("click", close); doc.addEventListener("keydown", key);
      });
    });
  }

  /* ---------- sidebar drawer on mobile ---------- */
  function drawer() {
    var btn = $(".menu-btn"), side = $(".sidebar");
    if (!btn || !side) { if (btn) btn.style.display = "none"; return; }
    var scrim = doc.createElement("div"); scrim.className = "scrim"; doc.body.appendChild(scrim);
    function set(open) {
      side.classList.toggle("open", open); scrim.classList.toggle("on", open);
      btn.setAttribute("aria-expanded", open ? "true" : "false");
    }
    btn.addEventListener("click", function () { set(!side.classList.contains("open")); });
    scrim.addEventListener("click", function () { set(false); });
    doc.addEventListener("keydown", function (e) { if (e.key === "Escape") set(false); });
    var cur = $("a[aria-current]", side);
    if (cur && cur.scrollIntoView && window.innerWidth > 860) { var top = cur.offsetTop - side.clientHeight / 2; if (top > 0) side.scrollTop = top; }
  }

  /* ---------- table of contents scrollspy ---------- */
  function scrollspy() {
    var links = $$(".toc a");
    if (!links.length || !("IntersectionObserver" in window)) return;
    var map = {};
    links.forEach(function (a) { map[a.getAttribute("href").slice(1)] = a; });
    var heads = Object.keys(map).map(function (id) { return doc.getElementById(id); }).filter(Boolean);
    var visible = {};
    var io = new IntersectionObserver(function (list) {
      list.forEach(function (en) { visible[en.target.id] = en.isIntersecting; });
      var on = heads.filter(function (h) { return visible[h.id]; })[0];
      if (!on) {
        var y = 120; on = heads[0];
        heads.forEach(function (h) { if (h.getBoundingClientRect().top < y) on = h; });
      }
      links.forEach(function (a) { a.classList.remove("on"); });
      if (on && map[on.id]) map[on.id].classList.add("on");
    }, { rootMargin: "-70px 0px -60% 0px" });
    heads.forEach(function (h) { io.observe(h); });
  }

  /* ---------- pointer spotlight on cards ---------- */
  function spotlight() {
    if (!finePointer) return;
    doc.addEventListener("pointermove", function (e) {
      var c = e.target.closest && e.target.closest(".card");
      if (!c) return;
      var r = c.getBoundingClientRect();
      c.style.setProperty("--mx", (e.clientX - r.left) + "px");
      c.style.setProperty("--my", (e.clientY - r.top) + "px");
    }, { passive: true });
  }

  /* ---------- terminal caret cursor ---------- */
  function caret() {
    if (!finePointer || reduced) return;
    var dot = doc.createElement("div"); dot.className = "cursor-dot"; doc.body.appendChild(dot);
    root.classList.add("cursor");
    doc.addEventListener("pointermove", function (e) {
      dot.style.transform = "translate(" + (e.clientX + 2) + "px," + (e.clientY - 2) + "px)";
      var hot = e.target.closest && e.target.closest("a,button,summary,input,[role=button]");
      dot.classList.toggle("hot", !!hot);
      if (e.target.closest && e.target.closest("input,textarea,pre")) { dot.style.opacity = "0"; root.classList.remove("cursor"); }
      else { dot.style.opacity = ""; root.classList.add("cursor"); }
    }, { passive: true });
    doc.addEventListener("pointerleave", function () { dot.style.transform = "translate(-100px,-100px)"; });
  }

  /* ---------- smooth scroll ---------- */
  function smooth() {
    if (reduced || typeof window.Lenis !== "function" || !finePointer) return;
    var lenis = new window.Lenis({ lerp: 0.12, wheelMultiplier: 1 });
    window.__lenis = lenis;
    function raf(t) { lenis.raf(t); requestAnimationFrame(raf); }
    requestAnimationFrame(raf);
    $$('a[href^="#"]').forEach(function (a) {
      a.addEventListener("click", function (e) {
        var id = a.getAttribute("href").slice(1); var t = id && doc.getElementById(id);
        if (!t) return;
        e.preventDefault(); lenis.scrollTo(t, { offset: -80 }); history.replaceState(null, "", "#" + id);
      });
    });
  }

  /* ---------- page curtain: View Transitions do the job over http(s); file:// gets this ---------- */
  function curtain() {
    if (reduced || !isFile) return;
    doc.addEventListener("click", function (e) {
      var a = e.target.closest && e.target.closest("a[href]");
      if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || a.target) return;
      var href = a.getAttribute("href");
      if (!/\.html(#.*)?$/.test(href) || /^[a-z]+:/i.test(href) && !/^file:/i.test(href)) return;
      if (href.indexOf("#") > -1 && href.split("#")[0] === "" ) return;
      e.preventDefault();
      var c = doc.createElement("div"); c.className = "curtain out";
      for (var i = 0; i < 6; i++) c.appendChild(doc.createElement("i"));
      doc.body.appendChild(c);
      setTimeout(function () { location.href = a.href; }, 300);
    });
    window.addEventListener("pageshow", function (e) { if (e.persisted) $$(".curtain").forEach(function (c) { c.remove(); }); });
  }

  /* ---------- search ---------- */
  function search() {
    var data = window.MATRIX_SEARCH || [];
    var dlg = $(".search"), input = $(".search input"), list = $(".search ul");
    if (!dlg) return;
    var sel = 0, results = [], opener = null;
    data.forEach(function (d) { d._t = fold(d.t + " " + (d.n || "")); d._x = fold(d.x); d._g = fold(d.g || ""); });
    function subseq(w, s) { var j = 0; for (var i = 0; i < s.length && j < w.length; i++) if (s[i] === w[j]) j++; return j === w.length; }
    function score(d, words) {
      var s = 0;
      for (var i = 0; i < words.length; i++) {
        var w = words[i];
        var ti = d._t.indexOf(w);
        if (ti === 0) s += 12; else if (ti > 0) s += 8;
        else if (d._g.indexOf(w) > -1) s += 4;
        else if (d._x.indexOf(w) > -1) s += 2;
        else if (w.length > 3 && subseq(w, d._t)) s += 1;
        else return 0;
      }
      if (d.k === "page") s += 5;
      return s;
    }
    function render() {
      var q = fold(input.value.trim());
      var words = q.split(/\s+/).filter(Boolean);
      if (!words.length) results = data.filter(function (d) { return d.k === "page"; }).slice(0, 9);
      else results = data.map(function (d) { return [score(d, words), d]; }).filter(function (p) { return p[0] > 0; })
        .sort(function (a, b) { return b[0] - a[0]; }).slice(0, 14).map(function (p) { return p[1]; });
      sel = 0;
      list.innerHTML = "";
      if (!results.length) { list.innerHTML = '<li class="empty">Nothing matches. Try a tool name like <code>rql-query</code> or a word like "lock".</li>'; return; }
      results.forEach(function (d, i) {
        var li = doc.createElement("li"); li.setAttribute("role", "option"); li.id = "sr" + i;
        var a = doc.createElement("a"); a.href = d.u;
        var kind = doc.createElement("span"); kind.className = "kind" + (d.k === "tool" ? " tool" : "");
        kind.textContent = d.k === "tool" ? "tool" : (d.k === "page" ? "page" : d.g);
        a.appendChild(kind); a.appendChild(doc.createTextNode(d.t));
        var sm = doc.createElement("small"); sm.textContent = d.x.length > 150 ? d.x.slice(0, 147) + "..." : d.x; a.appendChild(sm);
        li.appendChild(a); list.appendChild(li);
        li.addEventListener("mousemove", function () { if (sel !== i) { sel = i; mark(); } });
      });
      mark();
    }
    function mark() {
      $$("li", list).forEach(function (li, i) { li.setAttribute("aria-selected", i === sel ? "true" : "false"); });
      var cur = doc.getElementById("sr" + sel);
      if (cur) { input.setAttribute("aria-activedescendant", cur.id); cur.scrollIntoView({ block: "nearest" }); }
    }
    function open() { opener = doc.activeElement; dlg.classList.add("open"); input.value = ""; render(); input.focus(); if (window.__lenis) window.__lenis.stop(); }
    function close() { dlg.classList.remove("open"); if (window.__lenis) window.__lenis.start(); if (opener && opener.focus) opener.focus(); }
    $$(".search-btn").forEach(function (b) { b.addEventListener("click", open); });
    input.addEventListener("input", render);
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { e.preventDefault(); sel = Math.min(sel + 1, results.length - 1); mark(); }
      else if (e.key === "ArrowUp") { e.preventDefault(); sel = Math.max(sel - 1, 0); mark(); }
      else if (e.key === "Enter") { var r = results[sel]; if (r) { close(); location.href = r.u; } }
    });
    dlg.addEventListener("click", function (e) { if (e.target === dlg) close(); });
    doc.addEventListener("keydown", function (e) {
      var typing = /input|textarea|select/i.test((doc.activeElement || {}).tagName || "");
      if ((e.key === "k" && (e.ctrlKey || e.metaKey)) || (e.key === "/" && !typing)) { e.preventDefault(); open(); }
      else if (e.key === "Escape" && dlg.classList.contains("open")) close();
    });
  }

  /* ---------- tool catalog filters ---------- */
  function catalog() {
    var box = $("#tool-filter");
    if (!box) return;
    var tools = $$(".tool"), groups = $$(".tgroup");
    var state = { q: "", g: "all", r: "all" };
    var count = $(".tool-count"), empty = $(".empty-state");
    tools.forEach(function (t) { t._s = fold(t.getAttribute("data-s")); });
    function apply() {
      var words = fold(state.q).split(/\s+/).filter(Boolean), shown = 0;
      tools.forEach(function (t) {
        var ok = (state.g === "all" || t.getAttribute("data-g") === state.g) &&
          (state.r === "all" || t.getAttribute("data-r") === state.r || (state.r === "core" && t.hasAttribute("data-core"))) &&
          words.every(function (w) { return t._s.indexOf(w) > -1; });
        t.hidden = !ok; if (ok) shown++;
      });
      groups.forEach(function (g) { g.hidden = !$$(".tool", g).some(function (t) { return !t.hidden; }); });
      count.textContent = "Showing " + shown + " of " + tools.length + " tools";
      empty.hidden = shown > 0;
    }
    box.hidden = false;
    $("input", box).addEventListener("input", function (e) { state.q = e.target.value; apply(); });
    $$(".groups button", box).forEach(function (b) {
      b.addEventListener("click", function () {
        state.g = b.getAttribute("data-g");
        $$(".groups button", box).forEach(function (x) { x.setAttribute("aria-pressed", x === b ? "true" : "false"); });
        apply();
      });
    });
    $$(".seg button", box).forEach(function (b) {
      b.addEventListener("click", function () {
        state.r = b.getAttribute("data-r");
        $$(".seg button", box).forEach(function (x) { x.setAttribute("aria-pressed", x === b ? "true" : "false"); });
        apply();
      });
    });
    function openHash() {
      var id = decodeURIComponent(location.hash.slice(1)); var t = id && doc.getElementById(id);
      if (t && t.classList.contains("tool")) {
        t.open = true; t.hidden = false; t.parentElement.hidden = false;
        t.classList.add("flash"); setTimeout(function () { t.classList.remove("flash"); }, 2400);
        setTimeout(function () { t.scrollIntoView({ block: "center" }); }, 60);
      }
    }
    window.addEventListener("hashchange", openHash); openHash();
    apply();
  }

  /* ---------- packets on the home diagram wires ---------- */
  function wires() {
    var pk = $$(".wire .pk");
    if (!pk.length || reduced) return;
    var t0 = performance.now(), visible = true;
    if ("IntersectionObserver" in window) new IntersectionObserver(function (l) { visible = l[0].isIntersecting; }).observe($(".flow"));
    function frame(t) {
      if (visible && !doc.hidden) {
        pk.forEach(function (p, i) {
          var dir = p.classList.contains("c") ? 1 : -1;
          var u = ((t - t0) / 1600 + i * 0.37) % 1;
          var x = dir > 0 ? u * 100 : 100 - u * 100;
          p.setAttribute("cx", x);
          p.setAttribute("opacity", Math.sin(u * Math.PI).toFixed(2));
        });
      }
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }

  /* ---------- home: session demo ---------- */
  function demo() {
    var box = $("[data-demo]");
    if (!box) return;
    var tabs = $$(".demo-tabs button", box), trs = $$(".tr", box), timers = [], cur = 0, auto = true, seen = false;
    function clear() { timers.forEach(clearTimeout); timers = []; }
    function show(i, animate) {
      clear(); cur = i;
      tabs.forEach(function (t, k) { t.setAttribute("aria-selected", k === i ? "true" : "false"); t.tabIndex = k === i ? 0 : -1; });
      trs.forEach(function (tr, k) { tr.hidden = k !== i; });
      var tr = trs[i], parts = Array.prototype.slice.call(tr.children);
      if (!animate || reduced) { tr.classList.remove("typing"); parts.forEach(function (p) { p.classList.add("on"); }); return; }
      tr.classList.add("typing"); parts.forEach(function (p) { p.classList.remove("on"); });
      var t = 150;
      parts.forEach(function (p) {
        timers.push(setTimeout(function () { p.classList.add("on"); }, t));
        t += p.classList.contains("u") ? 650 : p.classList.contains("tc") ? 750 : 900;
      });
      if (auto) timers.push(setTimeout(function () { if (auto) show((cur + 1) % tabs.length, true); }, t + 4200));
    }
    tabs.forEach(function (t, i) {
      t.addEventListener("click", function () { auto = false; show(i, true); });
      t.addEventListener("keydown", function (e) {
        var d = e.key === "ArrowDown" || e.key === "ArrowRight" ? 1 : e.key === "ArrowUp" || e.key === "ArrowLeft" ? -1 : 0;
        if (!d) return;
        e.preventDefault(); auto = false;
        var n = (i + d + tabs.length) % tabs.length; tabs[n].focus(); show(n, true);
      });
    });
    show(0, false);
    if ("IntersectionObserver" in window && !reduced) {
      new IntersectionObserver(function (l) {
        if (l[0].isIntersecting && !seen) { seen = true; show(0, true); }
        if (!l[0].isIntersecting) { clear(); }
        else if (seen && !timers.length && auto) show(cur, true);
      }, { threshold: 0.35 }).observe(box);
    }
  }

  /* ---------- home: walkthrough console follows the step in view ---------- */
  function walk() {
    var steps = $$(".wstep"), lines = $$(".walk-body p"), st = $(".walk-state");
    if (!steps.length) return;
    var states = { 0: ["off", "NOT INSTALLED"], 1: ["ready", "INSTALLED"], 2: ["on", "ONLINE"], 3: ["on", "ONLINE - 1 agent"], 4: ["on", "WORKING"] };
    function set(n) {
      steps.forEach(function (s) { s.classList.toggle("on", +s.getAttribute("data-w") === n); });
      lines.forEach(function (p) {
        var w = +p.getAttribute("data-w");
        p.classList.toggle("shown", w <= n); p.classList.toggle("cur", w === n);
      });
      var s = states[n] || states[0];
      if (st) { st.setAttribute("data-s", s[0]); st.textContent = s[1]; }
    }
    if (!("IntersectionObserver" in window) || window.innerWidth <= 1100) { set(4); lines.forEach(function (p) { p.classList.add("cur"); }); return; }
    set(1);
    var io = new IntersectionObserver(function (list) {
      list.forEach(function (en) { if (en.isIntersecting) set(+en.target.getAttribute("data-w")); });
    }, { rootMargin: "-45% 0px -45% 0px" });
    steps.forEach(function (s) { io.observe(s); });
  }

  function boot() {
    copyButtons(); lightbox(); drawer(); scrollspy(); spotlight(); search(); catalog(); reveals(); curtain(); wires(); demo(); walk();
    smooth();
  }
  if (doc.readyState === "loading") doc.addEventListener("DOMContentLoaded", boot); else boot();
})();
