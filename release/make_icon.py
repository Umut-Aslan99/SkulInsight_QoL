# Draws the 256x256 Thunderstore icon for SkulInsight QoL as 32x32 pixel art (original art, no game assets).
from PIL import Image

N, S = 32, 8
BG1, BG2 = (34, 22, 48), (52, 34, 72)          # outer border, inner panel
SKULL, SHADE, EYE = (238, 232, 220), (178, 168, 156), (26, 18, 36)
NUM, NUM_EDGE = (255, 170, 60), (110, 45, 18)   # the damage number and its outline
SPARK = (255, 225, 90)

img = Image.new("RGBA", (N, N), BG1 + (255,))
px = img.load()


def put(x, y, c):
    px[x, y] = c + (255,)


# inner panel with cut corners
for y in range(2, 30):
    for x in range(2, 30):
        put(x, y, BG2)
for (x, y) in [(2, 2), (29, 2), (2, 29), (29, 29)]:
    put(x, y, BG1)

# skull: X = bone, s = shade, o = holes
skull = [
    "....XXXXXXXXXX....",
    "..XXXXXXXXXXXXXX..",
    ".XXXXXXXXXXXXXXXX.",
    "XXXXXXXXXXXXXXXXXX",
    "XXXooooXXXXooooXXX",
    "XXXooooXXXXooooXXX",
    "XXXooooXXXXooooXXX",
    "XXXXooXXXXXXooXXXs",
    "sXXXXXXXooXXXXXXXs",
    ".sXXXXXXooXXXXXXs.",
    "..sXXXXXXXXXXXXs..",
    "...XX.XX.XX.XX....",
    "...XX.XX.XX.XX....",
    "....ssssssssss....",
]
for j, row in enumerate(skull):
    for i, ch in enumerate(row):
        c = {"X": SKULL, "s": SHADE, "o": EYE}.get(ch)
        if c:
            put(7 + i, 12 + j, c)

# damage number "99" (4x6 digits), outlined on the outside only
nine = ["XXXX", "X..X", "X..X", "XXXX", "...X", "XXXX"]
cells = set()
for x0 in (5, 10):
    for j, row in enumerate(nine):
        for i, ch in enumerate(row):
            if ch == "X":
                cells.add((x0 + i, 4 + j))
holes = {(x0 + 1 + i, 5 + j) for x0 in (5, 10) for i in range(2) for j in range(2)}
for (x, y) in cells:
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            n = (x + dx, y + dy)
            if n not in cells and n not in holes:
                put(n[0], n[1], NUM_EDGE)
for (x, y) in cells:
    put(x, y, NUM)

# crit spark, top right
for (x, y) in [(24, 4), (24, 5), (24, 6), (24, 7), (24, 8), (22, 6), (23, 6), (25, 6), (26, 6)]:
    put(x, y, SPARK)
put(21, 3, SPARK)
put(27, 9, SPARK)

img.resize((N * S, N * S), Image.NEAREST).save("icon.png")
print("icon.png written")
