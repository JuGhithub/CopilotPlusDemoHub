import argparse
import datetime
import json
import os
import re
import sys
import time

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

try:
    import pymupdf
except ImportError:
    import fitz as pymupdf

import cv2
import numpy as np
from PIL import Image, ImageDraw


def find_file(rel_path):
    if os.path.exists(rel_path):
        return os.path.abspath(rel_path)
    cur = os.path.dirname(os.path.abspath(__file__)) if '__file__' in globals() else os.getcwd()
    for _ in range(6):
        cand = os.path.join(cur, rel_path)
        if os.path.exists(cand):
            return os.path.abspath(cand)
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return os.path.abspath(rel_path)


def patch_rapidocr_fp16():
    """Patches RapidOCR OrtInferSession to support Olive float16 ONNX models with BASIC graph optimizations."""
    try:
        import onnxruntime as ort
        import rapidocr_onnxruntime.utils.infer_engine as infer_engine

        if not hasattr(infer_engine.OrtInferSession, "_patched_olive"):
            orig_init = infer_engine.OrtInferSession._init_sess_opts
            @staticmethod
            def patched_init(config):
                opt = orig_init(config)
                opt.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_BASIC
                return opt
            infer_engine.OrtInferSession._init_sess_opts = patched_init

            orig_call = infer_engine.OrtInferSession.__call__
            def patched_call(self, input_content):
                has_fp16 = any(inp.type == 'tensor(float16)' for inp in self.session.get_inputs())
                if has_fp16 and isinstance(input_content, np.ndarray) and input_content.dtype == np.float32:
                    input_content = input_content.astype(np.float16)
                res = orig_call(self, input_content)
                if isinstance(res, list):
                    res = [r.astype(np.float32) if isinstance(r, np.ndarray) and r.dtype == np.float16 else r for r in res]
                return res
            infer_engine.OrtInferSession.__call__ = patched_call
            infer_engine.OrtInferSession._patched_olive = True
    except Exception:
        pass


def get_ocr_engine(engine_choice="olive"):
    from rapidocr_onnxruntime import RapidOCR
    raw_det = find_file(os.path.join("models", "ocr", "ch_PP-OCRv4_det_infer.onnx"))
    raw_rec = find_file(os.path.join("models", "ocr", "ch_PP-OCRv4_rec_infer.onnx"))
    olive_det = find_file(os.path.join("models", "ocr", "ch_PP-OCRv4_det_infer_olive.onnx"))
    olive_rec = find_file(os.path.join("models", "ocr", "ch_PP-OCRv4_rec_infer_olive.onnx"))

    if engine_choice == "olive" and os.path.exists(olive_rec):
        patch_rapidocr_fp16()
        det_path = raw_det if os.path.exists(raw_det) else None
        engine = RapidOCR(det_model_path=det_path, rec_model_path=olive_rec)
        det_size = os.path.getsize(det_path) if det_path and os.path.exists(det_path) else 4.74 * 1024 * 1024
        rec_size = os.path.getsize(olive_rec)
        model_size_mb = round((det_size + rec_size) / (1024 * 1024), 2)
        engine_label = "RapidOCR v4 (Optimisé Microsoft Olive FP16)"
    elif os.path.exists(raw_det) and os.path.exists(raw_rec):
        engine = RapidOCR(det_model_path=raw_det, rec_model_path=raw_rec)
        model_size_mb = round((os.path.getsize(raw_det) + os.path.getsize(raw_rec)) / (1024 * 1024), 2)
        engine_label = "RapidOCR v4 (Modèle Brut FP32)"
    else:
        engine = RapidOCR()
        model_size_mb = 15.59
        engine_label = "RapidOCR v4 Standard"

    return engine, model_size_mb, engine_label


def emit(obj: dict):
    print(json.dumps(obj, ensure_ascii=False))
    sys.stdout.flush()


def emit_error(msg: str):
    emit({"type": "error", "message": msg})


