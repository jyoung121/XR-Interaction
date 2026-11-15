# -*- coding: utf-8 -*-
"""푸딩(크렘 카라멜) 모델을 짓는다.

받아온 Flan.obj 를 잘라 쓰는 길은 두 번 해 보고 접었다. 그 에셋의
커스터드는 실제로 높이/지름 0.19 짜리 아주 납작한 원반이고, 눈에
보이던 부피는 전부 **접시**가 내던 것이었다. 접시를 떼면 햄 한 조각이
되고, 세로로 늘리면 옆면이 무늬의 캐러멜 띠만 훑어 온몸이 갈색이 됐다.

그래서 모양과 색을 둘 다 쥐고 짓는다. 무늬를 안 쓰고 재질 둘로 가른다 —
MirrorRig.Reskin 이 서브메시마다 .mtl 의 Kd 를 그대로 URP 색으로 옮긴다.

    python Tools/make_pudding.py

고치면 이 스크립트를 고치고 다시 돌린다. .obj 를 손으로 건드리지 말 것.
"""
import io, math, os

SEG  = 28     # 둘레를 나눈 수
RB   = 0.50   # 아래 반지름 (접시에 닿는 쪽). 지름 1.0 이 가장 긴 변이다
RT   = 0.40   # 위 반지름 — 틀이 위로 좁으니 엎으면 위가 좁다
H    = 0.55   # 높이. 실물 플랑이 지름 7~8 cm, 높이 4~4.5 cm 다
BAND = 0.13   # 캐러멜이 옆면을 타고 내려오는 기본 깊이
DRIP = 0.09   # 흘러내린 자락이 더 내려가는 양

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   '..', 'Assets', 'Resources', 'Models')


def build():
    verts, norms, faces = [], [], []

    def V(p):
        verts.append(p); return len(verts)

    def N(p):
        n = math.sqrt(sum(c * c for c in p)) or 1.0
        norms.append(tuple(c / n for c in p)); return len(norms)

    def R(y):
        return RB + (RT - RB) * (y / H)

    slope = (RB - RT) / H          # 옆면이 기운 만큼 법선이 위를 본다
    nUp, nDn = N((0, 1, 0)), N((0, -1, 0))

    top, cam, bot, sideN, camY = [], [], [], [], []
    for i in range(SEG):
        a = 2 * math.pi * i / SEG
        ca, sa = math.cos(a), math.sin(a)
        # 캐러멜 자락. 두 파장을 겹쳐 몇 군데만 길게 흐르게 한다.
        w1 = 0.5 + 0.5 * math.sin(3 * a + 0.7)
        w2 = 0.5 + 0.5 * math.sin(7 * a + 2.1)
        y = H - (BAND + DRIP * (0.65 * w1 + 0.35 * w2))
        camY.append(y)
        top.append(V((RT * ca, H, RT * sa)))
        cam.append(V((R(y) * ca, y, R(y) * sa)))
        bot.append(V((RB * ca, 0.0, RB * sa)))
        sideN.append(N((ca, slope, sa)))

    cTop = V((0.0, H, 0.0))
    cBot = V((0.0, 0.0, 0.0))

    # 감는 방향은 **바깥에서 볼 때 반시계**다. 처음에 반대로 감았더니
    # 뚜껑이 컬링돼 사라지고, 그 구멍으로 보이는 안쪽 벽이 회색 타원으로
    # 찍혔다 — "푸딩 가운데가 회색". 눈으로는 옆면이 멀쩡해 보여서
    # 못 잡는다. 부호 있는 부피가 음수인지로 가린다 (아래 check).
    for i in range(SEG):
        j = (i + 1) % SEG
        faces.append(('caramel', [(cTop, nUp), (top[j], nUp), (top[i], nUp)]))
        faces.append(('caramel', [(top[i], sideN[i]), (top[j], sideN[j]),
                                  (cam[j], sideN[j]), (cam[i], sideN[i])]))
        faces.append(('custard', [(cam[i], sideN[i]), (cam[j], sideN[j]),
                                  (bot[j], sideN[j]), (bot[i], sideN[i])]))
        faces.append(('custard', [(cBot, nDn), (bot[i], nDn), (bot[j], nDn)]))

    return verts, norms, faces, camY


def check(verts, faces):
    """부호 있는 부피. 양수라야 면이 바깥을 본다."""
    tot = 0.0
    for _, f in faces:
        idx = [vi for vi, _ in f]
        for k in range(1, len(idx) - 1):
            a, b, c = verts[idx[0] - 1], verts[idx[k] - 1], verts[idx[k + 1] - 1]
            tot += (a[0] * (b[1] * c[2] - b[2] * c[1])
                    - a[1] * (b[0] * c[2] - b[2] * c[0])
                    + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6.0
    return tot


def main():
    verts, norms, faces, camY = build()
    vol = check(verts, faces)
    assert vol > 0, "감는 방향이 뒤집혔다 (부피 %.6f)" % vol

    out = ["# 푸딩 (크렘 카라멜). Tools/make_pudding.py 가 짓는다.",
           "# 손으로 고치지 말 것 — 스크립트를 고치고 다시 돌린다.",
           "mtllib Pudding.mtl", "o Pudding"]
    for v in verts:
        out.append("v %.6f %.6f %.6f" % v)
    for n in norms:
        out.append("vn %.6f %.6f %.6f" % n)
    cur = None
    for mtl, f in sorted(faces, key=lambda x: x[0]):
        if mtl != cur:
            out.append("usemtl " + mtl); cur = mtl
        out.append("f " + " ".join("%d//%d" % (vi, ni) for vi, ni in f))
    io.open(os.path.join(OUT, 'Pudding.obj'), 'w',
            encoding='utf-8', newline='\n').write("\n".join(out) + "\n")

    io.open(os.path.join(OUT, 'Pudding.mtl'), 'w', encoding='utf-8',
            newline='\n').write(
        "# 푸딩 재질. 무늬를 안 쓴다 — Reskin 이 Kd 를 URP 색으로 옮긴다.\n"
        "newmtl caramel\nKd 0.612 0.306 0.082\nKa 0 0 0\n"
        "Ks 0.300 0.280 0.200\nNs 48.0\n\n"
        "newmtl custard\nKd 0.937 0.835 0.561\nKa 0 0 0\n"
        "Ks 0.120 0.120 0.100\nNs 24.0\n")

    xs = [v[0] for v in verts]; ys = [v[1] for v in verts]
    print("verts %d  faces %d  volume %+.6f" % (len(verts), len(faces), vol))
    print("span %.4f x %.4f   h/w %.3f   caramel y %.3f~%.3f (top %.3f)"
          % (max(xs) - min(xs), max(ys) - min(ys),
             (max(ys) - min(ys)) / (max(xs) - min(xs)),
             min(camY), max(camY), H))


if __name__ == '__main__':
    main()
