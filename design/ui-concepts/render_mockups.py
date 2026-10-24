"""Build and render the v0.3 UI chrome concepts.

Outputs (all inside design/ui-concepts):
  a-graphite-normal.html / .png
  a-graphite-hover.html  / .png
  b-seamless-normal.html / .png
  b-seamless-hover.html  / .png
  c-editorial-normal.html / .png
  c-editorial-hover.html  / .png
  a-graphite-800x600.png
  measurements.json

Uses Edge headless. No source files are touched.
"""

from __future__ import annotations

import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time

ROOT = pathlib.Path(__file__).resolve().parent
EDGE = pathlib.Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")
PROFILE = pathlib.Path(tempfile.gettempdir()) / "opencode" / "edge-ui-concepts"
OUT = ROOT

# ---------------------------------------------------------------- icons

def svg(paths: str, cls: str = "ico", fill: bool = False, vb: str = "0 0 16 16") -> str:
    paint = 'fill="currentColor" stroke="none"' if fill else 'fill="none" stroke="currentColor"'
    return (
        f'<svg class="{cls}" viewBox="{vb}" {paint} stroke-width="1.4" '
        f'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{paths}</svg>'
    )


ICONS = {
    "file": svg('<path d="M4.3 2.3h4.3l3.1 3.1v8.3H4.3z"/><path d="M8.6 2.3v3.1h3.1"/><path d="M6 11.2l1.6-1.8 1.2 1.2 1.3-1.5"/>'),
    "folder": svg('<path d="M2.2 4.3h4.1l1.4 1.5h6.1v7.5H2.2z"/>'),
    "prev": svg('<path d="M10 3.5L5.5 8l4.5 4.5"/>'),
    "next": svg('<path d="M6 3.5L10.5 8 6 12.5"/>'),
    "fit": svg('<path d="M6.2 9.8L2.5 13.5M2.5 10.7v2.8h2.8M9.8 6.2l3.7-3.7M10.7 2.5h2.8v2.8M6.2 6.2L2.5 2.5M5.3 2.5H2.5v2.8M9.8 9.8l3.7 3.7M13.5 10.7v2.8h-2.8"/>'),
    "winfit": svg('<rect x="2" y="3" width="12" height="8.6" rx="1"/><rect x="5.1" y="5.4" width="5.8" height="3.8" rx=".4"/><path d="M5.6 13.5h4.8"/>'),
    "slide": svg('<path d="M5.4 3.4l7 4.6-7 4.6z" fill="currentColor" stroke-linejoin="round"/>'),
    "recent": svg('<circle cx="8" cy="8" r="5.9"/><path d="M8 4.7V8l2.3 1.4"/>'),
    "settings": svg('<circle cx="8" cy="8" r="2.2"/><path d="M8 1.9v1.5M8 12.6v1.5M1.9 8h1.5M12.6 8h1.5M3.7 3.7l1.1 1.1M11.2 11.2l1.1 1.1M12.3 3.7l-1.1 1.1M4.8 11.2l-1.1 1.1"/>'),
}

MARK = (
    '<svg class="appmark" viewBox="0 0 16 16" fill="none" stroke="currentColor" '
    'stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
    '<rect x="1.5" y="2.4" width="13" height="11.2" rx="2"/>'
    '<circle cx="5.6" cy="6.2" r="1.05"/>'
    '<path d="M2.4 11.7l3.2-3 2.4 2.1 2.2-2.4 3.4 3.3"/>'
    "</svg>"
)

MIN = svg('<path d="M0 5h10"/>', vb="0 0 10 10")
MAX = svg('<rect x=".5" y=".5" width="9" height="9"/>', vb="0 0 10 10")
CLOSE = svg('<path d="M.5.5l9 9M9.5.5l-9 9"/>', vb="0 0 10 10")


def window_controls() -> str:
    return (
        '<div class="window-controls">'
        f'<button class="wbtn" data-control="최소화" aria-label="최소화">{MIN}</button>'
        f'<button class="wbtn" data-control="최대화" aria-label="최대화">{MAX}</button>'
        f'<button class="wbtn close" data-control="닫기" aria-label="닫기">{CLOSE}</button>'
        "</div>"
    )