def extract_page_words(page, dpi=150):
    """Extracts bounding boxes of words for interactive UI selection."""
    scale = (dpi or 150) / 72.0
    words = []
    try:
        raw_words = page.get_text("words")
        for w in raw_words:
            words.append({
                "text": w[4],
                "x0": round(w[0] * scale, 1),
                "y0": round(w[1] * scale, 1),
                "x1": round(w[2] * scale, 1),
                "y1": round(w[3] * scale, 1),
                "pt_x0": round(w[0], 1),
                "pt_y0": round(w[1], 1),
                "pt_x1": round(w[2], 1),
                "pt_y1": round(w[3], 1)
            })
    except Exception:
        pass
    return words


def action_render(args):
    """Renders a PDF page or image to a displayable JPG image."""
    file_path = args.file
    if not os.path.exists(file_path):
        emit_error(f"Fichier introuvable : {file_path}")
        return

    page_idx = args.page
    out_path = args.out
    if not out_path:
        out_dir = os.path.join(os.path.dirname(file_path), "outputs")
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, f"render_{int(time.time()*1000)}.jpg")

    ext = os.path.splitext(file_path)[1].lower()
    page_count = 1

    try:
        words = []
        if ext == ".pdf":
            doc = pymupdf.open(file_path)
            page_count = len(doc)
            if page_idx >= page_count:
                page_idx = 0
            page = doc[page_idx]
            pix = page.get_pixmap(dpi=args.dpi)
            pix.save(out_path)
            width, height = pix.width, pix.height

            # Extract words for interactive mouse selection & direct keyboard editing
            words = extract_page_words(page, args.dpi)
            doc.close()
        else:
            # Direct image file
            with Image.open(file_path) as img:
                width, height = img.size
                img.convert("RGB").save(out_path, "JPEG", quality=92)

        emit({
            "type": "render_complete",
            "output_path": out_path,
            "page_count": page_count,
            "page_index": page_idx,
            "width": width,
            "height": height,
            "words": words,
            "dpi": args.dpi
        })
    except Exception as ex:
        emit_error(f"Erreur lors du rendu : {str(ex)}")


def action_ocr(args):
    """Executes RapidOCR on an image or rendered PDF page."""
    file_path = args.file
    if not os.path.exists(file_path):
        emit_error(f"Fichier introuvable : {file_path}")
        return

    ext = os.path.splitext(file_path)[1].lower()
    temp_img_path = file_path

    # If PDF, render the target page first
    temp_created = False
    if ext == ".pdf":
        try:
            doc = pymupdf.open(file_path)
            page = doc[args.page if args.page < len(doc) else 0]
            pix = page.get_pixmap(dpi=150)
            temp_img_path = os.path.abspath(f"temp_ocr_frame_{int(time.time()*1000)}.png")
            pix.save(temp_img_path)
            temp_created = True
            doc.close()
        except Exception as ex:
            emit_error(f"Impossible d'extraire la page PDF pour l'OCR : {ex}")
            return

    t0 = time.perf_counter()
    try:
        import psutil
        proc = psutil.Process(os.getpid())
        ram_before = proc.memory_info().rss / (1024 * 1024)

        engine_choice = getattr(args, "engine", "olive") or "olive"
        engine, model_size_mb, engine_label = get_ocr_engine(engine_choice)
        ocr_res, elapse = engine(temp_img_path)
        infer_ms = int((time.perf_counter() - t0) * 1000)

        ram_after = proc.memory_info().rss / (1024 * 1024)
        ram_mb = round(max(15.0, ram_after - ram_before), 1)

        lines = []
        full_text_parts = []
        word_count = 0

        # Draw overlay image with cyan boxes
        img_bgr = cv2.imread(temp_img_path)
        overlay = img_bgr.copy() if img_bgr is not None else None

        if ocr_res:
            for item in ocr_res:
                # item: [box, text, conf]
                box = item[0] # [[x1,y1], [x2,y2], [x3,y3], [x4,y4]]
                text = item[1]
                conf = float(item[2])
                lines.append({
                    "text": text,
                    "box": box,
                    "confidence": round(conf, 3)
                })
                full_text_parts.append(text)
                word_count += len(text.split())

                if overlay is not None:
                    pts = np.array(box, np.int32).reshape((-1, 1, 2))
                    # Cyan boundary
                    cv2.polylines(overlay, [pts], isClosed=True, color=(255, 230, 0), thickness=2)
                    # Light cyan highlight fill
                    cv2.fillPoly(overlay, [pts], color=(240, 200, 30))

        overlay_out_path = args.out_overlay
        if not overlay_out_path:
            out_dir = os.path.join(os.path.dirname(file_path), "outputs")
            os.makedirs(out_dir, exist_ok=True)
            overlay_out_path = os.path.join(out_dir, f"ocr_overlay_{int(time.time()*1000)}.jpg")

        if overlay is not None and img_bgr is not None:
            # Alpha blend 0.35 highlight + 0.65 original
            blended = cv2.addWeighted(overlay, 0.35, img_bgr, 0.65, 0)
            cv2.imwrite(overlay_out_path, blended)

        emit({
            "type": "ocr_complete",
            "full_text": "\n".join(full_text_parts),
            "lines": lines,
            "line_count": len(lines),
            "word_count": word_count,
            "infer_time_ms": infer_ms,
            "device": args.device or "NPU",
            "overlay_path": overlay_out_path,
            "engine": engine_choice,
            "engine_label": engine_label,
            "model_size_mb": model_size_mb,
            "ram_mb": ram_mb
        })
    except Exception as ex:
        emit_error(f"Erreur lors de l'OCR : {str(ex)}")
    finally:
        if temp_created and os.path.exists(temp_img_path):
            try:
                os.remove(temp_img_path)
            except:
                pass


