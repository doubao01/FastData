# 上游授权状态

最后核对：2026-11-24。

## 结论

- 上游仓库 [`weizhonzhen/FastData`](https://github.com/weizhonzhen/FastData) 根目录没有 `LICENSE`，GitHub 仓库元信息中的 license 为空（2026-11-24 复核）。
- 维护者已确认 `weizhonzhen` 与 `doubao01` **不是同一人**（2026-11-24）。
- 因此本仓库不能单方面用 MIT 重新许可上游代码。对外分发（包括 NuGet 发布）需要上游作者的书面授权，或替换掉来源于上游的部分。

## 当前约束

- 状态：未授权。
- 在取得书面授权或完成独立重写之前：不发布 NuGet 包、不打正式 release tag。
- `.github/workflows/release.yml` 的 `Check upstream license gate` 会检查本文件是否包含 `状态：已授权`；没有该标记时 release 直接失败，避免误触发 tag 造成发布。
- 本文件只是状态记录，不是授权文件。

## 解除条件（满足其一）

1. 上游仓库新增 `LICENSE`，且覆盖本仓库当前使用的代码；或
2. 上游作者出具书面授权，明确允许本仓库以 MIT（或指定许可证）分发；把授权文件/邮件存档路径补充到本文件，并把状态改为 `状态：已授权`；或
3. 移除/重写所有来源于上游的代码，并由维护者确认独立著作权。
