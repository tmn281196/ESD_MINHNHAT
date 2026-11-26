"""Tiny helpers for the hand-drawn SVGs: boxes, arrows, labels in the noir palette (arch.css variables)."""
from xml.sax.saxutils import escape as esc

DEFS = '''<defs>
  <marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0 0 L10 5 L0 10 z" fill="currentColor"/>
  </marker>
  <marker id="ar-a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0 0 L10 5 L0 10 z" fill="#ffffff"/>
  </marker>
</defs>'''


def box(x, y, w, h, title, sub="", strong=False, dashed=False, center=False, fill="var(--surface-2)"):
    stroke = 'stroke="var(--accent)" stroke-width="2"' if strong else 'stroke="currentColor"'
    if dashed:
        stroke = 'stroke="var(--rule-strong)" stroke-dasharray="4 3"'
    s = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" {stroke}/>\n'
    if center:
        tx, anchor = x + w / 2, ' text-anchor="middle"'
    else:
        tx, anchor = x + 14, ""
    if sub:
        ty = y + h / 2 - 3
        s += f'<text x="{tx}" y="{ty}"{anchor} font-weight="600" fill="currentColor">{esc(title)}</text>\n'
        s += f'<text x="{tx}" y="{ty + 17}"{anchor} fill="var(--ink-2)" font-size="11">{esc(sub)}</text>\n'
    else:
        s += f'<text x="{tx}" y="{y + h / 2 + 4}"{anchor} font-weight="600" fill="currentColor">{esc(title)}</text>\n'
    return s


def heading(x, y, text, anchor="start", color="var(--accent)", size=14):
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-family="Google Sans, sans-serif" font-size="{size}" '
            f'font-weight="600" fill="{color}">{esc(text)}</text>\n')


def arrow(d, accent=False, both=False):
    color = 'stroke="var(--accent)"' if accent else 'stroke="currentColor"'
    marker = "url(#ar-a)" if accent else "url(#ar)"
    start = f' marker-start="{marker}"' if both else ""
    return f'<path d="{d}" {color} stroke-width="1.4" fill="none" marker-end="{marker}"{start}/>\n'


def label(x, y, text, accent=False, anchor="middle"):
    fill = "var(--accent)" if accent else "var(--ink-3)"
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-family="JetBrains Mono, monospace" font-size="10" '
            f'fill="{fill}">{esc(text)}</text>\n')


def figure(w, h, aria, inner, caption):
    return (f'<figure class="diagram">\n<svg viewBox="0 0 {w} {h}" role="img" aria-label="{esc(aria)}">\n{DEFS}\n'
            f'<g font-family="Google Sans, sans-serif" font-size="12.5">\n{inner}</g>\n</svg>\n'
            f'<figcaption>{caption}</figcaption>\n</figure>\n')
