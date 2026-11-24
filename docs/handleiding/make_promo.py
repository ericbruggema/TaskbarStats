# -*- coding: utf-8 -*-
"""Promotie-PDF (2 pagina's A4, kort en krachtig) in het Nederlands en Engels.

Gebruik (vanuit de repo-map):  python docs/handleiding/make_promo.py
Maakt docs/TaskbarStats-Promo-nl.pdf en docs/TaskbarStats-Promo-en.pdf.
Vereist reportlab en Pillow; opmaakhulpjes komen uit manual_common.py.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import manual_common as mc
from manual_common import (ACCENT, DARK, INK, MUTED, SOFT, LINE, W, ROOT, VERSION, md, _prep, h2, body, small,
                           side_by_side, fig, table, N)
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import cm
from reportlab.pdfbase import pdfmetrics
from reportlab.platypus import (BaseDocTemplate, Frame, NextPageTemplate, PageBreak, PageTemplate, Paragraph,
                                Spacer, Table, TableStyle, KeepTogether)

URL_REL = "https://github.com/ericbruggema/TaskbarStats/releases/latest"
URL_GH = "https://github.com/ericbruggema/TaskbarStats"
URL_WEB = "https://taskbarstats.com"
HERO_H = 12.2 * cm

T = {
    "nl": dict(
        out="TaskbarStats-Promo-nl.pdf", title="TaskbarStats - Promo", badge="GRATIS  ·  OPEN SOURCE  ·  WINDOWS 10/11",
        tagline=["De vitale functies van je pc,", "vlak naast de klok."],
        cta="Download gratis", cta_sub="github.com/ericbruggema/TaskbarStats/releases/latest",
        why="Waarom TaskbarStats?",
        feats=[("Altijd in beeld", "Een klein widget naast het systeemvak: CPU, GPU, geheugen, netwerk, schijf, batterij, temperatuur, ping en FPS."),
               ("Klopt met Taakbeheer", "Zelfde bronnen als Taakbeheer, dus de cijfers zijn te vertrouwen. Zuinig: ca. 2% van één kern."),
               ("Dashboard en cockpit", "Een groot bureaublad-dashboard en een fullscreen cockpit met grafieken, sensoren en alle specificaties."),
               ("Voor gamers", "Gratis FPS-teller met 1%-low, mini-overlay en klik-door (Ctrl+Alt+W) zodat je klikken naar de game gaan."),
               ("16 thema's, ook je eigen", "Neon, Matrix, Game Boy, Dracula... of bouw je eigen thema met een JSON-editor. Delen kan met één bestand."),
               ("Uitbreidbaar met plugins", "Claude-verbruik, weer, locatie, uptime-monitor of je eigen script/REST-waarden. Veilig: zonder admin-rechten.")],
        shots="Zo ziet het eruit", s1="Bureaublad-dashboard", s2="Fullscreen-cockpit",
        install="Installeren in 3 stappen",
        steps=["**Download** `TaskbarStats-Setup-<versie>.exe` van de GitHub-releases (of via taskbarstats.com).",
               "**Start** de installer, bevestig de Windows-melding en kies of hij met Windows mee moet starten.",
               "**Klaar**: het widget verschijnt naast de klok. Rechtsklik → *Instellingen* om alles naar wens aan te passen."],
        req="Windows 10/11 (64-bit). Geen .NET-installatie, geen account, geen internet nodig. Stil installeren: `/VERYSILENT /AUTOSTART=yes`.",
        opts="Opties", opt_head=["Tonen", "Uiterlijk", "Waar"],
        opts_rows=[["CPU, GPU, geheugen, netwerk, schijven, batterij", "Cijfers, meters, balken of grafieken", "Taakbalk-widget"],
                   ["Temperaturen, klokfrequentie, schijf actief", "16 thema's + eigen thema's", "Bureaublad-dashboard (klik-door)"],
                   ["Ping, processen (apps/achtergrond), FPS", "Eigen kleuren, drempels, lettertype", "Fullscreen-cockpit met tour"],
                   ["Meldingen, maandlimiet netwerkdata", "Achtergrondafbeelding, Windows-thema volgen", "NL, EN, DE of eigen taal"]],
        tips="Tips en trucs",
        tips_list=["**Ctrl+Alt+F** opent de fullscreen-cockpit; klik op een tegel voor details, **I** toont alle specificaties.",
                   "**Ctrl+Alt+D** maakt het dashboard klik-door: levend bureaubladbehang achter je vensters.",
                   "Houd de muis 2 s op het widget voor de tooltip; **middelklik** kopieert alle info naar het klembord.",
                   "Thema's proefrijden met **Voorbeeld (10 s)**; deel een thema door het bestand te exporteren.",
                   "Widget kwijt? Menu → **Vastplakken aan systeemvak**. Nieuwe pc? **Backup** exporteren/importeren.",
                   "Plugin **Script / REST**: toon elke waarde op je widget (Home Assistant, NAS, koers...)."],
        more="Meer weten?", links_label=["Website", "Downloads en broncode", "Fouten en ideeën"],
        foot=f"TaskbarStats {VERSION}  ·  MIT-licentie  ·  door Eric Bruggema, gebouwd met Claude Code",
        note="Screenshots tonen de Engelse interface met verzonnen waarden; de app draait ook in het Nederlands en Duits."),
    "en": dict(
        out="TaskbarStats-Promo-en.pdf", title="TaskbarStats - Promo", badge="FREE  ·  OPEN SOURCE  ·  WINDOWS 10/11",
        tagline=["Your PC's vital signs,", "right next to the clock."],
        cta="Download for free", cta_sub="github.com/ericbruggema/TaskbarStats/releases/latest",
        why="Why TaskbarStats?",
        feats=[("Always in view", "A tiny widget next to the notification area: CPU, GPU, memory, network, disk, battery, temperature, ping and FPS."),
               ("Matches Task Manager", "Same sources as Task Manager, so you can trust the numbers. Frugal: about 2% of one core."),
               ("Dashboard and cockpit", "A large desktop dashboard and a fullscreen cockpit with graphs, sensors and all specifications."),
               ("Made for gamers", "A free FPS counter with 1% low, mini overlay and click-through (Ctrl+Alt+W) so your clicks reach the game."),
               ("16 themes, plus your own", "Neon, Matrix, Game Boy, Dracula... or build your own with the JSON editor. Share it as a single file."),
               ("Extend with plugins", "Claude usage, weather, location, uptime monitor or your own script/REST values. Safe: no admin rights.")],
        shots="What it looks like", s1="Desktop dashboard", s2="Fullscreen cockpit",
        install="Install in 3 steps",
        steps=["**Download** `TaskbarStats-Setup-<version>.exe` from the GitHub releases (or via taskbarstats.com).",
               "**Run** the installer, confirm the Windows prompt and choose whether it should start with Windows.",
               "**Done**: the widget appears next to the clock. Right-click → *Settings* to make it yours."],
        req="Windows 10/11 (64-bit). No .NET install, no account, no internet needed. Silent install: `/VERYSILENT /AUTOSTART=yes`.",
        opts="Options", opt_head=["Show", "Look", "Where"],
        opts_rows=[["CPU, GPU, memory, network, disks, battery", "Numbers, gauges, bars or graphs", "Taskbar widget"],
                   ["Temperatures, clock speed, disk active", "16 themes + your own", "Desktop dashboard (click-through)"],
                   ["Ping, processes (apps/background), FPS", "Own colours, thresholds, font", "Fullscreen cockpit with tour"],
                   ["Notifications, monthly network data limit", "Background image, follow the Windows theme", "Dutch, English, German or your own language"]],
        tips="Tips and tricks",
        tips_list=["**Ctrl+Alt+F** opens the fullscreen cockpit; click a tile for details, **I** shows all specifications.",
                   "**Ctrl+Alt+D** makes the dashboard click-through: living wallpaper behind your windows.",
                   "Hover 2 s over the widget for the tooltip; **middle-click** copies all info to the clipboard.",
                   "Test-drive themes with **Preview (10 s)**; share a theme by exporting its file.",
                   "Lost the widget? Menu → **Stick to notification area**. New PC? Export/import a **Backup**.",
                   "The **Script / REST** plugin shows any value on your widget (Home Assistant, NAS, exchange rate...)."],
        more="Learn more", links_label=["Website", "Downloads and source", "Bugs and ideas"],
        foot=f"TaskbarStats {VERSION}  ·  MIT licence  ·  by Eric Bruggema, built with Claude Code",
        note="Screenshots show the English interface with made-up values; the app also runs in Dutch and German."),
}


def link(url, text=None):
    return f'<link href="{url}" color="#0A84FF"><u>{text or url.replace("https://", "")}</u></link>'


def build(lang):
    t = T[lang]
    out = os.path.join(ROOT, "docs", t["out"])

    def draw_hero(c, doc):
        pw, ph = A4
        c.setFillColor(DARK)
        c.rect(0, ph - HERO_H, pw, HERO_H, stroke=0, fill=1)
        c.setFillColor(ACCENT)
        c.rect(0, ph - 0.45 * cm, pw, 0.45 * cm, stroke=0, fill=1)
        x = 2 * cm
        c.setFont("UI-B", 8.5)
        c.setFillColor(colors.HexColor("#7FB8FF"))
        c.drawString(x, ph - 1.6 * cm, t["badge"])
        c.setFont("UI-B", 40)
        c.setFillColor(colors.white)
        c.drawString(x, ph - 3.0 * cm, "TaskbarStats")
        c.setFont("UI", 15)
        c.setFillColor(colors.HexColor("#C9D6E2"))
        c.drawString(x, ph - 4.05 * cm, t["tagline"][0])
        c.drawString(x, ph - 4.75 * cm, t["tagline"][1])
        # widgetvoorbeelden
        y = ph - 6.3 * cm
        for name in ("widget-digital.png", "widget-gauges.png"):
            path, w, h = _prep(name)
            iw = pw - 4 * cm
            ih = iw * h / w
            c.drawImage(path, x, y - ih + 0.2 * cm, width=iw, height=ih)
            y -= ih + 0.15 * cm
        # downloadknop
        bw, bh = 7.0 * cm, 1.35 * cm
        bx, by = x, ph - HERO_H + 0.7 * cm
        c.setFillColor(ACCENT)
        c.roundRect(bx, by, bw, bh, 7, stroke=0, fill=1)
        c.setFillColor(colors.white)
        c.setFont("UI-B", 13)
        c.drawCentredString(bx + bw / 2, by + 0.74 * cm, t["cta"])
        c.setFont("UI", 7.2)
        c.drawCentredString(bx + bw / 2, by + 0.3 * cm, "GitHub Releases  ·  Windows 10/11")
        c.linkURL(URL_REL, (bx, by, bx + bw, by + bh), relative=0)
        c.setFont("UI", 9)
        c.setFillColor(colors.HexColor("#9FB3C8"))
        c.drawString(bx + bw + 0.6 * cm, by + 0.75 * cm, t["cta_sub"])
        c.drawString(bx + bw + 0.6 * cm, by + 0.3 * cm, "taskbarstats.com")
        c.linkURL(URL_WEB, (bx + bw + 0.6 * cm, by + 0.2 * cm, bx + bw + 4 * cm, by + 0.55 * cm), relative=0)
        footer(c)

    def footer(c):
        pw, ph = A4
        c.setFont("UI", 7.6)
        c.setFillColor(MUTED)
        c.drawString(2 * cm, 1.0 * cm, t["foot"])

    def draw_p2(c, doc):
        pw, ph = A4
        c.setFillColor(ACCENT)
        c.rect(0, ph - 0.25 * cm, pw, 0.25 * cm, stroke=0, fill=1)
        footer(c)

    doc = BaseDocTemplate(out, pagesize=A4, title=t["title"], author="Eric Bruggema",
                          subject="TaskbarStats - " + t["tagline"][0] + " " + t["tagline"][1])
    f1 = Frame(2 * cm, 1.6 * cm, W, A4[1] - HERO_H - 1.6 * cm - 0.7 * cm, id="f1", leftPadding=0, rightPadding=0,
               topPadding=0, bottomPadding=0)
    f2 = Frame(2 * cm, 1.6 * cm, W, A4[1] - 1.6 * cm - 1.2 * cm, id="f2", leftPadding=0, rightPadding=0,
               topPadding=0, bottomPadding=0)
    doc.addPageTemplates([PageTemplate(id="p1", frames=[f1], onPage=draw_hero),
                          PageTemplate(id="p2", frames=[f2], onPage=draw_p2)])

    cardt = ParagraphStyle("cardt", parent=body, fontName="UI-B", fontSize=10.5, leading=13, textColor=DARK, spaceAfter=2)
    cardb = ParagraphStyle("cardb", parent=body, fontSize=8.8, leading=11.8, spaceAfter=0)
    sec = ParagraphStyle("sec", parent=h2, spaceBefore=0, spaceAfter=6, fontSize=15)
    s = [Spacer(1, 0.35 * cm), Paragraph(t["why"], sec)]

    # kaartjes 2 x 3
    cells = [[Paragraph(md(a), cardt), Paragraph(md(b), cardb)] for a, b in t["feats"]]
    rows = []
    for i in range(0, 6, 2):
        rows.append([cells[i], cells[i + 1]])
    cw = W / 2
    tb = Table(rows, colWidths=[cw, cw])
    tb.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), SOFT), ("VALIGN", (0, 0), (-1, -1), "TOP"),
                            ("LINEBEFORE", (0, 0), (-1, -1), 5, colors.white), ("LINEBELOW", (0, 0), (-1, -1), 5, colors.white),
                            ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
                            ("TOPPADDING", (0, 0), (-1, -1), 8), ("BOTTOMPADDING", (0, 0), (-1, -1), 8)]))
    s.append(tb)
    s.append(Spacer(1, 0.35 * cm))
    s.append(Paragraph(t["shots"], sec))
    # dashboard (staand) en fullscreen-overzicht (liggend) naast elkaar op gelijke hoogte
    from reportlab.platypus import Image
    ph_img = 5.5 * cm
    cellsx = []
    for name, cap in (("dashboard.png", t["s1"]), ("fullscreen-overview.png", t["s2"])):
        path, w, h = _prep(name)
        cellsx.append([Image(path, width=ph_img * w / h, height=ph_img), Paragraph(md(cap), mc.caption)])
    shots = Table([cellsx], colWidths=[W * 0.36, W * 0.64])
    shots.setStyle(TableStyle([("VALIGN", (0, 0), (-1, -1), "TOP"), ("LEFTPADDING", (0, 0), (-1, -1), 0)]))
    s.append(shots)
    s += [NextPageTemplate("p2"), PageBreak()]

    # ---- pagina 2
    s.append(Paragraph(t["install"], sec))
    s += N(t["steps"])
    s.append(Spacer(1, 3))
    s.append(Paragraph(md(t["req"]), small))
    s.append(Spacer(1, 0.4 * cm))
    s.append(Paragraph(t["opts"], sec))
    s += table([t["opt_head"]] + t["opts_rows"], [0.36, 0.34, 0.30], first_bold=False)
    s += fig("themes-fps.png", {"nl": "Een greep uit de 16 meegeleverde thema's, hier met FPS in het widget.",
                                "en": "A selection of the 16 built-in themes, here with FPS in the widget."}[lang])
    s.append(Paragraph(t["tips"], sec))
    for tip in t["tips_list"]:
        s.append(Paragraph(md(tip), mc.bullet, bulletText="•"))
    s.append(Spacer(1, 0.4 * cm))
    s.append(Paragraph(t["more"], sec))
    lab = t["links_label"]
    s.append(Paragraph(f"<b>{lab[0]}:</b> {link(URL_WEB)} &nbsp;&nbsp; <b>GitHub:</b> {link(URL_GH)}", body))
    s.append(Paragraph(f"<b>{lab[2]}:</b> {link(URL_GH + '/issues')}", body))
    s.append(Paragraph(md(t["note"]), small))
    doc.build(s)
    print("geschreven:", out, os.path.getsize(out) // 1024, "KB")


if __name__ == "__main__":
    for lg in ("nl", "en"):
        build(lg)
