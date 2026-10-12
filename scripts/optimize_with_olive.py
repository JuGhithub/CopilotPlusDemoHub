#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import os, sys, shutil, subprocess

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.abspath(os.path.join(SCRIPT_DIR, '..'))
MODELS_DIR = os.path.join(ROOT_DIR, 'models')
SCRATCH_DIR = os.path.join(ROOT_DIR, 'scratch')

def optimize_model_with_olive(input_onnx: str, output_onnx: str, pass_name: str = 'onnxfloattofloat16', force: bool = False):
    if not os.path.exists(input_onnx):
        raise FileNotFoundError(f'Input model not found: {input_onnx}')
    if os.path.exists(output_onnx) and not force:
        in_size_mb = os.path.getsize(input_onnx) / (1024 * 1024)
        out_size_mb = os.path.getsize(output_onnx) / (1024 * 1024)
        print(f'[Olive] {os.path.basename(output_onnx)} already exists ({out_size_mb:.2f} MB vs {in_size_mb:.2f} MB), skipping.')
        return
    base_name = os.path.splitext(os.path.basename(input_onnx))[0]
    temp_out_dir = os.path.join(SCRATCH_DIR, f'olive_{base_name}')
    if os.path.exists(temp_out_dir):
        shutil.rmtree(temp_out_dir, ignore_errors=True)
    print(f'[Olive] Optimizing {os.path.basename(input_onnx)} with pass \'{pass_name}\'...', flush=True)
    cmd = [
        sys.executable, '-m', 'olive', 'run-pass',
        '--pass-name', pass_name,
        '-m', input_onnx,
        '-o', temp_out_dir,
        '--device', 'gpu',
        '--provider', 'DmlExecutionProvider'
    ]
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        print(f'[Error] Olive optimization failed:\n{res.stderr}\n{res.stdout}')
        sys.exit(1)
    generated_onnx = os.path.join(temp_out_dir, os.path.basename(input_onnx))
    if not os.path.exists(generated_onnx):
        for root, _, files in os.walk(temp_out_dir):
            for f in files:
                if f.endswith('.onnx'):
                    generated_onnx = os.path.join(root, f)
                    break
    os.makedirs(os.path.dirname(output_onnx), exist_ok=True)
    shutil.copy2(generated_onnx, output_onnx)
    in_size_mb = os.path.getsize(input_onnx) / (1024 * 1024)
    out_size_mb = os.path.getsize(output_onnx) / (1024 * 1024)
    print(f'[Olive] Success! Saved to {output_onnx}')
    print(f'        Original size:  {in_size_mb:.2f} MB')
    print(f'        Optimized size: {out_size_mb:.2f} MB ({(1 - out_size_mb/in_size_mb)*100:.1f}% reduction)')

def main():
    print('=== Microsoft Olive Optimization Pipeline ===')
    os.makedirs(MODELS_DIR, exist_ok=True)
    os.makedirs(SCRATCH_DIR, exist_ok=True)
    
    # 1. Vision models
    yolov8s_input = os.path.join(MODELS_DIR, 'yolov8s-world.onnx')
    yolov8s_output = os.path.join(MODELS_DIR, 'yolov8s-world_olive.onnx')
    if os.path.exists(yolov8s_input):
        optimize_model_with_olive(yolov8s_input, yolov8s_output)
    
    yolov8n_seg_input = os.path.join(MODELS_DIR, 'yolov8n-seg.onnx')
    yolov8n_seg_output = os.path.join(MODELS_DIR, 'yolov8n-seg_olive.onnx')
    if os.path.exists(yolov8n_seg_input):
        optimize_model_with_olive(yolov8n_seg_input, yolov8n_seg_output)

    # 2. OCR models
    ocr_dir = os.path.join(MODELS_DIR, 'ocr')
    os.makedirs(ocr_dir, exist_ok=True)
    try:
        import rapidocr_onnxruntime
        rapid_models_dir = os.path.join(os.path.dirname(rapidocr_onnxruntime.__file__), 'models')
        det_src = os.path.join(rapid_models_dir, 'ch_PP-OCRv4_det_infer.onnx')
        rec_src = os.path.join(rapid_models_dir, 'ch_PP-OCRv4_rec_infer.onnx')
        
        det_raw = os.path.join(ocr_dir, 'ch_PP-OCRv4_det_infer.onnx')
        det_olive = os.path.join(ocr_dir, 'ch_PP-OCRv4_det_infer_olive.onnx')
        if os.path.exists(det_src) and not os.path.exists(det_raw):
            shutil.copy2(det_src, det_raw)
        if os.path.exists(det_raw):
            optimize_model_with_olive(det_raw, det_olive)

        rec_raw = os.path.join(ocr_dir, 'ch_PP-OCRv4_rec_infer.onnx')
        rec_olive = os.path.join(ocr_dir, 'ch_PP-OCRv4_rec_infer_olive.onnx')
        if os.path.exists(rec_src) and not os.path.exists(rec_raw):
            shutil.copy2(rec_src, rec_raw)
        if os.path.exists(rec_raw):
            optimize_model_with_olive(rec_raw, rec_olive)
    except Exception as e:
        print(f'[Warning] Could not optimize OCR models: {e}')

    print('[Done] All models optimized with Microsoft Olive!')

if __name__ == '__main__':
    main()
