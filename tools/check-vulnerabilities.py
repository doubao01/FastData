#!/usr/bin/env python3
"""检查 NuGet 依赖中的已知漏洞，发现问题时以非零码退出。

背景：`dotnet list package --vulnerable` 只负责「打印」发现的漏洞，
退出码始终是 0。原 CI 步骤因此一直是绿的——扫了但不阻断，
有高危漏洞也照样发版。这里解析它的 JSON 输出并显式判定失败。

用法：
    python3 tools/check-vulnerabilities.py <project.csproj> [<project.csproj> ...]
"""

import json
import os
import subprocess
import sys
import tempfile


ALLOWLIST = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                         "vulnerability-allowlist.txt")


def load_allowlist():
    """读取豁免清单，返回 {(包名, 公告ID): 原因}。"""
    allowed = {}
    if not os.path.exists(ALLOWLIST):
        return allowed
    with open(ALLOWLIST, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("|")
            if len(parts) < 3:
                continue
            allowed[(parts[0].strip(), parts[1].strip())] = parts[2].strip()
    return allowed


def scan(project):
    """返回 (漏洞列表, 错误信息)。漏洞列表元素为 (包名, 版本, 严重度, 公告链接)。"""
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as tmp:
        out_path = tmp.name

    try:
        result = subprocess.run(
            ["dotnet", "list", project, "package",
             "--vulnerable", "--include-transitive", "--format", "json"],
            capture_output=True, text=True, timeout=240)
    except subprocess.TimeoutExpired:
        return [], "查询漏洞库超时（240s），可能是网络不可达"

    if result.returncode != 0:
        return [], "dotnet list 执行失败：%s" % (result.stderr or result.stdout)[:400]

    try:
        with open(out_path, encoding="utf-8-sig") as f:
            data = json.load(f)
    except Exception:
        # 部分 SDK 版本忽略 --format json 时仍写 stdout，兜底从 stdout 解析
        try:
            data = json.loads(result.stdout)
        except Exception as ex:
            return [], "无法解析 dotnet list 输出：%s" % ex
    finally:
        try:
            os.unlink(out_path)
        except OSError:
            pass

    found = []
    for proj in data.get("projects", []):
        for fw in proj.get("frameworks", []):
            packages = fw.get("topLevelPackages", []) + fw.get("transitivePackages", [])
            for pkg in packages:
                for vul in pkg.get("vulnerabilities", []) or []:
                    found.append((
                        pkg.get("id"),
                        pkg.get("resolvedVersion"),
                        vul.get("severity", "?"),
                        vul.get("advisoryurl", ""),
                    ))
    return found, None


def main(argv):
    if len(argv) < 2:
        print("用法: check-vulnerabilities.py <project.csproj> ...", file=sys.stderr)
        return 2

    total = 0
    failed = False

    for project in argv[1:]:
        print("=== %s ===" % project)
        found, error = scan(project)

        if error:
            print("  扫描失败: %s" % error)
            failed = True
            continue

        if not found:
            print("  无已知漏洞")
            continue

        allowed = load_allowlist()
        for name, version, severity, url in found:
            advisory = url.rstrip("/").split("/")[-1] if url else ""
            reason = allowed.get((name, advisory))
            if reason:
                print("  [%s] %s %s  %s" % (severity, name, version, url))
                print("        ^ 已豁免：%s" % reason)
                continue
            print("  [%s] %s %s  %s" % (severity, name, version, url))
            total += 1

    if total:
        print()
        print("::error::发现 %d 处已知漏洞。修复方式：在 Directory.Packages.props 的"
              "「安全钉版」段固定到已修复版本。" % total)
        return 1

    if failed:
        print()
        print("::error::漏洞扫描未能完成（见上方错误），不能视为通过。")
        return 1

    print()
    print("依赖扫描通过：未发现已知漏洞")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))