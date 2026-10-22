"""Generate icon design candidates for WC Image Viewer.

Run: python design/generate_icon_concepts.py
Outputs preview sheets to design/icon-concepts/.
"""

from __future__ import annotations

import math
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "icon-concepts")
ASSETS = os.path.join(HERE, "..", "src", "WcImageViewer", "Assets")
ICO_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48),
             (64, 64), (128, 128), (256, 256)]
FONT_BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
FONT_EXT = r"C:\Windows\Fonts\ariblk.ttf"
FONT_KR = r"C:\Windows\Fonts\malgunbd.ttf"
FONT_KR_REG = r"C:\Windows\Fonts\malgun.ttf"

APP = 1024
FILE = 512
CARD = (16, 16, 496, 496)
FOOTER_TOP = 306
TEXT_LEFT = 22
TEXT_WIDTH = 442

ACCENTS = {
    ".jpg": "#2F7DF6", ".jpeg": "#2F7DF6", ".jpe": "#2F7DF6",
    ".png": "#1FA36B", ".bmp": "#8A63D2", ".gif": "#E0559B",
    ".tif": "#12A6A6", ".tiff": "#12A6A6", ".ico": "#5B6B7F",
    ".zip": "#E39A2D", ".cbz": "#E39A2D", ".7z": "#E39A2D", ".cb7": "#E39A2D",
    ".rar": "#C96A38", ".cbr": "#C96A38",
}

SAMPLES = [".jpg", ".tiff", ".zip", ".cbz"]


# ---------------------------------------------------------------- helpers

def rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


def rgba(value: str, alpha: int = 255) -> tuple[int, int, int, int]:
    return (*rgb(value), alpha)


def mix(a, b, t: float):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def vertical_gradient(size, top, bottom) -> Image.Image:
    w, h = size
    img = Image.new("RGBA", size)
    d = ImageDraw.Draw(img)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=mix(top, bottom, y / max(1, h - 1)))
    return img


def horizontal_gradient(size, left, right) -> Image.Image:
    w, h = size
    img = Image.new("RGBA", size)
    d = ImageDraw.Draw(img)
    for x in range(w):
        d.line([(x, 0), (x, h)], fill=mix(left, right, x / max(1, w - 1)))
    return img


def rounded_mask(size, box, radius) -> Image.Image:
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    return mask


def shadow(base: Image.Image, box, radius, offset=(0, 14), blur=26, alpha=120, spread=0):
    x0, y0, x1, y1 = box
    mask = Image.new("L", base.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (x0 + offset[0] - spread, y0 + offset[1] - spread,
         x1 + offset[0] + spread, y1 + offset[1] + spread),
        radius=radius + spread, fill=alpha)
    mask = mask.filter(ImageFilter.GaussianBlur(blur))
    layer = Image.new("RGBA", base.size, (0, 0, 0, 255))
    layer.putalpha(mask)
    base.alpha_composite(layer)


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


# ---------------------------------------------------------------- artwork

def scene(w: int, h: int) -> Image.Image:
    """Classic mountain photo."""
    img = vertical_gradient((w, h), rgba("#A9D2F2"), rgba("#EFF8FF"))
    d = ImageDraw.Draw(img)

    sun_x, sun_y, sun_r = w * 0.72, h * 0.28, w * 0.095
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse(
        (sun_x - sun_r * 2.4, sun_y - sun_r * 2.4,
         sun_x + sun_r * 2.4, sun_y + sun_r * 2.4),
        fill=rgba("#FFCF6B", 90))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(w * 0.05)))
    d = ImageDraw.Draw(img)
    d.ellipse((sun_x - sun_r, sun_y - sun_r, sun_x + sun_r, sun_y + sun_r),
              fill=rgba("#FFC24B"))

    far = [(0, h * 0.76), (w * 0.19, h * 0.50), (w * 0.40, h * 0.76),
           (w * 0.62, h * 0.46), (w * 0.84, h * 0.74), (w, h * 0.58),
           (w, h), (0, h)]
    d.polygon(far, fill=rgba("#6E97B5"))

    near = [(0, h * 0.90), (w * 0.24, h * 0.66), (w * 0.47, h * 0.91),
            (w * 0.70, h * 0.68), (w, h * 0.86), (w, h), (0, h)]
    d.polygon(near, fill=rgba("#2E5675"))
    return img