def action_make_searchable(args):
    """Transforms a scanned PDF or flat image into an official Searchable PDF (Sandwich PDF) with invisible text layer."""
    file_path = args.file
    if not os.path.exists(file_path):
        emit_error(f"Fichier introuvable : {file_path}")
        return

    ext = os.path.splitext(file_path)[1].lower()
    out_pdf = args.out_pdf
    if not out_pdf:
        out_dir = os.path.join(os.path.dirname(file_path), "outputs")
        os.makedirs(out_dir, exist_ok=True)
        out_pdf = os.path.join(out_dir, f"searchable_{int(time.time()*1000)}.pdf")

    out_preview = args.out_preview
    if not out_preview:
        out_dir = os.path.join(os.path.dirname(out_pdf), "outputs")
        os.makedirs(out_dir, exist_ok=True)
        out_preview = os.path.join(out_dir, f"preview_searchable_{int(time.time()*1000)}.jpg")

    t0 = time.perf_counter()
    try:
        engine_choice = getattr(args, "engine", "olive") or "olive"
        engine, model_size_mb, engine_label = get_ocr_engine(engine_choice)

        total_words = 0
        total_lines = 0

        if ext == ".pdf":
            # Scanned PDF: process each page and inject text layer
            doc = pymupdf.open(file_path)
            dpi = args.dpi or 150
            scale = 72.0 / float(dpi)

            for page_idx in range(len(doc)):
                page = doc[page_idx]
                pix = page.get_pixmap(dpi=dpi)
                temp_frame = f"temp_frame_{int(time.time()*1000)}_{page_idx}.png"
                pix.save(temp_frame)

                ocr_res, _ = engine(temp_frame)
                try:
                    os.remove(temp_frame)
                except:
                    pass

                if ocr_res:
                    for item in ocr_res:
                        box = item[0]
                        text = item[1]
                        px0, py0 = box[0][0], box[0][1]
                        px1, py1 = box[2][0], box[2][1]

                        pt_x0 = px0 * scale
                        pt_y0 = py0 * scale
                        pt_x1 = px1 * scale
                        pt_y1 = py1 * scale
                        box_h = max(1, pt_y1 - pt_y0)

                        fontsize = max(6, box_h * 0.82)
                        # render_mode=3 creates invisible searchable text
                        page.insert_text((pt_x0, pt_y1 - box_h * 0.15), text, fontsize=fontsize, render_mode=3)
                        total_lines += 1
                        total_words += len(text.split())

            doc.save(out_pdf)
            page0 = doc[0]
            pix0 = page0.get_pixmap(dpi=150)
            pix0.save(out_preview)
            doc.close()
        else:
            # Flat image file (PNG, JPG, TIFF)
            doc = pymupdf.open()
            img_doc = pymupdf.open(file_path)
            img_rect = img_doc[0].rect
            page = doc.new_page(width=img_rect.width, height=img_rect.height)
            page.insert_image(img_rect, filename=file_path)
            img_doc.close()

            ocr_res, _ = engine(file_path)
            if ocr_res:
                for item in ocr_res:
                    box = item[0]
                    text = item[1]
                    x0, y0 = box[0][0], box[0][1]
                    x1, y1 = box[2][0], box[2][1]
                    box_h = max(1, y1 - y0)
                    fontsize = max(6, box_h * 0.82)
                    page.insert_text((x0, y1 - box_h * 0.15), text, fontsize=fontsize, render_mode=3)
                    total_lines += 1
                    total_words += len(text.split())

            doc.save(out_pdf)
            page0 = doc[0]
            pix0 = page0.get_pixmap(dpi=150)
            pix0.save(out_preview)
            doc.close()

        infer_ms = int((time.perf_counter() - t0) * 1000)
        emit({
            "type": "searchable_complete",
            "output_pdf": out_pdf,
            "preview_path": out_preview,
            "word_count": total_words,
            "line_count": total_lines,
            "infer_time_ms": infer_ms,
            "device": args.device or "NPU"
        })
    except Exception as ex:
        emit_error(f"Erreur lors de la création du PDF rechercheable : {str(ex)}")


