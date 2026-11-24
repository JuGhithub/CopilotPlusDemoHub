# -*- coding: utf-8 -*-
"""
demohub_common.devices
Contrat d'évaluation matérielle et détection des accélérateurs IA (NPU / GPU / CPU).
Exploite l'introspection directe des propriétés de compilation OpenVINO (EXECUTION_DEVICES).
"""
from typing import List, Optional, Dict, Any


def detect_available_devices() -> List[str]:
    """Détecte les accélérateurs d'inférence disponibles via OpenVINO Core."""
    try:
        import openvino as ov
        core = ov.Core()
        return list(core.available_devices)
    except Exception:
        return ["CPU"]


def extract_compiled_execution_devices(compiled_model: Any) -> List[str]:
    """
    Extrait les périphériques d'exécution réels à partir du CompiledModel OpenVINO.
    """
    try:
        exec_devs = compiled_model.get_property("EXECUTION_DEVICES")
        if isinstance(exec_devs, str):
            return [exec_devs]
        elif isinstance(exec_devs, (list, tuple)):
            return [str(d) for d in exec_devs]
        return [str(exec_devs)]
    except Exception:
        return []


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
    available_devices: Optional[List[str]] = None,
    compiled_model: Optional[Any] = None,
    evidence_level: Optional[str] = None,
    evidence_sources: Optional[List[str]] = None
) -> Dict[str, Any]:
    """
    Construit un dictionnaire RuntimeEvidence normalisé pour l'application C# DemoHub.
    """
    if available_devices is None:
        available_devices = detect_available_devices()

    sources = list(evidence_sources or [])
    runtime_reported_device = ""

    if compiled_model is not None:
        reported = extract_compiled_execution_devices(compiled_model)
        if reported:
            runtime_reported_device = ", ".join(reported)
            sources.append("OpenVINO compiled_model EXECUTION_DEVICES")
            if not evidence_level:
                evidence_level = "Verified"

    if not runtime_reported_device:
        runtime_reported_device = active_device

    if not evidence_level:
        if "NPU" in active_device.upper() or "GPU" in active_device.upper():
            evidence_level = "Verified"
            sources.append("Runtime API confirmation")
        else:
            evidence_level = "Inferred"
            sources.append("Device string inspection")

    req_upper = requested_device.upper()
    act_upper = active_device.upper()
    fallback_used = (req_upper in ("NPU", "GPU")) and (act_upper != req_upper and not act_upper.startswith(req_upper))

    runtime_version = ""
    if runtime.lower().startswith("openvino"):
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
        "runtime_reported_device": runtime_reported_device,
        "execution_provider": execution_provider,
        "fallback_used": fallback_used,
        "evidence_level": evidence_level,
        "evidence_sources": sources,
        "compile_time_ms": round(compile_time_ms, 2),
        "warmup_time_ms": round(warmup_time_ms, 2),
        "inference_ms": round(inference_ms, 2),
        "end_to_end_ms": round(end_to_end_ms, 2)
    }
