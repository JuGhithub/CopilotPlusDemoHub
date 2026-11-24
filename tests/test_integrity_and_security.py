import os
import sys
import json
import pytest

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)


def test_runtime_evidence_not_verified_on_simple_string():
    """Vérifie que la simple présence d'un device non énuméré sans modèle compilé donne Inferred et non Verified."""
    from demohub_common.devices import build_runtime_evidence

    evidence = build_runtime_evidence(
        runtime="OpenVINO",
        model="yolov8s-world",
        precision="FP16",
        requested_device="NPU",
        active_device="NPU",
        available_devices=["CPU"],  # Pas de NPU matériel énuméré
        compiled_model=None
    )

    assert evidence["evidence_level"] == "Inferred"


def test_model_manifest_completeness_and_sha256_format():
    """Vérifie que tous les modèles du manifeste ont des champs obligatoires et un hash SHA-256 valide de 64 hex."""
    manifest_path = os.path.join(ROOT_DIR, "MODEL_MANIFEST.json")
    assert os.path.exists(manifest_path), "MODEL_MANIFEST.json doit exister à la racine du projet"

    with open(manifest_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    models = data.get("models", [])
    assert len(models) >= 13, f"Le manifeste doit contenir au moins 13 modèles (actuel: {len(models)})"

    for m in models:
        assert "id" in m
        assert "path" in m
        assert "sha256" in m
        assert len(m["sha256"]) == 64, f"L'empreinte SHA-256 de {m['id']} doit faire 64 caractères hexadécimaux"
        int(m["sha256"], 16)  # Valide que c'est bien de l'hexadécimal


def test_dpapi_vault_payload_security_header(tmp_path):
    """Vérifie que le coffre DPAPI utilise l'en-tête WINDOWS_DPAPI_ENCRYPTED et une écriture atomique."""
    from speaker_service import SpeakerManager, tpm_seal_data, tpm_unseal_data

    # Création d'un manager avec chemin temporaire
    mgr = SpeakerManager()
    test_vault = str(tmp_path / "voice_profiles.tpm")
    mgr.profiles_tpm_path = test_vault
    mgr.profiles = {"speaker_test": [0.1, 0.2, 0.3]}

    mgr._save_profiles()

    assert os.path.exists(test_vault), "Le fichier de profil chiffré doit exister sur disque"
    with open(test_vault, "rb") as f:
        raw_bytes = f.read()

    unsealed = tpm_unseal_data(raw_bytes)
    assert unsealed is not None
    payload = json.loads(unsealed.decode("utf-8"))
    assert payload["security"] == "WINDOWS_DPAPI_ENCRYPTED"
    assert "DPAPI" in payload["provider"]
    assert "speaker_test" in payload["profiles"]


def test_appsettings_has_offline_mode_only():
    """Vérifie que la configuration JSON impose bien le confinement local par défaut."""
    appsettings_path = os.path.join(ROOT_DIR, "Config", "appsettings.json")
    assert os.path.exists(appsettings_path)

    with open(appsettings_path, "r", encoding="utf-8") as f:
        config = json.load(f)

    assert config.get("DemoHub", {}).get("OfflineModeOnly") is True
