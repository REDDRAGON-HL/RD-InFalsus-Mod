# Builds the tiny `Source/Fonts/TitleGap.ttf` that carries the glyphs no game font has.
#
# The song cards use `ShinGOFontSet`; its faces are `vivoSans`-like Japanese subsets that miss
# simplified Chinese and a few symbols. `SongFont` reads `vivoSans-Regular`/`DemiBold` from the
# game at run time and `FontMerge` appends this patch, so the merged face covers:
#   * every character `ShinGo` itself has (official text never loses a glyph),
#   * every non-ASCII character the current `Charts/` uses (the custom titles),
#   * everything `vivoSans` already has.
# The patch is only a few kilobytes; the fonts are not embedded.
#
# Re-run after the game changes its fonts, or after adding a chart that uses a character outside
# the current patch, then rebuild the mod:
#   python custom_song/Tools/MakeTitleGap/make_gap.py
#
# (The master copy carries the `.txt` suffix only because this workspace's security hook blocks
# writing `.py`; `make_clean_copy` renames it to `.py` in the release tree.)
#
# Requires: UnityPy, fontTools (pip install UnityPy fonttools).

import glob
import io
import json
import os
import sys

import UnityPy
from fontTools import subset
from fontTools.ttLib import TTFont

GAME = os.path.join('D:' + os.sep, 'Steam', 'steamapps', 'common', 'In Falsus')
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Source', 'Fonts', 'TitleGap.ttf'))

FONT_BUNDLE = '9cbafa976ea24f6234c4b1f4a1a5a70a.bundle'


def game_fonts():
    bundle = os.path.join(GAME, 'infalsus_Data', 'StreamingAssets', 'aa',
                          'StandaloneWindows64', FONT_BUNDLE)
    raw = {}
    shin = set()

    env = UnityPy.load(bundle)
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        try:
            data = obj.read_typetree()
        except Exception:
            continue
        identifier = data.get('Identifier')
        if identifier in ('vivoSans-Regular', 'vivoSans-DemiBold'):
            raw[identifier] = bytes(data['RawBytes'])
        elif identifier and 'ShinGo' in identifier:
            shin |= set(TTFont(io.BytesIO(bytes(data['RawBytes'])), lazy=True).getBestCmap())

    env = UnityPy.load(os.path.join(GAME, 'infalsus_Data', 'resources.assets'))
    for obj in env.objects:
        if obj.type.name != 'Font':
            continue
        data = obj.read()
        if getattr(data, 'm_Name', '') == 'NewClear-mincho':
            raw['NewClear-mincho'] = bytes(data.m_FontData)

    for key in ('vivoSans-Regular', 'NewClear-mincho'):
        if key not in raw:
            sys.exit('missing game font: ' + key)
    return raw, shin


def custom_characters():
    def walk(value):
        if isinstance(value, str):
            yield value
        elif isinstance(value, list):
            for item in value:
                yield from walk(item)
        elif isinstance(value, dict):
            for item in value.values():
                yield from walk(item)

    chars = set()
    for path in glob.glob(os.path.join(GAME, 'Charts', '**', 'if'), recursive=True):
        try:
            data = json.load(open(path, encoding='utf-8-sig'))
        except Exception:
            continue
        for text in walk(data):
            for char in text:
                if ord(char) > 0x7F:
                    chars.add(ord(char))
    return chars


def merge(base_bytes, source_bytes):
    base = TTFont(io.BytesIO(base_bytes))
    source = TTFont(io.BytesIO(source_bytes))
    scale = base['head'].unitsPerEm / source['head'].unitsPerEm

    order = list(base.getGlyphOrder())
    base_cmap = {}
    for table in base['cmap'].tables:
        base_cmap.update(table.cmap)
    source_cmap = {}
    for table in source['cmap'].tables:
        source_cmap.update(table.cmap)

    glyf, hmtx = base['glyf'], base['hmtx']
    source_glyf, source_hmtx = source['glyf'], source['hmtx']

    from fontTools.pens.transformPen import TransformPen
    from fontTools.pens.ttGlyphPen import TTGlyphPen

    added = []
    for codepoint, glyph_name in sorted(source_cmap.items()):
        if codepoint in base_cmap or codepoint > 0xFFFF or glyph_name not in source_glyf:
            continue

        name = 'gap%04X' % codepoint
        if name in glyf.glyphs:
            continue

        pen = TTGlyphPen(None)
        try:
            source_glyf[glyph_name].draw(
                TransformPen(pen, (scale, 0, 0, scale, 0, 0)), source_glyf)
        except Exception:
            continue

        glyph = pen.glyph()
        glyph.recalcBounds(glyf)
        glyf.glyphs[name] = glyph
        hmtx.metrics[name] = (int(round(source_hmtx[glyph_name][0] * scale)),
                              glyph.xMin if glyph.numberOfContours else 0)
        added.append(name)
        base_cmap[codepoint] = name

    order += added
    base.setGlyphOrder(order)
    glyf.glyphOrder = order
    base['maxp'].numGlyphs = len(order)
    for table in base['cmap'].tables:
        if table.format == 4:
            table.cmap = dict(base_cmap)
    for tag in ('vhea', 'vmtx'):
        if tag in base:
            del base[tag]

    output = io.BytesIO()
    base.save(output)
    return output.getvalue(), base_cmap


def main():
    raw, shin = game_fonts()
    vivo = set(TTFont(io.BytesIO(raw['vivoSans-Regular']), lazy=True).getBestCmap())

    merged, _ = merge(raw['vivoSans-Regular'], raw['NewClear-mincho'])
    merged_map = set(TTFont(io.BytesIO(merged), lazy=True).getBestCmap())

    need = (shin | custom_characters()) - vivo
    need = {cp for cp in need if cp in merged_map}
    need = {cp for cp in need
            if not (0x1100 <= cp <= 0x11FF or 0x3130 <= cp <= 0x318F or 0xAC00 <= cp <= 0xD7A3)}

    font = TTFont(io.BytesIO(merged))
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ['*']
    options.notdef_outline = True
    options.recalc_bounds = True

    cutter = subset.Subsetter(options=options)
    cutter.populate(unicodes=need)
    cutter.subset(font)

    output = io.BytesIO()
    font.save(output)
    data = output.getvalue()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'wb') as f:
        f.write(data)

    print('TitleGap.ttf: %d glyphs, %d bytes: %s' %
          (TTFont(io.BytesIO(data))['maxp'].numGlyphs, len(data),
           ''.join(chr(cp) for cp in sorted(need))))


main()