# ---------------------------------------------------------------- thumbnails

THUMBS = [
    ("22% 30%", "none", "scale(1)"),
    ("72% 24%", "hue-rotate(-14deg) saturate(1.12)", "scale(1.25)"),
    ("46% 72%", "brightness(.88) contrast(1.08)", "scale(1)"),
    ("84% 62%", "hue-rotate(12deg)", "scale(1)"),
    ("30% 50%", "saturate(.7) brightness(1.12)", "scale(1.45)"),
    ("62% 80%", "hue-rotate(-24deg) brightness(.92)", "scale(1.15)"),
    ("14% 74%", "saturate(1.3) contrast(.95)", "scale(1)"),
    ("90% 34%", "hue-rotate(24deg) brightness(1.02)", "scale(1.3)"),
]


def thumbs(state: str) -> str:
    out = []
    for i, (pos, filt, scale) in enumerate(THUMBS, start=1):
        cls = "thumb"
        if i == 4:
            cls += " selected"
        if state == "hover" and i == 6:
            cls += " hot"
        style = f"object-position:{pos};transform:{scale}"
        if filt != "none":
            style += f";filter:{filt}"
        out.append(
            f'<button class="{cls}" tabindex="-1" aria-label="이미지 {i}">'
            f'<img src="assets/img0.jpg" alt="" style="{style}"></button>'
        )
    return "\n        ".join(out)


INFO_META = "3840 × 2400 px&nbsp;&nbsp;·&nbsp;&nbsp;9.2 MP&nbsp;&nbsp;·&nbsp;&nbsp;100% (맞춤)"


def info_a() -> str:
    return (
        '<div class="info">'
        '<span class="rule"></span>'
        '<div class="txt"><div class="name">img0.jpg</div>'
        f'<div class="meta">{INFO_META}</div></div>'
        "</div>"
    )


def info_b() -> str:
    return (
        '<div class="info">'
        '<div class="name"><span class="dot"></span>img0.jpg</div>'
        f'<div class="meta">{INFO_META}</div>'
        "</div>"
    )


def info_c() -> str:
    return (
        '<div class="info">'
        '<span class="rule"></span>'
        '<div class="txt"><div class="name">img0.jpg</div>'
        '<div class="meta">3840 × 2400 PX&nbsp;&nbsp;·&nbsp;&nbsp;9.2 MP&nbsp;&nbsp;·&nbsp;&nbsp;100%</div></div>'
        "</div>"
    )


TAB_A = '<div class="tabhint"><kbd>Tab</kbd> 정보</div>'
TAB_B = '<div class="tabhint"><kbd>Tab</kbd> 정보</div>'
TAB_C = '<div class="tabhint">TAB&nbsp;&nbsp;&nbsp;정보</div>'


# ---------------------------------------------------------------- chrome

def btn(label: str, cls: str = "btn", title: str = "") -> str:
    t = f' title="{title}"' if title else ""
    return f'<button class="{cls}" data-control="{label}"{t}>{label}</button>'


def tool(inner: str, tip: str, hot: bool) -> str:
    cls = "tool hot" if hot else "tool"
    return f'<span class="{cls}">{inner}<span class="tip">{tip}</span></span>'


def chrome_a(state: str) -> str:
    hot = state == "hover"
    fit_btn = tool(
        f'<button class="btn{ " hot" if hot else ""}" data-control="맞춤" title="창에 맞춤 (0)">맞춤</button>',
        "창에 맞춤 <kbd>0</kbd>",
        hot,
    )
    return f"""
  <header class="titlebar">
    <div class="brand">{MARK}
      <div class="title"><b>img0.jpg</b><span class="dim"> — WC Image Viewer</span></div>
    </div>
    {window_controls()}
  </header>
  <div class="toolbar">
    <div class="row">
      {btn("파일 열기", title="파일 열기 (Ctrl+O)")}
      {btn("폴더 열기", title="폴더 열기")}
      <span class="sep"></span>
      {btn("이전", title="이전 (←)")}
      {btn("다음", title="다음 (→)")}
      <span class="sep"></span>
      {fit_btn}
      {btn("100%", title="실제 크기 (1)")}
      <div class="zoom" data-control="배율 입력" title="표시 배율">100%</div>
      {btn("창 맞춤", title="이미지 크기에 창 맞춤 (W)")}
      <span class="sep"></span>
      {btn("슬라이드", title="슬라이드쇼 (S)")}
      {btn("최근", title="최근 항목")}
      {btn("설정", title="설정")}
    </div>
  </div>"""


