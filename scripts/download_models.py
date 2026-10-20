#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Copilot+ PC Demo - Téléchargement Automatisé des Modèles d'IA
Permet d'initialiser ou restaurer 100% des modèles locaux requis :
1. Whisper (Transcription STT Tiny, Base & Small OpenVINO)
2. Biométrie Vocale (VoxCeleb ECAPA-TDNN ONNX)
3. Vision par ordinateur (YOLOv8s-World Zero-Trust OpenVINO)
4. Gomme Magique (YOLOv8n-Seg Segmentation d'objets OpenVINO)
5. Studio Avatar (LCM DreamShaper 8-bit Diffusion OpenVINO)
"""

import os
import sys
import shutil
import argparse

# Configuration de l'encodage console Windows
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

GREEN = "\033[92m"
YELLOW = "\033[93m"
CYAN = "\033[96m"
RED = "\033[91m"
BOLD = "\033[1m"
RESET = "\033[0m"

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MODELS_DIR = os.path.join(ROOT_DIR, "models")


def log_step(title):
    print(f"\n{CYAN}{BOLD}▶ {title}{RESET}")


def log_ok(msg):
    print(f"  {GREEN}[OK]{RESET} {msg}")


def log_info(msg):
    print(f"  {YELLOW}[INFO]{RESET} {msg}")


def log_err(msg):
    print(f"  {RED}[ERREUR]{RESET} {msg}")


def ensure_models_dir():
    if not os.path.exists(MODELS_DIR):
        os.makedirs(MODELS_DIR, exist_ok=True)
        log_info(f"Dossier models/ créé : {MODELS_DIR}")


def download_whisper_models(force=False):
    log_step("Modèles de Transcription Audio Speech-to-Text (Whisper OpenVINO)")
    try:
        from huggingface_hub import snapshot_download
    except ImportError:
        log_err("huggingface_hub n'est pas installé. Lancez : pip install huggingface_hub")
        return False

    models = [
        ("whisper-tiny-fp16-ov", "OpenVINO/whisper-tiny-fp16-ov", "Whisper Tiny (Ultra-rapide)"),
        ("whisper-base-fp16-ov", "OpenVINO/whisper-base-fp16-ov", "Whisper Base (Haute précision)"),
        ("whisper-small-fp16-ov", "OpenVINO/whisper-small-fp16-ov", "Whisper Small (Transcription en direct)")
    ]
    ok_all = True

    for dir_name, repo_id, label in models:
        target = os.path.join(MODELS_DIR, dir_name)
        xml_file = os.path.join(target, "openvino_encoder_model.xml")
        bin_file = os.path.join(target, "openvino_encoder_model.bin")

        if not force and os.path.exists(xml_file) and os.path.exists(bin_file):
            log_ok(f"{label} déjà présent dans {dir_name}")
            continue

        log_info(f"Téléchargement de {label} depuis Hugging Face ({repo_id})...")
        try:
            snapshot_download(
                repo_id=repo_id,
                local_dir=target,
                ignore_patterns=["*.msgpack", "*.h5", "*.tflite", ".git*"]
            )
            log_ok(f"{label} téléchargé avec succès.")
        except Exception as e:
            log_err(f"Échec du téléchargement de {label} : {e}")
            ok_all = False
            continue
        if not os.path.exists(xml_file):
            log_err(f"{label} : fichier {xml_file} introuvable après téléchargement.")
            ok_all = False
    return ok_all


def download_speaker_model(force=False):
    log_step("Modèle de Biométrie Vocale Locuteur (ECAPA-TDNN ONNX)")
    try:
        from huggingface_hub import hf_hub_download
    except ImportError:
        log_err("huggingface_hub n'est pas installé.")
        return False

    target_dir = os.path.join(MODELS_DIR, "speaker")
    target_file = os.path.join(target_dir, "voxceleb.onnx")

    if not force and os.path.exists(target_file) and os.path.getsize(target_file) > 10 * 1024 * 1024:
        log_ok(f"Modèle biométrique VoxCeleb déjà présent ({round(os.path.getsize(target_file)/1024/1024, 1)} Mo)")
        return True

    os.makedirs(target_dir, exist_ok=True)
    log_info("Téléchargement de voxceleb.onnx depuis MelissaJ/spkrec-ecapa-voxceleb-onnx...")
    try:
        hf_hub_download(
            repo_id="MelissaJ/spkrec-ecapa-voxceleb-onnx",
            filename="voxceleb.onnx",
            local_dir=target_dir
        )
        log_ok("Modèle biométrique VoxCeleb téléchargé avec succès.")
        return os.path.exists(target_file)
    except Exception as e:
        log_err(f"Échec du téléchargement du modèle biométrique : {e}")
        return False


def _export_yolo(pt_name, target_dir_name, base_name, force):
    """Exporte un modèle YOLO au format OpenVINO (dossier) ET ONNX (fichier brut)."""
    from ultralytics import YOLO

    ov_target = os.path.join(MODELS_DIR, target_dir_name)
    ov_xml = os.path.join(ov_target, base_name + ".xml")
    onnx_target = os.path.join(MODELS_DIR, base_name + ".onnx")
    ok = True
    prev_cwd = os.getcwd()
    os.chdir(MODELS_DIR)  # ultralytics télécharge le .pt et exporte dans le dossier courant
    try:
        if not force and os.path.exists(ov_xml):
            log_ok(f"{base_name} (OpenVINO) déjà présent")
        else:
            log_info(f"Export OpenVINO de {base_name}...")
            try:
                exported = YOLO(pt_name).export(format="openvino")
                if os.path.abspath(exported) != os.path.abspath(ov_target):
                    shutil.rmtree(ov_target, ignore_errors=True)
                    shutil.copytree(exported, ov_target)
                log_ok(f"{base_name} (OpenVINO) généré.")
            except Exception as e:
                log_err(f"Échec export OpenVINO {base_name} : {e}")
                ok = False

        if not force and os.path.exists(onnx_target):
            log_ok(f"{base_name}.onnx déjà présent")
        else:
            log_info(f"Export ONNX de {base_name} (DirectML / Olive)...")
            try:
                exported = YOLO(pt_name).export(format="onnx", imgsz=640, simplify=False)
                if os.path.abspath(exported) != os.path.abspath(onnx_target):
                    shutil.copy2(exported, onnx_target)
                log_ok(f"{base_name}.onnx généré.")
            except Exception as e:
                log_err(f"Échec export ONNX {base_name} : {e}")
                ok = False
    finally:
        os.chdir(prev_cwd)
    return ok


def export_yolo_models(force=False):
    log_step("Modèles de Vision par Ordinateur (YOLOv8 OpenVINO + ONNX)")
    try:
        import ultralytics  # noqa: F401
    except ImportError:
        log_err("ultralytics n'est pas installé. Lancez : pip install ultralytics")
        return False

    ok = _export_yolo("yolov8n-seg.pt", "yolov8n-seg_openvino_model", "yolov8n-seg", force)
    local_world = os.path.join(ROOT_DIR, "yolov8s-worldv2.pt")
    pt_world = local_world if os.path.exists(local_world) else "yolov8s-world.pt"
    ok = _export_yolo(pt_world, "yolov8s-world_openvino_model", "yolov8s-world", force) and ok
    return ok


def prepare_ocr_models(force=False):
    log_step("Modèles OCR (RapidOCR PP-OCRv4 ONNX)")
    try:
        import rapidocr_onnxruntime
    except ImportError:
        log_err("rapidocr-onnxruntime n'est pas installé. Lancez : pip install rapidocr-onnxruntime")
        return False

    src_dir = os.path.join(os.path.dirname(rapidocr_onnxruntime.__file__), "models")
    dst_dir = os.path.join(MODELS_DIR, "ocr")
    os.makedirs(dst_dir, exist_ok=True)
    ok = True
    for name in ("ch_PP-OCRv4_det_infer.onnx", "ch_PP-OCRv4_rec_infer.onnx"):
        src, dst = os.path.join(src_dir, name), os.path.join(dst_dir, name)
        if not force and os.path.exists(dst):
            log_ok(f"{name} déjà présent")
        elif os.path.exists(src):
            shutil.copy2(src, dst)
            log_ok(f"{name} copié depuis RapidOCR.")
        else:
            log_err(f"{name} introuvable dans le paquet RapidOCR ({src_dir}).")
            ok = False
    return ok


def optimize_with_olive(force=False):
    """Génère les variantes *_olive.onnx (FP16). Optionnel : les démos retombent sur les modèles bruts."""
    log_step("Optimisation Microsoft Olive (modèles FP16, optionnel)")
    script = os.path.join(ROOT_DIR, "scripts", "optimize_with_olive.py")
    if not os.path.exists(script):
        log_info("optimize_with_olive.py introuvable, étape ignorée.")
        return True
    import subprocess
    res = subprocess.run([sys.executable, script], capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    if res.returncode != 0:
        log_info("Optimisation Olive indisponible sur ce poste (non bloquant) : "
                 "les démos Olive utiliseront les modèles bruts.")
        tail = (res.stdout + res.stderr).strip().splitlines()[-3:]
        for line in tail:
            log_info(f"  {line}")
        return False
    log_ok("Modèles Olive FP16 générés.")
    return True

def download_lcm_diffusion(force=False):
    log_step("Modèle Studio Avatar / Diffusion (LCM DreamShaper 8-bit OpenVINO)")
    target = os.path.join(MODELS_DIR, "lcm_dreamshaper_8bit")
    unet_bin = os.path.join(target, "unet", "openvino_model.bin")

    if not force and os.path.exists(unet_bin):
        log_ok("Modèle LCM DreamShaper 8-bit déjà présent")
        return True

    try:
        from huggingface_hub import snapshot_download
        log_info("Téléchargement de LCM DreamShaper 8-bit (~1 Go) depuis Hugging Face...")
        snapshot_download(
            repo_id="echarlaix/LCM_Dreamshaper_v7-openvino-8bit",
            local_dir=target,
            ignore_patterns=["*.msgpack", "*.h5", "*.tflite", ".git*"]
        )
        log_ok("Modèle LCM DreamShaper téléchargé avec succès.")
        return os.path.exists(unet_bin)
    except Exception as e:
        log_err(f"Échec du téléchargement LCM DreamShaper : {e}")
        return False


def main():
    parser = argparse.ArgumentParser(description="Télécharge et prépare tous les modèles d'IA pour Copilot+ Demo Hub.")
    parser.add_argument("--force", action="store_true", help="Forcer le re-téléchargement même si les modèles sont présents")
    parser.add_argument("--skip-diffusion", action="store_true", help="Ignorer le modèle de diffusion d'images (~1 Go)")
    args = parser.parse_args()

    print("======================================================================")
    print("      Copilot+ Demo Hub - Initialisation des Modèles d'IA Locaux      ")
    print("======================================================================")

    ensure_models_dir()
    results = {}
    results["Whisper (tiny / base / small)"] = download_whisper_models(force=args.force)
    results["Biométrie vocale VoxCeleb"] = download_speaker_model(force=args.force)
    results["YOLOv8 OpenVINO + ONNX"] = export_yolo_models(force=args.force)
    results["Modèles OCR RapidOCR"] = prepare_ocr_models(force=args.force)

    if not args.skip_diffusion:
        results["Avatar LCM DreamShaper"] = download_lcm_diffusion(force=args.force)

    # Optionnel : ne bloque jamais l'installation
    optimize_with_olive(force=args.force)

    failed = [name for name, ok in results.items() if not ok]
    print("\n======================================================================")
    if failed:
        print(f" {RED}[INCOMPLET]{RESET} Modèles manquants ou en erreur :")
        for name in failed:
            print(f"   - {name}")
        print(" Les fonctionnalités associées seront indisponibles.")
        print(" Relancez : python scripts\\download_models.py   (vérifiez la connexion Internet)")
        print("======================================================================\n")
        sys.exit(1)
    print(f" {GREEN}[SUCCÈS]{RESET} Tous les modèles d'IA nécessaires sont prêts dans models/")
    print("======================================================================\n")

if __name__ == "__main__":
    main()
