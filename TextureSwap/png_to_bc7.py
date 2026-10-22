# -*- coding: utf-8 -*-
"""PNG -> Unity 原始 BC7 字节，供本目录的 MelonLoader 插件运行时替换贴图用。

BC7 没有 Pillow 编码器，压缩交给同目录的 texconv.exe（Microsoft DirectXTex）：
    texconv -f BC7_UNORM -m 1 -o <临时目录> <输入.png>
输出是标准 DDS（128 字节 DDS 头 + 20 字节 DX10 扩展 + BC7 块数据），
切掉前 148 字节就是 Unity 要的原始数据。

**原样压，不缩放、不裁切、不加画布**：输出尺寸就是输入 PNG 的尺寸，必须和目标贴图一致
（Ayame / Iris / Story 是 4096x4096）。透明留白和角色比例在做图阶段自己调好。

**纵向翻转**：Unity 贴图原始数据自下而上，DDS 自上而下，所以压之前先翻一次。

**自检**：把生成的 DDS 再用 texconv 转回 PNG，和输入比一遍最大通道差。

用法：
    python png_to_bc7.py 输入.png [输出.bc7] [--texconv texconv.exe]
"""
import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

DDS_HEADER = 128
DX10_HEADER = 20


def find_texconv(explicit):
    if explicit:
        path = Path(explicit)
        if path.is_file():
            return path
        raise SystemExit('找不到 texconv: %s' % explicit)
    here = (Path(sys.executable).parent if getattr(sys, 'frozen', False)
            else Path(__file__).resolve().parent)
    for name in ('texconv.exe', 'texconv'):
        if (here / name).is_file():
            return here / name
    found = shutil.which('texconv')
    if found:
        return Path(found)
    raise SystemExit('找不到 texconv.exe：放到本目录，或用 --texconv 指定')


def run(cmd, cwd):
    result = subprocess.run([str(c) for c in cmd], cwd=str(cwd),
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode != 0:
        output = (result.stdout + result.stderr).decode('utf-8', 'replace')
        raise SystemExit('texconv 失败（%d）\n%s' % (result.returncode, output))


def convert(png_path, bc7_path, texconv):
    source = Image.open(png_path).convert('RGBA')
    width, height = source.size
    if width % 4 or height % 4:
        raise SystemExit('%dx%d 不是 4 的倍数，BC7 按 4x4 块编码，压不了' % (width, height))

    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        src = tmp / 'src.png'
        source.transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(src)

        run([texconv, '-f', 'BC7_UNORM', '-m', '1', '-y', '-o', tmp, src], tmp)
        dds = tmp / 'src.DDS'
        if not dds.is_file():
            found = list(tmp.glob('src.[Dd][Dd][Ss]'))
            if not found:
                raise SystemExit('texconv 没有产出 DDS')
            dds = found[0]

        raw = dds.read_bytes()
        if raw[:4] != b'DDS ' or raw[84:88] != b'DX10':
            raise SystemExit('texconv 输出不是带 DX10 头的 DDS')

        back_dir = tmp / 'back'
        back_dir.mkdir()
        run([texconv, '-ft', 'png', '-y', '-o', back_dir, dds], tmp)
        back_png = back_dir / 'src.png'
        if not back_png.is_file():
            raise SystemExit('texconv 没有产出回读 PNG')
        back = Image.open(back_png).convert('RGBA')
        if back.size != source.size:
            raise SystemExit('回读尺寸 %s 与输入 %s 不符' % (back.size, source.size))
        back = back.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        worst = max(abs(a - b) for a, b in zip(source.tobytes(), back.tobytes()))

        data = raw[DDS_HEADER + DX10_HEADER:]
        expected = ((width + 3) // 4) * ((height + 3) // 4) * 16
        if len(data) != expected:
            raise SystemExit('数据 %d 字节，与 %dx%d 的 %d 不符'
                             % (len(data), width, height, expected))

    Path(bc7_path).write_bytes(data)
    print('%s -> %s  %dx%d  %d 字节  最大通道差 %d'
          % (png_path, bc7_path, width, height, len(data), worst))


def main():
    parser = argparse.ArgumentParser(description='PNG 转 Unity 原始 BC7 数据')
    parser.add_argument('png', help='输入 PNG')
    parser.add_argument('bc7', nargs='?', help='输出 .bc7（默认同名换后缀）')
    parser.add_argument('--texconv', metavar='路径', help='texconv.exe 路径（默认找本目录）')
    args = parser.parse_args()

    png = Path(args.png)
    out = Path(args.bc7) if args.bc7 else png.with_suffix('.bc7')
    convert(png, out, find_texconv(args.texconv))
    return 0


if __name__ == '__main__':
    sys.exit(main())
