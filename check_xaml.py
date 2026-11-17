import sys
sys.stdout.reconfigure(encoding='utf-8')
with open(r'd:\tunvision\mwwz-samples\MwwzWpfTool\MainWindow.xaml', 'r', encoding='utf-8') as f:
    xaml = f.read()

import re
matches = re.findall(r'(?:Header|Content|Text|Title)="([^"]+)"', xaml)
for m in matches:
    words = m.split()
    if any(w.lower() in ['train', 'search', 'engine', 'contrast', 'score', 'features', 'subpixel', 'pyramid', 'metric', 'overlap', 'matches'] for w in words):
        print('English found in XAML:', m)
