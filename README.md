# In Falsus Mods

In Falsus 的 mod，都提供 **MelonLoader** 和 **BepInEx 6 IL2CPP** 构建

| Mod                        | 作用                               | 版本  |
| -------------------------- | ---------------------------------- | ----- |
| **InFalsusScoreCountdown** | 把游玩界面左上角的当前分换成倒扣分 | 3.0.0 |
| **InFalsusTextureSwap**    | 运行时替换已加载的立绘             | 1.2.0 |
| **InFalsusNoIotaLimit**    | 取消 1.0.7 的 9999 粒子总数上限    | 1.0.1 |
| **CustomSong**             | 从 `Charts/` 加载自制曲包          | 1.1.0 |

## 目录

```
mod/
├─ ScoreCountdown/             ScoreCountdown 源码 + MelonLoader 工程
├─ ScoreCountdown.BepInEx/     ScoreCountdown 的 BepInEx 工程
├─ TextureSwap/                TextureSwap 源码 + MelonLoader 工程
├─ TextureSwap.BepInEx/        TextureSwap 的 BepInEx 工程
├─ NoIotaLimit/                NoIotaLimit 源码 + BepInEx 工程
├─ NoIotaLimit.Melon/          NoIotaLimit 的 MelonLoader 工程
├─ CustomSong/                 自制谱加载器
└─ LoaderCompat.BepInEx/       MelonLoader → BepInEx 兼容层
```

## 安装

### MelonLoader

把 DLL 放进游戏目录的 `Mods/`：

- `InFalsusScoreCountdown.dll`
- `InFalsusTextureSwap.dll`（另需同目录的 `InFalsusTextureSwap.txt` 和数据文件，见 `TextureSwap/README.md`）
- `InFalsusNoIotaLimit.dll`
- `CustomSong.dll`（曲包放在 `Charts/`，格式见 `CustomSong/README.md`）

### BepInEx

- `InFalsusLoaderCompat.dll` → `BepInEx/core/`
- `InFalsusScoreCountdown.dll`、`InFalsusTextureSwap.dll`、`InFalsusNoIotaLimit.dll`、`CustomSong.dll` → `BepInEx/plugins/`

### Thunderstore / r2modman / Gale

| 包               | 内容                                      | 依赖                             |
| ---------------- | ----------------------------------------- | -------------------------------- |
| `LoaderCompat`   | `BepInEx/core/InFalsusLoaderCompat.dll`   | BepInExPack_IL2CPP               |
| `ScoreCountdown` | `BepInEx/plugins/InFalsusScoreCountdown/` | BepInExPack_IL2CPP、LoaderCompat |
| `TextureSwap`    | `BepInEx/plugins/InFalsusTextureSwap/`    | BepInExPack_IL2CPP               |
| `NoIotaLimit`    | `BepInEx/plugins/InFalsusNoIotaLimit/`    | BepInExPack_IL2CPP               |
| `CustomSong`     | `BepInEx/plugins/CustomSong/`             | BepInExPack_IL2CPP               |

在 r2modman/Gale 里选 In Falsus → 建 profile → 安装包，或把 zip 拖进管理器导入。
`LoaderCompat` 是 ScoreCountdown 的依赖，**必须一起装**
