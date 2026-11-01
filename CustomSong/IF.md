## `if` 文件示例：

### 基础
```json
{
  "id": "id",              //谱面id，需要尽可能的保证它是唯一的，若重复则不加载谱面
  "name": "music",                            //曲名
  "composer": "composer",                 //曲师（必需）
  "song": "music.ogg",                        //歌曲文件，接受 ogg / wav / mp3
  "illust": "illust",                   //曲绘画师；为可选参数
  "jacket": "jacket.png",                        //曲绘，接受 png / jpg / jpeg，建议为正方形
  "background": "bg.mp4",                     //谱面内的背景，接受 png 与 mp4；为可选参数
  "preview_seconds": ["0", "60"],             //预览音频时间[开始,结束]，以秒计算
  "charter": "charter",                        //谱师
  "chart": "chart.spc",                       //谱面文件（明文 .spc / .json 会自动转换）
  "lv": 15                                    //谱面难度，必须是 JSON 数字
}
```

### 差分拓展
```json
{
  "id": "id",
  "name": ["music", "music", "music", "工字钢"],
  "composer": ["composer","composer","composer","哇哦"],
  "song": ["audio.ogg", "audio.ogg", "audio.ogg", "encore.ogg"],
  "illust": ["illust", null, null, "illust"],                    //可用null占位，占位后当前等级不展示画师
  "jacket": ["jacket.png", "jkt.png", "jk.png", "t.png"],
  "background": [null, null, "bg.png", "bg.mp4"],
  "preview_seconds": [
    ["142.800", "182.185"],
    ["142.800", "182.185"],
    ["142.800", "182.185"],
    ["82.300", "100.586"]
  ],
  "charter": ["charter", "charter", null, "charter"],
  "chart": [null, null, null, "chart.spc"],
  "lv": [1, null, 1, 15]
}
```

### 多语言（五元数组）

`name` / `composer` / `illust` 三栏都可以按语言写，顺序固定
`[简体, 繁體, English, 日本語, 한국어]`，游戏用哪个语言就取哪个槽：

```json
{
  "id": "id",
  "name": ["曲名·简", "曲名·繁", "Title", "曲名·日", "곡명"],
  "composer": ["曲师·简", "曲师·繁", "Composer", "作曲者", "작곡가"],
  "illust": ["画师·简", "画师·繁", "Illustrator", "絵師", "일러스트"],
  "song": "music.ogg",
  "jacket": "jacket.png",
  "preview_seconds": ["0", "60"],
  "charter": "charter",
  "chart": "chart.spc",
  "lv": 15
}
```

### 多语言 × 逐难度

```json
{
  "id": "id",
  "name": [
    ["简·MIN", "简·EVO", "简·ULT", "简·FBD"],
    ["繁·MIN", "繁·EVO", "繁·ULT", "繁·FBD"],
    ["EN·MIN",  "EN·EVO",  "EN·ULT",  "EN·FBD"],
    ["日·MIN",  "日·EVO",  "日·ULT",  "日·FBD"],
    ["韩·MIN",  "韩·EVO",  "韩·ULT",  "韩·FBD"]
  ],
  "composer": "composer",
  "illust": "illust",
  "song": "music.ogg",
  "jacket": "jacket.png",
  "preview_seconds": ["0", "60"],
  "charter": "charter",
  "chart": "chart.spc",
  "lv": 15
}
```

某个格子写 `null` 时，用这首歌该语言的第一条文本补上。

四元里再套四元（4×4）不是有效写法。

### 对象写法（键名是语言，顺序随意）

键名只有 `sc` / `tc` / `en` / `ja` / `ko`（大小写不敏感），不用凑满五个，缺的语言用第一条写出来的补；
值也可以直接写逐难度的四元数组：

```json
{
  "id": "id",
  "name": { "en": "Title", "sc": "曲名", "ja": ["日·MIN", "日·EVO", "日·ULT", "日·FBD"] },
  "composer": { "sc": "曲师", "en": "Composer" },
  "illust":   { "sc": "画师", "en": "Illustrator" },
  "song": "music.ogg",
  "jacket": "jacket.png",
  "preview_seconds": ["0", "60"],
  "charter": "charter",
  "chart": "chart.spc",
  "lv": 15
}
```

不是语言的键（比如 `"chinese"`）会打一行警告然后忽略，不会因此丢掉整首歌。

### 三栏的写法

| 写法     | 例子                                       | 含义                     |
| -------- | ------------------------------------------ | ------------------------ |
| 字符串   | `"name": "曲名"`                           | 四个难度、五种语言都用它 |
| 四元数组 | `"name": ["A", null, null, "D"]`           | 逐难度差分               |
| 五元数组 | `"name": ["简","繁","EN","日","韩"]`       | 多语言                   |
| 5×4 嵌套 | `[[4 条], [4 条], [4 条], [4 条], [4 条]]` | 外层语言、内层难度       |
| 对象     | `{"sc": "…", "en": "…", "ja": [4 条]}`     | 按语言键写，可混四元数组 |

`pack.json` 的 `name` 和 7 张图也支持五元数组与对象写法。
