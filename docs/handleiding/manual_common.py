# -*- coding: utf-8 -*-
"""Gedeelde opmaak voor de handleiding (NL: make_handleiding.py, EN: make_manual_en.py).
Het script van een taal vult `L` (vaste teksten) en roept `build(story, out)` aan.

Vereist: reportlab en Pillow. Lettertypen: Segoe UI en Consolas uit C:\\Windows\\Fonts.
De screenshots komen uit docs/screenshots (verzonnen waarden, geen persoonlijke gegevens).
"""
import os
import re
import sys
import tempfile

from PIL import Image as PILImage
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import cm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, Frame, Image, KeepTogether, PageBreak,
                                PageTemplate, Paragraph, CondPageBreak, Preformatted, Spacer, Table,
                                TableStyle, NextPageTemplate)
from reportlab.platypus.tableofcontents import TableOfContents

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SHOTS = os.path.join(ROOT, "docs", "screenshots")
VERSION = "1.6.8"

# ---------------------------------------------------------------- lettertypen
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
pdfmetrics.registerFont(TTFont("UI", os.path.join(FONTS, "segoeui.ttf")))
pdfmetrics.registerFont(TTFont("UI-B", os.path.join(FONTS, "segoeuib.ttf")))
pdfmetrics.registerFont(TTFont("UI-I", os.path.join(FONTS, "segoeuii.ttf")))
pdfmetrics.registerFont(TTFont("UI-BI", os.path.join(FONTS, "segoeuiz.ttf")))
pdfmetrics.registerFont(TTFont("Mono", os.path.join(FONTS, "consola.ttf")))
pdfmetrics.registerFontFamily("UI", normal="UI", bold="UI-B", italic="UI-I", boldItalic="UI-BI")

# ---------------------------------------------------------------- kleuren
ACCENT = colors.HexColor("#0A84FF")
DARK = colors.HexColor("#0D1117")
INK = colors.HexColor("#1F2933")
MUTED = colors.HexColor("#5B6770")
SOFT = colors.HexColor("#EEF4FB")
LINE = colors.HexColor("#D5DEE8")
TIPBG = colors.HexColor("#EAF6EE")
TIPBAR = colors.HexColor("#2FA35B")
NOTEBG = colors.HexColor("#FFF6E5")
NOTEBAR = colors.HexColor("#F0A020")
CODEBG = colors.HexColor("#F4F6F8")

# ---------------------------------------------------------------- stijlen
body = ParagraphStyle("body", fontName="UI", fontSize=10, leading=14.2, textColor=INK,
                      spaceAfter=6, alignment=TA_LEFT)
small = ParagraphStyle("small", parent=body, fontSize=8.6, leading=11.5, textColor=MUTED, spaceAfter=0)
caption = ParagraphStyle("caption", parent=small, fontName="UI-I", alignment=1, spaceBefore=3, spaceAfter=10)
h1 = ParagraphStyle("h1", fontName="UI-B", fontSize=22, leading=26, textColor=DARK, spaceAfter=10)
h2 = ParagraphStyle("h2", fontName="UI-B", fontSize=14, leading=18, textColor=ACCENT,
                    spaceBefore=12, spaceAfter=5, keepWithNext=1)
h3 = ParagraphStyle("h3", fontName="UI-B", fontSize=10.6, leading=14, textColor=DARK,
                    spaceBefore=7, spaceAfter=2, keepWithNext=1)
lead = ParagraphStyle("lead", parent=body, fontSize=11.5, leading=16.5, textColor=MUTED, spaceAfter=10)
bullet = ParagraphStyle("bullet", parent=body, leftIndent=14, bulletIndent=3, spaceAfter=2.5)
cell = ParagraphStyle("cell", parent=body, fontSize=9, leading=12, spaceAfter=0)
cellb = ParagraphStyle("cellb", parent=cell, fontName="UI-B")
cellh = ParagraphStyle("cellh", parent=cell, fontName="UI-B", textColor=colors.white)
code = ParagraphStyle("code", fontName="Mono", fontSize=8.6, leading=11.5, textColor=INK)
toc1 = ParagraphStyle("toc1", fontName="UI-B", fontSize=12, leading=24, textColor=DARK)
toc2 = ParagraphStyle("toc2", fontName="UI", fontSize=9.5, leading=14, leftIndent=16, textColor=MUTED)

W = A4[0] - 4 * cm  # inhoudsbreedte

# vaste teksten per taal; ingevuld door het taalscript
L = {}


