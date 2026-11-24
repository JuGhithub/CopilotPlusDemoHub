# -*- coding: utf-8 -*-
"""
demohub_common.devices
Contrat d'évaluation matérielle et détection des accélérateurs IA (NPU / GPU / CPU).
"""
from typing import List, Optional, Dict, Any


def detect_available_devices() -> List[str]:
    """Détecte les accélérateurs d'inférence disponibles via OpenVINO Core."""
    try:
        import openvino as ov
        core = ov.Core()
        return list(core.available_devices)
    except Exception:
        # Repli standard si OpenVINO n'est pas initialisé ou en mode simulation
        return ["CPU"]


def build_runtime_evidence(
    runtime: str,
    model: str,
    precision: str,
    requested_device: str,
    active_device: str,
    compile_time_ms: float = 0.0,
    warmup_time_ms: float = 0.0,
    inference_ms: float = 0.0,
    end_to_end_ms: float = 0.0,
    execution_provider: str = "OpenVINO",
    available_devices: Optional[List[str]] = None
) -> Dict[str, Any]:
    """
    Construit un dictionnaire RuntimeEvidence normalisé pour l'application C# DemoHub.
    """
    if available_devices is None:
        available_devices = detect_available_devices()

    # Détermine si un mécanisme de repli (fallback) a été déclenché
    req_upper = requested_device.upper()
    act_upper = active_device.upper()
    fallback_used = (req_upper in ("NPU", "GPU")) and (act_upper != req_upper and not act_upper.startswith(req_upper))

    runtime_version = ""
    if runtime.lower() == "openvino":
        try:
            import openvino as ov
            runtime_version = ov.__version__
        except Exception:
            runtime_version = "unknown"

    return {
        "runtime": runtime,
        "runtime_version": runtime_version,
        "model": model,
        "precision": precision,
        "requested_device": requested_device,
        "available_devices": available_devices,
        "active_device": active_device,
        "execution_provider": execution_provider,
        "fallback_used": fallback_used,
        "compile_time_ms": round(compile_time_ms, 2),
        "warmup_time_ms": round(warmup_time_ms, 2),
        "inference_ms": round(inference_ms, 2),
        "end_to_end_ms": round(end_to_end_ms, 2)
    }
