import argparse
import json
import os
import sys
import time
import cv2
import numpy as np
import openvino as ov

COCO_CLASSES = [
    'person', 'bicycle', 'car', 'motorcycle', 'airplane', 'bus', 'train', 'truck', 'boat', 'traffic light',
    'fire hydrant', 'stop sign', 'parking meter', 'bench', 'bird', 'cat', 'dog', 'horse', 'sheep', 'cow',
    'elephant', 'bear', 'zebra', 'giraffe', 'backpack', 'umbrella', 'handbag', 'tie', 'suitcase', 'frisbee',
    'skis', 'snowboard', 'sports ball', 'kite', 'baseball bat', 'baseball glove', 'skateboard', 'surfboard',
    'tennis racket', 'bottle', 'wine glass', 'cup', 'fork', 'knife', 'spoon', 'bowl', 'banana', 'apple',
    'sandwich', 'orange', 'broccoli', 'carrot', 'hot dog', 'pizza', 'donut', 'cake', 'chair', 'couch',
    'potted plant', 'bed', 'dining table', 'toilet', 'tv', 'laptop', 'mouse', 'remote', 'keyboard',
    'cell phone', 'microwave', 'oven', 'toaster', 'sink', 'refrigerator', 'book', 'clock', 'vase',
    'scissors', 'teddy bear', 'hair drier', 'toothbrush'
]

FRENCH_CLASS_MAP = {
    "cravate": ["tie"],
    "tasse": ["cup", "wine glass"],
    "cafe": ["cup"],
    "café": ["cup"],
    "mug": ["cup"],
    "verre": ["wine glass", "cup"],
    "bouteille": ["bottle"],
    "telephone": ["cell phone"],
    "téléphone": ["cell phone"],
    "smartphone": ["cell phone"],
    "portable": ["cell phone", "laptop"],
    "ordinateur": ["laptop"],
    "pc": ["laptop"],
    "laptop": ["laptop"],
    "clavier": ["keyboard"],
    "souris": ["mouse"],
    "ecran": ["tv", "laptop"],
    "écran": ["tv", "laptop"],
    "plante": ["potted plant", "bowl", "vase"],
    "succulente": ["potted plant"],
    "pot": ["potted plant", "bowl", "vase"],
    "fleur": ["potted plant", "vase"],
    "vase": ["vase", "potted plant"],
    "personne": ["person"],
    "passant": ["person"],
    "passants": ["person"],
    "touriste": ["person"],
    "touristes": ["person"],
    "homme": ["person"],
    "femme": ["person"],
    "gens": ["person"],
    "arriere-plan": ["person"],
    "arrière-plan": ["person"],
    "fond": ["person"],
    "voiture": ["car"],
    "auto": ["car"],
    "vehicule": ["car", "truck"],
    "véhicule": ["car", "truck"],
    "camion": ["truck"],
    "velo": ["bicycle"],
    "vélo": ["bicycle"],
    "moto": ["motorcycle"],
    "sac": ["handbag", "backpack", "suitcase"],
    "sacoche": ["handbag"],
    "valise": ["suitcase"],
    "lunette": ["sunglasses"],
    "lunettes": ["sunglasses"],
    "chaise": ["chair"],
    "fauteuil": ["couch", "chair"],
    "siege": ["chair"],
    "siège": ["chair"],
    "livre": ["book"],
    "cahier": ["book"],
    "table": ["dining table"],
    "bureau": ["dining table"],
    "bateau": ["boat"],
    "barque": ["boat"],
    "chien": ["dog"],
    "chat": ["cat"],
    "oiseau": ["bird"],
    "cheval": ["horse"]
}

CLASS_NAME_FR = {
    "tie": "Cravate",
    "cup": "Tasse de café",
    "wine glass": "Verre",
    "bottle": "Bouteille",
    "cell phone": "Téléphone portable",
    "laptop": "Ordinateur portable",
    "keyboard": "Clavier",
    "mouse": "Souris",
    "tv": "Écran",
    "potted plant": "Plante en pot",
    "bowl": "Bol / Récipient",
    "vase": "Vase",
    "person": "Personne",
    "car": "Voiture",
    "truck": "Camion",
    "bus": "Bus",
    "bicycle": "Vélo",
    "motorcycle": "Moto",
    "handbag": "Sac à main",
    "backpack": "Sac à dos",
    "suitcase": "Valise",
    "chair": "Chaise",
    "couch": "Canapé",
    "dining table": "Table / Bureau",
    "book": "Livre",
    "boat": "Bateau",
    "dog": "Chien",
    "cat": "Chat",
    "bird": "Oiseau",
    "horse": "Cheval"
}

