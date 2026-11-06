# 🚀 Complete Installation Guide on Another PC — Copilot+ PC Demo (Multi-OEM)

🌐 **Language / Langue** : **[ 🇬🇧 English Installation Guide (Active) ](INSTALLATION_GUIDE.md)** | **[ 🇫🇷 Guide d'Installation (FR) ](GUIDE_INSTALLATION.md)**  
📖 **Main Documentation** : **[ 🇬🇧 README in English ](README.md)** | **[ 🇫🇷 README en Français ](README.fr.md)**

This guide covers all hardware requirements, software runtimes, local AI models, and device drivers needed to install and run the **Copilot+ PC Demo** on any Copilot+ PC (**Intel**, **AMD**, or **Qualcomm Snapdragon**) in 100% autonomous, on-device mode.

---

## 1. 💻 Hardware Requirements & Supported Silicon

The application embeds automatic hardware detection (`SiliconDetector.cs`) and natively supports every Copilot+ PC architecture:

| Manufacturer | Processor | Dedicated NPU (40+ TOPS) | GPU & Acceleration | Recommended Inference Engine |
| :--- | :--- | :--- | :--- | :--- |
| **Intel** | Core Ultra Series 2 (Lunar Lake 258V/268V/288V) or Series 1 | **Intel AI Boost (48 TOPS)** | Intel Arc Graphics (140V Xe2) | Native OpenVINO IR + DirectML |
| **AMD** | Ryzen AI 300 Series (e.g. AI 9 HX 370 / Strix Point) | **AMD Ryzen AI / XDNA 2 (50 TOPS)** | AMD Radeon 890M / 880M | ONNX DirectML + Olive FP16 |
| **Qualcomm** | Snapdragon X Elite / Plus (X1E-80, X1P / ARM64) | **Qualcomm Hexagon (45 TOPS)** | Qualcomm Adreno GPU | ONNX DirectML / QNN + Olive FP16 |
| **Universal** | Any Windows 11 64-bit PC | Multi-threaded CPU emulation | DirectX 12 compatible GPU | AVX-512 / ARM64 CPU Fallback |

- **RAM**: **32 GB LPDDR5X** recommended (16 GB minimum).
- **Storage**: **NVMe SSD with 15 to 20 GB free space** to store local AI weights.
- **Peripherals**: Integrated or USB Webcam + Microphone.
- **Operating System**: **Windows 11 64-bit version 24H2 (Build 26100+)**.

> 💡 **Recommended OEM Drivers**:
> - **Intel PCs**: Install the latest Intel NPU VPUX driver and Intel Arc GPU driver via the [Intel Driver & Support Assistant (IDSA)](https://www.intel.com/content/www/us/en/support/detect.html).
> - **AMD PCs**: Install AMD Adrenalin drivers and the AMD NPU IPU driver via the [AMD Software Installer](https://www.amd.com/en/support/download/drivers.html).
> - **Qualcomm PCs**: Make sure all Windows Update firmware updates from your manufacturer (Surface, Dell, Lenovo, ASUS...) are applied for the Hexagon NPU and Adreno GPU.
> - **Universal fallback**: If no NPU is enabled, Demo Hub automatically routes to DirectML GPU then host CPU without crashes.

---

## 2. ⚡ Method 1: 1-Click Automated Installation (Recommended)

When cloning the repository or copying the project folder to a new PC:

1. Open the project folder in a **PowerShell** terminal (or Windows Explorer).
2. Run the installation PowerShell script:
   ```powershell
   .\Installer_Prerequis.ps1
   ```
   *(Or double-click `Installer_Prerequis.bat`)*
3. The script executes all steps sequentially and unattended:
   - Enables Windows Long Paths (`LongPathsEnabled`) for PyTorch and OpenVINO.
   - Installs **.NET 8.0 SDK** (via `winget`).
   - Installs **Python 3.12** (via `winget`) with dynamic `PATH` refresh.
   - Upgrades `pip` and installs all AI dependencies from `requirements.txt` (OpenVINO, PyMuPDF, Torch, YOLO, Olive, etc.).
   - Installs **Microsoft Foundry Local CLI** (via `winget`).
   - Downloads and caches **Phi-4-mini** model variants (NPU, GPU, CPU).
   - **Automatically downloads 100% of local AI models** into `models/` via [`scripts/download_models.py`](scripts/download_models.py): Whisper (STT Tiny & Base OpenVINO), VoxCeleb Voice Biometrics (ONNX), YOLOv8s-World Vision, YOLOv8n-Seg Segmentation, and Avatar LCM DreamShaper.
   - Compiles the WPF `DemoHub.exe` application in Release mode.
   - **Executes the automatic health diagnostic** via `scripts\verify_installation.py`.
4. Once completed, launch the showcase:
   ```powershell
   .\Lancer_DemoHub.ps1
   ```
   *(Or double-click `Lancer_DemoHub.bat`)*

---

## 3. 🔍 Automated Diagnostic & Verification Script

The repository includes a comprehensive 39-point diagnostic script: [`scripts/verify_installation.py`](scripts/verify_installation.py).

Run it at any time in your terminal:
```powershell
python scripts\verify_installation.py
```

### What the diagnostic audits:
1. **OS & Windows Settings**: Windows 11 version, `LongPathsEnabled` registry key status.
2. **Runtimes**: Exact detection of Python (>= 3.10), .NET SDK 8.0, and Microsoft Foundry Local.
3. **AI Hardware Engines**: Hardware availability of OpenVINO (Intel AI Boost NPU, Arc GPU, CPU) and DirectML.
4. **Python Packages**: Import verification of all mandatory libraries.
5. **Local Models (`models/`)**: Presence, integrity, and sizes of all model weight files.
6. **Multimedia Devices**: Default microphone check and webcam stream readiness.
7. **Compiled Binaries**: Status of executables (`bin/Release`, `bin/Publish`, `bin/Debug`).

---

## 4. 🛠️ Method 2: Step-by-Step Manual Installation

If you prefer to install each component manually:

### Step 1: System Runtimes (via Winget)

Open a **PowerShell** terminal:

```powershell
# 1. .NET 8.0 SDK (C# WPF Application)
winget install Microsoft.DotNet.SDK.8

# 2. Python 3.12 (Vision, audio, OCR microservices)
winget install Python.Python.3.12

# 3. Microsoft Foundry Local CLI (Official local SLM runtime)
winget install Microsoft.FoundryLocal
```

---

### Step 2: Python Dependencies (AI & Signal Processing)

From the project root directory, run:

```powershell
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

#### Key packages list:
- `openvino`, `openvino-genai`, `openvino-tokenizers`: Ultra-low latency inference on Intel AI Boost NPU and Arc GPU.
- `ultralytics`: YOLOv8s-World (open-vocabulary vision) and YOLOv8n-Seg (instance segmentation).
- `torch` & `torchvision`: Local tensor computing backend.
- `opencv-python` & `pillow`: Real-time image processing and camera frame grabbing.
- `sounddevice` & `soundfile`: Real-time microphone audio capture and playback.
- `pymupdf`: High-performance PDF vector text manipulation and redaction.
- `rapidocr-onnxruntime`: On-device neural OCR engine for scanned paper documents.
- `onnxruntime-directml`: DirectX 12 DirectML accelerated ONNX engine.
- `olive-ai`: Microsoft Olive model compilation and graph fusion framework.
- `transformers` & `scipy`: NLP processing and acoustic feature vectors.

---

### Step 3: Download Microsoft Foundry Local LLM Models

These models are managed by the local Microsoft service (`foundry`):

```powershell
# NPU Variant (Compiled for Intel AI Boost via OpenVINO - 2.2 GB)
foundry model download phi-4-mini-instruct-openvino-npu

# GPU Variant (Optimized for Intel Arc iGPU via OpenVINO - 2.2 GB)
foundry model download phi-4-mini-instruct-openvino-gpu

# CPU Variant (Universal Fallback - 4.8 GB)
foundry model download Phi-4-mini-instruct-generic-cpu
```

Verify that models are cached:
```powershell
foundry status
```

---

### Step 4: Local Neural Models in the Project

Download all on-device vision, audio, and avatar models using the automated script:
```powershell
python scripts\download_models.py
```

The [`models/`](models/) directory will be populated with:
1. **`models/yolov8s-world_openvino_model/`**: Real-time zero-shot computer vision (cups, badges, headsets, glasses, people).
2. **`models/yolov8n-seg_openvino_model/`**: Spatial instance segmentation for Magic Eraser.
3. **`models/whisper-tiny-fp16-ov/` & `models/whisper-base-fp16-ov/`**: Bilingual Speech-to-Text in French & English.
4. **`models/speaker/`**: Speaker voiceprint biometric verification neural network.
5. **`models/lcm_dreamshaper_8bit/`**: Fast latent consistency diffusion model for Avatar Studio.
6. **`models/npu_cache/`**: Pre-compiled Intel NPU binary cache for instant cold starts.

---

### Step 5: Build and Run

Compile the solution using [`Compiler_DemoHub.bat`](Compiler_DemoHub.bat) or via command line:

```powershell
# High-performance Release build (Recommended)
dotnet build DemoHub\DemoHub.csproj -c Release

# Full standalone publication (x64)
dotnet publish DemoHub\DemoHub.csproj -c Release -r win-x64 --no-self-contained -o bin\Publish\x64
```

Then start the demo:
- With the optimized launcher: [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat) *(initializes Foundry daemon and pre-warms NPU/GPU hardware)*.
- Or directly: `dotnet run --project DemoHub\DemoHub.csproj -c Release`

---

## 5. 🧰 Common Troubleshooting & Error Resolution

### 1. Windows Long Paths Disabled
- **Symptom**: `FileNotFoundError` or truncated paths when installing PyTorch or OpenVINO.
- **Fix**: Open PowerShell as Administrator and run:
  ```powershell
  Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1
  ```

### 2. `foundry` Command Not Recognized
- **Cause**: `winget` installs the executable in `%LOCALAPPDATA%\Microsoft\WindowsApps`.
- **Fix**: Restart your terminal session, or use [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat) which sets the `PATH` automatically.

### 3. NPU Stays at 0% in Task Manager
- **Cause**: Intel NPU compute driver is missing or Windows is not on version 24H2.
- **Fix**: Open *Device Manager > Compute Devices* (or *Intel(R) AI Boost*), right click > *Update Driver*. Also verify that the NPU tab is visible in *Task Manager > Performance*.

### 4. Webcam Fails to Open in Access Control
- **Cause**: Windows camera permission disabled or camera in use by Teams/Zoom.
- **Fix**: Navigate to *Windows Settings > Privacy & security > Camera* and ensure desktop app access is turned ON. Close any active conferencing apps.

### 5. "Error Code 1" in Avatar Studio
- **Cause**: The LCM DreamShaper model is not yet downloaded in `models/lcm_dreamshaper_8bit` (excluded from git due to size > 1 GB), or `openvino-genai` is not installed.
- **Fix**: Run `python scripts\download_models.py` then `python -m pip install openvino-genai`.

---

## 6. 📦 USB Flash Drive Deployment Summary

To transport the demo to an offline PC using a USB drive:
1. Copy the **entire `CopilotPlusDemoHub` folder** (which already contains source code, models in `models/`, sample files in `Data/`, and `.bat` wrappers).
2. On the target PC, insert the drive and copy the folder onto the internal SSD (e.g. `C:\Demos\CopilotPlusDemo`).
3. Double-click `Installer_Prerequis.bat` (Run as Administrator recommended).
4. Double-click `Lancer_DemoHub.bat`.
