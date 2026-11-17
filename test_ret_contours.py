import cv2
import numpy as np

gray = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\retainers_01.png", cv2.IMREAD_GRAYSCALE)
blur = cv2.GaussianBlur(gray, (5, 5), 1.5)
_, binMat = cv2.threshold(blur, 0, 255, cv2.THRESH_BINARY_INV | cv2.THRESH_OTSU)
contours, _ = cv2.findContours(binMat, cv2.RETR_TREE, cv2.CHAIN_APPROX_NONE)
print("Found contours:", len(contours))
for i, c in enumerate(contours):
    area = cv2.contourArea(c)
    length = cv2.arcLength(c, True)
    if area > 100:
        x, y, w, h = cv2.boundingRect(c)
        print(f"Contour {i}: area={area}, len={length:.1f}, bbox=({x},{y},{w},{h})")
