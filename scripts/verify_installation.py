#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Copilot+ PC Demo - Diagnostic & Verification Script
Verifie l'integrite complete de l'environnement :
1. Systeme & Windows (Version, LongPaths)
2. Accelerateurs IA (NPU Intel AI Boost, GPU Arc, DirectML)
3. Runtimes (.NET 8, Python 3.12, Foundry Local)
4. Dependances logicielles Python
5. Modeles d'IA locaux embarques (models/)
6. Peripheriques multimedias (Webcam, Microphone)
7. Binaires compiles de l'application
"""

import sys
import os
import subprocess
import platform

# Ensure UTF-8 output
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

# ANSI colors
GREEN = "\033[92m"
YELLOW = "\033[93m"
RED = "\033[91m"
CYAN = "\033[96m"
BOLD = "\033[1m"
RESET = "\033[0m"

passed_checks = 0
warning_checks = 0
failed_checks = 0

def log_ok(title, detail=""):
    global passed_checks
    passed_checks += 1
    det = f" - {detail}" if detail else ""
    print(f"  {GREEN}[OK]{RESET} {BOLD}{title}{RESET}{det}")

def log_warn(title, detail=""):
    global warning_checks
    warning_checks += 1
    det = f" - {detail}" if detail else ""
    print(f"  {YELLOW}[ATTENTION]{RESET} {BOLD}{title}{RESET}{det}")

def log_fail(title, detail=""):
    global failed_checks
    failed_checks += 1
    det = f" - {detail}" if detail else ""
    print(f"  {RED}[ERREUR]{RESET} {BOLD}{title}{RESET}{det}")

def section(title):
    print(f"\n{CYAN}{BOLD}--- {title} ---{RESET}")

def check_system():
    section("1. Systeme d'exploitation & Configuration Windows")
    
    # OS Version
    os_name = platform.system()
    os_ver = platform.version()
    os_rel = platform.release()
    if os_name == "Windows":
        log_ok("Systeme d'exploitation", f"Windows {os_rel} (Build {os_ver})")
    else:
        log_warn("Systeme d'exploitation", f"{os_name} {os_ver} (Recommande : Windows 11 24H2+)")

    # LongPathsEnabled
    try:
        import winreg
        key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Control\FileSystem")
        val, _ = winreg.QueryValueEx(key, "LongPathsEnabled")
        winreg.CloseKey(key)
        if val == 1:
            log_ok("Support des chemins longs (LongPathsEnabled)", "Active")
        else:
            log_warn("Support des chemins longs (LongPathsEnabled)", "Desactive (Recommande d'activer pour PyTorch/OpenVINO)")
    except Exception:
        log_warn("Support des chemins longs (LongPathsEnabled)", "Impossible de verifier la cle de registre")

def check_runtimes():
    section("2. Runtimes & Socles Logiciels")
    
    # Python
    py_ver = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
    if sys.version_info.major == 3 and sys.version_info.minor >= 10:
        log_ok(f"Python {py_ver}", sys.executable)
    else:
        log_warn(f"Python {py_ver}", "Python 3.10 a 3.12 recommande")

    # .NET 8 SDK / Runtime
    try:
        res = subprocess.run(["dotnet", "--version"], capture_output=True, text=True, check=True)
        dotnet_ver = res.stdout.strip()
        if dotnet_ver.startswith("8."):
            log_ok(f".NET SDK {dotnet_ver}", "Version 8.0 detectee")
        else:
            log_ok(f".NET SDK {dotnet_ver}", "Version compatible")
    except Exception:
        log_fail(".NET SDK", "dotnet introuvable dans le PATH. Installez Microsoft.DotNet.SDK.8")

    # Microsoft Foundry Local CLI
    try:
        res = subprocess.run(["foundry", "--version"], capture_output=True, text=True, shell=True)
        if res.returncode == 0:
            foundry_ver = res.stdout.strip() or "Installe"
            log_ok("Microsoft Foundry Local CLI", foundry_ver)
        else:
            log_warn("Microsoft Foundry Local CLI", "Commande 'foundry' echoue. Verifiez %LOCALAPPDATA%\\Microsoft\\WindowsApps dans le PATH")
    except Exception:
        log_warn("Microsoft Foundry Local CLI", "Non installe ou absent du PATH (winget install Microsoft.FoundryLocal)")

def check_hardware_ai():
    section("3. Moteurs d'Acceleration IA (NPU, GPU, DirectML)")
    
    # 1. Identification du fondeur processeur (Intel / AMD / Qualcomm)
    cpu_vendor = "Intel"
    try:
        cmd = ['powershell', '-NoProfile', '-Command', '(Get-CimInstance Win32_Processor).Name']
        res = subprocess.run(cmd, capture_output=True, text=True, timeout=5)
        cpu_name = res.stdout.strip()
        if "AMD" in cpu_name or "Ryzen" in cpu_name:
            cpu_vendor = "AMD"
        elif "Snapdragon" in cpu_name or "Qualcomm" in cpu_name:
            cpu_vendor = "Qualcomm"
    except Exception:
        pass

    # 2. OpenVINO
    has_ov_npu = False
    has_ov_gpu = False
    try:
        import openvino as ov
        core = ov.Core()
        devices = core.available_devices
        log_ok("OpenVINO Core", f"Version {ov.__version__} - Peripheriques : {', '.join(devices)}")
        has_ov_npu = any("NPU" in d for d in devices)
        has_ov_gpu = any("GPU" in d for d in devices)
        if has_ov_npu:
            npu_dev = [d for d in devices if "NPU" in d][0]
            try:
                npu_name = core.get_property(npu_dev, "FULL_DEVICE_NAME")
            except Exception:
                npu_name = npu_dev
            log_ok("NPU Detecte (Copilot+ PC)", f"{npu_name} ({npu_dev})")
        if has_ov_gpu:
            gpu_dev = [d for d in devices if "GPU" in d][0]
            try:
                gpu_name = core.get_property(gpu_dev, "FULL_DEVICE_NAME")
            except Exception:
                gpu_name = gpu_dev
            log_ok("GPU Detecte", f"{gpu_name} ({gpu_dev})")
    except ImportError:
        log_fail("OpenVINO", "Module non installe (pip install openvino)")
    except Exception as e:
        log_warn("OpenVINO", str(e))

    # 3. Detection universelle du NPU si non detecte par OpenVINO (AMD Ryzen AI / Qualcomm Hexagon)
    if not has_ov_npu:
        pnp_found = False
        try:
            cmd = ['powershell', '-NoProfile', '-Command', 
                   "Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'NPU|AI Boost|Neural|IPU' -and $_.Name -notmatch 'Configuration' } | Select-Object -ExpandProperty Name"]
            res = subprocess.run(cmd, capture_output=True, text=True, timeout=5)
            names = [n.strip() for n in res.stdout.strip().split('\n') if n.strip()]
            if names:
                log_ok("NPU Detecte (Copilot+ PC)", f"{names[0]} (Accelerateur NPU {cpu_vendor})")
                pnp_found = True
        except Exception:
            pass
        if not pnp_found:
            if cpu_vendor == "Intel":
                log_warn("NPU Non Detecte", "Pilote Intel NPU Compute Driver non actif ou NPU absent")
            elif cpu_vendor == "AMD":
                log_warn("NPU Non Detecte", "Pilote AMD Ryzen AI NPU non actif ou NPU absent")
            elif cpu_vendor == "Qualcomm":
                log_warn("NPU Non Detecte", "Pilote Qualcomm Hexagon NPU non actif ou NPU absent")
            else:
                log_warn("NPU Non Detecte", "Aucun NPU Copilot+ PC detecte")

    # 4. ONNX Runtime DirectML (Moteur universel DirectX 12)
    try:
        import onnxruntime as ort
        providers = ort.get_available_providers()
        if "DmlExecutionProvider" in providers:
            log_ok("DirectML Execution Provider", "Disponible (Acceleration universelle GPU/NPU DirectX 12)")
        else:
            log_warn("DirectML Execution Provider", f"Non disponible. Providers presents : {', '.join(providers)}")
    except ImportError:
        log_fail("ONNX Runtime", "Module non installe (pip install onnxruntime-directml)")
    except Exception as e:
        log_warn("ONNX Runtime", str(e))

def check_python_packages():
    section("4. Bibliotheques Python Requises")
    
    required_packages = [
        ("openvino", "openvino"),
        ("openvino_genai", "openvino-genai"),
        ("torch", "torch"),
        ("torchvision", "torchvision"),
        ("ultralytics", "ultralytics (YOLO)"),
        ("cv2", "opencv-python"),
        ("PIL", "pillow"),
        ("numpy", "numpy"),
        ("scipy", "scipy"),
        ("transformers", "transformers"),
        ("sounddevice", "sounddevice"),
        ("soundfile", "soundfile"),
        ("pymupdf", "PyMuPDF"),
        ("rapidocr_onnxruntime", "rapidocr-onnxruntime"),
        ("psutil", "psutil")
    ]
    
    for mod_name, label in required_packages:
        try:
            mod = __import__(mod_name)
            ver = getattr(mod, "__version__", "OK")
            log_ok(label, f"v{ver}")
        except ImportError:
            log_fail(label, f"Manquant ! Installez avec: pip install -r requirements.txt")
        except Exception as e:
            log_warn(label, f"Erreur de chargement: {e}")

    # Optional Olive check
    try:
        import olive
        log_ok("Microsoft Olive (olive-ai)", f"v{getattr(olive, '__version__', 'OK')}")
    except ImportError:
        log_warn("Microsoft Olive (olive-ai)", "Optionnel pour optimisation continue (pip install olive-ai)")

def check_models():
    section("5. Modeles d'IA Embarques (models/)")
    
    root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    models_dir = os.path.join(root_dir, "models")
    
    if not os.path.exists(models_dir):
        log_fail("Dossier models/", f"Introuvable a l'emplacement : {models_dir}")
        return
    else:
        log_ok("Dossier models/", f"Present : {models_dir}")

    expected_items = [
        ("yolov8s-world_openvino_model", "Modele OpenVINO Vision YOLOv8-World", True),
        ("yolov8n-seg_openvino_model", "Modele OpenVINO Gomme Magique YOLOv8-Seg", True),
        ("whisper-tiny-fp16-ov", "Modele Whisper Tiny OpenVINO (Transcription rapide)", True),
        ("whisper-base-fp16-ov", "Modele Whisper Base OpenVINO (Transcription haute precision)", True),
        ("speaker", "Modele Biometrie Vocale Locuteur", True),
        ("lcm_dreamshaper_8bit", "Modele Diffusion d'Images LCM Dreamshaper", False),
        ("npu_cache", "Cache compilation NPU Intel", False),
        ("yolov8s-world.onnx", "Modele ONNX Source Vision", False),
        ("yolov8n-seg.onnx", "Modele ONNX Source Gomme Magique", False)
    ]

    for item, desc, is_critical in expected_items:
        full_path = os.path.join(models_dir, item)
        if os.path.exists(full_path):
            size_mb = 0
            if os.path.isfile(full_path):
                size_mb = os.path.getsize(full_path) / (1024 * 1024)
            else:
                for r, _, f in os.walk(full_path):
                    for file in f:
                        fp = os.path.join(r, file)
                        if os.path.isfile(fp):
                            size_mb += os.path.getsize(fp) / (1024 * 1024)
            log_ok(desc, f"{item} ({size_mb:.1f} Mo)")
        else:
            if is_critical:
                log_fail(desc, f"Fichier/Dossier manquant : models/{item}")
            else:
                log_warn(desc, f"Optionnel / non present : models/{item}")

def check_peripherals():
    section("6. Peripheriques d'Entree (Camera & Microphone)")
    
    # Sounddevice check
    try:
        import sounddevice as sd
        devices = sd.query_devices()
        input_devs = [d for d in devices if d.get("max_input_channels", 0) > 0]
        if input_devs:
            default_in = sd.query_devices(kind='input')
            log_ok("Microphone Audio", f"{default_in.get('name', 'Microphone')} ({len(input_devs)} peripherique(s) detecte(s))")
        else:
            log_warn("Microphone Audio", "Aucun peripherique d'entree audio detecte")
    except Exception as e:
        log_warn("Microphone Audio", f"Erreur de detection: {e}")

    # Webcam check
    try:
        import cv2
        cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)
        if cap.isOpened():
            ret, _ = cap.read()
            cap.release()
            if ret:
                log_ok("Webcam / Camera", "Flux video operationnel (Index 0)")
            else:
                log_ok("Webcam / Camera", "Peripherique connecte (Index 0)")
        else:
            log_warn("Webcam / Camera", "Aucune camera detectee a l'index 0 ou camera occupee")
    except Exception as e:
        log_warn("Webcam / Camera", f"Erreur de detection: {e}")

def check_app_binaries():
    section("7. Application Copilot+ PC Demo (DemoHub.exe)")
    
    root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    release_exe = os.path.join(root_dir, "DemoHub", "bin", "Release", "net8.0-windows10.0.18362", "DemoHub.exe")
    debug_exe = os.path.join(root_dir, "DemoHub", "bin", "Debug", "net8.0-windows10.0.18362", "DemoHub.exe")
    publish_exe = os.path.join(root_dir, "bin", "Publish", "x64", "DemoHub.exe")

    found_any = False
    if os.path.exists(release_exe):
        log_ok("Binaire Release (Recommande)", release_exe)
        found_any = True
    if os.path.exists(debug_exe):
        log_ok("Binaire Debug", debug_exe)
        found_any = True
    if os.path.exists(publish_exe):
        log_ok("Binaire Publie Autonome", publish_exe)
        found_any = True

    if not found_any:
        log_warn("Application non compilee", "Lancez Compiler_DemoHub.bat ou 'dotnet build DemoHub\\DemoHub.csproj -c Release'")

def main():
    print(f"\n{BOLD}======================================================================{RESET}")
    print(f"{BOLD}       Copilot+ PC Demo - Diagnostic Complet de l'Installation{RESET}")
    print(f"{BOLD}======================================================================{RESET}")
    
    check_system()
    check_runtimes()
    check_hardware_ai()
    check_python_packages()
    check_models()
    check_peripherals()
    check_app_binaries()

    print(f"\n{BOLD}======================================================================{RESET}")
    print(f"Bilan du diagnostic : {GREEN}{passed_checks} Reussis{RESET} | {YELLOW}{warning_checks} Avertissements{RESET} | {RED}{failed_checks} Erreurs{RESET}")
    print(f"{BOLD}======================================================================{RESET}")

    if failed_checks == 0:
        if warning_checks == 0:
            print(f"\n{GREEN}{BOLD}[PARFAIT] L'environnement est 100% pret pour toutes les demonstrations Copilot+ PC !{RESET}\n")
        else:
            print(f"\n{YELLOW}{BOLD}[FONCTIONNEL] L'environnement est operationnel avec quelques avertissements mineurs.{RESET}\n")
        return 0
    else:
        print(f"\n{RED}{BOLD}[ATTENTION] {failed_checks} composant(s) essentiel(s) necessitent votre attention.{RESET}")
        print("Veuillez consulter les erreurs ci-dessus ou executer 'Installer_Prerequis.bat'.\n")
        return 1

if __name__ == "__main__":
    sys.exit(main())
