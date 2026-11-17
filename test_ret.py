import cv2

ret1 = cv2.imread(r"d:\tunvision\mwwz-samples\imgs\retainers_01.png")
print("ret1 size:", ret1.shape)
cv2.imwrite(r"d:\tunvision\mwwz-samples\test_ret1_out.png", ret1)
