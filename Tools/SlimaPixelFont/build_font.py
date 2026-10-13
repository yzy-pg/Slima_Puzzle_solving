"""Build Slima's four real TrueType faces from pixel masks. License: OFL-1.1."""
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import shutil
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.utmp/fontdeps'))
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

SOURCE = Path(__file__).parent / 'source'
OUTPUT = ROOT / 'Assets/_Game/UI/Resources/SlimaUI/Fonts'
FACES = [('Regular', False, False), ('Bold', True, False), ('Italic', False, True), ('BoldItalic', True, True)]

# Original Slima alphabet: squared bowls, clipped corners, wide M/W, open counters.
# One character cell uses a 12-pixel em. Capitals/digits rise 9 pixels.
CAPS = {
    'A': '.#####./##...##/#.....#/#.....#/#######/#.....#/#.....#/#.....#/#.....#',
    'B': '######./#.....#/#.....#/#.....#/######./#.....#/#.....#/#.....#/######.',
    'C': '.#####./#.....#/#....../#....../#....../#....../#....../#.....#/.#####.',
    'D': '#####../#....#./#.....#/#.....#/#.....#/#.....#/#.....#/#....#./#####..',
    'E': '#######/#....../#....../#....../######./#....../#....../#....../#######',
    'F': '#######/#....../#....../#....../######./#....../#....../#....../#......',
    'G': '.#####./#.....#/#....../#....../#..####/#.....#/#.....#/#.....#/.#####.',
    'H': '#.....#/#.....#/#.....#/#.....#/#######/#.....#/#.....#/#.....#/#.....#',
    'I': '#####/..#../..#../..#../..#../..#../..#../..#../#####',
    'J': '...####/.....#./.....#./.....#./.....#./.....#./#....#./#....#./.####..',
    'K': '#.....#/#....#./#...#../#..#.../###..../#..#.../#...#../#....#./#.....#',
    'L': '#....../#....../#....../#....../#....../#....../#....../#....../#######',
    'M': '#.....#/##...##/#.#.#.#/#..#..#/#.....#/#.....#/#.....#/#.....#/#.....#',
    'N': '#.....#/##....#/#.#...#/#..#..#/#...#.#/#....##/#.....#/#.....#/#.....#',
    'O': '.#####./#.....#/#.....#/#.....#/#.....#/#.....#/#.....#/#.....#/.#####.',
    'P': '######./#.....#/#.....#/#.....#/######./#....../#....../#....../#......',
    'Q': '.#####./#.....#/#.....#/#.....#/#.....#/#.....#/#...#.#/#....#./.####.#',
    'R': '######./#.....#/#.....#/#.....#/######./#..#.../#...#../#....#./#.....#',
    'S': '.#####./#.....#/#....../#....../.#####./......#/......#/#.....#/.#####.',
    'T': '#######/...#.../...#.../...#.../...#.../...#.../...#.../...#.../...#...',
    'U': '#.....#/#.....#/#.....#/#.....#/#.....#/#.....#/#.....#/#.....#/.#####.',
    'V': '#.....#/#.....#/#.....#/#.....#/#.....#/.#...#./.#...#./..#.#../...#...',
    'W': '#.....#/#.....#/#.....#/#.....#/#.....#/#..#..#/#.#.#.#/##...##/#.....#',
    'X': '#.....#/#.....#/.#...#./..#.#../...#.../..#.#../.#...#./#.....#/#.....#',
    'Y': '#.....#/#.....#/.#...#./..#.#../...#.../...#.../...#.../...#.../...#...',
    'Z': '#######/......#/.....#./....#../...#.../..#..../.#...../#....../#######',
}
DIGITS = {
    '0': '.###./#...#/#..##/#..##/#.#.#/##..#/##..#/#...#/.###.',
    '1': '..#../.##../..#../..#../..#../..#../..#../..#../.###.',
    '2': '.###./#...#/....#/....#/...#./..#../.#.../#..../#####',
    '3': '####./....#/....#/....#/.###./....#/....#/....#/####.',
    '4': '...#./..##./.#.#./#..#./#..#./#####/...#./...#./...#.',
    '5': '#####/#..../#..../#..../####./....#/....#/....#/####.',
    '6': '.###./#..../#..../#..../####./#...#/#...#/#...#/.###.',
    '7': '#####/....#/....#/...#./...#./..#../..#../.#.../.#...',
    '8': '.###./#...#/#...#/#...#/.###./#...#/#...#/#...#/.###.',
    '9': '.###./#...#/#...#/#...#/.####/....#/....#/....#/.###.',
}
LOWER = {
    'a': '...../.###./....#/.####/#...#/#...#/.####',
    'b': '#..../#..../####./#...#/#...#/#...#/####.',
    'c': '...../.###./#...#/#..../#..../#...#/.###.',
    'd': '....#/....#/.####/#...#/#...#/#...#/.####',
    'e': '...../.###./#...#/#####/#..../#..../.####',
    'f': '..##./.#.../.#.../####./.#.../.#.../.#...',
    'g': '...../.####/#...#/#...#/.####/....#/....#/#...#/.###.',
    'h': '#..../#..../####./#...#/#...#/#...#/#...#',
    'i': '.#./.../##./.#./.#./.#./###',
    'j': '..#./..../..#./..#./..#./..#./..#./#.#./.#..',
    'k': '#..../#..../#...#/#..#./###../#..#./#...#',
    'l': '##./.#./.#./.#./.#./.#./###',
    'm': '......./##.##../#.#..#./#.#..#./#.#..#./#.#..#./#.#..#.',
    'n': '...../####./#...#/#...#/#...#/#...#/#...#',
    'o': '...../.###./#...#/#...#/#...#/#...#/.###.',
    'p': '...../####./#...#/#...#/#...#/####./#..../#..../#....',
    'q': '...../.####/#...#/#...#/#...#/.####/....#/....#/....#',
    'r': '...../#.##./##..#/#..../#..../#..../#....',
    's': '...../.####/#..../.###./....#/....#/####.',
    't': '.#.../.#.../####./.#.../.#.../.#..#/..##.',
    'u': '...../#...#/#...#/#...#/#...#/#...#/.####',
    'v': '...../#...#/#...#/#...#/.#.#./.#.#./..#..',
    'w': '......./#.....#/#.....#/#..#..#/#..#..#/#.#.#.#/.#...#.',
    'x': '...../#...#/.#.#./..#../.#.#./#...#/#...#',
    'y': '...../#...#/#...#/#...#/.####/....#/....#/#...#/.###.',
    'z': '...../#####/....#/...#./..#../.#.../#####',
}


