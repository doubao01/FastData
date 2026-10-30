#!/usr/bin/env bash
# 校验工作区干净：构建 / 测试 / 基准产生的文件必须全部被 .gitignore 覆盖。
#
# 为什么需要这道检查：
# 这个仓库已经出过几次「产物差点进仓库」的情况 ——
# BenchmarkDotNet.Artifacts/ 没被忽略、SQLite 恢复副本以时间戳结尾躲过 *.db。
# 靠人记得在提交前看一眼 git status 不可靠，交给 CI。
#
# 用法：在构建/测试/基准都跑完之后执行，无参数。
# 退出码：0 = 干净；1 = 有未忽略的文件（会把清单打出来）。

set -uo pipefail

fail=0

echo "=== 1. 已知产物路径必须被忽略 ==="
# 这些是各类工具真的会写出来的文件名。新增会产出文件的功能时，请补充到这里。
known_artifacts=(
  "bin/Release/placeholder.dll"
  "obj/Debug/placeholder.txt"
  "TestResults/placeholder.trx"
  "artifacts/placeholder.nupkg"
  "BenchmarkDotNet.Artifacts/results/placeholder.md"
  "tools/__pycache__/placeholder.pyc"
  "FastData.Tests/db_init_result.txt"
  "FastDataTest-OrmPerf.db"
  "FastDataTest-OrmPerf.db.before-restore-20261030120000"
  "some.log"
)

for path in "${known_artifacts[@]}"; do
  if git check-ignore -q "$path"; then
    echo "  OK   $path"
  else
    echo "  FAIL $path 未被 .gitignore 覆盖"
    fail=1
  fi
done

echo
echo "=== 2. 工作区必须没有未跟踪/未忽略的改动 ==="
# 构建与测试跑完后，除了被忽略的产物之外不应有任何变化。
# 出现内容说明某个工具写了 git 不认识的文件，或者测试改了被跟踪的文件。
unexpected="$(git status --porcelain)"
if [ -n "$unexpected" ]; then
  echo "  以下内容不在 .gitignore 覆盖范围内："
  echo "$unexpected" | sed 's/^/    /'
  echo
  echo "  处理方式："
  echo "    - 是产物 → 加到 .gitignore"
  echo "    - 是有意的源码改动 → 那说明构建过程修改了源文件，这本身是问题"
  fail=1
else
  echo "  OK   工作区干净"
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "工作区检查通过"
else
  echo "::error::工作区检查失败，见上方清单"
fi

exit "$fail"