# Custom Song

In Falsus 的自制谱加载 Mod ，内置AutoPlay

- 默认曲包名：`Custom Song`（可以在 `pack.json` 里改）
- 自制谱位置：`GAME_PATH/Charts`（不存在会自动创建）
- 自制谱成绩：`cs.sav`，与游戏主存档分开


## 功能

- **热重载**：游戏中增删曲包或谱面，回主菜单再进选曲即可生效，不用重启
- **明文谱面直接可用**：加载时自动转成游戏的加密格式，不用手动转换
- **同id差分**：同一个 `if` 的四个难度可以各用不同曲名、曲师、音频、预览、曲绘、谱面、设计者、定数
- **大谱面**：放宽了游戏的谱面池容量
- **整套曲包贴图**：曲包大卡片 / 未选中卡片 / 右侧进度条目 / 选曲页横幅，7 张图分别指定
- **AUTOPLAY**：设置页「音符设定」最后一行有开关；开启后不记录成绩、遭遇结果、粒子掉落
- **支持 `.falsum` 压缩包**：直接放进曲包文件夹下，自动解包成曲目文件夹并生成 `if`
- 曲名缺字自动补
- 自动识别 `.json` 文件生成 `if`


## 手动安装

**MelonLoader**：把 `CustomSong.dll` 放进 `GAME_PATH/Mods`

**BepInEx**：把 `CustomSong.dll` 放进 `GAME_PATH/BepInEx/plugins`。

## 建曲包与装谱

### 1. 建一个曲包

自制谱放在**游戏目录**的 `Charts/` 下（没有会自动建好，连带建一个 `Custom Song`）：

```
GAME_PATH/Charts/
  我的曲包/            ← 不写pack时文件夹名就是曲包名
    pack.json          ← 可选：改曲包名、换 7 张封面图
    歌曲A/             ← 每首歌一个文件夹
    歌曲B/
```

`Charts/` 下**只能放曲包文件夹**，直接把歌曲文件夹放在这里会被跳过。

### 2. 把谱装进去

三种来源，都是把东西丢进**曲包文件夹**里：

**① 已经是曲目文件夹（自带 `if`）**：整个文件夹拖进去即可。

```
我的曲包/歌曲A/
  if            曲目说明（曲名、曲师、谱面、定数……）
  audio.ogg     音频（ogg / mp3 / wav，纯音频）
  jkt.png       曲绘（png / jpg / jpeg）
  chart.spc     谱面（明文 .spc / .json 自动转换）
```

**② `.falsum` 压缩包**：直接把 `.falsum` 丢进曲包文件夹。进游戏后自动解压成同名文件夹、按里面的
`song.json` 生成 `if` 并加载；已解压过的不会再解压。

**③ In Falsus Studio 工程**：把整个工程目录拖进曲包文件夹，进游戏后自动按含 `infalsus.studio.json` 
生成 `if` 并将 `.ifstudio/` 下的谱面挪到曲目根目录；已有 `if` 的目录不会生成


## 曲包格式

```
Charts/
  MyPack/
    pack.json
    SongA/
      if
      audio.ogg
      jkt.png
      chart.spc
```


### `pack.json`


```json
{
  "name": ["我的曲包", "我的曲包", "My Pack", "マイパック", "마이 팩"],
  "visuals": {     // 曲包封面，可选
    "songSelect":      "packvisual-songselect-default.png",
    "songSelectHover": "packvisual-songselect-hover.png",
    "selected":        ["selected-sc.png", "selected-tc.png", "selected.png", null, null],
    "selectedHover":   "packvisual-selected-hover.png",
    "deselected":      "packvisual-deselected-default.png",
    "deselectedHover": "packvisual-deselected-hover.png",
    "outline":         "outline-packvisua.png"
  }
}
```

