# -*- coding: utf-8 -*-
"""
Tests unitaires pour les services IA Python de CopilotPlusDemoHub.
Vérifie la robustesse, la sécurité cryptographique DPAPI, et la normalisation linguistique.
"""
import json
import os
import sys
import pytest

# Assure l'inclusion de la racine du dépôt pour importer les modules de services
ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from speaker_service import tpm_seal_data, tpm_unseal_data, VAULT_HEADER
from lang_utils import clean_repetitions, score_languages


class TestDpapiBiometricVault:
    """Vérifie la cryptographie du coffre-fort biométrique Windows DPAPI."""

    def test_dpapi_sealing_roundtrip(self):
        """Vérifie que des données scellées avec DPAPI peuvent être déchiffrées à l'identique."""
        payload = {"speaker": "Julien_Test", "embedding": [0.123, -0.456, 0.789]}
        raw_bytes = json.dumps(payload).encode("utf-8")

        sealed = tpm_seal_data(raw_bytes)
        assert sealed.startswith(VAULT_HEADER)
        assert len(sealed) > len(raw_bytes)
        assert raw_bytes not in sealed  # Les données en clair ne doivent pas apparaître

        unsealed = tpm_unseal_data(sealed)
        assert unsealed == raw_bytes
        restored = json.loads(unsealed.decode("utf-8"))
        assert restored["speaker"] == "Julien_Test"
        assert len(restored["embedding"]) == 3

    def test_dpapi_empty_data(self):
        """Vérifie le comportement de repli sur payload vide."""
        assert tpm_unseal_data(b"") == b"{}"

    def test_dpapi_tamper_detection(self):
        """Vérifie qu'un payload scellé altéré/corrompu échoue au déchiffrement."""
        payload = b'{"secret": "confidential_voiceprint"}'
        sealed = bytearray(tpm_seal_data(payload))

        # Altère quelques octets dans le corps chiffré (après l'en-tête)
        if len(sealed) > len(VAULT_HEADER) + 8:
            sealed[-4] ^= 0xFF
            sealed[-3] ^= 0xAA

            with pytest.raises(Exception):
                tpm_unseal_data(bytes(sealed))


class TestLangUtils:
    """Vérifie la détection linguistique et le filtrage des répétitions acoustiques."""

    def test_clean_repetitions_simple(self):
        """Supprime les répétitions simples de mots."""
        noisy = "Merci merci merci pour cette présentation"
        cleaned = clean_repetitions(noisy)
        assert "merci merci merci" not in cleaned.lower()
        assert "présentation" in cleaned

    def test_clean_repetitions_phrase_loop(self):
        """Supprime les boucles d'hallucination Whisper multi-mots."""
        noisy = "And it needs a little bit of a little bit of a little bit of"
        cleaned = clean_repetitions(noisy)
        assert cleaned.count("a little bit") == 1

    def test_clean_repetitions_clean_text_unchanged(self):
        """Vérifie qu'un texte normal n'est pas altéré."""
        clean = "Bonjour à tous, bienvenue dans ce briefing technique."
        assert clean_repetitions(clean) == clean

    def test_score_languages_french(self):
        """Vérifie la classification d'un texte français avec accents et élisions."""
        text_fr = "C'est une excellente journée pour démontrer l'accélération matérielle sur le NPU."
        scores = score_languages(text_fr)
        assert scores["fr"] > scores["en"]
        assert scores["fr"] > scores["es"]

    def test_score_languages_english(self):
        """Vérifie la classification d'un texte anglais avec contractions et syntaxe spécifique."""
        text_en = "Don't forget that we're testing the hardware inference speed on the neural processing unit."
        scores = score_languages(text_en)
        assert scores["en"] > scores["fr"]
        assert scores["en"] > scores["de"]


class TestPdfOcrServiceContract:
    """Vérifie le contrat de service et la conformité d'exécution de pdf_ocr_service."""

    def test_find_file_helper(self):
        from pdf_ocr_service import find_file
        # Le fichier README.md ou GUIDE_INSTALLATION.md doit être localisé via l'arborescence
        readme_path = find_file("README.md")
        assert os.path.exists(readme_path)


class TestPdfRedactionSecurity:
    """Vérifie que le caviardage PDF est destructif et irréversible (pas de données résiduelles)."""

    def test_irreversible_pdf_redaction(self, tmp_path):
        import fitz  # PyMuPDF

        # 1. Créer un document contenant des identifiants sensibles
        doc = fitz.open()
        page = doc.new_page(width=595, height=842)
        sensitive_iban = "FR7630006000011234567890189"
        sensitive_name = "Jean DUPONT"
        page.insert_text((50, 100), f"Titulaire : {sensitive_name} - IBAN : {sensitive_iban}", fontsize=12)

        test_pdf = tmp_path / "sensitive.pdf"
        doc.save(str(test_pdf))
        doc.close()

        # 2. Caviardage destructif
        doc_to_redact = fitz.open(str(test_pdf))
        p = doc_to_redact[0]
        for term in (sensitive_iban, sensitive_name):
            for rect in p.search_for(term):
                p.add_redact_annot(rect, fill=(0, 0, 0))
        p.apply_redactions()

        redacted_pdf = tmp_path / "redacted.pdf"
        doc_to_redact.save(str(redacted_pdf))
        doc_to_redact.close()

        # 3. Réouverture et contrôle de non-rémanence
        doc_audited = fitz.open(str(redacted_pdf))
        text = doc_audited[0].get_text()

        # A. Absent du texte extrait
        assert sensitive_iban not in text
        assert sensitive_name not in text
        assert "Titulaire" in text

        # B. Absent des métadonnées et annotations
        for annot in (doc_audited[0].annots() or []):
            assert sensitive_iban not in annot.info.get("content", "")

        # C. Absent du flux binaire brut du fichier
        with open(str(redacted_pdf), "rb") as f:
            raw = f.read()
            assert sensitive_iban.encode("utf-8") not in raw
            assert sensitive_name.encode("utf-8") not in raw

        doc_audited.close()

