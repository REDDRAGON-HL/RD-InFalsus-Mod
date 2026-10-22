# -*- coding: utf-8 -*-
"""PNG -> Unity 原始 DXT5（BC3）字节，供本目录的 MelonLoader 插件运行时替换贴图用。

为什么要这一步：游戏的贴图是 DXT5，而 `Graphics.CopyTexture` 只肯在**同一格式组**之间拷，
所以喂进去的源也必须是 DXT5；可 `SetPixels32` 又不支持压缩格式（Unity 只允许
RGBA32/ARGB32/RGB24/Alpha8）。于是只能在这里先把像素压好，mod 那边用
`TextureFormat.DXT5` 建贴图 + `LoadRawTextureData` 直接塞进去。

压缩交给 Pillow 的 DDS 写入器（`pixel_format='DXT5'`），再把开头 128 字节的 DDS 头切掉，
剩下的就是 BC3 块数据 —— 与 Unity 的原始数据布局一致。

**纵向翻转**：Unity 贴图的原始数据是自下而上的（UnityPy 导出时会翻转一次），
所以写回去之前要先翻一次，游戏里才是正的。

**原样压，不缩放、不裁切、不加画布**：输出尺寸就是输入 PNG 的尺寸，必须和目标贴图一致
（妮雅/梅是 2048x2048）。原立绘四周的透明留白、角色在画布里的比例都是游戏原本的构图，
替换图要在做图阶段自己按这个构图调好，这里只负责压。

用法：
    python png_to_dxt5.py 输入.png [输出.dxt5]
"""
import argparse
import io
import sys
from pathlib import Path

from PIL import Image

DDS_HEADER = 128   # 'DDS ' 魔数 4 字节 + DDS_HEADER 124 字节


def convert(png_path, dxt5_path):
    source = Image.open(png_path).convert('RGBA')

    buffer = io.BytesIO()
    source.transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(
        buffer, format='DDS', pixel_format='DXT5')
    dds = buffer.getvalue()

    # 自检：解回来再翻正，和原图逐字节比一遍（DXT5 有损，看最大通道差）
    back = Image.open(io.BytesIO(dds)).convert('RGBA')
    back = back.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    worst = max(abs(a - b) for a, b in zip(source.tobytes(), back.tobytes()))

    Path(dxt5_path).write_bytes(dds[DDS_HEADER:])
    print('%s -> %s  %dx%d  %d 字节  最大通道差 %d'
          % (png_path, dxt5_path, source.width, source.height,
             len(dds) - DDS_HEADER, worst))


def main():
    parser = argparse.ArgumentParser(description='PNG 转 Unity 原始 DXT5 数据')
    parser.add_argument('png', help='输入 PNG')
    parser.add_argument('dxt5', nargs='?', help='输出 .dxt5（默认同名换后缀）')
    args = parser.parse_args()

    png = Path(args.png)
    out = Path(args.dxt5) if args.dxt5 else png.with_suffix('.dxt5')
    convert(png, out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