def chrome_b(state: str) -> str:
    hot = state == "hover"
    fit_btn = tool(
        f'<button class="iconbtn{ " hot" if hot else ""}" data-control="맞춤" title="창에 맞춤 (0)">{ICONS["fit"]}</button>',
        "창에 맞춤 <kbd>0</kbd>",
        hot,
    )
    return f"""
  <header class="bar">
    <div class="row">
      <div class="brand">{MARK}<span class="appname">WC Image Viewer</span></div>
      <span class="sep"></span>
      <button class="iconbtn" data-control="파일 열기" title="파일 열기 (Ctrl+O)">{ICONS["file"]}</button>
      <button class="iconbtn" data-control="폴더 열기" title="폴더 열기">{ICONS["folder"]}</button>
      <span class="sep"></span>
      <button class="iconbtn" data-control="이전" title="이전 (←)">{ICONS["prev"]}</button>
      <button class="iconbtn" data-control="다음" title="다음 (→)">{ICONS["next"]}</button>
      {fit_btn}
      <button class="iconbtn" data-control="100%" title="실제 크기 (1)"><span style="font-size:9.5px;font-weight:600;letter-spacing:-.02em">1:1</span></button>
      <div class="zoom" data-control="배율 입력" title="표시 배율">100%</div>
      <button class="iconbtn" data-control="창 맞춤" title="이미지 크기에 창 맞춤 (W)">{ICONS["winfit"]}</button>
      <span class="sep"></span>
      <button class="iconbtn" data-control="슬라이드" title="슬라이드쇼 (S)">{ICONS["slide"]}</button>
      <button class="iconbtn" data-control="최근" title="최근 항목">{ICONS["recent"]}</button>
      <button class="iconbtn" data-control="설정" title="설정">{ICONS["settings"]}</button>
      {window_controls()}
    </div>
  </header>"""


def chrome_c(state: str) -> str:
    hot = state == "hover"
    return f"""
  <header class="titlebar">
    <span class="appname">WC Image Viewer</span>
    {window_controls()}
  </header>
  <div class="toolbar">
    <div class="row">
      <div class="grp">
        {btn("파일 열기", title="파일 열기 (Ctrl+O)")}
        {btn("폴더 열기", title="폴더 열기")}
      </div>
      <div class="grp">
        {btn("이전", title="이전 (←)")}
        {btn("다음", title="다음 (→)")}
      </div>
      <div class="grp">
        {btn("맞춤", cls="btn hot" if hot else "btn", title="창에 맞춤 (0)")}
        {btn("100%", title="실제 크기 (1)")}
        <div class="zoom{' focus' if hot else ''}" data-control="배율 입력" title="표시 배율">100%</div>
        {btn("창 맞춤", title="이미지 크기에 창 맞춤 (W)")}
      </div>
      <div class="grp">
        {btn("슬라이드", title="슬라이드쇼 (S)")}
        {btn("최근", title="최근 항목")}
        {btn("설정", title="설정")}
      </div>
    </div>
  </div>"""


# ---------------------------------------------------------------- pages