# ---------------------------------------------------------------- hulpjes
def md(t):
    """Mini-opmaak: **vet**, *cursief*, `code` -> reportlab-markup. Verder platte tekst (& < > worden ontsnapt)."""
    t = t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    t = re.sub(r"`([^`]+)`", r'<font name="Mono" size="8.8" color="#B0307A">\1</font>', t)
    t = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", t)
    t = re.sub(r"(?<![\w*])\*([^*\n]+)\*(?![\w*])", r"<i>\1</i>", t)
    return t


def P(t, st=body):
    return Paragraph(md(t), st)


def H1(t, new_page=False):
    """Hoofdstuk: op een nieuwe pagina, of doorlopend als er nog genoeg ruimte is (voorkomt halve lege pagina's)."""
    return [PageBreak() if new_page else CondPageBreak(9 * cm), Spacer(1, 0 if new_page else 14), Paragraph(md(t), h1)]


def H2(t):
    return Paragraph(md(t), h2)


def H3(t):
    return Paragraph(md(t), h3)


def B(items):
    return [Paragraph(md(i), bullet, bulletText="•") for i in items]


def N(items):
    return [Paragraph(md(i), bullet, bulletText=f"{n}.") for n, i in enumerate(items, 1)]


def box(t, kind="tip"):
    bg, bar, label = (TIPBG, TIPBAR, L["tip"]) if kind == "tip" else (NOTEBG, NOTEBAR, L["note"])
    para = Paragraph(f'<b><font color="{bar.hexval().replace("0x", "#")}">{label}</font></b>&nbsp; ' + md(t),
                     ParagraphStyle("b", parent=body, spaceAfter=0, fontSize=9.6, leading=13.5))
    tb = Table([[para]], colWidths=[W])
    tb.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), bg), ("LINEBEFORE", (0, 0), (0, -1), 3, bar),
        ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 10),
        ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7)]))
    return [tb, Spacer(1, 8)]


def codeblock(text):
    pre = Preformatted(text.strip("\n"), code)
    tb = Table([[pre]], colWidths=[W])
    tb.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), CODEBG), ("BOX", (0, 0), (-1, -1), 0.5, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 9), ("TOPPADDING", (0, 0), (-1, -1), 6),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 6)]))
    return [tb, Spacer(1, 8)]


def table(rows, widths, header=True, first_bold=True):
    data = []
    for r, row in enumerate(rows):
        out = []
        for c, v in enumerate(row):
            if header and r == 0:
                out.append(Paragraph(md(v), cellh))
            else:
                out.append(Paragraph(md(v), cellb if (c == 0 and first_bold) else cell))
        data.append(out)
    tb = Table(data, colWidths=[w * W for w in widths], repeatRows=1 if header else 0)
    st = [("VALIGN", (0, 0), (-1, -1), "TOP"), ("LINEBELOW", (0, 0), (-1, -1), 0.4, LINE),
          ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
          ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4)]
    if header:
        st += [("BACKGROUND", (0, 0), (-1, 0), ACCENT)]
        st += [("BACKGROUND", (0, i), (-1, i), SOFT) for i in range(2, len(rows), 2)]
    else:
        st += [("BACKGROUND", (0, i), (-1, i), SOFT) for i in range(0, len(rows), 2)]
    tb.setStyle(TableStyle(st))
    return [tb, Spacer(1, 9)]


_tmp = tempfile.mkdtemp(prefix="tsmanual_")


def _prep(name, max_px=1500):
    """Verklein grote screenshots zodat de PDF compact blijft (PNG, geen kwaliteitsverlies bij normaal formaat)."""
    src = os.path.join(SHOTS, name)
    im = PILImage.open(src)
    if im.width > max_px:
        im = im.resize((max_px, round(im.height * max_px / im.width)), PILImage.LANCZOS)
    dst = os.path.join(_tmp, name)
    im = im.convert("RGB").quantize(colors=256, method=PILImage.Quantize.MEDIANCUT, dither=PILImage.Dither.NONE) \
        if im.width > 1000 and name.startswith(("themes", "fullscreen")) else im
    im.save(dst, optimize=True)
    return dst, im.width, im.height


def fig(name, cap, width=None, max_h=None):
    path, w, h = _prep(name)
    width = width or W
    height = width * h / w
    if max_h and height > max_h:
        width, height = width * max_h / height, max_h
    img = Image(path, width=width, height=height)
    img.hAlign = "CENTER"
    # dunne rand zodat lichte screenshots niet wegvallen
    tb = Table([[img]], colWidths=[width + 2])
    tb.setStyle(TableStyle([("BOX", (0, 0), (-1, -1), 0.5, LINE), ("LEFTPADDING", (0, 0), (-1, -1), 0),
                            ("RIGHTPADDING", (0, 0), (-1, -1), 0), ("TOPPADDING", (0, 0), (-1, -1), 0),
                            ("BOTTOMPADDING", (0, 0), (-1, -1), 0)]))
    tb.hAlign = "CENTER"
    return [KeepTogether([tb, Paragraph(md(cap), caption)])]


