# InFalsusTextureSwap

更换游戏的看板立绘

Replaces the game's character illustrations.

## 装

1. 游戏目录里要有 BepInEx 6 IL2CPP。
2. 把 `InFalsusTextureSwap.dll` 放进 `BepInEx/plugins/InFalsusTextureSwap/`。
3. 配置文件和数据文件放在 **DLL 旁边**。
4. 启动游戏。

## 写配置

`InFalsusTextureSwap.txt`，一行一条 `贴图名 = 数据文件`：

```
Nia_UIcharacterillust = aaa.dxt5
```

贴图名字：

```
Nia_UIcharacterillust
Mei_UIcharacterillust
Ayame_UIcharacterillust
Iris_UI_characterillust
Story_UIcharacterillust
```

## 压图

使用[仓库](https://github.com/REDDRAGON-HL/RD-InFalsus-Mod)下 `TextureSwap/` 目录里的脚本或 release 里的 TextureSwap-Tools：

**Nia / Mei（DXT5，2048×2048）**

```bash
python png_to_dxt5.py 我的图.png
```

**Ayame / Iris / Story（BC7，4096×4096）**

```bash
python png_to_bc7.py 我的图.png
```

运行需要 Python + Pillow。BC7 需要同目录的 `texconv.exe`，不要删。

## 制作立绘

使用[该仓库](https://github.com/REDDRAGON-HL/InFalsus-Resource)~~或在社交平台找我~~获取原立绘：

1. 把要改的立绘原图拉伸成 4:5。
2. 在绘图软件里把原图放下层做参考，上层放你的立绘并调整。
3. 导出后再压成 1:1。

详细步骤见仓库的 `TextureSwap/README.md`。
