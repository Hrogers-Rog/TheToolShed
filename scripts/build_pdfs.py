"""Build this repository's PDF manuals from the Markdown in docs/.

Usage:  python scripts/build_pdfs.py
Needs:  pip install reportlab
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from md2pdf import build  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def P(*parts):
    return os.path.join(REPO, *parts)


MANUALS = [
    dict(out=P("docs", "pdf", "Toolshed-User-Manual.pdf"),
         title="Toolshed User Manual",
         subtitle="Service facilities, fuels, interchanges, and couplers for Railroader",
         sections=[("Getting Started", P("docs","GETTING_STARTED.md")),
                   ("Service Facilities", P("docs","SERVICE_FACILITIES.md")),
                   ("Oil and Wood Firing", P("docs","OIL_WOOD_FIRING.md")),
                   ("Link and Pin Couplers", P("docs","LINK_AND_PIN.md")),
                   ("Selective Interchanges", P("docs","SELECTIVE_INTERCHANGES.md"))]),
]


def main():
    ok = 0
    for m in MANUALS:
        os.makedirs(os.path.dirname(m["out"]), exist_ok=True)
        for _, p in m["sections"]:
            if not os.path.isfile(p):
                print("  ! missing section source: %s" % p)
        try:
            path, n = build(m["out"], m["title"], m["subtitle"], m["sections"])
            print("OK  %-44s %2d sections  %6.1f KB"
                  % (os.path.basename(path), n, os.path.getsize(path) / 1024.0))
            ok += 1
        except Exception as e:
            print("FAIL %-44s %s: %s" % (os.path.basename(m["out"]), type(e).__name__, e))
    print("")
    print("%d/%d manuals built" % (ok, len(MANUALS)))
    return 0 if ok == len(MANUALS) else 1


if __name__ == "__main__":
    raise SystemExit(main())
