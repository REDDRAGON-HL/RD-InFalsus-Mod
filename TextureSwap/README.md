# InFalsusTextureSwap

运行时替换游戏的看板立绘

Replace the game's character standees at runtime

## 装

把 DLL、配置文件和压好的数据文件放同一个目录：

- MelonLoader：游戏目录的 `Mods/`
- BepInEx：`BepInEx/plugins/InFalsusTextureSwap/`（r2modman / Gale 里用「打开配置目录」进这个文件夹）

## 制作立绘

使用[该仓库](https://github.com/REDDRAGON-HL/InFalsus-Resource)~~或在社交平台找我~~获取原立绘，把要改的立绘拉伸成6500×8186

到绘图软件中把拉伸过的原图放在下层做参考，上层导入你的立绘并调整

导出后再把图压成1:1比例

## 压立绘文件

使用仓库源码里的 Python 脚本或者 TextureSwap-Tools 里同名的 `.exe`

bc7 需要用到同目录的 `texconv.exe` ，不要删掉它

**Nia / Mei（DXT5，2048×2048）**

```bash
python png_to_dxt5.py 我的图.png
```

**Ayame / Iris / Story（BC7，4096×4096）**

```bash
python png_to_bc7.py 我的图.png
```

## 写配置

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
