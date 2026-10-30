#!/usr/bin/env python3
"""文档 API 拼写检查。

抽文档里的 ```csharp 代码块，把出现的 Xxx(...) / a.Xxx(...) 调用名逐个拿去源码里找定义，
找不到就报出来。它能挡住“文档写了不存在的 API 名”，但**不证明示例能编译**
（类型、重载、必填参数都检查不了）—— 真正编译验证见 FastData/docs/DOC_SAMPLES.md 的说明。

用法：
    python3 tools/check-docs-api.py            # 相对仓库根运行
退出码 0 = 全通过，1 = 有可疑调用。
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCAN_DIRS = ["FastData", "FastData.Untility"]

# 语法关键字与非 API 调用
SKIP_WORDS = {
    "if", "for", "while", "new", "var", "return", "nameof", "typeof", "using", "foreach",
    "catch", "switch", "lock", "throw", "await", "name", "value", "get", "set", "this", "base",
    "is", "as", "in", "out", "ref", "params", "default", "sizeof", "checked", "unchecked",
}

def code_files():
    for d in SCAN_DIRS:
        yield from (ROOT / d).rglob("*.cs")

_cache = None

def load_index():
    """一次性把源码里所有“名字 → 是否出现过定义形态”建成索引，避免上千次 grep。"""
    global _cache
    if _cache is not None:
        return _cache
    idx = set()
    call_re = re.compile(r"\b(\w+)\s*[<(]")
    decl_re = re.compile(r"\bclass\s+(\w+)|\b(?:interface|struct|enum|record)\s+(\w+)")
    for p in code_files():
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for m in call_re.finditer(text):
            idx.add(m.group(1))
        for m in decl_re.finditer(text):
            idx.add(m.group(1) or m.group(2))
    _cache = idx
    return idx

ALLOWLIST = ROOT / "tools/docs-api-allowlist.txt"

def load_allowlist():
    """文档里合法出现的“用户侧代码”名字 —— 示例里调用读者自己的方法，不是库 API。"""
    if not ALLOWLIST.exists():
        return set()
    names = set()
    for line in ALLOWLIST.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            names.add(line)
    return names

def is_known(name, index, allow):
    if name in index or name in allow:
        return True
    # [Cache(...)] / [TableName(...)] 这类特性：源码里定义的是 CacheAttribute
    return (name + "Attribute") in index

def main():
    files = [ROOT / "README.md", ROOT / "FastData/README.md"]
    docs_dir = ROOT / "FastData/docs"
    if docs_dir.is_dir():
        files += sorted(docs_dir.glob("*.md"))

    index = load_index()
    allow = load_allowlist()
    call_re = re.compile(r"(?:(\w+)\s*\.\s*)?(\w+)\s*\(")

    total_calls = 0
    problems = []
    scanned = 0
    for f in files:
        if not f.exists():
            continue
        scanned += 1
        text = f.read_text(encoding="utf-8", errors="replace")
        blocks = re.findall(r"```(?:csharp|cs)\n(.*?)```", text, re.S)
        code = "\n".join(blocks)
        calls = set()
        for m in call_re.finditer(code):
            recv, name = m.group(1), m.group(2)
            if name in SKIP_WORDS or len(name) < 3:
                continue
            calls.add((recv, name))
        total_calls += len(calls)
        missing = [(r, n) for r, n in sorted(calls, key=lambda x: x[1])
                   if not is_known(n, index, allow)]
        if missing:
            problems.append((f.relative_to(ROOT), missing, len(blocks), len(calls)))

    print(f"扫描 {scanned} 个文档，代码块内去重调用 {total_calls} 个")
    if not problems:
        print("通过：文档里出现的每个方法名都能在源码中找到定义（拼写层面，非编译验证）。")
        return 0

    print("\n以下名字在源码里找不到定义，逐个确认是“文档虚构 API”还是“用户侧示例代码”：\n")
    for rel, missing, nb, nc in problems:
        print(f"== {rel} （{nb} 块 / {nc} 调用）==")
        for r, n in missing:
            print(f"   {r + '.' if r else ''}{n}()")
        print()
    return 1

if __name__ == "__main__":
    sys.exit(main())