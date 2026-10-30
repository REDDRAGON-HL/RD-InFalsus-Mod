# Custom Song

In Falsus 的自制谱加载 Mod

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
- 自动识别 `infalsus.studio.json` 文件生成 `if`


## 手动安装

**MelonLoader**：把 `CustomSong.dll` 放进 `GAME_PATH/Mods`

**BepInEx**：把 `CustomSong.dll` 放进 `GAME_PATH/BepInEx/plugins`。


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
  "name": "My Pack",
  "visuals": {     // 曲包封面，可选
    "songSelect":      "packvisual-songselect-default.png",
    "songSelectHover": "packvisual-songselect-hover.png",
    "selected":        "packvisual-selected-default.png",
    "selectedHover":   "packvisual-selected-hover.png",
    "deselected":      "packvisual-deselected-default.png",
    "deselectedHover": "packvisual-deselected-hover.png",
    "outline":         "outline-packvisua.png"
  }
}
```

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
3. `jacket` 必须是曲目文件夹里的 `.png` / `.jpg` / `.jpeg`；`song` 只能是 `.ogg` / `.wav`，且必须是**纯音频**的
   ogg（带视频流的 ogg 播不出来：`ffprobe -show_entries stream=codec_name,codec_type` 只该有一条 `vorbis,audio`）；
4. `preview_seconds` 必须落在音频时长以内

完整字段说明（含 `illust`、`background`、逐难度差分的写法）见仓库的 `IF.md`。


## 热重载

游戏中往 `Charts` 增删曲包 / 谱面，**回主菜单再进选曲**即可，不用重启游戏


## 成绩

`cs.sav` 独立于游戏主存档。


## AUTOPLAY

设置页「游戏和UI设置 → 音符设定」最后一行是 `AUTO` 开关（默认**关**，不写盘）。

开启后不会记录成绩、遭遇结果、粒子，掉落栏为 `AUTOPLAY` 字样；

和 **NoIotaLimit** 一起装也没关系：两个 mod 都要接管 `IotaInventory.Add`，一个入口只能有一个生效；
被它占住时本项目的拦截会跳过并在日志里告警（开 AUTO 时游戏本来也不发放粒子）。

## 与别的 mod 共存

- 钩子在一帧之后才安装，别的 mod 先占住的入口一律让出并写一行日志
  （`a method at 0x… was already hooked by another mod`），不会去叠第二层 detour。
- 装了 **In Falsus Practice** 时，帧驱动改走 `LogicalNotePlayer._hb`，不再钩 `_VD._Oz` / `Track.Update`
  —— 游玩中打开练习模式不会再闪退。
- `ResultsScene.Start` 被别的 mod（例如 In Falsus Rating）占用时，结算页改由掉落容器装饰，
  `AUTOPLAY` 灰块照常出现。


## 致谢

参考了 [InFalsusChartLoader](https://github.com/Its-MagicNeko/InFalsusChartLoader) 的实现。

- [MelonLoader](https://github.com/LavaGang/MelonLoader)
- [BepInEx](https://github.com/BepInEx/BepInEx)