def find_model_xml(script_dir):
    candidates = [
        os.path.join(script_dir, "models", "yolov8n-seg_openvino_model", "yolov8n-seg.xml"),
        os.path.join(script_dir, "..", "models", "yolov8n-seg_openvino_model", "yolov8n-seg.xml"),
        os.path.join(script_dir, "yolov8n-seg_openvino_model", "yolov8n-seg.xml"),
        "models/yolov8n-seg_openvino_model/yolov8n-seg.xml"
    ]
    cur = script_dir
    for _ in range(7):
        c = os.path.abspath(os.path.join(cur, "models", "yolov8n-seg_openvino_model", "yolov8n-seg.xml"))
        if os.path.exists(c):
            return c
        p = os.path.dirname(cur)
        if p == cur:
            break
        cur = p
    for cand in candidates:
        if os.path.exists(cand):
            return os.path.abspath(cand)
    return ""

def find_onnx_model(script_dir):
    cur = script_dir
    for _ in range(7):
        c = os.path.abspath(os.path.join(cur, "models", "yolov8n-seg.onnx"))
        if os.path.exists(c):
            return c
        p = os.path.dirname(cur)
        if p == cur:
            break
        cur = p
    return "models/yolov8n-seg.onnx"

def find_olive_model(script_dir):
    cur = script_dir
    for _ in range(7):
        c = os.path.abspath(os.path.join(cur, "models", "yolov8n-seg_olive.onnx"))
        if os.path.exists(c):
            return c
        p = os.path.dirname(cur)
        if p == cur:
            break
        cur = p
    return "models/yolov8n-seg_olive.onnx"

ENGLISH_CLASS_MAP = {
    "cup": ["cup", "wine glass"],
    "coffee": ["cup"],
    "mug": ["cup"],
    "tea": ["cup"],
    "glass": ["wine glass", "cup"],
    "wine": ["wine glass"],
    "bottle": ["bottle"],
    "phone": ["cell phone"],
    "smartphone": ["cell phone"],
    "cellphone": ["cell phone"],
    "mobile": ["cell phone"],
    "telephone": ["cell phone"],
    "laptop": ["laptop"],
    "computer": ["laptop"],
    "pc": ["laptop"],
    "screen": ["tv", "laptop"],
    "monitor": ["tv", "laptop"],
    "keyboard": ["keyboard"],
    "mouse": ["mouse"],
    "tie": ["tie"],
    "necktie": ["tie"],
    "tourist": ["person"],
    "tourists": ["person"],
    "person": ["person"],
    "people": ["person"],
    "passerby": ["person"],
    "passersby": ["person"],
    "man": ["person"],
    "woman": ["person"],
    "background": ["person"],
    "chair": ["chair"],
    "couch": ["couch", "chair"],
    "sofa": ["couch"],
    "desk": ["dining table"],
    "table": ["dining table"],
    "plant": ["potted plant", "bowl", "vase"],
    "flower": ["potted plant", "vase"],
    "pot": ["potted plant"],
    "vase": ["vase", "potted plant"],
    "car": ["car"],
    "vehicle": ["car", "truck"],
    "truck": ["truck"],
    "bike": ["bicycle"],
    "bicycle": ["bicycle"],
    "motorcycle": ["motorcycle"],
    "bag": ["handbag", "backpack", "suitcase"],
    "backpack": ["backpack"],
    "handbag": ["handbag"],
    "suitcase": ["suitcase"],
    "book": ["book"],
    "boat": ["boat"],
    "dog": ["dog"],
    "cat": ["cat"],
    "bird": ["bird"]
}

