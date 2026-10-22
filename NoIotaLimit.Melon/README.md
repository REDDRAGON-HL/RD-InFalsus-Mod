# InFalsusNoIotaLimit

取消 1.0.7 新增的 **9999 粒子总数上限**。

1. 把 `IotaInventory.MaxTotalIotaCount` 改成 `UInt16` 最大值 **65535**
2. 把 UI 里硬编码的 `cmp ebx, 9999` 两处改成 `cmp ebx, 65535`，让粒子数量显示和界面元素
   不再按 9999 截断

## 安装

MelonLoader：

```
Mods/InFalsusNoIotaLimit.dll
```

启动后日志里应能看到：

```
[InFalsusNoIotaLimit] loading: removing the 9999 particle cap
[InFalsusNoIotaLimit] MaxTotalIotaCount 9999 -> 65535
[InFalsusNoIotaLimit] UI cap patch: 2 site(s)
```
