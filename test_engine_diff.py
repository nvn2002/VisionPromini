import cv2
import numpy as np

img54 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\engine_part_054.png", cv2.IMREAD_GRAYSCALE)
img99 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\engine_part_099.png", cv2.IMREAD_GRAYSCALE)

print("img54 mean:", np.mean(img54))
print("img99 mean:", np.mean(img99))
