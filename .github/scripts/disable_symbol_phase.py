# -*- coding: utf-8 -*-
"""Unity 가 만든 Xcode 프로젝트에서 심볼 처리 쉘스크립트 단계를 무력화한다.

서명 없이(CODE_SIGNING_ALLOWED=NO) 빌드하면 'Process symbols for UnityFramework'
단계가 exit 65 로 실패한다. 이 단계는 크래시 로그 심볼화를 위한 것이라
사이드로드 테스트에는 필요 없다.

사용: python3 disable_symbol_phase.py <project.pbxproj 경로>

정규식을 쓰지 않고 줄 단위로 처리한다. pbxproj 에서 shellScript 는 한 줄이다.
"""
import io
import sys

KEYWORDS = ("symbol",)          # 이름에 이 단어가 들어간 단계만 건드린다


def main(path):
    lines = io.open(path, encoding="utf-8").read().splitlines(keepends=True)

    # 1단계: 각 쉘스크립트 블록의 범위와 이름을 찾는다
    blocks = []          # (시작줄, 끝줄, 이름)
    start = None
    name = ""
    for i, ln in enumerate(lines):
        s = ln.strip()
        if "isa = PBXShellScriptBuildPhase;" in s:
            start = i
            name = ""
            # 블록 시작은 보통 바로 윗줄의 "XXXX /* 이름 */ = {"
            if i > 0 and "/*" in lines[i - 1]:
                seg = lines[i - 1]
                a, b = seg.find("/*"), seg.find("*/")
                if a != -1 and b > a:
                    name = seg[a + 2:b].strip()
        elif start is not None:
            if s.startswith("name = "):
                name = s[len("name = "):].rstrip(";").strip().strip('"')
            if s == "};":
                blocks.append((start, i, name))
                start = None

    print("쉘스크립트 단계 %d개" % len(blocks))

    # 2단계: 대상 블록의 shellScript 줄을 통째로 교체
    changed = 0
    for b_start, b_end, b_name in blocks:
        hit = any(k in b_name.lower() for k in KEYWORDS)
        print("  %-9s %s" % ("무력화" if hit else "유지", b_name or "(이름없음)"))
        if not hit:
            continue
        for i in range(b_start, b_end + 1):
            stripped = lines[i].lstrip()
            if stripped.startswith("shellScript = "):
                indent = lines[i][:len(lines[i]) - len(stripped)]
                lines[i] = indent + 'shellScript = "exit 0;";\n'
                changed += 1
                break

    if changed == 0:
        print("경고: 바꾼 줄이 없다. 단계 이름이 달라졌을 수 있다.")
    else:
        io.open(path, "w", encoding="utf-8", newline="").writelines(lines)
        print("교체한 shellScript: %d줄" % changed)
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("사용: python3 disable_symbol_phase.py <project.pbxproj>")
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