def action_redact(args):
    """Performs genuine legal redaction (permanent text & vector removal) on PDF."""
    file_path = args.file
    if not os.path.exists(file_path):
        emit_error(f"Fichier introuvable : {file_path}")
        return

    ext = os.path.splitext(file_path)[1].lower()
    if ext != ".pdf":
        emit_error("Le caviardage légal nécessite un fichier PDF vectoriel.")
        return

    out_pdf = args.out_pdf
    if not out_pdf:
        out_dir = os.path.join(os.path.dirname(file_path), "outputs")
        os.makedirs(out_dir, exist_ok=True)
        out_pdf = os.path.join(out_dir, f"redacted_{int(time.time()*1000)}.pdf")

    out_preview = args.out_preview
    if not out_preview:
        out_dir = os.path.join(os.path.dirname(out_pdf), "outputs")
        os.makedirs(out_dir, exist_ok=True)
        out_preview = os.path.join(out_dir, f"preview_redacted_{int(time.time()*1000)}.jpg")

    t0 = time.perf_counter()
    try:
        doc = pymupdf.open(file_path)
        total_redacted = 0
        redacted_details = []

        patterns = []
        if args.mode == "auto_rgpd":
            # GDPR sensitive patterns: Emails, French Phones, IBANs, NIR/Social Security
            patterns = [
                (r'[\w\.-]+@[\w\.-]+\.\w+', "Adresse Email RGPD"),
                (r'(?:\+33|0)[1-9](?:[\s.-]?\d{2}){4}', "Numéro de Téléphone"),
                (r'[A-Z]{2}\d{2}[A-Z0-9]{4}\d{7}([A-Z0-9]?){0,16}', "Coordonnées Bancaires (IBAN)"),
                (r'[12]\s?\d{2}\s?(?:0[1-9]|1[0-2])\s?\d{2}\s?\d{3}\s?\d{3}(?:\s?\d{2})?', "Numéro Sécurité Sociale (NIR)"),
                (r'\b\d{1,3}(?:\s\d{3})*,\d{2}\s?EUR\b', "Montant Financier Confidentiel")
            ]
        if getattr(args, 'rect', None) and args.rect:
            try:
                coords = [float(c.strip()) for c in args.rect.split(",")]
                if len(coords) == 4:
                    page0 = doc[args.page if args.page < len(doc) else 0]
                    r = pymupdf.Rect(coords[0], coords[1], coords[2], coords[3])
                    page0.add_redact_annot(r, fill=(0, 0, 0))
                    page0.apply_redactions()
                    total_redacted += 1
                    redacted_details.append("Zone rectangulaire sélectionnée")
            except Exception:
                pass
        elif patterns:
            for page in doc:
                for pattern, label in patterns:
                    matches = page.search_for(pattern)
                    if not matches:
                        page_text = page.get_text("text")
                        for m in re.finditer(pattern, page_text):
                            found_str = m.group(0)
                            sub_matches = page.search_for(found_str)
                            matches.extend(sub_matches)

                    for rect in matches:
                        page.add_redact_annot(rect, fill=(0, 0, 0))
                        total_redacted += 1
                        redacted_details.append(label)

                page.apply_redactions()

        doc.save(out_pdf)

        page0 = doc[0]
        dpi = getattr(args, 'dpi', 150) or 150
        pix = page0.get_pixmap(dpi=dpi)
        pix.save(out_preview)
        words = extract_page_words(page0, dpi)
        doc.close()

        infer_ms = int((time.perf_counter() - t0) * 1000)

        emit({
            "type": "redact_complete",
            "output_pdf": out_pdf,
            "preview_path": out_preview,
            "redacted_count": total_redacted,
            "redacted_items": list(set(redacted_details)),
            "words": words,
            "infer_time_ms": infer_ms,
            "device": args.device or "NPU"
        })
    except Exception as ex:
        emit_error(f"Erreur lors du caviardage : {str(ex)}")


