#!/usr/bin/env python3
"""Writes minimal.pdf: a hand-written PDF 1.4, one A4 page, "Minimal PDF without content" in Helvetica 24 pt (a
standard Type 1 font, so nothing is embedded), five objects and a correct cross-reference table. No dependencies.
The localtest app embeds the file and answers every PDF request in minimal mode with it (see ../README.md).
Run from anywhere: python3 -I make-minimal-pdf.py; the output is deterministic."""
import os

TEXT = "Minimal PDF without content"
stream = f"BT /F1 24 Tf 72 769 Td ({TEXT}) Tj ET".encode("ascii")
objects = [
    b"<< /Type /Catalog /Pages 2 0 R >>",
    b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
    b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    b"<< /Length " + str(len(stream)).encode("ascii") + b" >>\nstream\n" + stream + b"\nendstream",
]
out = bytearray(b"%PDF-1.4\n")
offsets = []
for number, body in enumerate(objects, start=1):
    offsets.append(len(out))
    out += f"{number} 0 obj\n".encode("ascii") + body + b"\nendobj\n"
xref = len(out)
out += f"xref\n0 {len(objects) + 1}\n".encode("ascii") + b"0000000000 65535 f \n"
for offset in offsets:
    out += f"{offset:010d} 00000 n \n".encode("ascii")
out += f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode("ascii")
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "minimal.pdf"), "wb") as f:
    f.write(out)
