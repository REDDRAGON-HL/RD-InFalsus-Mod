# In Falsus Mods

In Falsus 的 mod，同时提供 **MelonLoader** 和 **BepInEx 6 IL2CPP** 两套构建

| Mod                        | 作用                                 | 版本  |
| -------------------------- | ------------------------------------ | ----- |
| **InFalsusScoreCountdown** | 把游玩界面左上角的当前分换成倒扣分   | 3.0.0 |
| **InFalsusTextureSwap**    | 运行时替换已加载的立绘，不改游戏文件 | 1.1.0 |



## 目录

```
mod/
├─ ScoreCountdown/             ScoreCountdown 源码 + MelonLoader 工程
├─ ScoreCountdown.BepInEx/     ScoreCountdown 的 BepInEx 工程
├─ TextureSwap/                TextureSwap 源码 + MelonLoader 工程
├─ TextureSwap.BepInEx/        TextureSwap 的 BepInEx 工程
└─ LoaderCompat.BepInEx/       MelonLoader → BepInEx 兼容层
```


## 安装

### MelonLoader

把 DLL 放进游戏目录的 `Mods/`：

- `InFalsusScoreCountdown.dll`
- `InFalsusTextureSwap.dll`（另需同目录的 `InFalsusTextureSwap.txt` 和数据文件，见 `TextureSwap/README.md`）

### BepInEx

- `InFalsusLoaderCompat.dll` → `BepInEx/core/`
- `InFalsusScoreCountdown.dll`、`InFalsusTextureSwap.dll` → `BepInEx/plugins/`

### Thunderstore / r2modman / Gale

`package.py` 生成三个包：

| 包                       | 内容                                      | 依赖                                     |
| ------------------------ | ----------------------------------------- | ---------------------------------------- |
| `InFalsusLoaderCompat`   | `BepInEx/core/InFalsusLoaderCompat.dll`   | BepInExPack_IL2CPP                       |
| `InFalsusScoreCountdown` | `BepInEx/plugins/InFalsusScoreCountdown/` | BepInExPack_IL2CPP、InFalsusLoaderCompat |
| `InFalsusTextureSwap`    | `BepInEx/plugins/InFalsusTextureSwap/`    | BepInExPack_IL2CPP                       |

在 r2modman/Gale 里选 In Falsus → 建 profile → 安装包，或把 zip 拖进管理器导入。
`InFalsusLoaderCompat` 是ScoreCountdown 的依赖，**必须一起装**