def page(concept: str, state: str) -> str:
    if concept == "a":
        chrome = chrome_a(state)
        info = info_a()
        tab = TAB_A
        strip = f'<div class="thumbs">\n        {thumbs(state)}\n        <span class="count"><b>4</b> / 8</span>\n      </div>'
        stage = f"""
    <main class="stage">
      <div class="viewer">
        <img class="photo" src="assets/img0.jpg" alt="Windows bloom wallpaper">
        <div class="scrim-top"></div>
        {info}
        {tab}
      </div>
      {strip}
    </main>"""
    elif concept == "b":
        chrome = chrome_b(state)
        info = info_b()
        tab = TAB_B
        dock = f'<div class="thumbs">\n        {thumbs(state)}\n        <span class="count"><b>4</b> / 8</span>\n      </div>'
        stage = f"""
    <main class="stage">
      <div class="viewer">
        <img class="photo" src="assets/img0.jpg" alt="Windows bloom wallpaper">
        <div class="scrim-top"></div>
        {info}
        {tab}
        {dock}
      </div>
    </main>"""
    else:
        chrome = chrome_c(state)
        info = info_c()
        tab = TAB_C
        strip = f'<div class="thumbs">\n        {thumbs(state)}\n        <span class="count"><b>4</b> / 8</span>\n      </div>'
        stage = f"""
    <main class="stage">
      <div class="viewer">
        <img class="photo" src="assets/img0.jpg" alt="Windows bloom wallpaper">
        <div class="scrim-top"></div>
        {info}
        {tab}
      </div>
      {strip}
    </main>"""

    measure_js = r"""
  <script>
  (function () {
    var el = function (s) { return document.querySelector(s); };
    var w = function (n) { return n ? Math.round(n.getBoundingClientRect().width * 10) / 10 : 0; };
    var h = function (n) { return n ? Math.round(n.getBoundingClientRect().height * 10) / 10 : 0; };
    var row = el('.row');
    var kids = row ? Array.prototype.slice.call(row.children) : [];
    var gap = row ? (parseFloat(getComputedStyle(row).columnGap) || 0) : 0;
    var intrinsic = row ? Math.round(kids.reduce(function (a, n, i) {
      var cs = getComputedStyle(n);
      var ml = parseFloat(cs.marginLeft) || 0;
      var mr = parseFloat(cs.marginRight) || 0;
      if (n.classList.contains('window-controls')) { ml = 0; }
      return a + n.getBoundingClientRect().width + ml + mr + (i ? gap : 0);
    }, 0)) : null;
    var controls = Array.prototype.map.call(document.querySelectorAll('[data-control]'), function (n) {
      var r = n.getBoundingClientRect();
      return { n: n.getAttribute('data-control'), w: Math.round(r.width), h: Math.round(r.height) };
    });
    var thumb = el('.thumb');
    var strip = el('.thumbs');
    var data = {
      concept: '__CONCEPT__',
      state: '__STATE__',
      viewport: [window.innerWidth, window.innerHeight],
      titlebar: h(el('.titlebar')),
      toolbar: h(el('.toolbar')),
      mergedBar: h(el('.bar')),
      chrome: h(el('.titlebar')) + h(el('.bar')) + h(el('.toolbar')),
      viewer: h(el('.viewer')),
      strip: h(strip),
      stripMode: strip ? (getComputedStyle(strip).position === 'absolute' ? 'overlay' : 'flow') : 'none',
      row: row ? {
        client: Math.round(row.clientWidth),
        scroll: Math.round(row.scrollWidth),
        intrinsic: intrinsic,
        slack: Math.round(row.clientWidth - intrinsic)
      } : null,
      rowOverflow: row ? row.scrollWidth > row.clientWidth + 1 : null,
      thumb: thumb ? [Math.round(thumb.getBoundingClientRect().width), Math.round(thumb.getBoundingClientRect().height)] : null,
      controls: controls
    };
    var s = document.createElement('script');
    s.type = 'application/json';
    s.id = 'measure';
    s.textContent = JSON.stringify(data);
    document.body.appendChild(s);
  })();
  </script>"""

    measure_js = measure_js.replace("__CONCEPT__", concept.upper()).replace("__STATE__", state)

    return f"""<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="utf-8">
<title>WC Image Viewer — concept {concept.upper()} / {state}</title>
<link rel="stylesheet" href="mockup.css">
</head>
<body class="c-{concept} s-{state}">
  <div class="app c-{concept}">
{chrome}
{stage}
  </div>
{measure_js}
</body>
</html>
"""


