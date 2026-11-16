#!/usr/bin/env python3
"""Empacota o mod em um .scs (ZIP) pronto para a pasta mod do jogo.

Uso:  python build_mod.py [saida.scs]
Cada arquivo em def/ vira parte do pacote; manifest.sii e mod_description.txt
ficam na raiz. mod_icon.jpg é opcional (276x162 recomendado).
"""
import pathlib, sys, zipfile

root = pathlib.Path(__file__).parent
out = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root / "truckhub_economia.scs"
files = [root / "manifest.sii", root / "mod_description.txt"]
icon = root / "mod_icon.jpg"
if icon.exists():
    files.append(icon)
files += sorted(p for p in (root / "def").rglob("*") if p.is_file() and p.name != ".gitkeep")

with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for f in files:
        z.write(f, f.relative_to(root).as_posix())
print(f"{out} • {len(files)} arquivo(s)")
if not any(f.parts[-2] == "def" or "def" in f.relative_to(root).parts for f in files):
    print("AVISO: pasta def/ vazia — o pacote não altera nada no jogo ainda.")
