# 🚀 Complete Installation Guide on Another PC — Copilot+ PC Demo (Multi-OEM)

🌐 **Language / Langue** : **[ 🇬🇧 English Installation Guide (Active) ](INSTALLATION_GUIDE.md)** | **[ 🇫🇷 Guide d'Installation (FR) ](GUIDE_INSTALLATION.md)**  
📖 **Main Documentation** : **[ 🇬🇧 README in English ](README.md)** | **[ 🇫🇷 README en Français ](README.fr.md)**

This guide covers all hardware requirements, software runtimes, local AI models, and device drivers needed to install and run the **Copilot+ PC Demo** on any Copilot+ PC (**Intel**, **AMD**, or **Qualcomm Snapdragon**) in 100% autonomous, on-device mode.

---

## 1. 💻 Hardware Requirements & Supported Silicon

The application embeds automatic hardware detection ([`SiliconDetector.cs`](DemoHub/Core/SiliconDetector.cs)) and supports Copilot+ PC architectures:

| Manufacturer | Processor | Dedicated NPU (40+ TOPS) | GPU & Acceleration | Recommended Inference Engine |
| :--- | :--- | :--- | :--- | :--- |
| **Intel** (Reference) | Core Ultra Series 2 (Lunar Lake 258V/268V/288V) or Series 1 | **Intel AI Boost (48 TOPS)** | Intel Arc Graphics (140V Xe2) | Native OpenVINO IR (100% NPU) + DirectML |
| **AMD** (Hybrid) | Ryzen AI 300 Series (e.g. AI 9 HX 370 / Strix Point) | **AMD Ryzen AI / XDNA 2 (50 TOPS)** | AMD Radeon 890M / 880M | ONNX DirectML + Olive FP16 / CPU Fallback |
| **Qualcomm** (Experimental) | Snapdragon X Elite / Plus (X1E-80, X1P / ARM64) | **Qualcomm Hexagon (45 TOPS)** | Qualcomm Adreno GPU | ONNX DirectML / QNN + Olive FP16 / Native win-arm64 |
| **Universal** | Any Windows 11 64-bit PC | Multi-threaded CPU emulation | DirectX 12 compatible GPU | AVX-512 / ARM64 CPU Fallback |

- **RAM**: **32 GB LPDDR5X** recommended (16 GB minimum).
- **Storage**: **NVMe SSD with 15 to 20 GB free space** to store local AI weights.
- **Peripherals**: Integrated or USB Webcam + Microphone.
- **Operating System**: **Windows 11 64-bit version 24H2 (Build 26100+)**.

> 💡 **Recommended OEM Drivers**:
> - **Intel PCs**: Install the latest Intel NPU VPUX driver and Intel Arc GPU driver via the [Intel Driver & Support Assistant (IDSA)](https://www.intel.com/content/www/us/en/support/detect.html).
> - **AMD PCs**: Install AMD Adrenalin drivers and the AMD NPU IPU driver via the [AMD Software Installer](https://www.amd.com/en/support/download/drivers.html).
> - **Qualcomm PCs**: Make sure all Windows Update firmware updates from your manufacturer (Surface, Dell, Lenovo, ASUS...) are applied for the Hexagon NPU and Adreno GPU.
> - **Controlled Fallback**: If no NPU is available for a given workload, Demo Hub automatically routes to DirectML GPU then host CPU without crashes.

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
   - **Strict Python isolation**: automatically configures a local `.venv` (`python -m venv .venv --system-site-packages`).
   - Deterministic dependency installation from locked [`requirements.lock`](requirements.lock) (OpenVINO, PyMuPDF, Torch, YOLO, Olive, RapidOCR, etc.).
   - Installs **Microsoft Foundry Local CLI** (via `winget`).
   - Downloads and caches **Phi-4-mini** model variants (NPU, GPU, CPU).
   - **Downloads and verifies local AI models** into `models/` via [`scripts/download_models.py`](scripts/download_models.py): Whisper (STT Tiny & Base OpenVINO), VoxCeleb Voice Biometrics (ONNX), YOLOv8s-World Vision, YOLOv8n-Seg Segmentation, and Avatar LCM DreamShaper.
   - Verifies SHA-256 cryptographic model supply chain integrity.
   - Compiles the WPF `DemoHub.exe` application in Release mode.
   - **Executes automatic health diagnostic and live silicon smoke tests** via `scripts\verify_installation.py`.
4. Once completed, launch the showcase:
   ```powershell
   .\Lancer_DemoHub.ps1
   ```
   *(Or double-click `Lancer_DemoHub.bat`)*

---

## 3. 🔍 Diagnostic & Automated Test Suite

The project includes complete diagnostic and testing tooling:

### 1. Full Diagnostic & Live Inference Smoke Tests ([`scripts/verify_installation.py`](scripts/verify_installation.py))
```powershell
# Run diagnostics using the isolated virtual environment
.venv\Scripts\python.exe scripts\verify_installation.py
```
Validates 8 operational areas:
1. **System & Windows**: Windows 11 version, `LongPathsEnabled` registry setting.
2. **Runtimes**: Exact detection of Python (>= 3.10), .NET SDK 8.0, and Microsoft Foundry Local.
3. **AI Acceleration**: Hardware presence of OpenVINO (Intel AI Boost NPU, Arc GPU, CPU) and DirectML.
4. **Python Packages**: Importability and compatibility of all required libraries.
5. **Local Models (`models/`)**: File presence and structural integrity for all weights.
6. **Multimedia Peripherals**: Microphone activity and live webcam stream.
7. **Application Binaries**: Compiled executables (`bin/Release`, `bin/Publish`, `bin/Debug`).
8. **Live Silicon Smoke Tests**:
   - Hardware Windows DPAPI Biometric Vault sealing & unsealing.
   - Live GPU DirectML inference test.
   - Live 100% native Intel AI Boost NPU inference test.

### 2. Supply-Chain & SHA-256 Cryptographic Verification
```powershell
.venv\Scripts\python.exe scripts\download_models.py --verify-integrity
```
Calculates and verifies the SHA-256 checksum of every critical local model to detect corruption or tampering.

### 3. Automated Unit Test Execution
```powershell
# C# WPF Unit Tests (8 tests: ProcessRunner, telemetry, silicon detection)
dotnet test DemoHub.Tests\DemoHub.Tests.csproj -r win-x64

# Python Services Tests (9 tests: DPAPI vault, anti-tamper, FR/EN language filters)
.venv\Scripts\python.exe -m pytest tests/ -v
```

---

## 4. 🛠️ Method 2: Step-by-Step Manual Installation

If you prefer to install each component manually:

### Step 1: System Software Prerequisites (via Winget)
```powershell
# 1. .NET 8.0 SDK (WPF C# Application)
winget install Microsoft.DotNet.SDK.8

# 2. Python 3.12 (Vision, Audio, OCR micro-services)
winget install Python.Python.3.12

# 3. Microsoft Foundry Local CLI (Local LLM runner)
winget install Microsoft.FoundryLocal
```

---

### Step 2: Virtual Environment & Pinned Dependencies
In the repository root:
```powershell
# 1. Create virtual environment with system package access
python -m venv .venv --system-site-packages

# 2. Activate virtual environment
.\.venv\Scripts\Activate.ps1

# 3. Deterministic install using requirements.lock
python -m pip install --upgrade pip
python -m pip install -r requirements.lock
```

---

### Step 3: Download Microsoft Foundry Local LLM Models
```powershell
# NPU variant (Intel AI Boost via OpenVINO - 2.2 GB)
foundry model download phi-4-mini-instruct-openvino-npu

# GPU variant (Intel Arc via OpenVINO - 2.2 GB)
foundry model download phi-4-mini-instruct-openvino-gpu

# CPU variant (Universal fallback - 4.8 GB)
foundry model download Phi-4-mini-instruct-generic-cpu

# Check local cache status
foundry status
```

---

### Step 4: Download On-Device AI Models
```powershell
python scripts\download_models.py
```
This populates [`models/`](models/) with:
1. **`models/yolov8s-world_openvino_model/`**: Real-time object detection (cup, badge, headphones, glasses, person).
2. **`models/yolov8n-seg_openvino_model/`**: Spatial segmentation for magic eraser.
3. **`models/whisper-tiny-fp16-ov/` & `models/whisper-base-fp16-ov/`**: French & English Speech-to-Text.
4. **`models/speaker/`**: Speaker voice biometrics neural network.
5. **`models/lcm_dreamshaper_8bit/`**: Fast image diffusion model (Latent Consistency Model).
6. **`models/npu_cache/`**: Pre-compiled Intel NPU binary cache for instant cold start.

---

### Step 5: Multi-Architecture Build & Packaging

```powershell
# Release build (x64)
dotnet build DemoHub\DemoHub.csproj -c Release

# Self-contained publish for Intel / AMD (win-x64)
dotnet publish DemoHub\DemoHub.csproj -c Release -r win-x64 --no-self-contained -o bin\Publish\x64

# Native self-contained publish for Snapdragon X (win-arm64)
dotnet publish DemoHub\DemoHub.csproj -c Release -r win-arm64 --no-self-contained -o bin\Publish\arm64
```

Create release packages:
```powershell
# Package for x64 PCs (Intel Core Ultra / AMD Ryzen AI)
.\scripts\creer_package_release.ps1 -Runtime win-x64

# Package for ARM64 PCs (Qualcomm Snapdragon X Elite / Plus)
.\scripts\creer_package_release.ps1 -Runtime win-arm64
```

Then launch the showcase:
- Via launcher: [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat).
- Or directly: `dotnet run --project DemoHub\DemoHub.csproj -c Release`

---

## 5. 🧰 Troubleshooting & FAQ

### 1. Windows Long Paths are disabled
- **Symptom**: `FileNotFoundError` or truncated paths during PyTorch or OpenVINO installation.
- **Fix**: Open PowerShell as Administrator and run:
  ```powershell
  Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1
  ```

### 2. `foundry` is not recognized in command prompt
- **Cause**: `winget` installs to `%LOCALAPPDATA%\Microsoft\WindowsApps`.
- **Fix**: Reopen your terminal, or use [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat) which automatically amends the `PATH`.

### 3. NPU shows 0% in Task Manager
- **Cause**: Intel NPU driver is not loaded or workload is running on GPU/CPU.
- **Fix**: Open *Device Manager > Compute Devices > Intel(R) AI Boost*, right click > *Update driver*. Verify the NPU appears in *Task Manager > Performance*.

### 4. Webcam does not open in Access Control Demo
- **Cause**: Windows camera privacy permissions disabled or camera used by Teams/Zoom.
- **Fix**: Go to *Windows Settings > Privacy & security > Camera* and enable desktop app access. Close Teams/Zoom.

### 5. Error "Code 1" in Avatar Studio
- **Cause**: LCM DreamShaper model not present in `models/lcm_dreamshaper_8bit` or `openvino-genai` missing.
- **Fix**: Run `python scripts\download_models.py` and verify via `python scripts\download_models.py --verify-integrity`.

---

## 6. 📦 Offline USB Deployment Summary

To carry the demo to another PC via USB stick or external drive:
1. Copy the **entire `CopilotPlusDemoHub` folder** (contains source code, `models/`, sample datasets `Data/`, and scripts).
2. On target PC, copy to NVMe SSD (e.g. `C:\Demos\CopilotPlusDemo`).
3. Right-click `Installer_Prerequis.bat` > *Run as Administrator*.
4. Double-click `Lancer_DemoHub.bat`.
