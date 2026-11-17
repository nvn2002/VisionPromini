import cv2
import numpy as np

img54 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\engine_part_054.png", cv2.IMREAD_GRAYSCALE)
img99 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\engine_part_099.png", cv2.IMREAD_GRAYSCALE)
print("img54:", img54.shape if img54 is not None else None)
print("img99:", img99.shape if img99 is not None else None)

ret1 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\retainers_01.png", cv2.IMREAD_GRAYSCALE)
ret2 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\retainers_02.png", cv2.IMREAD_GRAYSCALE)
print("ret1:", ret1.shape if ret1 is not None else None)
print("ret2:", ret2.shape if ret2 is not None else None)
