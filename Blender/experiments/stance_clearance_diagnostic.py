"""Geometric comparison of the current stance line and an illustrative near stance.

This does not simulate the avatar, colliders, hands, or a real pool stroke.
Dimensions and clearance match MatchConfig.asset and ShotMath.StandBehind.
"""
import math

HALF_LENGTH, HALF_WIDTH = 1.12, .56
CLEARANCE = .195 + .28 + .02
EX, EY = HALF_LENGTH + CLEARANCE, HALF_WIDTH + CLEARANCE
SIDE, BACK = .28, .75


def outside_distance(x, y):
    if abs(x) >= EX or abs(y) >= EY:
        return 0.0
    return min(EX - abs(x), EY - abs(y))


samples = []
for ix in range(21):
    x = -1.02 + ix * 2.04 / 20
    for iy in range(11):
        y = -.46 + iy * .92 / 10
        for angle in range(0, 360, 10):
            dx, dy = math.cos(math.radians(angle)), math.sin(math.radians(angle))
            ox, oy = x - dy * SIDE, y + dx * SIDE
            tx = (EX + math.copysign(1, dx) * ox) / abs(dx) if abs(dx) > 1e-4 else math.inf
            ty = (EY + math.copysign(1, dy) * oy) / abs(dy) if abs(dy) > 1e-4 else math.inf
            back = max(BACK, min(tx, ty) + .02)
            near_x, near_y = ox - dx * BACK, oy - dy * BACK
            samples.append((back, outside_distance(near_x, near_y)))

print(f"sample_count={len(samples)} rectangle={EX:.3f}x{EY:.3f}")
for threshold in (1.0, 1.5, 2.0):
    print(f"back_gt_{threshold:.1f}={sum(back > threshold for back, _ in samples)}")
for threshold in (.2, .4, .6):
    print(f"near_stance_inside_by_gt_{threshold:.1f}={sum(gap > threshold for _, gap in samples)}")
print(f"max_back={max(back for back, _ in samples):.3f}")
print(f"max_near_inside_distance={max(gap for _, gap in samples):.3f}")