def read_bdf(path):
    glyphs = {}
    code = advance = 0
    pixels = set()
    for line in path.read_text(encoding='utf-8').splitlines():
        if line.startswith('ENCODING '):
            code = int(line.split()[1])
        elif line.startswith('DWIDTH '):
            advance = int(line.split()[1])
        elif line.startswith('BBX '):
            width, height, left, bottom = map(int, line.split()[1:])
        elif line == 'BITMAP':
            pixels = set()
            row = 0
            bitmap = True
        elif line == 'ENDCHAR':
            glyphs[code] = (pixels, advance)
            bitmap = False
        elif 'bitmap' in locals() and bitmap:
            bits = int(line, 16)
            for x in range(width):
                if bits & (1 << (len(line) * 4 - 1 - x)):
                    pixels.add((left + x, bottom + height - 1 - row))
            row += 1
    return glyphs


def original_latin(glyphs):
    for char, pattern in (CAPS | DIGITS | LOWER).items():
        rows = pattern.split('/')
        assert len({len(row) for row in rows}) == 1, char
        bottom = -2 if char in 'gjpqy' else 0
        pixels = {(x + 1, bottom + len(rows) - 1 - y) for y, row in enumerate(rows)
                  for x, cell in enumerate(row) if cell == '#'}
        glyphs[ord(char)] = (pixels, len(rows[0]) + 2)


