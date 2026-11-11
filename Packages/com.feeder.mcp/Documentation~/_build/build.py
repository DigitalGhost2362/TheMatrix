# -*- coding: utf-8 -*-
"""Builds the Matrix Bridge docs site into Documentation~/.

  python -I Documentation~/_build/build.py

Reads content/*.html (page bodies with a meta header), data/tools.json (dumped from the Editor's tool
registry), ../CHANGELOG.md and ../package.json, and writes every page from one shell template, the search
index and the cropped screenshots. Needs Pillow only for the screenshots step.
"""
import html
import json
import pathlib
import re
import unicodedata

HERE = pathlib.Path(__file__).resolve().parent
SITE = HERE.parent
PKG = SITE.parent
CONTENT = HERE / "content"
RAW = HERE / "shots-raw"
IMG = SITE / "assets" / "img"

NAV = [
    ("Get started", ["index", "install", "setup", "connect", "first-session"]),
    ("Everyday use", ["bridge-window", "skills-window", "queries", "editor-lock", "team"]),
    ("Going further", ["runtime-tools", "batch-ci", "hyper-testing", "health"]),
    ("Reference", ["tools", "reference", "troubleshooting", "changelog"]),
]
EXPERIMENTAL = {"hyper-testing"}

SERVER_TOOLS = [
    ("editor-health", "Health", "ReadOnly",
     "Says whether the Unity Editor is responsive, busy, reloading, stalled or not linked, and what holds it: open windows "
     "and dialogs, Matrix calls in flight and the last lines of the Editor log. Works while Unity is hung or closed."),
    ("tool-call", "Call by name", "Writes",
     "Calls a Unity tool by name when it was just switched on and the client has not listed it yet. Refused for tools the "
     "session already lists and for tools covered by Claude Code permission rules or hooks."),
    ("editor-lock-acquire", "Lock / Acquire", "Writes",
     "Claims the shared Editor for one burst of work (a compile, one play session, an edit-mode batch). Waits in a "
     "first-come queue when someone else holds it."),
    ("editor-lock-release", "Lock / Release", "Writes", "Gives the Editor back after a burst of work."),
    ("editor-lock-status", "Lock / Status", "ReadOnly", "Says who holds the Editor, for what, since when, and who waits."),
]

ASCII_MAP = {
    "—": " - ", "–": "-", "→": "->", "←": "<-", "⇒": "=>", "…": "...", "×": "x",
    "‘": "'", "’": "'", "“": '"', "”": '"', "≤": "<=", "≥": ">=", "•": "-", " ": " ",
}


def ascii_text(s):
    for k, v in ASCII_MAP.items():
        s = s.replace(k, v)
    return s


def esc(s):
    return html.escape(s, quote=True)


