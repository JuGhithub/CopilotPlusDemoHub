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

# Expanded vocabulary matching the YOLOv8s-World Zero-Trust + Anti-Spoofing model
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

SPOOF_LABELS = {
    "cell phone", "smartphone", "mobile phone", "phone screen",
    "tablet", "ipad", "printed photo", "picture frame", "photograph"
}

PERSON_LABELS = {
    "person", "human face"
}


def find_model_path(base_dir):
    cur = base_dir
    for _ in range(6):
        cand = os.path.abspath(os.path.join(cur, "models", "yolov8s-world_openvino_model"))
        if os.path.isdir(cand) and (
            os.path.exists(os.path.join(cand, "yolov8s-world.xml"))
            or os.path.exists(os.path.join(cand, "yolov8s-worldv2.xml"))
        ):
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
    req = (req_device or "NPU").upper()
    try:
        import openvino as ov
        core = ov.Core()
        avail = [d.upper() for d in core.available_devices]
        if "NPU" in req and "NPU" in avail:
            return "intel:npu"
    except Exception:
        pass
    # Note: YOLOv8s-Worldv2 (WorldDetect cross-attention head) in OpenVINO 2026+ can trigger
    # an Intel Arc OpenCL driver fault (TDR/BSOD) when compiled on 'intel:gpu'.
    # OpenVINO LATENCY mode on NPU (~17 ms) or CPU (~10 ms) executes YOLOv8s-World FP16 with 100% stability.
    return "cpu"


def draw_hud_box(img, box, label, color, conf=None, text_color=(10, 15, 25)):
    x1, y1, x2, y2 = [int(v) for v in box]
    h, w, _ = img.shape
    x1, y1 = max(0, x1), max(0, y1)
    x2, y2 = min(w, x2), min(h, y2)

    # Semi-transparent fill
    overlay = img.copy()
    cv2.rectangle(overlay, (x1, y1), (x2, y2), color, -1)
    cv2.addWeighted(overlay, 0.14, img, 0.86, 0, img)

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
    text = f"{label} ({int(conf * 100)}%)" if conf is not None else label
    (tw, th), _ = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, 0.50, 2)
    badge_y1 = max(0, y1 - th - 8)
    badge_y2 = badge_y1 + th + 8
    cv2.rectangle(img, (x1, badge_y1), (min(w, x1 + tw + 10), badge_y2), color, -1)
    cv2.putText(img, text, (x1 + 5, badge_y2 - 5), cv2.FONT_HERSHEY_SIMPLEX, 0.50, text_color, 2, cv2.LINE_AA)


def smooth_box(old_box, new_box, alpha=0.65):
    if old_box is None:
        return new_box
    return [alpha * n + (1.0 - alpha) * o for o, n in zip(old_box, new_box)]


def box_area(b):
    return max(0.0, b[2] - b[0]) * max(0.0, b[3] - b[1])


def box_intersection(a, b):
    ix1 = max(a[0], b[0])
    iy1 = max(a[1], b[1])
    ix2 = min(a[2], b[2])
    iy2 = min(a[3], b[3])
    if ix2 <= ix1 or iy2 <= iy1:
        return 0.0
    return (ix2 - ix1) * (iy2 - iy1)


def is_box_spoofed_by_screen(obj_box, spoof_boxes):
    """
    Returns (is_spoofed, matched_spoof_box, matched_spoof_conf, matched_spoof_name)
    Checks if an equipment bounding box is displayed inside or on a smartphone, tablet, or photo frame.
    """
    if obj_box is None or not spoof_boxes:
        return False, None, 0.0, ""

    oa = box_area(obj_box)
    if oa <= 1.0:
        return False, None, 0.0, ""

    ocx = 0.5 * (obj_box[0] + obj_box[2])
    ocy = 0.5 * (obj_box[1] + obj_box[3])

    for s_box, s_conf, s_name in spoof_boxes:
        sa = box_area(s_box)
        if sa <= 1.0:
            continue
        inter = box_intersection(obj_box, s_box)
        ioa = inter / oa          # Fraction of object inside the screen/phone box
        ios = inter / sa          # Fraction of screen/phone box covered by the object
        center_inside = (s_box[0] <= ocx <= s_box[2]) and (s_box[1] <= ocy <= s_box[3])

        # Picture frames hanging high on background walls shouldn't false-trigger unless object is inside them
        if s_name == "picture frame" and ioa < 0.55:
            continue

        if ioa >= 0.28 or ios >= 0.38 or (center_inside and ioa >= 0.15):
            return True, s_box, s_conf, s_name

    return False, None, 0.0, ""