def face_pixels(pixels, bold, italic):
    # Half-pixel grid: thicken by half a source pixel so Chinese counters stay open.
    cells = {(x * 2 + dx, y * 2 + dy) for x, y in pixels for dx in (0, 1) for dy in (0, 1)}
    if bold:
        cells |= {(x + 1, y) for x, y in cells}
    if italic:
        # 14-degree stepped slant; no smooth parallelogram shearing of pixel squares.
        cells = {(x + (y + 4) // 4, y) for x, y in cells}
    return cells


def outline(cells):
    pen = TTGlyphPen(None)
    # Union adjacent pixels before tracing. Internal rectangle edges can leave
    # scan-line gaps in rasterizers and needlessly inflate the font files.
    edges = set()
    for x, y in cells:
        corners = [(x, y), (x, y + 1), (x + 1, y + 1), (x + 1, y)]
        for a, b in zip(corners, corners[1:] + corners[:1]):
            if (b, a) in edges:
                edges.remove((b, a))
            else:
                edges.add((a, b))
    outgoing = defaultdict(set)
    for a, b in edges:
        outgoing[a].add(b)
    while outgoing:
        start = min(outgoing)
        current = start
        previous = None
        loop = []
        while True:
            loop.append(current)
            choices = outgoing[current]
            if previous is None:
                following = min(choices)
            else:
                dx, dy = current[0] - previous[0], current[1] - previous[1]
                def turn(point):
                    nx, ny = point[0] - current[0], point[1] - current[1]
                    cross = dx * ny - dy * nx
                    dot = dx * nx + dy * ny
                    return (0 if cross < 0 else 1 if dot > 0 else 2 if cross > 0 else 3, point)
                following = min(choices, key=turn)
            choices.remove(following)
            if not choices:
                del outgoing[current]
            previous, current = current, following
            if current == start:
                break
        corners = []
        for i, point in enumerate(loop):
            before, after = loop[i - 1], loop[(i + 1) % len(loop)]
            if (point[0] - before[0]) * (after[1] - point[1]) != (point[1] - before[1]) * (after[0] - point[0]):
                corners.append((point[0] * 50, point[1] * 50))
        pen.moveTo(corners[0])
        for point in corners[1:]:
            pen.lineTo(point)
        pen.closePath()
    return pen.glyph()


def build_face(glyphs, style, bold, italic):
    codes = sorted(c for c in glyphs if 0 <= c <= 0xFFFF)
    names = {c: f'uni{c:04X}' for c in codes}
    order = ['.notdef'] + list(names.values())
    fb = FontBuilder(1200, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(names)
    shapes, metrics = {}, {}
    for code in [-1] + codes:
        pixels, advance = glyphs[code]
        cells = face_pixels(pixels, bold, italic)
        name = '.notdef' if code == -1 else names[code]
        shapes[name] = outline(cells)
        lsb = min((x for x, _ in cells), default=0) * 50
        metrics[name] = (advance * 100 + (300 if italic and cells else 0), lsb)
    fb.setupGlyf(shapes)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=1000, descent=-200, lineGap=100)
    readable_style = 'Bold Italic' if style == 'BoldItalic' else style
    fb.setupNameTable({
        'familyName': f'Slima Pixel UI {readable_style}', 'styleName': readable_style,
        'uniqueFontIdentifier': f'SlimaPixelUI-{style}-1.0',
        'fullName': f'Slima Pixel UI {readable_style}', 'psName': f'SlimaPixelUI-{style}',
        'version': 'Version 1.000',
        'copyright': 'Original Latin glyphs and style design: Slima project, 2026. Chinese and other base glyphs: Copyright (c) 2022, TakWolf and upstream authors; see bundled licenses.',
        'licenseDescription': 'SIL Open Font License, Version 1.1. This is a renamed derivative of Fusion Pixel Font; see OFL.txt and LICENSES.',
        'licenseInfoURL': 'https://openfontlicense.org',
        'typographicFamily': 'Slima Pixel UI', 'typographicSubfamily': readable_style,
    })
    selection = (1 if italic else 0) | (32 if bold else 0)
    if not bold and not italic:
        selection = 64
    fb.setupOS2(sTypoAscender=1000, sTypoDescender=-200, sTypoLineGap=100,
                usWinAscent=1100, usWinDescent=250, usWeightClass=700 if bold else 400,
                fsSelection=selection, sxHeight=700, sCapHeight=900, fsType=0)
    fb.setupPost(italicAngle=-14.036 if italic else 0, underlinePosition=-150, underlineThickness=50)
    fb.setupMaxp()
    fb.font['head'].macStyle = (1 if bold else 0) | (2 if italic else 0)
    fb.font['head'].created = fb.font['head'].modified = 3874694400
    fb.font.recalcTimestamp = False
    path = OUTPUT / f'SlimaPixelUI-{style}.ttf'
    fb.save(path)
    font = TTFont(path)
    assert set(font.getBestCmap()) == set(codes)
    assert font['OS/2'].usWeightClass == (700 if bold else 400)
    font.close()
    # Exact raster check catches winding/hole mistakes, diagonal touching cells,
    # and the internal-edge artifacts that rectangle-only glyphs can produce.
    raster_font = ImageFont.truetype(str(path), 24)
    for char in 'ASMWxy018史莱姆选择存档设置@?':
        pixels, _ = glyphs[ord(char)]
        expected = {(8 + x, 32 - y - 1) for x, y in face_pixels(pixels, bold, italic)}
        image = Image.new('1', (64, 64))
        draw = ImageDraw.Draw(image)
        draw.fontmode = '1'
        draw.text((8, 32), char, font=raster_font, anchor='ls', fill=1)
        actual = {(x, y) for y in range(64) for x in range(64) if image.getpixel((x, y))}
        assert actual == expected, f'Raster mismatch: {style} {char}'
    meta = path.with_suffix('.ttf.meta')
    if not meta.exists():
        meta.write_text(f'''fileFormatVersion: 2
guid: {uuid.uuid4().hex}
TrueTypeFontImporter:
  externalObjects: {{}}
  serializedVersion: 4
  fontSize: 24
  forceTextureCase: -2
  characterSpacing: 0
  characterPadding: 1
  includeFontData: 1
  fontName: Slima Pixel UI {readable_style}
  fontNames:
  - Slima Pixel UI {readable_style}
  fallbackFontReferences: []
  customCharacters:
  fontRenderingMode: 2
  ascentCalculationMode: 1
  useLegacyBoundsCalculation: 0
  shouldRoundAdvanceValue: 1
  userData:
  assetBundleName:
  assetBundleVariant:
''', encoding='utf-8')
    print(f'{style}: {len(codes):,} characters, {path.stat().st_size:,} bytes', flush=True)
    return {'style': style, 'characters': len(codes), 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def specimen():
    image = Image.new('RGB', (1600, 1240), '#f1f8ff')
    draw = ImageDraw.Draw(image)
    draw.fontmode = '1'
    font_path = lambda style: str(OUTPUT / f'SlimaPixelUI-{style}.ttf')
    title = ImageFont.truetype(font_path('BoldItalic'), 60)
    draw.text((72, 32), 'SLIMA / PIXEL TYPE', font=title, fill='#24496b')
    small = ImageFont.truetype(font_path('Regular'), 24)
    draw.text((76, 108), 'BLUE + WHITE  |  REGULAR / BOLD / ITALIC / BOLD ITALIC', font=small, fill='#428ac2')
    for row, (style, bold, italic) in enumerate(FACES):
        y = 184 + row * 246
        draw.rectangle((60, y, 1540, y + 216), fill='#ffffff', outline='#8acbf0', width=4)
        label = ImageFont.truetype(font_path('Bold'), 24)
        draw.text((88, y + 14), style.upper(), font=label, fill='#428ac2')
        english = ImageFont.truetype(font_path(style), 48)
        chinese = ImageFont.truetype(font_path(style), 36)
        draw.text((88, y + 50), 'Slima  ABC xyz  0123456789', font=english, fill='#24496b')
        draw.text((88, y + 111), '史莱姆 · 方块谜境  /  开始游戏  设置  选择存档', font=chinese, fill='#24496b')
        draw.text((88, y + 161), '分裂、融合，让每一步都有新的可能。', font=chinese, fill='#428ac2')
    image.save(ROOT / 'Docs/images/SlimaPixelFontSpecimen.png')


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    glyphs = read_bdf(SOURCE / 'fusion-pixel-12px-monospaced-zh_hans.bdf')
    original_latin(glyphs)
    ui_source = (ROOT / 'Assets/_Game/Scripts/Runtime/SlimaFrontEnd.cs').read_text(encoding='utf-8-sig')
    required = {ord(c) for c in ui_source if ord(c) > 127}
    missing = required - glyphs.keys()
    assert not missing, f'Missing UI characters: {sorted(missing)}'
    records = [build_face(glyphs, *face) for face in FACES]
    shutil.copyfile(SOURCE / 'OFL.txt', OUTPUT / 'OFL.txt')
    shutil.copytree(SOURCE / 'LICENSES', OUTPUT / 'LICENSES', dirs_exist_ok=True)
    (Path(__file__).parent / 'build-report.json').write_text(json.dumps({'family': 'Slima Pixel UI', 'ui_coverage': len(required), 'faces': records}, indent=2) + '\n', encoding='utf-8')
    specimen()
    print('UI character coverage verified. Four fonts and specimen saved.', flush=True)


if __name__ == '__main__':
    main()