def extract_target_classes(prompt_text):
    prompt_clean = prompt_text.lower().replace("'", " ").replace("-", " ")
    words = prompt_clean.split()
    target_classes = set()
    is_background_only = any(bg in prompt_clean for bg in [
        "touriste", "touristes", "passant", "passants", "arriere plan", "arrière plan", "fond",
        "tourist", "tourists", "people", "background", "passerby", "passersby"
    ])
    
    for word in words:
        if word in FRENCH_CLASS_MAP:
            target_classes.update(FRENCH_CLASS_MAP[word])
        if word in ENGLISH_CLASS_MAP:
            target_classes.update(ENGLISH_CLASS_MAP[word])
        if word in COCO_CLASSES:
            target_classes.add(word)

    for phrase, classes in FRENCH_CLASS_MAP.items():
        if " " in phrase and phrase in prompt_clean:
            target_classes.update(classes)
    for phrase, classes in ENGLISH_CLASS_MAP.items():
        if " " in phrase and phrase in prompt_clean:
            target_classes.update(classes)
            
    return list(target_classes), is_background_only

_MODEL_CACHE = {}

def get_or_compile_model(model_path, engine, requested_device):
    cache_key = (model_path, engine, (requested_device or "").upper())
    if cache_key in _MODEL_CACHE:
        model_type, compiled_obj, full_device_name, target_device, _ = _MODEL_CACHE[cache_key]
        return (model_type, compiled_obj, full_device_name, target_device, 0)

    t_compile_0 = time.perf_counter()
    if engine in ("onnx", "onnx_olive"):
        import onnxruntime as ort
        is_olive = engine == "onnx_olive"
        target_device = (requested_device or "GPU").upper()
        providers = ['DmlExecutionProvider', 'CPUExecutionProvider'] if target_device in ("GPU", "NPU", "AUTO") else ['CPUExecutionProvider']
        sess_opts = ort.SessionOptions()
        sess_opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        sess = ort.InferenceSession(model_path, sess_opts, providers=providers)
        compile_time_ms = int((time.perf_counter() - t_compile_0) * 1000)
        active_provider = sess.get_providers()[0]
        if "Dml" in active_provider:
            full_device_name = "DirectML (Intel Arc GPU / Olive FP16)" if is_olive else "DirectML (Intel Arc GPU / DirectX 12)"
        else:
            full_device_name = f"CPUExecutionProvider ({active_provider})"
        result = ("onnx", (sess, is_olive), full_device_name, target_device, compile_time_ms)
    else:
        core = ov.Core()
        avail = core.available_devices
        target_device = (requested_device or "NPU").upper()
        if target_device == "AUTO" or target_device not in avail:
            target_device = "NPU" if "NPU" in avail else ("GPU" if "GPU" in avail else "CPU")
        try:
            full_device_name = core.get_property(target_device, "FULL_DEVICE_NAME")
        except Exception:
            full_device_name = target_device
        model = core.read_model(model_path)
        compiled = core.compile_model(model, target_device)
        compile_time_ms = int((time.perf_counter() - t_compile_0) * 1000)
        result = ("openvino", compiled, full_device_name, target_device, compile_time_ms)

    _MODEL_CACHE[cache_key] = result
    return result

