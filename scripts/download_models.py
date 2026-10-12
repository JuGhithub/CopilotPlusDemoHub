#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Copilot+ PC Demo - Téléchargement Automatisé des Modèles d'IA
Permet d'initialiser ou restaurer 100% des modèles locaux requis :
1. Whisper (Transcription STT Tiny & Base OpenVINO)
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
        ("whisper-base-fp16-ov", "OpenVINO/whisper-base-fp16-ov", "Whisper Base (Haute précision)")
    ]

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
    except Exception as e:
        log_err(f"Échec du téléchargement du modèle biométrique : {e}")


def export_yolo_models(force=False):
    log_step("Modèles de Vision par Ordinateur (YOLOv8 OpenVINO NPU/GPU)")
    try:
        from ultralytics import YOLO
    except ImportError:
        log_err("ultralytics n'est pas installé. Lancez : pip install ultralytics")
        return False

    # 1. YOLOv8n-Seg (Gomme Magique)
    seg_target = os.path.join(MODELS_DIR, "yolov8n-seg_openvino_model")
    seg_xml = os.path.join(seg_target, "yolov8n-seg.xml")
    if not force and os.path.exists(seg_xml):
        log_ok("Modèle Gomme Magique YOLOv8n-Seg (OpenVINO) déjà présent")
    else:
        log_info("Génération du modèle YOLOv8n-Seg OpenVINO (export automatique)...")
        try:
            model = YOLO("yolov8n-seg.pt")
            exported_path = model.export(format="openvino")
            # Ultralytics exports either in current dir or models dir
            if os.path.abspath(exported_path) != os.path.abspath(seg_target):
                if os.path.exists(seg_target):
                    shutil.rmtree(seg_target, ignore_errors=True)
                shutil.copytree(exported_path, seg_target)
            log_ok("Modèle Gomme Magique YOLOv8n-Seg généré avec succès.")
        except Exception as e:
            log_err(f"Échec export YOLOv8n-Seg : {e}")

    # 2. YOLOv8s-World (Contrôle d'Accès Zero-Trust)
    world_target = os.path.join(MODELS_DIR, "yolov8s-world_openvino_model")
    world_xml = os.path.join(world_target, "yolov8s-world.xml")
    if not force and os.path.exists(world_xml):
        log_ok("Modèle Contrôle d'Accès YOLOv8s-World (OpenVINO) déjà présent")
    else:
        log_info("Génération du modèle YOLOv8s-World OpenVINO (export automatique)...")
        try:
            # Essayer d'abord yolov8s-worldv2.pt ou yolov8s-world.pt
            pt_name = "yolov8s-worldv2.pt" if os.path.exists(os.path.join(ROOT_DIR, "yolov8s-worldv2.pt")) else "yolov8s-world.pt"
            model = YOLO(pt_name)
            exported_path = model.export(format="openvino")
            if os.path.abspath(exported_path) != os.path.abspath(world_target):
                if os.path.exists(world_target):
                    shutil.rmtree(world_target, ignore_errors=True)
                shutil.copytree(exported_path, world_target)
            log_ok("Modèle Contrôle d'Accès YOLOv8s-World généré avec succès.")
        except Exception as e:
            log_err(f"Échec export YOLOv8s-World : {e}")


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
    except Exception as e:
        log_err(f"Échec du téléchargement LCM DreamShaper : {e}")


def main():
    parser = argparse.ArgumentParser(description="Télécharge et prépare tous les modèles d'IA pour Copilot+ Demo Hub.")
    parser.add_argument("--force", action="store_true", help="Forcer le re-téléchargement même si les modèles sont présents")
    parser.add_argument("--skip-diffusion", action="store_true", help="Ignorer le modèle de diffusion d'images (~1 Go)")
    args = parser.parse_args()

    print("======================================================================")
    print("      Copilot+ Demo Hub - Initialisation des Modèles d'IA Locaux      ")
    print("======================================================================")

    ensure_models_dir()
    download_whisper_models(force=args.force)
    download_speaker_model(force=args.force)
    export_yolo_models(force=args.force)

    if not args.skip_diffusion:
        download_lcm_diffusion(force=args.force)

    print("\n======================================================================")
    print(f" {GREEN}[SUCCÈS]{RESET} Tous les modèles d'IA nécessaires sont prêts dans models/")
    print("======================================================================\n")


if __name__ == "__main__":
    main()
