#!/usr/bin/env python3
"""Gera o .scs do TruckHub Economia Realista a partir do def.scs da SUA instalação.

Os arquivos do jogo NÃO ficam no repositório: este script lê a pasta `def` que você extraiu
do jogo, aplica só os valores de economy_profile.json e empacota cópias alteradas.
Se uma chave não existir (versão do jogo diferente), ele para e avisa em vez de adivinhar.

Uso:
  python build_mod.py --game ets2|ats --def-dir CAMINHO/PARA/def [--out arquivo.scs] [--check]
  --check  só valida e mostra o que mudaria, sem gerar o pacote.
"""
import argparse, json, pathlib, re, sys, zipfile

HERE = pathlib.Path(__file__).parent


class PatchError(Exception):
    pass


def set_scalar(text, key, value):
    rx = re.compile(rf"(?m)^(\s*{re.escape(key)}:\s*)(-?[\d.]+)([^\n]*)$")
    found = rx.findall(text)
    if len(found) != 1:
        raise PatchError(f"chave '{key}' encontrada {len(found)}x (esperado 1x)")
    old = found[0][1]
    return rx.sub(lambda m: f"{m.group(1)}{value}{m.group(3)}", text, count=1), old


def set_indexed(text, key, index, value):
    rx = re.compile(rf"(?m)^(\s*{re.escape(key)}\[{index}\]:\s*)(-?[\d.]+)([^\n]*)$")
    found = rx.findall(text)
    if len(found) != 1:
        raise PatchError(f"chave '{key}[{index}]' encontrada {len(found)}x (esperado 1x)")
    old = found[0][1]
    return rx.sub(lambda m: f"{m.group(1)}{value}{m.group(3)}", text, count=1), old


def add_brl(text, ratio):
    if re.search(r'currency_code\[\]:\s*"BRL"', text):
        raise PatchError("o jogo já possui BRL na lista de moedas")
    block = (
        '\n\tcurrency_code[]: "BRL"\n'
        f"\tcurrency_ratio[]: {ratio}\n"
        '\tcurrency_sign1[]: ""\n'
        '\tcurrency_sign2[]: "R$ "\n'
        '\tcurrency_sign3[]: ""\n'
    )
    m = list(re.finditer(r"\n\}\s*\n\}\s*$", text))
    if not m:
        raise PatchError("fim do arquivo economy_data.sii em formato inesperado")
    i = m[-1].start()
    return text[:i] + block + text[i:]


def scale_files(d, sub, key, factor, nd, skip_todo=False):
    """Multiplica `key` em todos os .sii/.sui de def/<sub>; devolve {caminho_no_pacote: texto}, nº de valores."""
    out, n = {}, 0
    rx = re.compile(rf"(?m)^(\s*(?:{key}):\s*)(-?[\d.]+)([^\n]*)$")
    for f in sorted((d / sub).glob("*.s*i")):
        if f.suffix not in (".sii", ".sui"):
            continue
        t = f.read_text(encoding="utf-8")
        cnt = [0]

        def rep(m):
            if skip_todo and "todo" in m.group(3).lower():
                return m.group(0)
            cnt[0] += 1
            v = round(float(m.group(2)) * factor, nd)
            return f"{m.group(1)}{int(v) if nd == 0 else v}{m.group(3)}"
        t2 = rx.sub(rep, t)
        if cnt[0]:
            out[f"def/{sub}/{f.name}"] = t2
            n += cnt[0]
    return out, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, choices=["ets2", "ats"])
    ap.add_argument("--def-dir", required=True, help="pasta def extraída do jogo")
    ap.add_argument("--out", default=None)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--profile", default=str(HERE / "economy_profile.json"))
    a = ap.parse_args()

    profile = json.loads(pathlib.Path(a.profile).read_text(encoding="utf-8"))["games"][a.game]
    out_path = pathlib.Path(a.out) if a.out else HERE / f"truckhub_economia_{a.game}.scs"
    d = pathlib.Path(a.def_dir)
    if not (d / "economy_data.sii").exists():
        sys.exit(f"economy_data.sii não encontrado em {d}")

    out_files = {}
    changes = []
    try:
        eco = (d / "economy_data.sii").read_text(encoding="utf-8")
        first = re.search(r'currency_code\[\]:\s*"([A-Z*]+)"', eco)
        if not first or first.group(1) != profile["game_currency"]:
            raise PatchError(
                f"o def informado parece ser de outro jogo (moeda padrão {first.group(1) if first else '?'}, "
                f"esperado {profile['game_currency']} para --game {a.game})")
        for key, spec in profile.get("economy_data", {}).items():
            eco, old = set_scalar(eco, key, spec["value"])
            changes.append(f"economy_data.{key}: {old} -> {spec['value']}")
        cur = profile.get("currency", {})
        if cur.get("add_brl"):
            eco = add_brl(eco, cur["brl_per_unit"])
            changes.append(f"currency: + BRL (1 {profile['game_currency']} = {cur['brl_per_unit']} R$)")
        out_files["def/economy_data.sii"] = eco

        pol = (d / "police_data.sii").read_text(encoding="utf-8")
        for idx, spec in profile.get("police_data", {}).get("fine_amounts", {}).items():
            pol, old = set_indexed(pol, "fine_amounts", idx, spec["value"])
            changes.append(f"police_data.fine_amounts[{idx}]: {old} -> {spec['value']}")
        out_files["def/police_data.sii"] = pol
        fuel = profile.get("fuel", {}).get("factor", 1.0)
        if fuel != 1.0:
            fo, n = scale_files(d, "country", "fuel_price", fuel, 5)
            if not n:
                raise PatchError("nenhum fuel_price em def/country")
            out_files.update(fo)
            changes.append(f"country.fuel_price: x{fuel} em {n} país(es)/estado(s)")
        ferry = profile.get("ferry", {}).get("factor", 1.0)
        if ferry != 1.0:
            fo, total = scale_files(d, "ferry/connection", "price(?:_bus)?", ferry, 0, skip_todo=True)
            out_files.update(fo)
            changes.append(f"ferry: x{ferry} em {total} preço(s)")
    except PatchError as e:
        sys.exit(f"ERRO: {e}\nO def.scs desta versão do jogo é diferente do esperado; nada foi gerado.")

    print("\n".join(changes))
    if a.check:
        print("OK (--check): nenhuma alteração gravada.")
        return

    out = out_path
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        manifest = (HERE / "manifest.sii").read_text(encoding="utf-8")
        manifest = re.sub(r'display_name:\s*"[^"]*"', f'display_name: "TruckHub Economia Realista ({profile["label"]})"', manifest)
        z.writestr("manifest.sii", manifest.encode("utf-8"))
        z.write(HERE / "mod_description.txt", "mod_description.txt")
        if (HERE / "mod_icon.jpg").exists():
            z.write(HERE / "mod_icon.jpg", "mod_icon.jpg")
        for name, content in out_files.items():
            z.writestr(name, content.encode("utf-8"))
    print(f"\n{out} gerado • {len(out_files)} arquivo(s) de definição")


if __name__ == "__main__":
    main()
