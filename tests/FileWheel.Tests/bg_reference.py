"""Masque de reference (pretraitement identique a rembg / IS-Net) pour verifier l'implementation C#.
Usage : python3 bg_reference.py modele.onnx image.png masque.pgm"""
import sys
import numpy as np
import onnxruntime as ort
from PIL import Image

model, src, dst = sys.argv[1:4]
img = Image.open(src).convert("RGB")
im = np.array(img.resize((1024, 1024), Image.Resampling.LANCZOS)).astype(np.float64)
im = im / max(np.max(im), 1e-6)
x = (im - 0.5).transpose((2, 0, 1))[None].astype(np.float32)
s = ort.InferenceSession(model, providers=["CPUExecutionProvider"])
pred = s.run(None, {s.get_inputs()[0].name: x})[0][:, 0, :, :]
pred = (pred - pred.min()) / (pred.max() - pred.min())
mask = Image.fromarray((np.squeeze(pred) * 255).astype("uint8"), mode="L").resize(img.size, Image.Resampling.LANCZOS)
mask.save(dst)