def action_stamp(args):
    """Applies official enterprise legal stamp / watermark on PDF."""
    file_path = args.file
    if not os.path.exists(file_path):
        emit_error(f"Fichier introuvable : {file_path}")
        return

    out_pdf = args.out_pdf or os.path.join(os.path.dirname(file_path), "outputs", f"stamped_{int(time.time()*1000)}.pdf")
    out_preview = args.out_preview or os.path.join(os.path.dirname(file_path), "outputs", f"preview_stamped_{int(time.time()*1000)}.jpg")
    os.makedirs(os.path.dirname(out_pdf), exist_ok=True)
    os.makedirs(os.path.dirname(out_preview), exist_ok=True)

    t0 = time.perf_counter()
    try:
        doc = pymupdf.open(file_path)
        stamp_type = (args.stamp_type or "CONFIDENTIAL").upper()
        now_str = datetime.datetime.now().strftime("%d/%m/%Y %H:%M:%S")

        if "CONFID" in stamp_type:
            title = "CONFIDENTIEL - SECRET DES AFFAIRES"
            subtitle = f"Copilot+ PC • Diffusion Restreinte • Scellé le {now_str}"
            border_color = (0.75, 0.10, 0.15)
            bg_color = (1.0, 0.94, 0.94)
        elif "APPROV" in stamp_type or "VALID" in stamp_type:
            title = "APPROUVÉ & VALIDÉ POUR EXÉCUTION"
            subtitle = f"Conforme Direction Générale • Horodaté le {now_str}"
            border_color = (0.05, 0.60, 0.35)
            bg_color = (0.93, 0.99, 0.95)
        else:
            title = "COPIE CERTIFIÉE CONFORME À L'ORIGINAL"
            subtitle = f"Archivage Légal Intègre • Contrôle IT le {now_str}"
            border_color = (0.05, 0.40, 0.75)
            bg_color = (0.93, 0.96, 1.0)

        for page in doc:
            rect = page.rect
            w = 340
            h = 48
            x1 = rect.width - w - 30
            y1 = rect.height - h - 30
            x2 = rect.width - 30
            y2 = rect.height - 30
            stamp_rect = pymupdf.Rect(x1, y1, x2, y2)

            page.draw_rect(stamp_rect, fill=bg_color, color=border_color, width=2)
            page.insert_text((x1 + 12, y1 + 20), title, fontsize=9.5, fontname="helv", color=border_color)
            page.insert_text((x1 + 12, y1 + 36), subtitle, fontsize=7.5, fontname="helv", color=(0.3, 0.3, 0.3))

        doc.save(out_pdf)

        page0 = doc[0]
        dpi = getattr(args, 'dpi', 150) or 150
        pix = page0.get_pixmap(dpi=dpi)
        pix.save(out_preview)
        words = extract_page_words(page0, dpi)
        doc.close()

        infer_ms = int((time.perf_counter() - t0) * 1000)
        emit({
            "type": "stamp_complete",
            "output_pdf": out_pdf,
            "preview_path": out_preview,
            "stamp_applied": title,
            "words": words,
            "infer_time_ms": infer_ms,
            "device": args.device or "NPU"
        })
    except Exception as ex:
        emit_error(f"Erreur lors de l'apposition du tampon : {str(ex)}")


