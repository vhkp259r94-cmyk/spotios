#!/usr/bin/env python3
"""Genera site/source.json: la «fuente» de SideStore / AltStore para instalar y actualizar Nsound con un solo enlace.

Lo ejecuta el flujo de la IPA al publicar una versión. Uso:
  python tools/make_source.py --tag v0.8.0-beta.1 --version 0.8.0 --build 7 --ipa Nsound-0.8.0-beta.1-unsigned.ipa \
         --sha256 <hex> --size <bytes> [--date 2026-11-12] [--notes "texto"] [--out site/source.json]
`--version` y `--build` deben ser los de la app (CFBundleShortVersionString / CFBundleVersion): así SideStore detecta las actualizaciones.
"""
import argparse
import datetime
import json
import os

SITE = 'https://vhkp259r94-cmyk.github.io/spotios'


def build(a):
    repo = os.environ.get('GITHUB_REPOSITORY', 'vhkp259r94-cmyk/spotios')
    url = f'https://github.com/{repo}/releases/download/{a.tag}/{a.ipa}'
    date = a.date or datetime.date.today().isoformat()
    version = {
        'version': a.version, 'buildVersion': str(a.build), 'date': date,
        'localizedDescription': a.notes, 'downloadURL': url, 'size': int(a.size), 'minOSVersion': '16.0', 'sha256': a.sha256,
    }
    return {
        'name': 'Nsound', 'identifier': 'org.librestream.source', 'subtitle': 'Reproductor personal de música y vídeo',
        'description': 'Fuente de Nsound (beta). Proyecto personal y sin ánimo de lucro; no está afiliado a YouTube ni a ningún servicio de música.',
        'iconURL': f'{SITE}/logo.png', 'website': f'{SITE}/', 'tintColor': 'ff4d62',
        'apps': [{
            'name': 'Nsound', 'bundleIdentifier': 'org.librestream.LibreStream', 'developerName': 'Nsound',
            'subtitle': 'Música y vídeo sin cuentas ni suscripciones',
            'localizedDescription': 'Busca, guarda tus listas, importa desde otros servicios, mira la letra sincronizada y sigue escuchando con la pantalla apagada. Beta: puede tener fallos.',
            'iconURL': f'{SITE}/logo.png', 'tintColor': 'ff4d62',
            'screenshotURLs': [f'{SITE}/img/nsound/inicio.jpg', f'{SITE}/img/nsound/musica.jpg', f'{SITE}/img/nsound/reproduciendo.jpg'],
            # Formato antiguo (AltStore clásico) además del nuevo (versions)
            'version': a.version, 'versionDate': date, 'downloadURL': url, 'size': int(a.size),
            'versions': [version],
            'appPermissions': {'entitlements': [], 'privacy': {}},
        }],
        'news': [],
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--tag', required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--build', required=True)
    p.add_argument('--ipa', required=True)
    p.add_argument('--sha256', required=True)
    p.add_argument('--size', required=True)
    p.add_argument('--date')
    p.add_argument('--notes', default='Versión de pruebas (beta).')
    p.add_argument('--out', default='site/source.json')
    a = p.parse_args()
    data = build(a)
    with open(a.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')
    print('fuente escrita en', a.out, '->', a.version, f'({a.build})')


if __name__ == '__main__':
    main()
