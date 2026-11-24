# -*- coding: utf-8 -*-
"""
Tests unitaires pour demohub_common.protocol et demohub_common.devices.
"""
import io
import json
import os
import sys
import pytest

ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from demohub_common.protocol import (
    emit_ready,
    emit_progress,
    emit_result,
    emit_error,
    log_diagnostic,
    PROTOCOL_VERSION,
)
from demohub_common.devices import build_runtime_evidence, detect_available_devices


class TestProtocolEmitter:
    """Vérifie l'émission conforme des messages JSON Lines demohub/1.0."""

    def test_emit_ready(self, monkeypatch):
        fake_stdout = io.StringIO()
        monkeypatch.setattr(sys, "stdout", fake_stdout)

        emit_ready("ocr_service", "ready")
        output = fake_stdout.getvalue().strip()

        assert output.startswith("{") and output.endswith("}")
        parsed = json.loads(output)
        assert parsed["protocol"] == PROTOCOL_VERSION
        assert parsed["type"] == "ready"
        assert parsed["service"] == "ocr_service"
        assert parsed["status"] == "ready"
        assert "timestamp" in parsed

    def test_emit_progress(self, monkeypatch):
        fake_stdout = io.StringIO()
        monkeypatch.setattr(sys, "stdout", fake_stdout)

        emit_progress(0.85, "Compilation du modèle")
        parsed = json.loads(fake_stdout.getvalue().strip())

        assert parsed["type"] == "progress"
        assert parsed["progress"] == 0.85
        assert parsed["message"] == "Compilation du modèle"

    def test_emit_progress_clamping(self, monkeypatch):
        fake_stdout = io.StringIO()
        monkeypatch.setattr(sys, "stdout", fake_stdout)

        emit_progress(1.5, "Débordement")
        parsed = json.loads(fake_stdout.getvalue().strip())
        assert parsed["progress"] == 1.0

        fake_stdout.seek(0)
        fake_stdout.truncate(0)

        emit_progress(-0.5, "Sous-débordement")
        parsed = json.loads(fake_stdout.getvalue().strip())
        assert parsed["progress"] == 0.0

    def test_emit_result(self, monkeypatch):
        fake_stdout = io.StringIO()
        monkeypatch.setattr(sys, "stdout", fake_stdout)

        data_payload = {"count": 4, "labels": ["cat", "dog"]}
        emit_result(data_payload)
        parsed = json.loads(fake_stdout.getvalue().strip())

        assert parsed["type"] == "result"
        assert parsed["data"] == data_payload

    def test_emit_error(self, monkeypatch):
        fake_stdout = io.StringIO()
        monkeypatch.setattr(sys, "stdout", fake_stdout)

        emit_error("Périphérique non disponible", code="ERR_NO_DEVICE")
        parsed = json.loads(fake_stdout.getvalue().strip())

        assert parsed["type"] == "error"
        assert parsed["error"] == "Périphérique non disponible"
        assert parsed["code"] == "ERR_NO_DEVICE"

    def test_log_diagnostic_goes_to_stderr(self, monkeypatch):
        fake_stdout = io.StringIO()
        fake_stderr = io.StringIO()
        monkeypatch.setattr(sys, "stdout", fake_stdout)
        monkeypatch.setattr(sys, "stderr", fake_stderr)

        log_diagnostic("Chargement du cache OpenVINO")

        # Stdout doit rester parfaitement vide
        assert fake_stdout.getvalue() == ""
        # Stderr doit contenir le log préfixé
        assert "[demohub] Chargement du cache OpenVINO" in fake_stderr.getvalue()


class TestDevicesEvidence:
    """Vérifie la normalisation du contrat RuntimeEvidence."""

    def test_build_runtime_evidence_nominal_npu(self):
        ev = build_runtime_evidence(
            runtime="OpenVINO",
            model="yolov8n-seg",
            precision="FP16",
            requested_device="NPU",
            active_device="NPU.0",
            compile_time_ms=1200.5,
            warmup_time_ms=250.0,
            inference_ms=45.2,
            end_to_end_ms=62.8,
            available_devices=["CPU", "GPU", "NPU.0"]
        )

        assert ev["runtime"] == "OpenVINO"
        assert ev["model"] == "yolov8n-seg"
        assert ev["requested_device"] == "NPU"
        assert ev["active_device"] == "NPU.0"
        assert ev["fallback_used"] is False
        assert ev["inference_ms"] == 45.2

    def test_build_runtime_evidence_fallback_detection(self):
        ev = build_runtime_evidence(
            runtime="OpenVINO",
            model="phi-4",
            precision="INT4",
            requested_device="NPU",
            active_device="CPU",
            available_devices=["CPU"]
        )

        assert ev["requested_device"] == "NPU"
        assert ev["active_device"] == "CPU"
        assert ev["fallback_used"] is True
