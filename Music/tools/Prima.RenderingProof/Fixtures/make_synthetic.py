"""Regenerate the original, licence-free two-system PDF used by the rendering gate."""
from pathlib import Path

commands = ["0.7 G 0.7 w"]
for top in (500, 320):
    for staff in (0, 52):
        for line in range(5):
            y = top - staff - line * 7
            commands.append(f"55 {y} m 735 {y} l S")
    commands.append(f"0 G 1 w 55 {top} m 55 {top - 80} l S")
    for bar in range(1, 5):
        x = 55 + bar * 136
        commands.append(f"{x} {top} m {x} {top - 80} l S")
    for note in range(12):
        x = 88 + note * 48
        y = top - 21 + (note % 5) * 3.5
        commands.append(f"0 g {x - 3} {y - 2} 7 4 re f {x + 4} {y} m {x + 4} {y + 25} l S")
    commands.append("0.7 G 0.7 w")
stream = ("\n".join(commands) + "\n").encode("ascii")
objects = [
    b"<< /Type /Catalog /Pages 2 0 R >>",
    b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << >> /Contents 4 0 R >>",
    b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"endstream",
]
pdf = bytearray(b"%PDF-1.4\n")
offsets = [0]
for index, body in enumerate(objects, 1):
    offsets.append(len(pdf))
    pdf.extend(f"{index} 0 obj\n".encode() + body + b"\nendobj\n")
xref = len(pdf)
pdf.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode())
for offset in offsets[1:]:
    pdf.extend(f"{offset:010d} 00000 n \n".encode())
pdf.extend(f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
Path(__file__).with_name("synthetic.pdf").write_bytes(pdf)