`name` 和每张图都可以写成五元数组，顺序是 `[简体, 繁體, English, 日本語, 한국어]`，游戏用哪个语言就取哪个槽；
图只写要区分的语言即可，缺的语言用 English 那张（英文也没有就用第一个非空的）。游戏里切语言立刻换图。

也可以写成语言对象，键名 `sc` / `tc` / `en` / `ja` / `ko`（顺序随意、大小写不敏感）：

```json
{
  "name": { "sc": "我的曲包", "en": "My Pack", "ja": "マイパック" },
  "visuals": {
    "songSelect": { "sc": "banner-sc.png", "en": "banner-en.png" },
    "selected":   { "sc": "selected-sc.png", "ja": "selected-ja.png" }
  }
}
```

`name` 缺的语言按第一条写出来的补；图缺的语言用 English 那张（英文也没有就用第一个非空的）。

不写 `pack.json` 时自动使用文件夹名作为曲包名

### `if`

```json
{
  "id": "my_song",
  "name": "My Song",
  "composer": "Composer",
  "song": "audio.ogg",
  "jacket": "jkt.png",
  "preview_seconds": ["42.0", "72.0"],
  "charter": [null, null, "Charter A", "Charter B"],
  "chart":   [null, null, "normal.spc", "hard.spc"],
  "lv":      [null, null, 9, 13]
}
```

1. `lv` 必须是 JSON 数字；
2. 某个难度有 `chart`，就必须同时有 `charter` 和 `lv`；
3. `jacket` 必须是曲目文件夹里的 `.png` / `.jpg` / `.jpeg`；`song` 只能是 `.ogg` / `.wav` / `.mp3`；
4. `preview_seconds` 必须落在音频时长以内

`name` / `composer` / `illust` 另有三种写法：五元数组按语言
`[简体, 繁體, English, 日本語, 한국어]` 各写一份；四元数组逐难度；两者都要时写嵌套
（**外层语言、内层难度**，即 5 行 × 4 列，某个格子写 `null` 就用该语言的第一条文本补）。
也可以写成语言对象：`{"sc": "…", "en": "…", "ja": [...]}` —— 键名只有 `sc/tc/en/ja/ko`
（大小写不敏感）、顺序随意，缺的语言自动补；`pack.json` 的曲包名和 7 张图同样支持。

```json
{
  "id": "my_song",
  "name": { "en": "Title", "sc": "歌名", "ja": ["日·MIN", "日·EVO", "日·ULT", "日·FBD"] },
  "composer": { "sc": "曲师", "en": "Composer" },
  "illust":   { "sc": "画师", "en": "Illustrator" },
  "song": "audio.ogg",
  "jacket": "jkt.png",
  "preview_seconds": ["42.0", "72.0"],
  "charter": [null, null, "Charter A", "Charter B"],
  "chart":   [null, null, "normal.spc", "hard.spc"],
  "lv":      [null, null, 9, 13]
}
```

完整字段说明与其它几种写法的完整示例见同包的 `IF.md`。


## 热重载

游戏中往 `Charts` 增删曲包 / 谱面，**回主菜单再进选曲**即可，不用重启游戏


## 成绩

`cs.sav` 独立于游戏主存档。


## AUTOPLAY

设置页「游戏和UI设置 → 音符设定」最后一行是 `AUTO` 开关（默认**关**，不写盘）。

开启后不会记录成绩、遭遇结果、粒子，掉落栏为 `AUTOPLAY` 字样；

和 **NoIotaLimit** 一起装也没关系：两个 mod 都要接管 `IotaInventory.Add`，一个入口只能有一个生效；
被它占住时本项目的拦截会跳过并在日志里告警（开 AUTO 时游戏本来也不发放粒子）。


## 致谢

参考了 [InFalsusChartLoader](https://github.com/Its-MagicNeko/InFalsusChartLoader) 的实现。

- [MelonLoader](https://github.com/LavaGang/MelonLoader)
- [BepInEx](https://github.com/BepInEx/BepInEx)
