# -*- coding: utf-8 -*-
"""
demohub_common - Module utilitaire partagé pour les micro-services CopilotPlusDemoHub.
Fournit le protocole de communication JSON Lines IPC et l'évaluation matérielle.
"""
from .protocol import (
    emit_ready,
    emit_progress,
    emit_result,
    emit_error,
    log_diagnostic,
    PROTOCOL_VERSION,
)
from .devices import build_runtime_evidence, detect_available_devices

__all__ = [
    "emit_ready",
    "emit_progress",
    "emit_result",
    "emit_error",
    "log_diagnostic",
    "PROTOCOL_VERSION",
    "build_runtime_evidence",
    "detect_available_devices",
]