def detect_optical_screen_bezel(frame, obj_box, person_box=None):
    """
    Optical fallback anti-spoofing (< 2 ms):
    Detects if an object is surrounded by a sharp glowing rectangular smartphone bezel
    held in the hand (e.g. when zoomed in so YOLO only detects the object on the screen).
    """
    if obj_box is None:
        return False, None

    h, w, _ = frame.shape
    x1, y1, x2, y2 = [int(v) for v in obj_box]
    bw, bh = x2 - x1, y2 - y1
    if bw < 35 or bh < 35:
        return False, None

    # Expand ROI around the object to look for a surrounding rectangular phone bezel
    pad_x = int(bw * 0.45)
    pad_y = int(bh * 0.55)
    rx1 = max(0, x1 - pad_x)
    ry1 = max(0, y1 - pad_y)
    rx2 = min(w, x2 + pad_x)
    ry2 = min(h, y2 + pad_y)

    roi = frame[ry1:ry2, rx1:rx2]
    if roi.size == 0 or roi.shape[0] < 50 or roi.shape[1] < 50:
        return False, None

    gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
    blur = cv2.GaussianBlur(gray, (5, 5), 0)
    edges = cv2.Canny(blur, 60, 160)
    kernel = np.ones((3, 3), np.uint8)
    edges = cv2.dilate(edges, kernel, iterations=1)

    contours, _ = cv2.findContours(edges, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
    roi_area = float(roi.shape[0] * roi.shape[1])
    obj_rel_box = [x1 - rx1, y1 - ry1, x2 - rx1, y2 - ry1]
    obj_area = float(bw * bh)

    for cnt in contours:
        area = cv2.contourArea(cnt)
        if area < obj_area * 1.15 or area > roi_area * 0.92:
            continue
        peri = cv2.arcLength(cnt, True)
        approx = cv2.approxPolyDP(cnt, 0.03 * peri, True)
        if len(approx) == 4 and cv2.isContourConvex(approx):
            cx, cy, cw, ch = cv2.boundingRect(approx)
            # Check rectangularity (how closely the 4-gon matches its axis-aligned bounding box)
            rect_fill = area / max(1.0, float(cw * ch))
            aspect = float(cw) / max(1.0, float(ch))
            if rect_fill >= 0.86 and (0.42 <= aspect <= 2.35):
                cand_box = [cx, cy, cx + cw, cy + ch]
                inter = box_intersection(obj_rel_box, cand_box)
                if inter / obj_area >= 0.85:
                    # Also verify dark bezel border vs bright screen interior
                    inner_mask = np.zeros_like(gray)
                    cv2.drawContours(inner_mask, [approx], -1, 255, -1)
                    border_mask = np.zeros_like(gray)
                    cv2.drawContours(border_mask, [approx], -1, 255, 6)
                    inner_mean = cv2.mean(gray, mask=inner_mask)[0]
                    border_mean = cv2.mean(gray, mask=border_mask)[0]
                    # A glowing smartphone screen has a significantly brighter interior than its black bezel
                    if inner_mean - border_mean >= 38.0:
                        abs_box = [rx1 + cx, ry1 + cy, rx1 + cx + cw, ry1 + cy + ch]
                        # Ensure it doesn't just match a background monitor behind the user's head
                        if person_box is not None:
                            py1, py2 = person_box[1], person_box[3]
                            head_bottom = py1 + 0.42 * (py2 - py1)
                            if abs_box[1] >= head_bottom:
                                return True, abs_box
    return False, None


def analyze_and_draw(model, frame, ov_device, conf_thresh=0.20, is_live=False, tracker_state=None, hold_sec=4.5):
    # Run inference at slightly lower raw threshold so we can catch spoof screens/phones down to 0.14
    raw_conf = min(0.14, conf_thresh)
    results = model(frame, conf=raw_conf, device=ov_device, verbose=False)
    now = time.perf_counter()

    h, w, _ = frame.shape
    min_obj_dim = max(28, int(min(h, w) * 0.075))

    person_boxes = []
    spoof_boxes = []
    cup_candidates = []
    glasses_candidates = []
    headset_candidates = []

    for r in results:
        for box in r.boxes:
            cls_id = int(box.cls[0].item())
            conf = float(box.conf[0].item())
            name = model.names.get(cls_id, "").lower()
            xyxy = box.xyxy[0].tolist()

            w_box = xyxy[2] - xyxy[0]
            h_box = xyxy[3] - xyxy[1]

            if name in PERSON_LABELS and conf >= 0.35:
                if w_box >= min_obj_dim and h_box >= min_obj_dim:
                    person_boxes.append((xyxy, conf, name))
                continue

            if name in SPOOF_LABELS and conf >= 0.14:
                if w_box >= min_obj_dim and h_box >= min_obj_dim:
                    spoof_boxes.append((xyxy, conf, name))
                continue

            # Equipment candidates must meet conf_thresh and minimum relative size
            if conf < conf_thresh:
                continue

            if name in CUP_LABELS:
                if w_box >= min_obj_dim and h_box >= min_obj_dim:
                    cup_candidates.append((xyxy, conf, name))
            elif name in GLASSES_LABELS:
                # Glasses are naturally wide and thin (e.g. 120x33 px)
                if w_box >= max(35, int(w * 0.08)) and h_box >= 16:
                    glasses_candidates.append((xyxy, conf, name))
            elif name in HEADSET_LABELS:
                if w_box >= min_obj_dim and h_box >= min_obj_dim:
                    headset_candidates.append((xyxy, conf, name))

    # Identify primary foreground person (largest person box covering >= 10% of frame)
    primary_person = None
    if person_boxes:
        person_boxes.sort(key=lambda item: box_area(item[0]), reverse=True)
        largest_p = person_boxes[0][0]
        if box_area(largest_p) >= 0.10 * (w * h):
            primary_person = largest_p

    # Evaluate candidates with Anti-Spoofing & Biometric Head Coherence
    active_spoof_alerts = []  # List of (spoof_box, spoof_conf, spoof_label_fr)
    spoofed_items_fr = []

    def evaluate_candidates(candidates, item_key, item_label_fr, require_person_overlap=False):
        best_valid = (False, 0.0, None, "")
        item_spoofed = False

        # Sort by confidence descending
        candidates.sort(key=lambda x: x[1], reverse=True)

        for xyxy, conf, name in candidates:
            # 1. Check YOLO-World screen/phone/photo containment
            is_spf, s_box, s_conf, s_name = is_box_spoofed_by_screen(xyxy, spoof_boxes)
            if not is_spf:
                # 2. Check optical screen bezel fallback
                is_opt, opt_box = detect_optical_screen_bezel(frame, xyxy, primary_person)
                if is_opt:
                    is_spf, s_box, s_conf, s_name = True, opt_box, conf, "smartphone screen"

            # 3. For wearable head items (glasses/headset): if held low in the hands near a phone, flag as spoof;
            #    if sitting on a distant background desk outside the foreground person, ignore background clutter.
            if not is_spf and require_person_overlap and primary_person is not None:
                inter_p = box_intersection(xyxy, primary_person)
                ioa_p = inter_p / max(1.0, box_area(xyxy))
                if ioa_p < 0.20:
                    # Distant background object (not worn by the foreground user and not on a phone) -> ignore
                    continue
                # Check if glasses/headset is suspiciously low on the torso (held in hands) while a phone is present
                ocy = 0.5 * (xyxy[1] + xyxy[3])
                p_top, p_bot = primary_person[1], primary_person[3]
                rel_y = (ocy - p_top) / max(1.0, p_bot - p_top)
                if rel_y > 0.52 and spoof_boxes:
                    is_spf = True
                    s_box, s_conf, s_name = spoof_boxes[0]

            if is_spf:
                item_spoofed = True
                if s_box is not None:
                    active_spoof_alerts.append((s_box, max(s_conf, conf), item_label_fr, xyxy))
                if item_label_fr not in spoofed_items_fr:
                    spoofed_items_fr.append(item_label_fr)
                continue

            if not best_valid[0]:
                best_valid = (True, conf, xyxy, name)

        return best_valid[0], best_valid[1], best_valid[2], best_valid[3], item_spoofed

    cup_detected, cup_conf, cup_box, cup_name, cup_spoofed = evaluate_candidates(
        cup_candidates, "cup", "Tasse de café", require_person_overlap=False
    )
    glasses_detected, glasses_conf, glasses_box, glasses_name, glasses_spoofed = evaluate_candidates(
        glasses_candidates, "glasses", "Lunettes", require_person_overlap=True
    )
    headset_detected, headset_conf, headset_box, headset_name, headset_spoofed = evaluate_candidates(
        headset_candidates, "headset", "Casque audio", require_person_overlap=True
    )

    spoof_detected = bool(cup_spoofed or glasses_spoofed or headset_spoofed)

    # Persistance temporelle en mode webcam direct : chaque objet réel reste verrouillé quelques secondes,
    # MAIS toute tentative de triche (spoofing) purge immédiatement l'objet concerné !
    if is_live and tracker_state is not None:
        if spoof_detected:
            tracker_state["spoof_ts"] = now
            tracker_state["spoof_items"] = list(spoofed_items_fr)
            tracker_state["spoof_alerts"] = list(active_spoof_alerts)
        elif "spoof_ts" in tracker_state and (now - tracker_state["spoof_ts"]) <= 2.5:
            spoof_detected = True
            spoofed_items_fr = tracker_state.get("spoof_items", [])
            active_spoof_alerts = tracker_state.get("spoof_alerts", [])

        items = [
            ("cup", cup_detected, cup_conf, cup_box, cup_name, cup_spoofed),
            ("glasses", glasses_detected, glasses_conf, glasses_box, glasses_name, glasses_spoofed),
            ("headset", headset_detected, headset_conf, headset_box, headset_name, headset_spoofed),
        ]
        resolved = {}
        for key, det, conf, box, name, is_spf in items:
            if is_spf:
                # Immediately purge latched memory if the user tries to spoof this item
                tracker_state.pop(key, None)
                resolved[key] = (False, 0.0, None, "", False)
                continue

            mem = tracker_state.get(key)
            if det and box is not None:
                prev_box = mem["box"] if (mem and now - mem["ts"] <= 1.0) else None
                smoothed = smooth_box(prev_box, box, alpha=0.65)
                tracker_state[key] = {
                    "ts": now,
                    "conf": max(conf, mem["conf"] * 0.95) if (mem and now - mem["ts"] <= 1.0) else conf,
                    "box": smoothed,
                    "name": name,
                }
                resolved[key] = (True, tracker_state[key]["conf"], smoothed, name, False)
            elif mem is not None and (now - mem["ts"]) <= hold_sec:
                resolved[key] = (True, mem["conf"], mem["box"], mem["name"], True)
            else:
                resolved[key] = (False, 0.0, None, "", False)

        cup_detected, cup_conf, cup_box, cup_name, _ = resolved["cup"]
        glasses_detected, glasses_conf, glasses_box, glasses_name, _ = resolved["glasses"]
        headset_detected, headset_conf, headset_box, headset_name, _ = resolved["headset"]

    annotated = frame.copy()

    # Draw genuine boxes
    if cup_detected and cup_box is not None:
        label_txt = "TASSE DE CAFE" if "cup" in cup_name or "mug" in cup_name else f"BOISSON ({cup_name.upper()})"
        draw_hud_box(annotated, cup_box, label_txt, (0, 195, 255), cup_conf)

    if glasses_detected and glasses_box is not None:
        draw_hud_box(annotated, glasses_box, "LUNETTES", (255, 229, 0), glasses_conf)

    if headset_detected and headset_box is not None:
        draw_hud_box(annotated, headset_box, "CASQUE AUDIO", (255, 60, 200), headset_conf)

    # Draw Anti-Spoofing Alert Boxes (Crimson Red)
    drawn_spoof_screens = []
    for s_box, s_conf, item_fr, item_box in active_spoof_alerts:
        hud_item = item_fr.upper().replace("É", "E").replace("È", "E")
        # Draw inner spoofed object box in orange/red
        draw_hud_box(
            annotated,
            item_box,
            f"TRICHE : {hud_item} (2D)",
            (0, 70, 255),
            conf=None,
            text_color=(255, 255, 255),
        )
        # Draw outer smartphone/screen box once
        if not any(box_intersection(s_box, prev) / max(1.0, box_area(s_box)) > 0.6 for prev in drawn_spoof_screens):
            drawn_spoof_screens.append(s_box)
            draw_hud_box(
                annotated,
                s_box,
                "SPOOFING : ECRAN / PHOTO INTERDIT",
                (40, 20, 240),
                conf=s_conf,
                text_color=(255, 255, 255),
            )

    # Access is strictly denied if any spoofing is active
    access_granted = bool(cup_detected and glasses_detected and headset_detected and not spoof_detected)
    spoof_reason = (
        f"Présentation sur écran/photo détectée ({', '.join(spoofed_items_fr)})"
        if spoof_detected and spoofed_items_fr
        else ("Présentation d'écran/photo détectée" if spoof_detected else "")
    )

    # Top Status Banner on the image
    banner_h = 40
    cv2.rectangle(annotated, (0, 0), (w, banner_h), (12, 18, 30), -1)

    prefix = "LIVE SCAN" if is_live else "SCAN"
    if spoof_detected:
        cv2.rectangle(annotated, (0, 0), (w, banner_h), (20, 12, 65), -1)
        cv2.line(annotated, (0, banner_h), (w, banner_h), (40, 40, 255), 3)
        hud_items = ", ".join(s.upper().replace("É", "E").replace("È", "E") for s in spoofed_items_fr)
        spoof_banner = f"[ALERTE ANTI-SPOOFING] TRICHE DETECTEE ({hud_items} SUR ECRAN)"
        cv2.putText(annotated, spoof_banner, (12, 26),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.48, (100, 120, 255), 2, cv2.LINE_AA)
    elif access_granted:
        cv2.line(annotated, (0, banner_h), (w, banner_h), (50, 215, 75), 2)
        cv2.putText(annotated, f"[{prefix} : ACCES AUTORISE] 3/3 PREREQUIS REELS CONFORMES", (12, 26),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.52, (50, 215, 75), 2, cv2.LINE_AA)
    else:
        missing = []
        if not cup_detected:
            missing.append("TASSE")
        if not glasses_detected:
            missing.append("LUNETTES")
        if not headset_detected:
            missing.append("CASQUE")
        msg = f"[{prefix} : EN ATTENTE] MANQUE : {', '.join(missing)}"
        cv2.line(annotated, (0, banner_h), (w, banner_h), (50, 50, 240), 2)
        cv2.putText(annotated, msg, (12, 26),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.50, (80, 100, 255), 2, cv2.LINE_AA)

    return (
        annotated,
        cup_detected, cup_conf, cup_box, cup_name, cup_spoofed,
        glasses_detected, glasses_conf, glasses_box, glasses_name, glasses_spoofed,
        headset_detected, headset_conf, headset_box, headset_name, headset_spoofed,
        spoof_detected, spoof_reason, access_granted
    )


def main():
    sys.stdout.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser(description="Security Access Vision Service: Coffee, Glasses, Headset & Anti-Spoofing")
    parser.add_argument("--image", default="", help="Path to input image file")
    parser.add_argument("--webcam", action="store_true", help="Capture a single live frame from webcam")
    parser.add_argument("--stream", action="store_true", help="Run real-time webcam video stream with live inference")
    parser.add_argument("--out", default="", help="Path to output annotated image")
    parser.add_argument("--conf", type=float, default=0.20, help="Confidence threshold")
    parser.add_argument("--device", default="GPU", choices=["NPU", "GPU", "CPU"], help="Compute device")
    parser.add_argument("--engine", default="openvino", choices=["openvino", "onnx", "onnx_olive"], help="Inference engine backend (openvino, onnx, onnx_olive)")
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))

    selected_engine = args.engine
    if selected_engine == "openvino":
        try:
            import openvino  # noqa: F401
        except ImportError:
            # Automatic fallback to ONNX Runtime (DirectML/CPU) when OpenVINO is not installed (e.g. Windows ARM64 / Snapdragon)
            selected_engine = "onnx_olive" if os.path.exists(find_olive_model_path(script_dir)) else "onnx"

    if selected_engine == "openvino":
        model_dir = find_model_path(script_dir)
        if os.path.exists(model_dir):
            inference_device = resolve_ov_device(args.device)
            engine_name = "OpenVINO"
            try:
                model = YOLO(model_dir)
                dummy = np.zeros((640, 640, 3), dtype=np.uint8)
                _ = model(dummy, device=inference_device, verbose=False)
            except Exception:
                selected_engine = "onnx_olive" if os.path.exists(find_olive_model_path(script_dir)) else "onnx"
        else:
            selected_engine = "onnx_olive" if os.path.exists(find_olive_model_path(script_dir)) else "onnx"

    if selected_engine in ("onnx", "onnx_olive"):
        is_olive = selected_engine == "onnx_olive"
        model_path = find_olive_model_path(script_dir) if is_olive else find_onnx_model_path(script_dir)
        if not os.path.exists(model_path):
            model_path = find_onnx_model_path(script_dir)
            is_olive = False
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
            if ("GPU" in args.device.upper() or "NPU" in args.device.upper()) and 'DmlExecutionProvider' in ort.get_available_providers():
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

        stop_event = threading.Event()
        def stdin_listener():
            try:
                for line in sys.stdin:
                    if "stop" in line.lower() or "quit" in line.lower() or "exit" in line.lower():
                        stop_event.set()
                        break
            except Exception:
                stop_event.set()

        t_stdin = threading.Thread(target=stdin_listener, daemon=True)
        t_stdin.start()

        for _ in range(3):
            cap.read()

        t_start = time.perf_counter()
        frame_idx = 0
        tracker_state = {}

        while not stop_event.is_set():
            ret, frame = cap.read()
            if not ret or frame is None:
                time.sleep(0.02)
                continue

            t0 = time.perf_counter()
            (
                annotated,
                cup_det, cup_conf, cup_box, cup_name, cup_spf,
                glasses_det, glasses_conf, glasses_box, glasses_name, glasses_spf,
                headset_det, headset_conf, headset_box, headset_name, headset_spf,
                spoof_det, spoof_reason, granted
            ) = analyze_and_draw(
                model, frame, inference_device, conf_thresh=args.conf, is_live=True, tracker_state=tracker_state, hold_sec=4.5
            )
            infer_ms = int((time.perf_counter() - t0) * 1000)

            frame_idx += 1
            elapsed = time.perf_counter() - t_start
            fps = round(frame_idx / max(0.001, elapsed), 1)

            _, buf = cv2.imencode('.jpg', annotated, [cv2.IMWRITE_JPEG_QUALITY, 72])
            b64_str = base64.b64encode(buf).decode('ascii')

            payload = {
                "type": "live_frame",
                "cup_detected": cup_det,
                "cup_conf": round(cup_conf, 2),
                "cup_label": cup_name,
                "cup_box": [round(v, 1) for v in cup_box] if cup_box else None,
                "cup_spoofed": cup_spf,
                "glasses_detected": glasses_det,
                "glasses_conf": round(glasses_conf, 2),
                "glasses_label": glasses_name,
                "glasses_box": [round(v, 1) for v in glasses_box] if glasses_box else None,
                "glasses_spoofed": glasses_spf,
                "headset_detected": headset_det,
                "headset_conf": round(headset_conf, 2),
                "headset_label": headset_name,
                "headset_box": [round(v, 1) for v in headset_box] if headset_box else None,
                "headset_spoofed": headset_spf,
                "spoof_detected": spoof_det,
                "spoof_reason": spoof_reason,
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
    (
        annotated,
        cup_det, cup_conf, cup_box, cup_name, cup_spf,
        glasses_det, glasses_conf, glasses_box, glasses_name, glasses_spf,
        headset_det, headset_conf, headset_box, headset_name, headset_spf,
        spoof_det, spoof_reason, granted
    ) = analyze_and_draw(
        model, frame, inference_device, conf_thresh=args.conf, is_live=False
    )
    infer_ms = int((time.perf_counter() - t0) * 1000)

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
        "cup_spoofed": cup_spf,
        "glasses_detected": glasses_det,
        "glasses_conf": round(glasses_conf, 2),
        "glasses_label": glasses_name,
        "glasses_box": [round(v, 1) for v in glasses_box] if glasses_box else None,
        "glasses_spoofed": glasses_spf,
        "headset_detected": headset_det,
        "headset_conf": round(headset_conf, 2),
        "headset_label": headset_name,
        "headset_box": [round(v, 1) for v in headset_box] if headset_box else None,
        "headset_spoofed": headset_spf,
        "spoof_detected": spoof_det,
        "spoof_reason": spoof_reason,
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
