import argparse
import base64
import json
import os
import sys
import threading
import time
import cv2
import numpy as np
from ultralytics import YOLO

# Expanded vocabulary matching the newly trained OpenVINO model
CUP_LABELS = {
    "cup", "coffee cup", "mug", "coffee mug", "tea cup", "paper cup",
    "espresso cup", "travel mug", "thermos", "tumbler", "glass",
    "drink", "beverage", "water bottle", "bottle", "can"
}

GLASSES_LABELS = {
    "glasses", "eyeglasses", "sunglasses", "reading glasses", "spectacles", "eyewear"
}

HEADSET_LABELS = {
    "headphones", "headset", "earphones", "earbuds", "head phone"
}

def find_model_path(base_dir):
    cur = base_dir
    for _ in range(6):
        cand = os.path.abspath(os.path.join(cur, "models", "yolov8s-world_openvino_model"))
        if os.path.isdir(cand) and os.path.exists(os.path.join(cand, "yolov8s-world.xml")):
            return cand
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return "models/yolov8s-world_openvino_model"

def find_onnx_model_path(base_dir):
    cur = base_dir
    for _ in range(6):
        cand = os.path.abspath(os.path.join(cur, "models", "yolov8s-world.onnx"))
        if os.path.isfile(cand):
            return cand
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return "models/yolov8s-world.onnx"

def find_olive_model_path(base_dir):
    cur = base_dir
    for _ in range(6):
        cand = os.path.abspath(os.path.join(cur, "models", "yolov8s-world_olive.onnx"))
        if os.path.isfile(cand):
            return cand
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return "models/yolov8s-world_olive.onnx"

def resolve_ov_device(req_device):
    req = (req_device or "GPU").upper()
    if "GPU" in req:
        return "intel:gpu"
    elif "NPU" in req:
        return "intel:gpu"
    return "intel:cpu"

def draw_hud_box(img, box, label, color, conf):
    x1, y1, x2, y2 = [int(v) for v in box]
    h, w, _ = img.shape
    x1, y1 = max(0, x1), max(0, y1)
    x2, y2 = min(w, x2), min(h, y2)

    # Semi-transparent fill
    overlay = img.copy()
    cv2.rectangle(overlay, (x1, y1), (x2, y2), color, -1)
    cv2.addWeighted(overlay, 0.12, img, 0.88, 0, img)

    # Neon border
    cv2.rectangle(img, (x1, y1), (x2, y2), color, 2, cv2.LINE_AA)

    # Corner brackets
    length = min(20, (x2 - x1) // 4, (y2 - y1) // 4)
    if length > 4:
        cv2.line(img, (x1, y1), (x1 + length, y1), color, 3, cv2.LINE_AA)
        cv2.line(img, (x1, y1), (x1, y1 + length), color, 3, cv2.LINE_AA)
        cv2.line(img, (x2, y1), (x2 - length, y1), color, 3, cv2.LINE_AA)
        cv2.line(img, (x2, y1), (x2, y1 + length), color, 3, cv2.LINE_AA)
        cv2.line(img, (x1, y2), (x1 + length, y2), color, 3, cv2.LINE_AA)
        cv2.line(img, (x1, y2), (x1, y2 - length), color, 3, cv2.LINE_AA)
        cv2.line(img, (x2, y2), (x2 - length, y2), color, 3, cv2.LINE_AA)
        cv2.line(img, (x2, y2), (x2, y2 - length), color, 3, cv2.LINE_AA)

    # Badge label
    text = f"{label} ({int(conf * 100)}%)"
    (tw, th), _ = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, 0.50, 2)
    badge_y1 = max(0, y1 - th - 8)
    badge_y2 = y1
    cv2.rectangle(img, (x1, badge_y1), (x1 + tw + 10, badge_y2), color, -1)
    cv2.putText(img, text, (x1 + 5, badge_y2 - 5), cv2.FONT_HERSHEY_SIMPLEX, 0.50, (10, 15, 25), 2, cv2.LINE_AA)