def detect_and_segment(image_path, prompt, click_x, click_y, out_preview, out_mask, model_path, engine="openvino", requested_device="NPU"):
    orig_img = cv2.imread(image_path)
    if orig_img is None:
        return {"type": "error", "message": f"Impossible d'ouvrir l'image : {image_path}"}
    h0, w0 = orig_img.shape[:2]

    # Preprocessing: Letterbox resize to 640x640 with border padding (114, 114, 114)
    r = min(640 / h0, 640 / w0)
    nw, nh = int(round(w0 * r)), int(round(h0 * r))
    dw, dh = (640 - nw) / 2, (640 - nh) / 2
    top, bottom = int(round(dh - 0.1)), int(round(dh + 0.1))
    left, right = int(round(dw - 0.1)), int(round(dw + 0.1))

    resized = cv2.resize(orig_img, (nw, nh), interpolation=cv2.INTER_LINEAR)
    letterbox = cv2.copyMakeBorder(resized, top, bottom, left, right, cv2.BORDER_CONSTANT, value=(114, 114, 114))

    blob = cv2.cvtColor(letterbox, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    blob = np.transpose(blob, (2, 0, 1))[np.newaxis, ...]

    model_type, compiled_obj, full_device_name, target_device, compile_time_ms = get_or_compile_model(model_path, engine, requested_device)

    if model_type == "onnx":
        sess, is_olive = compiled_obj
        inp_name = sess.get_inputs()[0].name
        inp_type = sess.get_inputs()[0].type
        blob_in = blob.astype(np.float16) if ("float16" in inp_type or is_olive) else blob

        t_infer_0 = time.perf_counter()
        res = sess.run(None, {inp_name: blob_in})
        infer_ms = int((time.perf_counter() - t_infer_0) * 1000)

        out0 = np.squeeze(res[0]).T.astype(np.float32)
        proto = np.squeeze(res[1]).astype(np.float32)
    else:
        compiled = compiled_obj
        t_infer_0 = time.perf_counter()
        req = compiled.create_infer_request()
        req.infer({0: blob})
        infer_ms = int((time.perf_counter() - t_infer_0) * 1000)

        out0 = np.squeeze(req.get_output_tensor(0).data).T  # shape (8400, 116)
        proto = np.squeeze(req.get_output_tensor(1).data)   # shape (32, 160, 160)

    boxes_raw = out0[:, :4]
    scores_raw = out0[:, 4:84]
    coeffs_raw = out0[:, 84:]

    confidences = np.max(scores_raw, axis=1)
    class_ids = np.argmax(scores_raw, axis=1)

    # Filter candidates with threshold >= 0.22
    valid_mask = confidences >= 0.22
    if not np.any(valid_mask):
        return {
            "type": "detect_result",
            "found": False,
            "message": "Aucun objet identifiable n'a été détecté dans cette image.",
            "device": target_device,
            "device_name": full_device_name,
            "infer_time_ms": infer_ms,
            "detected_items": []
        }

    boxes_filt = boxes_raw[valid_mask]
    conf_filt = confidences[valid_mask]
    cls_filt = class_ids[valid_mask]
    coeffs_filt = coeffs_raw[valid_mask]

    # Convert to xywh format for NMS
    cv_boxes = []
    for b in boxes_filt:
        cx, cy, w, h = b
        cv_boxes.append([int(cx - w / 2), int(cy - h / 2), int(w), int(h)])

    indices = cv2.dnn.NMSBoxes(cv_boxes, conf_filt.tolist(), score_threshold=0.22, nms_threshold=0.45)
    if len(indices) == 0:
        return {
            "type": "detect_result",
            "found": False,
            "message": "Aucun objet retenu après filtrage spatial.",
            "device": target_device,
            "device_name": full_device_name,
            "infer_time_ms": infer_ms,
            "detected_items": []
        }

    # Extract detected objects with masks
    detected_objects = []
    for rank, idx in enumerate(indices):
        i = idx if isinstance(idx, (int, np.integer)) else idx[0]
        cid = int(cls_filt[i])
        cls_en = COCO_CLASSES[cid] if cid < len(COCO_CLASSES) else f"class_{cid}"
        cls_fr = CLASS_NAME_FR.get(cls_en, cls_en.capitalize())
        conf = float(conf_filt[i])

        # Bounding box in letterbox space
        bx, by, bw, bh = cv_boxes[i]

        # Convert bounding box to original image coordinates
        orig_bx = max(0, int((bx - left) / r))
        orig_by = max(0, int((by - top) / r))
        orig_bw = int(bw / r)
        orig_bh = int(bh / r)
        orig_x2 = min(w0, orig_bx + orig_bw)
        orig_y2 = min(h0, orig_by + orig_bh)
        area = (orig_x2 - orig_bx) * (orig_y2 - orig_by)

        # Compute instance segmentation mask
        coeff = coeffs_filt[i]
        mask_160 = np.sum(proto * coeff[:, None, None], axis=0)
        mask_160 = 1.0 / (1.0 + np.exp(-mask_160)) # Sigmoid activation

        # Resize to 640x640 letterbox
        mask_640 = cv2.resize(mask_160, (640, 640), interpolation=cv2.INTER_LINEAR)
        # Crop letterbox padding
        mask_cropped = mask_640[top:top + nh, left:left + nw]
        # Resize to original image size
        mask_orig = cv2.resize(mask_cropped, (w0, h0), interpolation=cv2.INTER_LINEAR)
        bin_mask = (mask_orig > 0.45).astype(np.uint8) * 255

        # Strictly restrict mask inside object bounding box with slight margin
        margin_x = max(4, int(orig_bw * 0.05))
        margin_y = max(4, int(orig_bh * 0.05))
        bbox_lim = np.zeros_like(bin_mask)
        bbox_lim[max(0, orig_by - margin_y):min(h0, orig_y2 + margin_y),
                 max(0, orig_bx - margin_x):min(w0, orig_x2 + margin_x)] = 255
        bin_mask = cv2.bitwise_and(bin_mask, bbox_lim)

        detected_objects.append({
            "index": rank,
            "class_en": cls_en,
            "class_fr": cls_fr,
            "confidence": round(conf, 2),
            "area": area,
            "bbox": [orig_bx, orig_by, orig_x2, orig_y2],
            "mask": bin_mask
        })

    # Selection Logic:
    target_indices = []
    target_name_fr = "Objet sélectionné"
    target_name_en = "Selected object"
    target_confidence = 0.90

    # 1. Click Selection
    if click_x is not None and click_y is not None:
        px = int(click_x * w0) if click_x <= 1.0 else int(click_x)
        py = int(click_y * h0) if click_y <= 1.0 else int(click_y)
        px = max(0, min(w0 - 1, px))
        py = max(0, min(h0 - 1, py))

        # Check mask hit
        hit_idx = None
        min_area = float("inf")
        for obj in detected_objects:
            if obj["mask"][py, px] > 0:
                if obj["area"] < min_area: # Prefer smaller/specific object
                    min_area = obj["area"]
                    hit_idx = obj["index"]

        # If not direct hit on mask, check bounding boxes
        if hit_idx is None:
            min_dist = float("inf")
            for obj in detected_objects:
                x1, y1, x2, y2 = obj["bbox"]
                if x1 <= px <= x2 and y1 <= py <= y2:
                    cx = (x1 + x2) / 2.0
                    cy = (y1 + y2) / 2.0
                    dist = (cx - px) ** 2 + (cy - py) ** 2
                    if dist < min_dist:
                        min_dist = dist
                        hit_idx = obj["index"]

        if hit_idx is not None:
            target_indices.append(hit_idx)
            sel = next(o for o in detected_objects if o["index"] == hit_idx)
            target_name_fr = sel["class_fr"]
            target_name_en = sel["class_en"].capitalize()
            target_confidence = sel["confidence"]

    # 2. Prompt Selection
    if not target_indices and prompt:
        target_classes, is_bg_only = extract_target_classes(prompt)
        matched = [o for o in detected_objects if o["class_en"] in target_classes]

        if matched:
            if "person" in target_classes and is_bg_only:
                person_items = [o for o in matched if o["class_en"] == "person"]
                if len(person_items) > 1:
                    # Sort by area descending: #0 is foreground subject, rest are background tourists
                    person_items.sort(key=lambda o: o["area"], reverse=True)
                    target_indices = [o["index"] for o in person_items[1:]]
                    target_name_fr = "Personnes en arrière-plan (Touristes)"
                    target_name_en = "Background people (Tourists)"
                    target_confidence = min(o["confidence"] for o in person_items[1:])
                else:
                    target_indices = [person_items[0]["index"]]
                    target_name_fr = person_items[0]["class_fr"]
                    target_name_en = person_items[0]["class_en"].capitalize()
                    target_confidence = person_items[0]["confidence"]
            else:
                # Select highest confidence match
                best = max(matched, key=lambda o: o["confidence"])
                target_indices = [best["index"]]
                target_name_fr = best["class_fr"]
                target_name_en = best["class_en"].capitalize()
                target_confidence = best["confidence"]
        else:
            # Fallback substring match
            for o in detected_objects:
                if o["class_fr"].lower() in prompt.lower() or o["class_en"].lower() in prompt.lower():
                    target_indices.append(o["index"])
                    target_name_fr = o["class_fr"]
                    target_name_en = o["class_en"].capitalize()
                    target_confidence = o["confidence"]
                    break

    # 3. Default prominent object fallback
    if not target_indices and detected_objects:
        # Avoid picking pure background tables if other objects exist
        salient = [o for o in detected_objects if o["class_en"] not in ["dining table"]]
        best = max(salient if salient else detected_objects, key=lambda o: o["confidence"])
        target_indices.append(best["index"])
        target_name_fr = best["class_fr"]
        target_name_en = best["class_en"].capitalize()
        target_confidence = best["confidence"]

    # Combine selected masks
    combined_mask = np.zeros((h0, w0), dtype=np.uint8)
    for idx in target_indices:
        sel_obj = next(o for o in detected_objects if o["index"] == idx)
        combined_mask = np.maximum(combined_mask, sel_obj["mask"])

    # Adaptive dilation: slightly expand mask to eliminate edge artifacts
    ksize = max(5, int(min(w0, h0) * 0.009))
    if ksize % 2 == 0:
        ksize += 1
    kernel = np.ones((ksize, ksize), np.uint8)
    dilated_mask = cv2.dilate(combined_mask, kernel, iterations=2)

    # Save binary mask for inpainting
    os.makedirs(os.path.dirname(os.path.abspath(out_mask)), exist_ok=True)
    cv2.imwrite(out_mask, dilated_mask)

    # Create glowing neon cyan preview overlay
    overlay = orig_img.copy()
    cyan_tint = np.zeros_like(orig_img)
    cyan_tint[dilated_mask > 0] = [255, 235, 0] # BGR Neon Cyan
    
    contours, _ = cv2.findContours(dilated_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    cv2.drawContours(overlay, contours, -1, (255, 245, 0), 3) # Crisp neon border
    preview_img = cv2.addWeighted(overlay, 0.72, cyan_tint, 0.28, 0)
    
    os.makedirs(os.path.dirname(os.path.abspath(out_preview)), exist_ok=True)
    cv2.imwrite(out_preview, preview_img)

    return {
        "type": "detect_result",
        "found": True,
        "target_name_fr": target_name_fr,
        "target_name_en": target_name_en,
        "confidence": target_confidence,
        "device": target_device,
        "device_name": full_device_name,
        "preview_path": os.path.abspath(out_preview),
        "mask_path": os.path.abspath(out_mask),
        "infer_time_ms": infer_ms,
        "compile_time_ms": compile_time_ms,
        "detected_items": [
            {
                "name_fr": o["class_fr"],
                "name_en": o["class_en"],
                "conf": o["confidence"],
                "area": o["area"]
            }
            for o in detected_objects
        ]
    }

def inpaint_erase(image_path, mask_path, output_path, method="telea", requested_device="NPU"):
    orig_img = cv2.imread(image_path)
    mask = cv2.imread(mask_path, cv2.IMREAD_GRAYSCALE)
    if orig_img is None or mask is None:
        return {"type": "error", "message": "Fichier image ou masque introuvable."}

    t0 = time.perf_counter()
    flag = cv2.INPAINT_TELEA if method == "telea" else cv2.INPAINT_NS
    
    radius = max(5, int(min(orig_img.shape[:2]) * 0.007))
    inpainted = cv2.inpaint(orig_img, mask, inpaintRadius=radius, flags=flag)
    infer_ms = int((time.perf_counter() - t0) * 1000)

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    cv2.imwrite(output_path, inpainted)

    return {
        "type": "erase_result",
        "success": True,
        "output_path": os.path.abspath(output_path),
        "infer_time_ms": infer_ms,
        "device": requested_device
    }

def main():
    parser = argparse.ArgumentParser(description="Copilot+ Magic Eraser & Object Inpainting Service (OpenVINO)")
    parser.add_argument("--action", required=True, choices=["detect", "erase", "daemon"], help="Action to perform")
    parser.add_argument("--device", default="NPU", choices=["NPU", "GPU", "CPU", "AUTO"], help="Target Silicon Device")
    parser.add_argument("--image", default="", help="Path to input image")
    parser.add_argument("--prompt", default="", help="Object description in French or English")
    parser.add_argument("--click_x", type=float, default=None, help="Relative X coordinate (0.0 to 1.0)")
    parser.add_argument("--click_y", type=float, default=None, help="Relative Y coordinate (0.0 to 1.0)")
    parser.add_argument("--out_preview", default=None, help="Output path for preview with cyan mask")
    parser.add_argument("--out_mask", default=None, help="Output path for binary mask")
    parser.add_argument("--mask", default=None, help="Input binary mask path for erase action")
    parser.add_argument("--output", default=None, help="Output path for erased/inpainted image")
    parser.add_argument("--method", default="telea", choices=["telea", "ns"], help="Inpainting algorithm (telea, ns)")
    parser.add_argument("--engine", default="openvino", choices=["openvino", "onnx", "onnx_olive"], help="Inference engine (openvino, onnx, onnx_olive)")
    
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    script_dir = os.path.dirname(os.path.abspath(__file__))

    if args.engine == "onnx":
        model_path = find_onnx_model(script_dir)
    elif args.engine == "onnx_olive":
        model_path = find_olive_model(script_dir)
    else:
        model_path = find_model_xml(script_dir)

    if not model_path or not os.path.exists(model_path):
        print(json.dumps({"type": "error", "message": f"Modèle ({args.engine}) introuvable : {model_path}"}, ensure_ascii=False))
        sys.exit(1)

    if args.action == "daemon":
        try:
            get_or_compile_model(model_path, args.engine, args.device)
        except Exception:
            pass
        print(json.dumps({"type": "ready", "engine": args.engine, "device": args.device}, ensure_ascii=False), flush=True)
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            if line in ("quit", "exit"):
                break
            try:
                req = json.loads(line)
                req_act = req.get("action", "detect")
                if req_act == "detect":
                    img_p = req.get("image", "")
                    prompt_p = req.get("prompt", "")
                    cx = req.get("click_x", None)
                    cy = req.get("click_y", None)
                    out_p = req.get("out_preview", os.path.join(script_dir, "Data", "Images", "outputs", "preview.jpg"))
                    out_m = req.get("out_mask", os.path.join(script_dir, "Data", "Images", "outputs", "mask.png"))
                    req_dev = req.get("device", args.device)
                    req_eng = req.get("engine", args.engine)
                    res = detect_and_segment(
                        image_path=img_p,
                        prompt=prompt_p,
                        click_x=cx,
                        click_y=cy,
                        out_preview=out_p,
                        out_mask=out_m,
                        model_path=model_path,
                        engine=req_eng,
                        requested_device=req_dev
                    )
                    print(json.dumps(res, ensure_ascii=False), flush=True)
                elif req_act == "erase":
                    img_p = req.get("image", "")
                    mask_p = req.get("mask", "")
                    out_p = req.get("output", "")
                    meth = req.get("method", "telea")
                    req_dev = req.get("device", args.device)
                    res = inpaint_erase(img_p, mask_p, out_p, method=meth, requested_device=req_dev)
                    print(json.dumps(res, ensure_ascii=False), flush=True)
            except Exception as ex:
                print(json.dumps({"type": "error", "message": str(ex)}, ensure_ascii=False), flush=True)

    elif args.action == "detect":
        out_prev = args.out_preview or os.path.join(script_dir, "Data", "Images", "outputs", "preview.jpg")
        out_m = args.out_mask or os.path.join(script_dir, "Data", "Images", "outputs", "mask.png")
        res = detect_and_segment(
            image_path=args.image,
            prompt=args.prompt,
            click_x=args.click_x,
            click_y=args.click_y,
            out_preview=out_prev,
            out_mask=out_m,
            model_path=model_path,
            engine=args.engine,
            requested_device=args.device
        )
        print(json.dumps(res, ensure_ascii=False), flush=True)

    elif args.action == "erase":
        if not args.mask or not args.output:
            print(json.dumps({"type": "error", "message": "--mask et --output requis pour 'erase'"}, ensure_ascii=False))
            sys.exit(1)
        method = getattr(args, "method", "telea")
        res = inpaint_erase(args.image, args.mask, args.output, method=method, requested_device=args.device)
        print(json.dumps(res, ensure_ascii=False), flush=True)

if __name__ == "__main__":
    main()