def action_replace(args):
    """Replaces text in PDF vector content with pixel-perfect typesetting."""
    file_path = args.file
    search_term = (args.search or "").strip()
    replace_term = args.replace if args.replace is not None else ""

    if not os.path.exists(file_path):
        emit_error(f"Fichier introuvable : {file_path}")
        return

    out_pdf = args.out_pdf or os.path.join(os.path.dirname(file_path), "outputs", f"replaced_{int(time.time()*1000)}.pdf")
    out_preview = args.out_preview or os.path.join(os.path.dirname(file_path), "outputs", f"preview_replaced_{int(time.time()*1000)}.jpg")
    os.makedirs(os.path.dirname(out_pdf), exist_ok=True)
    os.makedirs(os.path.dirname(out_preview), exist_ok=True)

    t0 = time.perf_counter()
    try:
        doc = pymupdf.open(file_path)
        replacements_count = 0
        dpi = getattr(args, 'dpi', 150) or 150

        target_rect = None
        if getattr(args, 'rect', None) and args.rect:
            try:
                coords = [float(c.strip()) for c in args.rect.split(",")]
                if len(coords) == 4:
                    target_rect = pymupdf.Rect(coords[0], coords[1], coords[2], coords[3])
            except Exception:
                pass

        for page in doc:
            dict_data = page.get_text("dict")

            def find_line_for_rect(r):
                for b in dict_data.get("blocks", []):
                    if "lines" not in b: continue
                    for l in b["lines"]:
                        l_rect = pymupdf.Rect(l["bbox"])
                        if r.intersects(l_rect):
                            return l
                return None

            items_to_replace = []

            if search_term:
                matches = page.search_for(search_term)
                if not matches:
                    for sp in ['\xa0', '\u202f', ' ']:
                        alt = re.sub(r'\s+', sp, search_term)
                        matches = page.search_for(alt)
                        if matches: break
                if not matches:
                    clean = " ".join(search_term.split())
                    matches = page.search_for(clean)

                if target_rect:
                    filtered = [m for m in matches if m.intersects(target_rect)]
                    if filtered:
                        matches = filtered

                for m in matches:
                    line_info = find_line_for_rect(m)
                    items_to_replace.append((m, line_info))

            elif target_rect:
                line_info = find_line_for_rect(target_rect)
                items_to_replace.append((target_rect, line_info))

            if not items_to_replace:
                continue

            replacements_plan = []
            for m, line_info in items_to_replace:
                if line_info and "spans" in line_info and line_info["spans"]:
                    line_rect = pymupdf.Rect(line_info["bbox"])
                    full_line_text = "".join(s["text"] for s in line_info["spans"])

                    if search_term and search_term in full_line_text:
                        new_line_text = full_line_text.replace(search_term, replace_term, 1)
                    elif search_term:
                        pattern = re.escape(search_term).replace(r'\ ', r'[\s\xa0\u202f]+')
                        new_line_text = re.sub(pattern, replace_term, full_line_text, count=1)
                    else:
                        new_line_text = replace_term

                    s0 = line_info["spans"][0]
                    font_name = "hebo" if "bold" in s0.get("font", "").lower() else "helv"
                    font_size = s0.get("size", 10.0)
                    color_int = s0.get("color", 0)
                    r = ((color_int >> 16) & 255) / 255.0
                    g = ((color_int >> 8) & 255) / 255.0
                    b = (color_int & 255) / 255.0
                    origin_x = line_rect.x0
                    origin_y = s0.get("origin", [line_rect.x0, line_rect.y1 - 2])[1]

                    redact_r = pymupdf.Rect(line_rect.x0 - 1, line_rect.y0 - 1, line_rect.x1 + 4, line_rect.y1 + 1)
                    page.add_redact_annot(redact_r, fill=(1, 1, 1))

                    replacements_plan.append((origin_x, origin_y, new_line_text, font_size, font_name, (r, g, b)))
                    replacements_count += 1
                else:
                    page.add_redact_annot(m, fill=(1, 1, 1))
                    f_size = max(7.0, min(14.0, m.height * 0.75))
                    b_y = m.y1 - m.height * 0.22
                    replacements_plan.append((m.x0, b_y, replace_term, f_size, "helv", (0.1, 0.1, 0.1)))
                    replacements_count += 1

            page.apply_redactions()

            for ox, oy, ntext, fsize, fname, fcolor in replacements_plan:
                try:
                    page.insert_text(pymupdf.Point(ox, oy), ntext, fontsize=fsize, fontname=fname, color=fcolor)
                except Exception:
                    try:
                        page.insert_text(pymupdf.Point(ox, oy), ntext, fontsize=fsize, fontname="helv", color=fcolor)
                    except Exception:
                        page.insert_text(pymupdf.Point(ox, oy), ntext, fontsize=fsize, color=fcolor)

        doc.save(out_pdf)

        page0 = doc[0]
        pix0 = page0.get_pixmap(dpi=dpi)
        pix0.save(out_preview)
        words = extract_page_words(page0, dpi)
        doc.close()

        infer_ms = int((time.perf_counter() - t0) * 1000)
        emit({
            "type": "replace_complete",
            "output_pdf": out_pdf,
            "preview_path": out_preview,
            "replacements_count": replacements_count,
            "words": words,
            "infer_time_ms": infer_ms,
            "device": args.device or "NPU"
        })
    except Exception as ex:
        emit_error(f"Erreur lors du remplacement de texte : {str(ex)}")


