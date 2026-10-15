"""检查文档入口与职责；实现真实性由 PR 中的代码和运行证据审查。"""

from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit


def check_documents(root):
    errors = []
    module = "Assets/ForgottenMemories/PastoralGridV2/README.md"
    required = ["README.md", "CONTRIBUTING.md", "Docs/GitWorkflow.md", module]
    for name in required:
        if not (root / name).is_file():
            errors.append(f"Missing document: {name}")
    if errors:
        return errors

    overview = (root / "README.md").read_text(encoding="utf-8-sig")
    for target in [module, "CONTRIBUTING.md", "Docs/Architecture.md", "Docs/GitWorkflow.md"]:
        if f"]({target})" not in overview:
            errors.append(f"Root README must link to {target}")

    # 根目录保持简介与导航，Demo 的详细章节归对应模块。
    detail_sections = {"快速运行", "已实现功能", "代码结构与调用", "验证与复现", "当前边界"}
    headings = set(re.findall(r"^##\s+(.+?)\s*$", overview, re.MULTILINE))
    for heading in sorted(detail_sections & headings):
        errors.append(f"Move demo details from root README to module README: {heading}")
    module_text = (root / module).read_text(encoding="utf-8-sig")
    for heading in sorted(detail_sections):
        if f"## {heading}\n" not in module_text:
            errors.append(f"Module README is missing section: {heading}")

    # 只检查版本控制范围内的文档，不扫描 Library 等本地缓存。
    names = subprocess.check_output(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=root
    ).decode("utf-8").split("\0")
    for name in sorted(set(names)):
        if not name.endswith(".md"):
            continue
        path = root / name
        if not path.is_file():
            continue
        for target in re.findall(r"!?\[[^\]]*\]\(([^)\s]+)\)", path.read_text(encoding="utf-8-sig")):
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            destination = (path.parent / unquote(parsed.path)).resolve()
            if not destination.is_relative_to(root) or not destination.exists():
                errors.append(f"Invalid local link in {name}: {target}")
    return errors


if __name__ == "__main__":
    repository = Path(__file__).resolve().parents[2]
    issues = check_documents(repository)
    if issues:
        print("\n".join(issues), file=sys.stderr)
        raise SystemExit(1)
    print("Documentation responsibilities and local links passed.")
