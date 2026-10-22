# 装

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

注意 Iris 多一个下划线：`Iris_UI_characterillust`。

## 压图

和 MelonLoader 版一样，用仓库 `TextureSwap/` 目录里的脚本（本包不包含）：

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

`原图` 文件夹里有 5 张原立绘：

1. 把要改的立绘原图拉伸成 4:5。
2. 在绘图软件里把原图放下层做参考，上层放你的立绘并调整。
3. 导出后再压成 1:1。

详细步骤见仓库的 `TextureSwap/README.md`。
