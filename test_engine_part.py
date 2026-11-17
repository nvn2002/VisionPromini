import cv2
import numpy as np

img54 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\engine_part_054.png", cv2.IMREAD_GRAYSCALE)
img99 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\engine_part_099.png", cv2.IMREAD_GRAYSCALE)

print("img54 min, max, mean:", np.min(img54), np.max(img54), np.mean(img54))
print("img99 min, max, mean:", np.min(img99), np.max(img99), np.mean(img99))

# Check Sobel gradient magnitudes
dx54 = cv2.Sobel(img54, cv2.CV_32F, 1, 0, ksize=3)
dy54 = cv2.Sobel(img54, cv2.CV_32F, 0, 1, ksize=3)
mag54 = np.sqrt(dx54**2 + dy54**2)

dx99 = cv2.Sobel(img99, cv2.CV_32F, 1, 0, ksize=3)
dy99 = cv2.Sobel(img99, cv2.CV_32F, 0, 1, ksize=3)
mag99 = np.sqrt(dx99**2 + dy99**2)

print("mag54 mean:", np.mean(mag54), "pixels > 15:", np.sum(mag54 > 15))
print("mag99 mean:", np.mean(mag99), "pixels > 15:", np.sum(mag99 > 15))
