# 制作立绘

`原图`文件夹下有5张原立绘，把要改的立绘拉伸成4:5比例

到绘图软件中把拉伸过的原图放在下层做参考，上层导入你的立绘并调整

导出后再把图压成1:1比例

# 压立绘文件

如果你有 Python 环境用 `.py`；没有就直接用同名的 `.exe`

bc7 需要用到同目录的 `texconv.exe` ，不要删掉它

**Nia / Mei（DXT5，2048×2048）**

```bash
python png_to_dxt5.py 我的图.png
```

**Ayame / Iris / Story（BC7，4096×4096）**

```bash
python png_to_bc7.py 我的图.png
```

# 写配置

`Mods/InFalsusTextureSwap.txt`，一行一条 `贴图名 = 数据文件`：

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
对我也不知道为什么iris多一个下划线

# 装
把 `InFalsusTextureSwap.dll`、`InFalsusTextureSwap.txt` 和你的 `.dxt5` / `.bc7` 数据文件一起放进游戏目录的 `Mods/`