# ---------------------------------------------------------------- render

def run_edge(args: list[str], timeout: int = 90) -> subprocess.CompletedProcess:
    PROFILE.mkdir(parents=True, exist_ok=True)
    cmd = [
        str(EDGE),
        "--headless",
        "--disable-gpu",
        "--hide-scrollbars",
        "--no-first-run",
        "--disable-extensions",
        "--force-device-scale-factor=1",
        f"--user-data-dir={PROFILE}",
    ] + args
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)


def screenshot(html: pathlib.Path, png: pathlib.Path, width: int, height: int) -> None:
    png.parent.mkdir(parents=True, exist_ok=True)
    if png.exists():
        png.unlink()
    res = run_edge([
        f"--screenshot={png}",
        f"--window-size={width},{height}",
        html.as_uri(),
    ])
    if not png.exists() or png.stat().st_size < 10000:
        raise RuntimeError(f"Edge screenshot failed for {html.name}\nSTDOUT:{res.stdout}\nSTDERR:{res.stderr}")


def measure(html: pathlib.Path, width: int, height: int) -> dict:
    # In dump-dom mode Edge counts the window frame (24 x 92 px here), while
    # --screenshot captures the content box. Compensate so measurements line up
    # with the screenshots, and assert the resulting viewport below.
    res = run_edge([
        "--dump-dom",
        f"--window-size={width + 24},{height + 92}",
        html.as_uri(),
    ], timeout=90)
    dom = res.stdout
    marker = 'id="measure">'
    idx = dom.find(marker)
    if idx < 0:
        raise RuntimeError(f"no measurement payload for {html.name}\nSTDERR:{res.stderr[:2000]}")
    start = idx + len(marker)
    end = dom.find("</script>", start)
    data = json.loads(dom[start:end])
    if data.get("viewport") != [width, height]:
        raise RuntimeError(f"viewport mismatch for {html.name}: {data.get('viewport')} != [{width}, {height}]")
    return data


def main() -> None:
    if not EDGE.exists():
        sys.exit(f"Edge not found: {EDGE}")

    concepts = ["a", "b", "c"]
    states = ["normal", "hover"]
    names = {"a": "a-graphite", "b": "b-seamless", "c": "c-editorial"}

    generated = []
    for c in concepts:
        for s in states:
            html = OUT / f"{names[c]}-{s}.html"
            html.write_text(page(c, s), encoding="utf-8")
            generated.append(html)

    measurements = {}
    for c in concepts:
        for s in states:
            html = OUT / f"{names[c]}-{s}.html"
            for (w, h) in ((1280, 800), (800, 600)):
                m = measure(html, w, h)
                measurements[f"{c}-{s}-{w}x{h}"] = m
                print(f"measured {names[c]}-{s} @ {w}x{h}: chrome={m['chrome']} row={m['row']} overflow={m['rowOverflow']}")

    (OUT / "measurements.json").write_text(
        json.dumps(measurements, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    shots = [
        (OUT / "a-graphite-normal.html", OUT / "a-graphite-normal.png", 1280, 800),
        (OUT / "a-graphite-hover.html", OUT / "a-graphite-hover.png", 1280, 800),
        (OUT / "b-seamless-normal.html", OUT / "b-seamless-normal.png", 1280, 800),
        (OUT / "b-seamless-hover.html", OUT / "b-seamless-hover.png", 1280, 800),
        (OUT / "c-editorial-normal.html", OUT / "c-editorial-normal.png", 1280, 800),
        (OUT / "c-editorial-hover.html", OUT / "c-editorial-hover.png", 1280, 800),
        (OUT / "a-graphite-normal.html", OUT / "a-graphite-800x600.png", 800, 600),
    ]
    for html, png, w, h in shots:
        screenshot(html, png, w, h)
        print(f"rendered {png.name} ({png.stat().st_size} bytes)")

    print("done")


if __name__ == "__main__":
    main()