def side_by_side(a, cap_a, b, cap_b, width=None):
    """Twee screenshots naast elkaar."""
    colw = (W - 10) / 2
    cells = []
    for name, cap in ((a, cap_a), (b, cap_b)):
        path, w, h = _prep(name)
        img = Image(path, width=colw, height=colw * h / w)
        cells.append([img, Paragraph(md(cap), caption)])
    tb = Table([[cells[0], cells[1]]], colWidths=[colw + 5, colw + 5])
    tb.setStyle(TableStyle([("VALIGN", (0, 0), (-1, -1), "TOP"), ("LEFTPADDING", (0, 0), (-1, -1), 0),
                            ("RIGHTPADDING", (0, 0), (-1, -1), 5)]))
    return [KeepTogether([tb])]


# ---------------------------------------------------------------- documentsjabloon
class Doc(BaseDocTemplate):
    def __init__(self, fn, **kw):
        super().__init__(fn, pagesize=A4, leftMargin=2 * cm, rightMargin=2 * cm, topMargin=2.2 * cm,
                         bottomMargin=2 * cm, title=L["title"],
                         author="Eric Bruggema", subject=L["subject"],
                         **kw)
        fr = Frame(self.leftMargin, self.bottomMargin, W, A4[1] - self.topMargin - self.bottomMargin, id="f",
                   leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        cover_fr = Frame(0, 0, A4[0], A4[1], id="c", leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates([PageTemplate(id="cover", frames=[cover_fr], onPage=draw_cover),
                               PageTemplate(id="page", frames=[fr], onPage=draw_page)])

    def afterFlowable(self, f):
        if isinstance(f, Paragraph):
            if f.style.name == "h1":
                self.notify("TOCEntry", (0, f.getPlainText(), self.page))


def draw_cover(c, doc):
    pw, ph = A4
    c.setFillColor(DARK)
    c.rect(0, 0, pw, ph, stroke=0, fill=1)
    c.setFillColor(ACCENT)
    c.rect(0, ph - 0.5 * cm, pw, 0.5 * cm, stroke=0, fill=1)
    c.setFillColor(colors.white)
    c.setFont("UI-B", 44)
    c.drawString(2.2 * cm, ph - 6.2 * cm, "TaskbarStats")
    c.setFont("UI", 18)
    c.setFillColor(colors.HexColor("#9FB3C8"))
    c.drawString(2.2 * cm, ph - 7.6 * cm, L["cover_title"])
    c.setFont("UI", 12.5)
    c.setFillColor(colors.HexColor("#C9D6E2"))
    y = ph - 9.2 * cm
    for line in L["cover_lines"]:
        c.drawString(2.2 * cm, y, line)
        y -= 0.65 * cm
    # widgetvoorbeeld
    path, w, h = _prep("widget-stijlen.png")
    iw = pw - 4.4 * cm
    c.drawImage(path, 2.2 * cm, 4.6 * cm, width=iw, height=iw * h / w, mask=None)
    c.setFont("UI", 10)
    c.setFillColor(colors.HexColor("#9FB3C8"))
    c.drawString(2.2 * cm, 2.6 * cm, L["cover_foot"])
    c.drawString(2.2 * cm, 2.0 * cm, "github.com/ericbruggema/TaskbarStats")


def draw_page(c, doc):
    pw, ph = A4
    c.setStrokeColor(LINE)
    c.setLineWidth(0.5)
    c.line(2 * cm, ph - 1.6 * cm, pw - 2 * cm, ph - 1.6 * cm)
    c.setFont("UI-B", 8.5)
    c.setFillColor(ACCENT)
    c.drawString(2 * cm, ph - 1.4 * cm, "TaskbarStats")
    c.setFont("UI", 8.5)
    c.setFillColor(MUTED)
    c.drawString(2 * cm + pdfmetrics.stringWidth("TaskbarStats", "UI-B", 8.5) + 8, ph - 1.4 * cm, L["header"])
    c.drawRightString(pw - 2 * cm, 1.2 * cm, f"{doc.page}")
    c.drawString(2 * cm, 1.2 * cm, "taskbarstats.com")




def build(story, out):
    doc = Doc(out)
    # multiBuild: twee rondes voor de paginanummers van de inhoudsopgave
    doc.multiBuild(story)
    print("geschreven:", out, os.path.getsize(out) // 1024, "KB")