def photo_card(w: int, h: int, border: int, radius: int) -> Image.Image:
    card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=radius, fill=rgba("#FCFCFD"))
    inset = border
    inner = (inset, inset, w - inset - 1, h - inset - 1)
    inner_radius = max(2, radius - int(border * 0.7))
    picture = scene(inner[2] - inner[0] + 1, inner[3] - inner[1] + 1)
    card.paste(picture, (inner[0], inner[1]),
               rounded_mask((picture.width, picture.height),
                            (0, 0, picture.width - 1, picture.height - 1), inner_radius))
    d.rounded_rectangle(inner, radius=inner_radius, outline=(10, 30, 50, 34), width=2)
    return card


def squircle(size: int, light: bool = False) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    if light:
        fill = horizontal_gradient((size, size), rgba("#F8F9FB"), rgba("#E2E7EF"))
    else:
        fill = vertical_gradient((size, size), rgba("#2B3344"), rgba("#0C0F16"))
    mask = rounded_mask((size, size), (0, 0, size - 1, size - 1), int(size * 0.225))
    img.paste(fill, (0, 0), mask)

    d = ImageDraw.Draw(img)
    d.rounded_rectangle((2, 2, size - 3, size - 3), radius=int(size * 0.225),
                        outline=rgba("#FFFFFF", 34 if not light else 150), width=3)
    if not light:
        # top gloss
        gloss = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ImageDraw.Draw(gloss).ellipse(
            (-size * 0.25, -size * 0.72, size * 1.25, size * 0.22),
            fill=rgba("#FFFFFF", 26))
        img.alpha_composite(gloss.filter(ImageFilter.GaussianBlur(size * 0.02)))
    else:
        shine = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ImageDraw.Draw(shine).ellipse(
            (-size * 0.4, -size * 0.9, size * 1.4, size * 0.05),
            fill=rgba("#FFFFFF", 120))
        img.alpha_composite(shine.filter(ImageFilter.GaussianBlur(size * 0.03)))
    return img


def magnifier(img: Image.Image, cx: float, cy: float, r: float, ring: float):
    glass = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glass)
    gd.ellipse((cx - r, cy - r, cx + r, cy + r), fill=rgba("#8FD4FF", 34))
    img.alpha_composite(glass.filter(ImageFilter.GaussianBlur(r * 0.02)))

    d = ImageDraw.Draw(img)
    angle = math.radians(45)
    hx, hy = cx + math.cos(angle) * (r + ring * 0.2), cy + math.sin(angle) * (r + ring * 0.2)
    ex, ey = cx + math.cos(angle) * (r * 1.62), cy + math.sin(angle) * (r * 1.62)
    d.line([(hx, hy), (ex, ey)], fill=rgba("#F6FBFF"), width=int(ring * 1.85))
    d.line([(hx, hy), (ex, ey)], fill=rgba("#1B2430"), width=int(ring * 1.45))
    d.line([(hx, hy), (ex - math.cos(angle) * ring * 0.5,
                       ey - math.sin(angle) * ring * 0.5)],
           fill=rgba("#4A6078"), width=int(ring * 0.5))
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=rgba("#F6FBFF"), width=int(ring))
    d.ellipse((cx - r + ring * 0.55, cy - r + ring * 0.55,
               cx + r - ring * 0.55, cy + r - ring * 0.55),
              outline=rgba("#7FC8F0", 120), width=max(2, int(ring * 0.16)))
    d.arc((cx - r * 0.84, cy - r * 0.84, cx + r * 0.84, cy + r * 0.84),
          start=198, end=252, fill=rgba("#FFFFFF", 210), width=max(3, int(ring * 0.34)))