def slugify(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()
    s = re.sub(r"<[^>]+>", "", s)
    s = re.sub(r"[^a-zA-Z0-9]+", "-", s).strip("-").lower()
    return s or "section"


def strip_tags(s):
    return html.unescape(re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", s))).strip()


# ---------------------------------------------------------------- content pages

def read_page(slug):
    text = (CONTENT / f"{slug}.html").read_text(encoding="utf-8")
    m = re.match(r"\s*<!--(.*?)-->", text, re.S)
    meta = {}
    if m:
        for line in m.group(1).strip().splitlines():
            if ":" in line:
                k, v = line.split(":", 1)
                meta[k.strip()] = v.strip()
        text = text[m.end():]
    meta["body"] = text.strip()
    meta["slug"] = slug
    return meta


TOOL_NAMES = set()
IMG_SIZES = {}


def menu_html(path):
    parts = [p.strip() for p in path.split(">")]
    inner = '<span class="sep" aria-hidden="true"></span>'.join(f'<span class="m">{esc(p)}</span>' for p in parts)
    return f'<span class="menu" role="text" aria-label="{esc(" > ".join(parts))}"><span class="sr">{esc(" > ".join(parts))}</span><span aria-hidden="true" style="display:contents">{inner}</span></span>'


def tool_link(name):
    return f'<a class="tool-ref" href="tools.html#{esc(name)}"><code>{esc(name)}</code></a>'


def shot_html(spec):
    parts = [p.strip() for p in spec.split("|")]
    file, alt = parts[0], parts[1]
    caption = parts[2] if len(parts) > 2 else ""
    w, h = IMG_SIZES.get(file, (None, None))
    if w is None:
        raise SystemExit(f"missing image {file}")
    cap = f"<figcaption>{caption}</figcaption>" if caption else ""
    return (f'<figure class="shot rv"><a class="shot-frame" href="assets/img/{esc(file)}">'
            f'<img src="assets/img/{esc(file)}" alt="{esc(alt)}" width="{w}" height="{h}" loading="lazy" decoding="async"></a>{cap}</figure>')


def expand(body):
    body = re.sub(r"\[\[menu:(.+?)\]\]", lambda m: menu_html(html.unescape(m.group(1))), body)
    body = re.sub(r"\[\[tool:([a-z0-9-]+)\]\]", lambda m: tool_link(m.group(1)), body)
    body = re.sub(r"\[\[shot:(.+?)\]\]", lambda m: shot_html(m.group(1)), body)
    body = re.sub(r"<pre data-label=\"([^\"]*)\">(.*?)</pre>",
                  lambda m: f'<div class="code-wrap" data-label="{m.group(1)}"><pre>{m.group(2)}</pre></div>', body, flags=re.S)
    body = re.sub(r"(<table>.*?</table>)", r'<div class="table-wrap">\1</div>', body, flags=re.S)
    for name in re.findall(r'tools\.html#([a-z0-9-]+)', body):
        if name not in TOOL_NAMES:
            raise SystemExit(f"unknown tool link: {name}")
    return body


def add_ids(body):
    toc, seen = [], set()

    def fix(m):
        level, attrs, inner = m.group(1), m.group(2) or "", m.group(3)
        idm = re.search(r'id="([^"]+)"', attrs)
        hid = idm.group(1) if idm else slugify(strip_tags(inner))
        base, i = hid, 2
        while hid in seen:
            hid = f"{base}-{i}"
            i += 1
        seen.add(hid)
        if level == "2":
            toc.append((hid, strip_tags(inner)))
        attrs = re.sub(r'\s*id="[^"]+"', "", attrs)
        return f'<h{level}{attrs} id="{hid}">{inner}<a class="anchor" href="#{hid}" aria-label="Link to this section">#</a></h{level}>'

    body = re.sub(r"<h([23])((?:\s[^>]*)?)>(.*?)</h\1>", fix, body, flags=re.S)
    return body, toc


# ---------------------------------------------------------------- shell

LOGO = ('<svg viewBox="0 0 32 32" aria-hidden="true"><rect x="1" y="1" width="30" height="30" rx="8" fill="#06140b" stroke="#1d5a34"/>'
        '<path d="M9 10l6 6-6 6" fill="none" stroke="#00ff41" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>'
        '<rect x="19" y="9" width="3.2" height="14" rx="1.2" fill="#00ff41"/></svg>')
SEARCH_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/></svg>'
MENU_ICON = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16"/></svg>'
FONTS = ("https://fonts.googleapis.com/css2?family=Chakra+Petch:wght@500;600;700"
         "&family=IBM+Plex+Sans:ital,wght@0,400;0,500;0,600;1,400&family=JetBrains+Mono:wght@400;500;600&display=swap")


def shell(page, pages, version, main_html, home=False):
    slug = page["slug"]
    title = "Matrix Bridge docs" if home else f'{page["title"]} - Matrix Bridge docs'
    top = []
    for href, label, key in [("install.html", "Guide", "guide"), ("tools.html", "Tools", "tools"), ("changelog.html", "What's new", "changelog")]:
        cur = (key == "tools" and slug == "tools") or (key == "changelog" and slug == "changelog") or (
            key == "guide" and slug not in ("tools", "changelog", "index"))
        top.append(f'<a href="{href}"{" aria-current=\"page\"" if cur else ""}>{label}</a>')
    head_scripts = "<script>document.documentElement.classList.add('js')</script>"
    scripts = ['<script src="assets/search-index.js" defer></script>', '<script src="assets/vendor/lenis.min.js" defer></script>',
               '<script src="assets/site.js" defer></script>']
    if home:
        scripts.append('<script src="assets/vendor/three.min.js" data-three defer></script>')
        scripts.append('<script src="assets/hero.js" defer></script>')
    return f"""<!doctype html>
<html lang="en" data-page="{slug}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{esc(title)}</title>
<meta name="description" content="{esc(page.get("lead_plain", page.get("lead", "")))}">
<meta name="theme-color" content="#07090a">
<link rel="icon" href="assets/img/logo_window_icon.png">
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="{FONTS}">
<link rel="stylesheet" href="assets/style.css">
{head_scripts}
</head>
<body class="{"home" if home else "docpage"}">
<a class="skip" href="#main">Skip to content</a>
<header class="topbar">
  {"" if home else f'<button class="icon-btn menu-btn" type="button" aria-label="Open the page list" aria-expanded="false">{MENU_ICON}</button>'}
  <a class="brand" href="index.html" aria-label="Matrix Bridge docs home">{LOGO}<span>Matrix Bridge</span><span class="ver">v{esc(version)}</span></a>
  <nav class="topnav" aria-label="Main">{"".join(top)}</nav>
  <span class="spacer"></span>
  <button class="search-btn" type="button" aria-label="Search the docs">{SEARCH_ICON}<span class="lbl">Search docs and tools</span><kbd>Ctrl K</kbd></button>
</header>
<div class="progress" aria-hidden="true"></div>
{main_html}
<footer class="foot"><div class="in">
  <span>Matrix Bridge {esc(version)} - MIT license. These pages ship inside the package in <code>Documentation~</code>.</span>
  <span><a href="install.html">Install</a> - <a href="troubleshooting.html">Troubleshooting</a> - <a href="changelog.html">What's new</a></span>
</div></footer>
<div class="search" role="dialog" aria-modal="true" aria-label="Search">
  <div class="box">
    <input type="search" placeholder="Search pages and tools, e.g. lock, setup, rql-query" aria-label="Search" role="combobox" aria-controls="search-results" aria-expanded="true" autocomplete="off">
    <ul id="search-results" role="listbox"></ul>
    <div class="hint"><span><kbd>Up</kbd> <kbd>Down</kbd> move</span><span><kbd>Enter</kbd> open</span><span><kbd>Esc</kbd> close</span></div>
  </div>
</div>
{"".join(scripts)}
</body>
</html>
"""


def sidebar(pages, current):
    out = ['<aside class="sidebar" aria-label="Pages">']
    for group, slugs in NAV:
        out.append(f"<h3>{esc(group)}</h3><ul>")
        for s in slugs:
            p = pages[s]
            cur = ' aria-current="page"' if s == current else ""
            tag = '<span class="tag-x">exp</span>' if s in EXPERIMENTAL else ""
            out.append(f'<li><a href="{s}.html"{cur}>{esc(p["nav"])}{tag}</a></li>')
        out.append("</ul>")
    out.append("</aside>")
    return "".join(out)


def order():
    return [s for _, slugs in NAV for s in slugs]


def doc_page(page, pages, version, body, toc):
    slug = page["slug"]
    seq = order()
    i = seq.index(slug)
    prev_s = seq[i - 1] if i > 0 else None
    next_s = seq[i + 1] if i + 1 < len(seq) else None
    pager = '<nav class="pager" aria-label="Previous and next page">'
    if prev_s:
        pager += f'<a class="prev" href="{prev_s}.html"><small>Previous</small><span>{esc(pages[prev_s]["nav"])}</span></a>'
    if next_s:
        pager += f'<a class="next" href="{next_s}.html"><small>Next</small><span>{esc(pages[next_s]["nav"])}</span></a>'
    pager += "</nav>"
    toc_html = ""
    if len(toc) > 1:
        toc_html = '<nav class="toc" aria-label="On this page"><h3>On this page</h3><ul>' + "".join(
            f'<li><a href="#{h}">{esc(t)}</a></li>' for h, t in toc) + "</ul></nav>"
    else:
        toc_html = '<div class="toc" aria-hidden="true"></div>'
    group = next(g for g, slugs in NAV if slug in slugs)
    main = f"""<div class="layout">
{sidebar(pages, slug)}
<main id="main" class="doc">
  <p class="crumb">{esc(group)}</p>
  <h1>{page["title"]}</h1>
  <p class="lead">{page.get("lead", "")}</p>
  <div class="doc-body">
{body}
  </div>
  {pager}
</main>
{toc_html}
</div>"""
    return shell(page, pages, version, main)


# ---------------------------------------------------------------- tools page

def inline_md(s, names):
    s = esc(ascii_text(s))
    s = re.sub(r"`([^`]+)`", lambda m: f"<code>{m.group(1)}</code>", s)
    s = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"&#x27;([a-z0-9]+(?:-[a-z0-9]+)+)&#x27;",
               lambda m: tool_link(m.group(1)) if m.group(1) in names else f"<code>{m.group(1)}</code>", s)
    return s


def load_tools():
    tools = json.loads((HERE / "data" / "tools.json").read_text(encoding="utf-8"))
    for name, label, risk, desc in SERVER_TOOLS:
        tools.append({"name": name, "title": "Server / " + label, "group": "Server", "mono": "SV",
                      "blurb": "Answered by the Matrix server itself, even while Unity is busy, reloading or closed. Always on.",
                      "label": label, "summary": desc.split(". ")[0].rstrip(".") + ".", "description": desc, "risk": risk,
                      "tokens": 0, "core": False, "server": True, "companions": []})
    return tools


GROUP_ORDER = ["Server", "Query", "Scenes", "GameObjects", "Assets", "Prefabs", "Code & Tests", "Vision", "Editor",
               "Packages", "Profiler", "Rendering", "Animation", "Build", "VFX", "Batch", "Menu", "Matrix", "Hyper Testing"]


def tools_page(pages, version, tools):
    names = {t["name"] for t in tools}
    groups = {}
    for t in tools:
        groups.setdefault(t["group"], []).append(t)
    keys = sorted(groups, key=lambda g: (GROUP_ORDER.index(g) if g in GROUP_ORDER else 99, g))
    risk_label = {"ReadOnly": ("ro", "read-only"), "Writes": ("wr", "writes"), "Destructive": ("de", "destructive")}
    blocks = []
    for g in keys:
        items = groups[g]
        blurb = items[0].get("blurb") or ""
        rows = []
        for t in items:
            cls, rl = risk_label.get(t["risk"], ("wr", "writes"))
            tags = []
            if t.get("server"):
                tags.append('<span class="tag srv">server</span>')
            if t.get("core"):
                tags.append('<span class="tag core" title="Turned on by Setup">core</span>')
            tags.append(f'<span class="tag {cls}">{rl}</span>')
            tok = f'<span class="tok" title="Tokens this tool adds to every request while it is on">~{t["tokens"]} tokens</span>' if t["tokens"] else '<span class="tok">always on</span>'
            desc = inline_md(t["description"], names)
            summ = inline_md(t["summary"], names)
            summ = re.sub(r"<a class=\"tool-ref\" href=\"[^\"]+\">(<code>.*?</code>)</a>", r"\1", summ)
            comp = ""
            if t.get("companions"):
                comp = '<p class="with">Works with ' + ", ".join(tool_link(c) for c in t["companions"] if c in names) + "</p>"
            search = " ".join([t["name"], t["label"], t["group"], ascii_text(t["summary"]), rl, "core" if t.get("core") else ""])
            rows.append(f"""<details class="tool" id="{esc(t["name"])}" data-g="{esc(g)}" data-r="{cls}"{" data-core" if t.get("core") else ""} data-s="{esc(search)}">
<summary><span class="nm"><b>{esc(t["label"])}</b><code>{esc(t["name"])}</code></span><span class="sm">{summ}</span><span class="meta">{"".join(tags)}{tok}</span></summary>
<div class="body"><p>{desc}</p>{comp}</div></details>""")
        mono = esc(items[0].get("mono", ""))
        blocks.append(f'<section class="tgroup" data-g="{esc(g)}"><h2 id="group-{slugify(g)}"><span class="mono">{mono}</span>{esc(g)}</h2>'
                      f'<p>{esc(blurb)}</p>{"".join(rows)}</section>')
    group_btns = f'<button type="button" data-g="all" aria-pressed="true">All<span>{len(tools)}</span></button>' + "".join(
        f'<button type="button" data-g="{esc(g)}" aria-pressed="false">{esc(g)}<span>{len(groups[g])}</span></button>' for g in keys)
    core_n = sum(1 for t in tools if t.get("core"))
    filt = f"""<div class="tool-bar" id="tool-filter" hidden>
<div class="row"><label class="sr" for="tq">Filter tools</label><input id="tq" type="search" placeholder="Filter by name or what it does, e.g. screenshot, prefab, play" autocomplete="off">
<div class="seg" role="group" aria-label="Show"><button type="button" data-r="all" aria-pressed="true">All</button><button type="button" data-r="core" aria-pressed="false">Core</button><button type="button" data-r="ro" aria-pressed="false">Read-only</button><button type="button" data-r="wr" aria-pressed="false">Writes</button><button type="button" data-r="de" aria-pressed="false">Destructive</button></div></div>
<div class="groups" role="group" aria-label="Group">{group_btns}</div>
<p class="tool-count" aria-live="polite"></p>
</div>"""
    page = pages["tools"]
    intro = expand(page["body"])
    body = intro + filt + "".join(blocks) + '<div class="empty-state" hidden>No tool matches. Clear the filter or pick All.</div>'
    main = f"""<div class="layout wide">
{sidebar(pages, "tools")}
<main id="main" class="doc">
  <p class="crumb">Reference</p>
  <h1>{page["title"]}</h1>
  <p class="lead">{page["lead"].replace("{N}", str(len(tools))).replace("{CORE}", str(core_n))}</p>
  <div class="doc-body" style="max-width:none">{body}</div>
</main>
</div>"""
    page["lead_plain"] = strip_tags(page["lead"].replace("{N}", str(len(tools))).replace("{CORE}", str(core_n)))
    return shell(page, pages, version, main)


# ---------------------------------------------------------------- changelog

def md_inline(s):
    s = esc(ascii_text(s))
    s = re.sub(r"`([^`]+)`", r"<code>\1</code>", s)
    s = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', s)
    return s


def changelog_html():
    lines = (PKG / "CHANGELOG.md").read_text(encoding="utf-8").splitlines()
    out, items, cur, in_rel, para = [], [], None, False, []

    def flush_list():
        nonlocal cur
        if cur is not None:
            items.append(cur)
            cur = None
        if items:
            out.append("<ul>" + "".join(f"<li>{md_inline(i)}</li>" for i in items) + "</ul>")
            items.clear()

    def flush_para():
        if para:
            out.append(f"<p>{md_inline(' '.join(para))}</p>")
            para.clear()

    for line in lines:
        if line.startswith("# "):
            continue
        m = re.match(r"## \[([^\]]+)\](?: - (.+))?", line)
        if m:
            flush_list(); flush_para()
            if in_rel:
                out.append("</section>")
            ver, date = m.group(1), m.group(2) or ""
            label = "Not released yet" if ver.lower() == "unreleased" else date
            vid = "v-" + slugify(ver)
            out.append(f'<section class="release rv"><h2 id="{vid}">{esc(ver)} <small>{esc(label)}</small></h2>')
            in_rel = True
            continue
        if not in_rel:
            continue
        if line.startswith("### "):
            flush_list(); flush_para()
            out.append(f"<h3>{esc(line[4:].strip())}</h3>")
        elif re.match(r"^\s*- ", line):
            flush_para()
            if cur is not None:
                items.append(cur)
            cur = re.sub(r"^\s*- ", "", line)
        elif line.strip() == "":
            if cur is not None:
                items.append(cur)
                cur = None
            flush_para()
        elif cur is not None:
            cur += " " + line.strip()
        else:
            para.append(line.strip())
    flush_list(); flush_para()
    if in_rel:
        out.append("</section>")
    return "\n".join(out)


# ---------------------------------------------------------------- images

CROPS = {
    # raw file -> (output, box as fractions of width/height or None for full)
    "BridgeOnline.png": ("bridge-online.png", (0.30, 0.0, 0.70, 1.0)),
    "BridgeLocked.png": ("bridge-locked.png", (0.30, 0.0, 0.70, 1.0)),
    "MatrixSkillsWindow.png": ("skills-window.png", None),
    "SceneViewLocked.png": ("scene-view-locked.png", None),
}


def images():
    try:
        from PIL import Image
    except ImportError:
        Image = None
    for raw, (out, box) in CROPS.items():
        src, dst = RAW / raw, IMG / out
        if Image and src.exists() and (not dst.exists() or dst.stat().st_mtime < src.stat().st_mtime):
            im = Image.open(src).convert("RGB")
            if box:
                w, h = im.size
                im = im.crop((int(box[0] * w), int(box[1] * h), int(box[2] * w), int(box[3] * h)))
            im.save(dst, optimize=True)
    for p in IMG.glob("*.png"):
        try:
            from PIL import Image as I2
            IMG_SIZES[p.name] = I2.open(p).size
        except ImportError:
            IMG_SIZES[p.name] = (1200, 700)
    for p in IMG.glob("*.svg"):
        m = re.search(r'viewBox="0 0 (\d+) (\d+)"', p.read_text(encoding="utf-8"))
        IMG_SIZES[p.name] = (int(m.group(1)), int(m.group(2))) if m else (1200, 700)


# ---------------------------------------------------------------- main

def main():
    version = json.loads((PKG / "package.json").read_text(encoding="utf-8"))["version"]
    images()
    tools = load_tools()
    TOOL_NAMES.update(t["name"] for t in tools)
    pages = {s: read_page(s) for s in order()}
    for p in pages.values():
        p.setdefault("nav", p.get("title", p["slug"]))
        p["lead_plain"] = strip_tags(p.get("lead", ""))

    search = []
    for slug in order():
        p = pages[slug]
        if slug == "tools":
            out = tools_page(pages, version, tools)
            search.append({"k": "page", "t": p["title"], "x": p["lead_plain"], "u": "tools.html", "g": "Reference"})
        elif slug == "index":
            body = expand(p["body"].replace("{VERSION}", esc(version)))
            out = shell(p, pages, version, f'<main id="main">{body}</main>', home=True)
            search.append({"k": "page", "t": "Home", "x": p["lead_plain"], "u": "index.html", "g": "Get started"})
        else:
            raw = p["body"]
            if slug == "changelog":
                raw = raw.replace("{CHANGELOG}", changelog_html())
            body, toc = add_ids(expand(raw))
            if slug == "changelog":
                toc = toc[:8]
            out = doc_page(p, pages, version, body, toc)
            group = next(g for g, s in NAV if slug in s)
            search.append({"k": "page", "t": p["title"], "n": p["nav"], "x": p["lead_plain"], "u": f"{slug}.html", "g": group})
            if slug != "changelog":
                for hid, ht in toc:
                    sec = re.search(rf'id="{re.escape(hid)}".*?</h2>(.*?)(?=<h2|$)', body, re.S)
                    text = strip_tags(sec.group(1))[:220] if sec else ""
                    search.append({"k": "sec", "t": ht, "x": text, "u": f"{slug}.html#{hid}", "g": p["nav"]})
        (SITE / f"{slug}.html").write_text(out, encoding="utf-8", newline="\n")
    for t in tools:
        search.append({"k": "tool", "t": t["name"], "x": ascii_text(t["summary"]), "u": f'tools.html#{t["name"]}', "g": t["group"]})
    (SITE / "assets" / "search-index.js").write_text(
        "window.MATRIX_SEARCH = " + json.dumps(search, ensure_ascii=True, separators=(",", ":")) + ";\n", encoding="utf-8", newline="\n")
    print(f"built {len(order())} pages, {len(tools)} tools, {len(search)} search entries, v{version}")


if __name__ == "__main__":
    main()
