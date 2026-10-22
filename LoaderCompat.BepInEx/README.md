# InFalsusLoaderCompat

**这不是一个 mod，而是 BepInEx 转接层。**

ScoreCountdown 的源码原本是按 MelonLoader 的 API 写的（`MelonMod`、`MelonLogger`、
`MelonEnvironment`、`NativeHook<T>`、`[MelonInfo]`/`[MelonGame]`）。BepInEx 没有这些类型，
所以这里用一个很小的共享程序集把它们映射到 BepInEx 6 IL2CPP 的对应物：

| MelonLoader                                                | BepInEx                                                  |
| ---------------------------------------------------------- | -------------------------------------------------------- |
| `MelonMod`                                                 | `BepInEx.Unity.IL2CPP.BasePlugin`                        |
| `NativeHook<T>`                                            | `BepInEx.Unity.IL2CPP.Hook.INativeDetour.CreateAndApply` |
| `MelonEnvironment.GameRootDirectory` / `UserDataDirectory` | `BepInEx.Paths`                                          |
| `MelonLogger` / `LoggerInstance`                           | `ManualLogSource`                                        |
| `Update` / `LateUpdate`                                    | 共享的 `MelonUpdateBehaviour` 转发                       |

这样 ScoreCountdown 的 BepInEx 工程可以直接 include 原 MelonLoader 源码，业务逻辑一行不用改。

## 安装

**r2modman / Gale：** 安装 ScoreCountdown 时会自动作为依赖装上，
文件落在 `BepInEx/core/`。

**手动：** 把 `InFalsusLoaderCompat.dll` 放到 `BepInEx/core/`。

它自己没有 `[BepInPlugin]`，单独安装不会产生任何功能；只有 ScoreCountdown
引用它。TextureSwap 不依赖它。

## 注意

`MelonUpdateBehaviour` 必须只在这个共享程序集里定义一次。如果每个插件各编译一份，
第二个副本会报 `does not have a corresponding IL2CPP class pointer`。
