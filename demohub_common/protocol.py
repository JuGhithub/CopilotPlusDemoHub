# -*- coding: utf-8 -*-
"""
demohub_common.protocol
Gestionnaire des flux IPC JSON Lines (demohub/1.0).
Garantit que stdout est réservé aux payloads structurés et stderr aux logs.
"""
import json
import sys
import time
from typing import Any, Optional

PROTOCOL_VERSION = "demohub/1.0"


def _emit_json(payload: dict) -> None:
    """Émet un message JSON sur stdout avec terminaison newline et vidage du tampon."""
    if "protocol" not in payload:
        payload["protocol"] = PROTOCOL_VERSION
    if "timestamp" not in payload:
        payload["timestamp"] = time.time()

    line = json.dumps(payload, ensure_ascii=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def emit_ready(service: str, status: str = "ready") -> None:
    """Notifie l'application hôte que le service est initialisé et prêt."""
    _emit_json({
        "type": "ready",
        "service": service,
        "status": status
    })


def emit_progress(progress: float, message: str = "") -> None:
    """Émet une progression d'inférence ou de calcul (valeur entre 0.0 et 1.0)."""
    _emit_json({
        "type": "progress",
        "progress": max(0.0, min(1.0, float(progress))),
        "message": message
    })


def emit_result(data: Any) -> None:
    """Émet le résultat final de l'exécution."""
    _emit_json({
        "type": "result",
        "data": data
    })


def emit_error(error: str, code: Optional[str] = None, traceback: Optional[str] = None) -> None:
    """Émet un signal d'erreur structuré vers l'application hôte."""
    payload = {
        "type": "error",
        "error": str(error)
    }
    if code:
        payload["code"] = code
    if traceback:
        payload["traceback"] = traceback
    _emit_json(payload)


def log_diagnostic(message: str) -> None:
    """Écrit une ligne de log diagnostique sur stderr sans interférer avec le canal JSON."""
    sys.stderr.write(f"[demohub] {message}\n")
    sys.stderr.flush()
