## `if` 文件示例：

### 基础
```json
{
  "id": "id",              //谱面id，需要尽可能的保证它是唯一的，若重复则不加载谱面
  "name": "music",                            //曲名
  "composer": "composer",                 //曲师（也可写 4 元数组，逐难度不同）
  "song": "music.ogg",                        //歌曲文件，接受ogg、wav（mp3 会被 FMOD 拒）；须是纯音频 ogg，带视频流的播不出来
  "illust": "illust",                   //曲绘画师；为可选参数
  "jacket": "jacket.png",                        //曲绘，只接受png，建议为正方形
  "background": "bg.mp4",                     //谱面内的背景，接受png与mp4；为可选参数
  "preview_seconds": ["0", "60"],             //预览音频时间[开始,结束]，以秒计算
  "charter": "charter",                        //谱师
  "chart": "chart.spc",                       //谱面文件
  "lv": 15                                    //谱面难度
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