def app_icon(concept: int) -> Image.Image:
    size = APP
    if concept == 4:
        img = squircle(size, light=True)
    else:
        img = squircle(size)

    if concept == 1:
        w = int(size * 0.60)
        card = photo_card(w, w, int(size * 0.043), int(size * 0.058))
        shadow(img, ((size - w) // 2, (size - w) // 2 + int(size * 0.01),
                     (size + w) // 2, (size + w) // 2 + int(size * 0.01)),
               int(size * 0.06), offset=(0, 18), blur=30, alpha=150)
        card = card.rotate(-4, resample=Image.BICUBIC, expand=True)
        img.alpha_composite(card, ((size - card.width) // 2, (size - card.height) // 2))

    elif concept == 2:
        back_a = photo_card(560, 560, 34, 46).rotate(-14, resample=Image.BICUBIC, expand=True)
        back_b = photo_card(580, 580, 34, 46).rotate(9, resample=Image.BICUBIC, expand=True)
        back_a.putalpha(back_a.getchannel("A").point(lambda a: int(a * 0.55)))
        back_b.putalpha(back_b.getchannel("A").point(lambda a: int(a * 0.78)))
        img.alpha_composite(back_a, (int(size * 0.12), int(size * 0.14)))
        img.alpha_composite(back_b, (int(size * 0.30), int(size * 0.10)))

        w = 590
        shadow(img, ((size - w) // 2, (size - w) // 2 + 10,
                     (size + w) // 2, (size + w) // 2 + 10),
               int(size * 0.062), offset=(0, 20), blur=34, alpha=165)
        card = photo_card(w, w, 38, 48)
        img.alpha_composite(card, ((size - w) // 2, (size - w) // 2 + 6))

    elif concept == 3:
        w = 570
        x = int(size * 0.13)
        y = int(size * 0.15)
        shadow(img, (x, y, x + w, y + w), int(size * 0.06),
               offset=(0, 18), blur=32, alpha=160)
        card = photo_card(w, w, 38, 50)
        img.alpha_composite(card, (x, y))
        magnifier(img, size * 0.665, size * 0.665, size * 0.195, size * 0.045)

    elif concept == 4:
        d = ImageDraw.Draw(img)
        w_font = font(FONT_BOLD, 640)
        d.text((size * 0.485, size * 0.455), "W", font=w_font, fill=rgba("#171C26"), anchor="mm")
        bar = horizontal_gradient((330, 30), rgba("#2F7DF6"), rgba("#34D3C8"))
        mask = rounded_mask((330, 30), (0, 0, 329, 29), 15)
        img.paste(bar, (int(size * 0.5 - 165), int(size * 0.77)), mask)

    return img


def draw_extension_text(card: Image.Image, ext: str, light_text: bool = True) -> None:
    """Extension as large as the icon width allows, anchored bottom-left."""
    text = ext.lstrip(".").upper()
    d = ImageDraw.Draw(card)
    probe = font(FONT_EXT, 100)
    box100 = d.textbbox((0, 0), text, font=probe)
    w100 = box100[2] - box100[0]
    h100 = box100[3] - box100[1]
    by_width = int(TEXT_WIDTH / w100 * 100)
    by_height = int((card.height - (FOOTER_TOP - CARD[1]) - 34) / (h100 / 100))
    size = max(24, min(by_width, by_height))
    face = font(FONT_EXT, size)
    box = d.textbbox((0, 0), text, font=face)
    baseline_y = card.height - 16 - box[3]

    fill = rgba("#FFFFFF")
    stroke = (8, 12, 20, 64) if light_text else None
    if len(text) == 2:
        # Two glyphs only: spread them across the full label width.
        first_box = d.textbbox((0, 0), text[0], font=face)
        second_box = d.textbbox((0, 0), text[1], font=face)
        positions = (
            (text[0], TEXT_LEFT - first_box[0]),
            (text[1], TEXT_LEFT + TEXT_WIDTH - second_box[2]),
        )
        for glyph, x in positions:
            d.text((x, baseline_y), glyph, font=face, fill=fill,
                   stroke_width=4 if stroke else 0, stroke_fill=stroke)
        return

    d.text((TEXT_LEFT - box[0], baseline_y), text, font=face, fill=fill,
           stroke_width=4 if stroke else 0, stroke_fill=stroke)


def file_icon(concept: int, ext: str) -> Image.Image:
    img = Image.new("RGBA", (FILE, FILE), (0, 0, 0, 0))
    accent = ACCENTS.get(ext, "#2F7DF6")
    card_w = CARD[2] - CARD[0]
    card_h = CARD[3] - CARD[1]
    radius = 58
    footer_y = FOOTER_TOP - CARD[1]
    card_mask = rounded_mask((card_w, card_h), (0, 0, card_w - 1, card_h - 1), radius)

    shadow(img, CARD, radius, offset=(0, 10), blur=16, alpha=110)

    if concept == 2:
        back = Image.new("RGBA", (card_w, card_h), (0, 0, 0, 0))
        ImageDraw.Draw(back).rounded_rectangle(
            (0, 0, card_w - 1, card_h - 1), radius=radius, fill=rgba("#DDE3EB", 235))
        img.alpha_composite(back, (CARD[0] - 14, CARD[1] - 14))
        img.alpha_composite(back, (CARD[0] - 7, CARD[1] - 7))

    card = Image.new("RGBA", (card_w, card_h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(card)
    if concept in (1, 2, 3):
        draw.rounded_rectangle((0, 0, card_w - 1, card_h - 1), radius=radius,
                               fill=rgba("#FCFCFD"))
        photo_box = (16, 16, card_w - 17, footer_y)
        photo_mask = Image.new("L", (photo_box[2] - photo_box[0] + 1,
                                     photo_box[3] - photo_box[1] + 1), 0)
        ImageDraw.Draw(photo_mask).rounded_rectangle(
            (0, 0, photo_mask.width - 1, photo_mask.height - 1), radius=38, fill=255)
        ImageDraw.Draw(photo_mask).rectangle(
            (0, 38, photo_mask.width - 1, photo_mask.height - 1), fill=255)
        card.paste(scene(photo_mask.width, photo_mask.height),
                   (photo_box[0], photo_box[1]), photo_mask)
    else:
        card.paste(scene(card_w, card_h), (0, 0), card_mask)

    footer = Image.new("RGBA", (card_w, card_h), (0, 0, 0, 0))
    foot_draw = ImageDraw.Draw(footer)
    if concept == 4:
        foot_draw.rectangle((0, footer_y, card_w - 1, card_h - 1),
                            fill=rgba("#121722", 247))
        foot_draw.rectangle((0, footer_y, card_w - 1, footer_y + 7), fill=rgba(accent))
    else:
        foot_draw.rectangle((0, footer_y, card_w - 1, card_h - 1), fill=rgba(accent))
    card.alpha_composite(footer)

    card.putalpha(Image.composite(card.getchannel("A"),
                                  Image.new("L", card.size, 0), card_mask))
    draw = ImageDraw.Draw(card)
    if concept == 4:
        draw.rounded_rectangle((1, 1, card_w - 2, card_h - 2), radius=radius - 1,
                               outline=rgba("#FFFFFF", 70), width=3)
    else:
        draw.rectangle((0, footer_y, card_w - 1, footer_y + 2), fill=(0, 0, 0, 36))

    draw_extension_text(card, ext, light_text=True)
    img.alpha_composite(card, (CARD[0], CARD[1]))

    if concept == 3:
        magnifier(img, 392, 110, 74, 20)
    return img


# ---------------------------------------------------------------- sheet

def paste_scaled(dst: Image.Image, src: Image.Image, size: int, x: int, y: int):
    dst.alpha_composite(src.resize((size, size), Image.LANCZOS), (x, y))


def make_sheet(concept: int, slug: str, title: str, subtitle: str, badge_note: str) -> None:
    W, H = 1440, 840
    sheet = Image.new("RGBA", (W, H), (27, 27, 30, 255))
    d = ImageDraw.Draw(sheet)
    d.text((48, 34), title, font=font(FONT_KR, 34), fill=rgba("#F2F2F4"))
    d.text((48, 86), subtitle, font=font(FONT_KR_REG, 20), fill=rgba("#9A9AA2"))
    d.text((48, 116), badge_note, font=font(FONT_KR_REG, 17), fill=rgba("#6F6F78"))

    # app icon panel
    d.text((48, 168), "앱 아이콘", font=font(FONT_KR, 17), fill=rgba("#B9B9C2"))
    app = app_icon(concept)
    paste_scaled(sheet, app, 256, 48, 202)
    for i, s in enumerate((64, 48, 32, 24, 16)):
        paste_scaled(sheet, app, s, 328 + i * 70, 202 + (64 - s) // 2)
    d.text((328, 282), "64  48  32  24  16", font=font(FONT_KR_REG, 14), fill=rgba("#77777F"))

    # file icons
    d.text((510, 168), "파일 아이콘", font=font(FONT_KR, 17), fill=rgba("#B9B9C2"))
    files = {ext: file_icon(concept, ext) for ext in SAMPLES}
    for i, ext in enumerate(SAMPLES):
        x = 510 + i * 224
        paste_scaled(sheet, files[ext], 176, x, 202)
        d.text((x + 88, 390), ext.lstrip(".").upper(), font=font(FONT_BOLD, 17),
               fill=rgba(ACCENTS[ext]), anchor="ma")
        for j, s in enumerate((48, 32, 16)):
            paste_scaled(sheet, files[ext], s, x + 10 + j * 58, 416)

    # light background / explorer mock
    d.rounded_rectangle((48, 484, W - 48, H - 40), radius=16, fill=(244, 245, 247, 255))
    d.text((72, 506), "밝은 배경 · 실제 크기", font=font(FONT_KR, 16), fill=(90, 92, 100, 255))
    paste_scaled(sheet, app, 128, 78, 548)
    paste_scaled(sheet, app, 48, 224, 570)
    paste_scaled(sheet, app, 24, 224, 630)

    names = {".jpg": "sunset.jpg", ".tiff": "scan.tiff", ".zip": "photos.zip", ".cbz": "comic.cbz"}
    for i, ext in enumerate(SAMPLES):
        x = 330 + i * 262
        paste_scaled(sheet, files[ext], 112, x, 548)
        d.text((x + 124, 568), names[ext], font=font(FONT_KR_REG, 15), fill=(35, 37, 43, 255))
        for j, s in enumerate((48, 32, 16)):
            paste_scaled(sheet, files[ext], s, x + 124 + j * 48, 596)
        d.text((x + 124, 660), "48   32   16", font=font(FONT_KR_REG, 11), fill=(150, 152, 160, 255))

    # explorer row
    paste_scaled(sheet, files[".jpg"], 32, 900, 744)
    d.text((940, 746), "IMG_2041.JPG", font=font(FONT_KR_REG, 16), fill=(35, 37, 43, 255))
    paste_scaled(sheet, files[".zip"], 32, 1130, 744)
    d.text((1170, 746), "photos.zip", font=font(FONT_KR_REG, 16), fill=(35, 37, 43, 255))

    sheet.convert("RGB").save(os.path.join(OUT, f"{concept:02d}-{slug}.png"), quality=95)


def write_gallery():
    cards = [
        ("01-frame.png", "시안 1 · 프레임", "사진 프레임 + 가로를 채우는 확장자",
         "가장 무난하고 정직한 모양. 흰 프레임 덕분에 밝은 탐색기와 어두운 배경 모두에서 사진으로 즉시 읽히고, 확장자는 아래 띠에 꽉 차게 들어갑니다.",
         "흔한 편", True),
        ("02-stack.png", "시안 2 · 스택", "겹친 카드 + 가로를 채우는 확장자",
         "여러 장을 넘겨 보는 뷰어 느낌이 가장 강합니다. 뒤쪽 카드가 살짝 비쳐 보여 파일이 여러 개 겹친 듯한 인상을 줍니다.",
         "16px에서 겹친 카드가 다소 뭉개짐", False),
        ("03-lens.png", "시안 3 · 렌즈", "사진 + 돋보기 + 가로를 채우는 확장자",
         "이미지 뷰어라는 기능이 한눈에 보입니다. 돋보기는 사진 영역에만 있어 확장자 띠를 가리지 않습니다.",
         "작은 크기에서 요소가 많음", False),
        ("04-mono.png", "시안 4 · 모노", "W 모노그램 + 다크 확장자 배너",
         "앱은 밝은 바탕의 W로 어느 배경에서나 또렷하고, 파일 아이콘은 사진 + 다크 배너로 확장자 대비가 가장 강합니다.",
         "사진 앱 느낌은 상대적으로 약함", False),
    ]

    card_html = ""
    for image, title, subtitle, body, con, recommended in cards:
        badge = '<span class="pick">추천</span>' if recommended else ""
        card_html += f"""
      <article class="card">
        <a href="{image}" target="_blank"><img src="{image}" alt="{title}"></a>
        <div class="body">
          <h2>{title} {badge}</h2>
          <p class="sub">{subtitle}</p>
          <p>{body}</p>
          <p class="con">단점 · {con}</p>
        </div>
      </article>"""

    html = f"""<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<title>WC Image Viewer 아이콘 시안</title>
<style>
  :root {{ color-scheme: dark; }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; padding: 40px 32px 64px; background: #161619; color: #E8E8EC;
         font: 15px/1.6 "Malgun Gothic", "Segoe UI", sans-serif; }}
  header {{ max-width: 1320px; margin: 0 auto 28px; }}
  h1 {{ margin: 0 0 6px; font-size: 28px; letter-spacing: -0.02em; }}
  header p {{ margin: 0; color: #9A9AA4; }}
  .hint {{ margin-top: 10px; color: #D6B36A; font-size: 13px; }}
  .grid {{ max-width: 1320px; margin: 0 auto; display: grid; gap: 22px; }}
  .card {{ background: #1F1F23; border: 1px solid #2E2E34; border-radius: 14px; overflow: hidden; }}
  .card img {{ display: block; width: 100%; height: auto; background: #1B1B1E; }}
  .body {{ padding: 18px 22px 22px; }}
  h2 {{ margin: 0; font-size: 19px; }}
  .sub {{ margin: 2px 0 8px; color: #A9A9B3; font-size: 13px; }}
  .body p {{ margin: 6px 0; }}
  .con {{ color: #8C8C96; font-size: 13px; }}
  .pick {{ display: inline-block; margin-left: 6px; padding: 1px 8px; border-radius: 99px;
           background: #1F4D3D; color: #7FE0B4; font-size: 12px; vertical-align: 2px; }}
  footer {{ max-width: 1320px; margin: 26px auto 0; color: #77777F; font-size: 13px; }}
</style>
</head>
<body>
  <header>
    <h1>WC Image Viewer · 아이콘 시안</h1>
    <p>앱 아이콘과 파일 연결 아이콘 4가지 방향입니다. 확장자 글자는 사진 아래 풀폭 띠에 크게 들어갑니다.</p>
    <p class="hint">사진을 클릭하면 원본 크기로 열립니다. 시트 안에 256 / 64 / 48 / 32 / 16px 실제 크기를 함께 배치했습니다.</p>
  </header>
  <div class="grid">{card_html}
  </div>
  <footer>선택한 시안으로 앱 아이콘(.ico)과 15개 확장자 아이콘을 만들고, exe·창·파일 연결에 적용합니다.</footer>
</body>
</html>"""
    with open(os.path.join(OUT, "gallery.html"), "w", encoding="utf-8") as handle:
        handle.write(html)


def write_final():
    """Write the selected concept (1) as production .ico assets."""
    icon_dir = os.path.join(ASSETS, "FileIcons")
    os.makedirs(icon_dir, exist_ok=True)
    app_path = os.path.join(ASSETS, "app.ico")
    app_icon(1).save(app_path, format="ICO", sizes=ICO_SIZES)

    for ext in ACCENTS:
        file_icon(1, ext).save(os.path.join(icon_dir, ext.lstrip(".") + ".ico"),
                               format="ICO", sizes=ICO_SIZES)

    produced = [app_path] + [os.path.join(icon_dir, ext.lstrip(".") + ".ico") for ext in ACCENTS]
    for path in produced:
        with Image.open(path) as image:
            assert image.format == "ICO", path
            assert len(image.info["sizes"]) >= len(ICO_SIZES) - 1, path
    assert len(os.listdir(icon_dir)) == len(ACCENTS), "unexpected icon count"
    print(f"wrote {len(produced)} icons to {ASSETS}")


def main():
    os.makedirs(OUT, exist_ok=True)
    concepts = [
        (1, "frame", "시안 1  프레임", "사진 프레임 · 가로를 채우는 확장자", "사진 위, 확장자는 아래 풀폭 띠에 크게. 가장 무난하고 확장자가 한눈에 들어옵니다."),
        (2, "stack", "시안 2  스택", "겹친 카드 · 가로를 채우는 확장자", "여러 장을 다루는 뷰어 느낌. 뒤쪽 카드가 살짝 비쳐 보입니다."),
        (3, "lens", "시안 3  렌즈", "사진 + 돋보기 · 가로를 채우는 확장자", "이미지 뷰어라는 기능이 바로 보입니다. 돋보기는 사진 영역에만 들어갑니다."),
        (4, "mono", "시안 4  모노", "다크 배너 · 가로를 채우는 확장자", "앱은 밝은 바탕의 W, 파일은 사진 + 다크 배너. 작은 크기에서 확장자가 가장 또렷합니다."),
    ]
    for concept, slug, title, subtitle, note in concepts:
        make_sheet(concept, slug, title, subtitle, note)
        app_icon(concept).resize((256, 256), Image.LANCZOS).save(
            os.path.join(OUT, f"app-{concept}.png"))
        for ext in SAMPLES:
            file_icon(concept, ext).resize((256, 256), Image.LANCZOS).save(
                os.path.join(OUT, f"file-{concept}-{ext.lstrip('.')}.png"))
    write_gallery()
    print("done:", OUT)


if __name__ == "__main__":
    if "--final" in sys.argv:
        write_final()
    else:
        main()