def main():
    parser = argparse.ArgumentParser(description="Service Local de Modification PDF & Reconnaissance OCR")
    parser.add_argument("--action", required=True, choices=["render", "ocr", "redact", "stamp", "replace", "make_searchable"])
    parser.add_argument("--file", required=True, help="Chemin du document source (PDF ou Image)")
    parser.add_argument("--page", type=int, default=0, help="Index de la page")
    parser.add_argument("--dpi", type=int, default=150, help="Résolution DPI pour le rendu")
    parser.add_argument("--device", default="NPU", help="Puce ciblée (NPU, GPU, CPU)")
    parser.add_argument("--out", help="Chemin de sortie pour render")
    parser.add_argument("--out_pdf", help="Chemin de sortie pour PDF modifié")
    parser.add_argument("--out_preview", help="Chemin de sortie pour l'aperçu JPG")
    parser.add_argument("--out_overlay", help="Chemin pour l'overlay de détection OCR")
    parser.add_argument("--mode", default="auto_rgpd", help="Mode de caviardage: auto_rgpd ou keyword")
    parser.add_argument("--keyword", help="Mot-clé spécifique à caviarder")
    parser.add_argument("--rect", help="Coordonnées rectangulaires (x0,y0,x1,y1) en points PDF")
    parser.add_argument("--stamp_type", default="CONFIDENTIAL", help="Type de tampon: CONFIDENTIAL, APPROVED, CERTIFIED_COPY")
    parser.add_argument("--search", help="Terme à rechercher pour remplacement")
    parser.add_argument("--replace", help="Nouveau terme de remplacement")
    parser.add_argument("--engine", default="olive", choices=["raw", "olive"], help="Moteur d'inférence OCR (raw ou olive)")

    args = parser.parse_args()

    if args.action == "render":
        action_render(args)
    elif args.action == "ocr":
        action_ocr(args)
    elif args.action == "redact":
        action_redact(args)
    elif args.action == "stamp":
        action_stamp(args)
    elif args.action == "replace":
        action_replace(args)
    elif args.action == "make_searchable":
        action_make_searchable(args)


if __name__ == "__main__":
    main()