def analyze_and_draw(model, frame, ov_device, conf_thresh=0.20, is_live=False):
    results = model(frame, conf=conf_thresh, device=ov_device, verbose=False)

    cup_detected = False
    cup_conf = 0.0
    cup_box = None
    cup_name = ""

    glasses_detected = False
    glasses_conf = 0.0
    glasses_box = None
    glasses_name = ""

    headset_detected = False
    headset_conf = 0.0
    headset_box = None
    headset_name = ""

    annotated = frame.copy()

    for r in results:
        for box in r.boxes:
            cls_id = int(box.cls[0].item())
            conf = float(box.conf[0].item())
            name = model.names.get(cls_id, "").lower()
            xyxy = box.xyxy[0].tolist()

            # Filter tiny background noise (< 25px in width or height)
            w_box = xyxy[2] - xyxy[0]
            h_box = xyxy[3] - xyxy[1]
            if w_box < 25 or h_box < 25:
                continue

            if name in CUP_LABELS and conf > cup_conf:
                cup_detected = True
                cup_conf = conf
                cup_box = xyxy
                cup_name = name

            if name in GLASSES_LABELS and conf > glasses_conf:
                glasses_detected = True
                glasses_conf = conf
                glasses_box = xyxy
                glasses_name = name

            if name in HEADSET_LABELS and conf > headset_conf:
                headset_detected = True
                headset_conf = conf
                headset_box = xyxy
                headset_name = name

    # Draw boxes
    if cup_detected and cup_box is not None:
        label_txt = "TASSE DE CAFE" if "cup" in cup_name or "mug" in cup_name else f"BOISSON ({cup_name.upper()})"
        draw_hud_box(annotated, cup_box, label_txt, (0, 195, 255), cup_conf)

    if glasses_detected and glasses_box is not None:
        draw_hud_box(annotated, glasses_box, "LUNETTES", (255, 229, 0), glasses_conf)

    if headset_detected and headset_box is not None:
        draw_hud_box(annotated, headset_box, "CASQUE AUDIO", (255, 60, 200), headset_conf)

    # Top Status Banner on the image
    access_granted = cup_detected and glasses_detected and headset_detected
    h, w, _ = annotated.shape
    banner_h = 38
    cv2.rectangle(annotated, (0, 0), (w, banner_h), (12, 18, 30), -1)

    prefix = "LIVE SCAN" if is_live else "SCAN"
    if access_granted:
        cv2.line(annotated, (0, banner_h), (w, banner_h), (50, 215, 75), 2)
        cv2.putText(annotated, f"[{prefix} : ACCES AUTORISE] 3/3 PREREQUIS CONFORMES", (12, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.52, (50, 215, 75), 2, cv2.LINE_AA)
    else:
        missing = []
        if not cup_detected: missing.append("TASSE")
        if not glasses_detected: missing.append("LUNETTES")
        if not headset_detected: missing.append("CASQUE")
        msg = f"[{prefix} : EN ATTENTE] MANQUE : {', '.join(missing)}"
        cv2.line(annotated, (0, banner_h), (w, banner_h), (50, 50, 240), 2)
        cv2.putText(annotated, msg, (12, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.50, (80, 100, 255), 2, cv2.LINE_AA)

    return (annotated, cup_detected, cup_conf, cup_box, cup_name,
            glasses_detected, glasses_conf, glasses_box, glasses_name,
            headset_detected, headset_conf, headset_box, headset_name, access_granted)

def main():
    sys.stdout.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser(description="Security Access Vision Service: Coffee & Glasses Detection")
    parser.add_argument("--image", default="", help="Path to input image file")
    parser.add_argument("--webcam", action="store_true", help="Capture a single live frame from webcam")
    parser.add_argument("--stream", action="store_true", help="Run real-time webcam video stream with live inference")
    parser.add_argument("--out", default="", help="Path to output annotated image")
    parser.add_argument("--conf", type=float, default=0.20, help="Confidence threshold")
    parser.add_argument("--device", default="GPU", choices=["NPU", "GPU", "CPU"], help="Compute device")
    parser.add_argument("--engine", default="openvino", choices=["openvino", "onnx", "onnx_olive"], help="Inference engine backend (openvino, onnx, onnx_olive)")
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))

    if args.engine in ("onnx", "onnx_olive"):
        is_olive = args.engine == "onnx_olive"
        model_path = find_olive_model_path(script_dir) if is_olive else find_onnx_model_path(script_dir)
        if not os.path.exists(model_path):
            print(json.dumps({
                "type": "error",
                "message": f"Modèle {'Olive FP16' if is_olive else 'ONNX'} introuvable : {model_path}"
            }, ensure_ascii=False), flush=True)
            sys.exit(1)
        try:
            import onnxruntime as ort
            model = YOLO(model_path, task="detect")
            dummy = np.zeros((640, 640, 3), dtype=np.uint8)
            _ = model(dummy, verbose=False)
            if "GPU" in args.device.upper() or "NPU" in args.device.upper():
                model.predictor.model.session = ort.InferenceSession(
                    model_path,
                    providers=['DmlExecutionProvider', 'CPUExecutionProvider']
                )
                engine_name = "ONNX Runtime + Olive FP16 (DirectML)" if is_olive else "ONNX Runtime (DirectML)"
            else:
                model.predictor.model.session = ort.InferenceSession(
                    model_path,
                    providers=['CPUExecutionProvider']
                )
                engine_name = "ONNX Runtime + Olive FP16 (CPU)" if is_olive else "ONNX Runtime (CPU)"
            inference_device = "cpu"
        except Exception as e:
            print(json.dumps({
                "type": "error",
                "message": f"Impossible de charger le modèle {'Olive FP16' if is_olive else 'ONNX'} ({model_path}) : {e}"
            }, ensure_ascii=False), flush=True)
            sys.exit(1)
    else:
        model_dir = find_model_path(script_dir)
        if not os.path.exists(model_dir):
            print(json.dumps({
                "type": "error",
                "message": f"Modèle OpenVINO introuvable : {model_dir}"
            }, ensure_ascii=False), flush=True)
            sys.exit(1)
        inference_device = resolve_ov_device(args.device)
        engine_name = "Intel OpenVINO"
        try:
            model = YOLO(model_dir)
        except Exception as e:
            print(json.dumps({
                "type": "error",
                "message": f"Impossible de charger le modèle OpenVINO ({model_dir}) : {e}"
            }, ensure_ascii=False), flush=True)
            sys.exit(1)

    # -------------------------------------------------------------------
    # MODE 1: REAL-TIME CONTINUOUS WEBCAM STREAM
    # -------------------------------------------------------------------
    if args.stream:
        cap = cv2.VideoCapture(0)
        if not cap.isOpened():
            print(json.dumps({
                "type": "error",
                "message": "Impossible d'accéder à la webcam en direct (caméra absente ou occupée)."
            }, ensure_ascii=False), flush=True)
            sys.exit(1)

        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)

        # Non-blocking stdin listener for stop commands
        stop_event = threading.Event()
        def stdin_listener():
            try:
                for line in sys.stdin:
                    if "stop" in line.lower() or "quit" in line.lower() or "exit" in line.lower():
                        stop_event.set()
                        break
            except:
                stop_event.set()

        t_stdin = threading.Thread(target=stdin_listener, daemon=True)
        t_stdin.start()

        # Warmup grab
        for _ in range(3):
            cap.read()

        t_start = time.perf_counter()
        frame_idx = 0

        while not stop_event.is_set():
            ret, frame = cap.read()
            if not ret or frame is None:
                time.sleep(0.02)
                continue

            t0 = time.perf_counter()
            (annotated, cup_det, cup_conf, cup_box, cup_name,
             glasses_det, glasses_conf, glasses_box, glasses_name,
             headset_det, headset_conf, headset_box, headset_name, granted) = analyze_and_draw(
                model, frame, inference_device, conf_thresh=args.conf, is_live=True
            )
            infer_ms = int((time.perf_counter() - t0) * 1000)

            frame_idx += 1
            elapsed = time.perf_counter() - t_start
            fps = round(frame_idx / max(0.001, elapsed), 1)

            # Encode frame as JPEG to memory
            _, buf = cv2.imencode('.jpg', annotated, [cv2.IMWRITE_JPEG_QUALITY, 72])
            b64_str = base64.b64encode(buf).decode('ascii')

            payload = {
                "type": "live_frame",
                "cup_detected": cup_det,
                "cup_conf": round(cup_conf, 2),
                "cup_label": cup_name,
                "cup_box": [round(v, 1) for v in cup_box] if cup_box else None,
                "glasses_detected": glasses_det,
                "glasses_conf": round(glasses_conf, 2),
                "glasses_label": glasses_name,
                "glasses_box": [round(v, 1) for v in glasses_box] if glasses_box else None,
                "headset_detected": headset_det,
                "headset_conf": round(headset_conf, 2),
                "headset_label": headset_name,
                "headset_box": [round(v, 1) for v in headset_box] if headset_box else None,
                "access_granted": granted,
                "infer_time_ms": infer_ms,
                "fps": fps,
                "device": args.device,
                "engine": engine_name,
                "frame_b64": b64_str
            }
            print(json.dumps(payload, ensure_ascii=False), flush=True)

        cap.release()
        return

    # -------------------------------------------------------------------
    # MODE 2: SINGLE-SHOT (IMAGE FILE OR SINGLE WEBCAM SNAPSHOT)
    # -------------------------------------------------------------------
    source_name = "image"
    frame = None

    if args.webcam:
        source_name = "webcam"
        cap = cv2.VideoCapture(0)
        if not cap.isOpened():
            print(json.dumps({
                "type": "error",
                "message": "Impossible d'accéder à la webcam."
            }, ensure_ascii=False), flush=True)
            sys.exit(1)
        for _ in range(3):
            cap.read()
        ret, frame = cap.read()
        cap.release()
        if not ret or frame is None:
            print(json.dumps({
                "type": "error",
                "message": "Échec de capture webcam."
            }, ensure_ascii=False), flush=True)
            sys.exit(1)
    else:
        if not args.image or not os.path.exists(args.image):
            print(json.dumps({
                "type": "error",
                "message": f"Fichier image source introuvable : {args.image}"
            }, ensure_ascii=False), flush=True)
            sys.exit(1)
        frame = cv2.imread(args.image)
        if frame is None:
            print(json.dumps({
                "type": "error",
                "message": f"Impossible de décoder l'image : {args.image}"
            }, ensure_ascii=False), flush=True)
            sys.exit(1)

    t0 = time.perf_counter()
    (annotated, cup_det, cup_conf, cup_box, cup_name,
     glasses_det, glasses_conf, glasses_box, glasses_name,
     headset_det, headset_conf, headset_box, headset_name, granted) = analyze_and_draw(
        model, frame, inference_device, conf_thresh=args.conf, is_live=False
    )
    infer_ms = int((time.perf_counter() - t0) * 1000)

    # Save output annotated image
    out_path = args.out
    if not out_path:
        out_dir = os.path.join(script_dir, "Data", "Images", "outputs")
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, f"access_scan_{int(time.time()*1000)}.jpg")
    else:
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)

    cv2.imwrite(out_path, annotated)

    result = {
        "type": "access_verification_result",
        "cup_detected": cup_det,
        "cup_conf": round(cup_conf, 2),
        "cup_label": cup_name,
        "cup_box": [round(v, 1) for v in cup_box] if cup_box else None,
        "glasses_detected": glasses_det,
        "glasses_conf": round(glasses_conf, 2),
        "glasses_label": glasses_name,
        "glasses_box": [round(v, 1) for v in glasses_box] if glasses_box else None,
        "headset_detected": headset_det,
        "headset_conf": round(headset_conf, 2),
        "headset_label": headset_name,
        "headset_box": [round(v, 1) for v in headset_box] if headset_box else None,
        "access_granted": granted,
        "annotated_image": os.path.abspath(out_path),
        "infer_time_ms": infer_ms,
        "device": args.device,
        "engine": engine_name,
        "source": source_name
    }
    print(json.dumps(result, ensure_ascii=False), flush=True)

if __name__ == "__main__":
    main()
