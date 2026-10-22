# InFalsusNoIotaLimit

取消 1.0.7 新增的 **9999 粒子总数上限**。

1. 把 `IotaInventory.MaxTotalIotaCount` 改成 `UInt16` 最大值 **65535**
2. 把 UI 里硬编码的 `cmp ebx, 9999` 两处改成 `cmp ebx, 65535`，让粒子数量显示和界面元素
   不再按 9999 截断

## 安装

BepInEx 6 IL2CPP：

```
BepInEx/plugins/InFalsusNoIotaLimit.dll
```

启动后日志里应能看到：

```
[InFalsusNoIotaLimit] loading: removing the 9999 particle cap
[InFalsusNoIotaLimit] MaxTotalIotaCount 9999 -> 65535
[InFalsusNoIotaLimit] UI cap patch: 2 site(s)
```

## 配置

`BepInEx/config/REDDRAGON-HL.InFalsusNoIotaLimit.cfg`

```ini
[General]
PatchUiCap = true
```

- `PatchUiCap = true`：同时改 UI 的 9999 比较（默认）
- `PatchUiCap = false`：只改功能上限，UI 仍按 9999 显示/隐藏元素