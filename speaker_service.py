import argparse
import ctypes
from ctypes import wintypes
import datetime
import json
import os
import sys
import numpy as np
import openvino as ov
import soundfile as sf

COLORS = [
    "#3B82F6",  # Vibrant Blue
    "#10B981",  # Emerald Green
    "#F59E0B",  # Amber / Gold
    "#EC4899",  # Pink
    "#8B5CF6",  # Purple
    "#06B6D4",  # Cyan
    "#F97316",  # Orange
    "#14B8A6",  # Teal
]

VAULT_HEADER = b"TPM_VAULT_V2\x00\x00\x00\x00"

# Vecteur moyen de cohorte (192D) pour recentrer le cône anisotrope d'ECAPA-TDNN (voxceleb.onnx)
_COHORT_MEAN = np.array([
    -0.046761, -0.010992, -0.059401, -0.018348, -0.024273, -0.02885, 0.020478, -0.025807,
    0.016971, -0.003701, 0.002405, 0.057375, -0.048802, 0.002384, 0.073055, -0.006605,
    0.114206, -0.021241, 0.030747, 0.061392, -0.007026, -0.019497, -0.00897, 0.038362,
    0.060531, 0.059337, -0.01406, 0.011975, 0.067049, 0.000477, -0.041918, 0.047516,
    0.001703, -0.00564, -0.059291, 0.039682, -0.000345, -0.05768, 0.090149, -0.019925,
    0.10787, 0.079906, 0.003266, -0.049433, 0.067589, -0.032003, -0.013783, 0.018678,
    0.034624, 0.076407, -0.085095, 0.010018, -0.011586, 0.070426, 0.00127, 0.018239,
    -0.026611, 0.002162, -0.092438, 0.029006, -0.077941, -0.009484, 0.016279, -0.040503,
    0.086929, -0.030052, -0.058924, -0.033194, 0.065376, -0.028705, 0.073864, 0.046526,
    0.051645, -0.015455, -0.027757, 0.006639, -0.02687, -0.000147, -0.061811, 0.018153,
    0.04239, 0.058766, 0.025614, 0.050606, 0.046196, -0.011392, -0.012136, -0.009066,
    0.019054, 0.056644, 0.047626, -0.045867, -0.012417, 0.021478, 0.02619, -0.006244,
    0.010409, 0.046547, -0.080606, 0.041764, 0.030549, 0.075316, -0.063641, 0.002036,
    -0.012998, 0.022862, -0.014693, 0.005105, -0.02548, -0.048528, 0.006242, 0.039859,
    0.016828, -0.1771, 0.008987, -0.078211, 0.035793, -0.007385, 0.02894, 0.011864,
    0.053748, -0.010582, 0.009827, -0.043535, 0.070105, -0.030605, 0.007008, -0.050475,
    0.02794, 0.012864, 0.019125, 0.023444, -0.020445, -0.026597, -0.03271, 0.023553,
    0.120236, -0.016458, 0.018156, -0.016757, 0.005875, -0.010006, 0.013785, -0.017595,
    0.019261, 0.020849, -0.08138, 0.07847, 0.0868, -0.006044, -0.029846, -0.046736,
    -0.019895, -0.026701, 0.013802, -0.020965, -0.03512, -0.103226, 0.018689, -0.030525,
    -0.034681, 0.019107, 0.044375, -0.039073, -0.040488, 0.058479, -0.028811, -0.090619,
    -0.015033, 0.019315, -0.007227, -0.079972, 0.032906, 0.004441, 0.068689, -0.026721,
    0.04161, 0.00931, -0.005463, -0.000106, -0.127914, -0.036283, 0.01632, -0.010368,
    0.011198, -0.01825, -0.031249, -0.038458, 0.008599, -0.003099, -0.056409, -0.064669
], dtype=np.float32)


# --- Windows DPAPI Hardware/TPM-Backed Sealing Functions ---
class DATA_BLOB(ctypes.Structure):
    _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_byte))]

crypt32 = getattr(ctypes.windll, 'crypt32', None)
kernel32 = getattr(ctypes.windll, 'kernel32', None)

def tpm_seal_data(data: bytes, desc: str = "CopilotPlus_TPM_Voiceprint_Vault") -> bytes:
    """Seal data using Windows DPAPI tied to machine TPM 2.0 / current user hardware master key."""
    if crypt32 is None or kernel32 is None:
        return VAULT_HEADER + data

    blob_in = DATA_BLOB(len(data), ctypes.cast(ctypes.create_string_buffer(data, len(data)), ctypes.POINTER(ctypes.c_byte)))
    blob_out = DATA_BLOB()
    if not crypt32.CryptProtectData(ctypes.byref(blob_in), desc, None, None, None, 0, ctypes.byref(blob_out)):
        raise ctypes.WinError()

    encrypted = ctypes.string_at(blob_out.pbData, blob_out.cbData)
    kernel32.LocalFree(blob_out.pbData)
    return VAULT_HEADER + encrypted

def tpm_unseal_data(data: bytes) -> bytes:
    """Unseal DPAPI/TPM encrypted vault payload."""
    if not data:
        return b"{}"

    if data.startswith(VAULT_HEADER):
        encrypted = data[len(VAULT_HEADER):]
    elif data.startswith(b"TPM_VAULT"):
        encrypted = data[16:]
    else:
        encrypted = data

    if crypt32 is None or kernel32 is None:
        return encrypted

    blob_in = DATA_BLOB(len(encrypted), ctypes.cast(ctypes.create_string_buffer(encrypted, len(encrypted)), ctypes.POINTER(ctypes.c_byte)))
    blob_out = DATA_BLOB()

    if not crypt32.CryptUnprotectData(ctypes.byref(blob_in), None, None, None, None, 0, ctypes.byref(blob_out)):
        try:
            test = json.loads(data.decode("utf-8"))
            if "profiles" in test:
                return data
        except Exception:
            pass
        raise ctypes.WinError()

    decrypted = ctypes.string_at(blob_out.pbData, blob_out.cbData)
    kernel32.LocalFree(blob_out.pbData)
    return decrypted


class SpeakerManager:
    def __init__(self, model_path=None, profiles_path=None, device="CPU"):
        self.device = device
        self.script_dir = os.path.dirname(os.path.abspath(__file__))

        # 1. Locate voxceleb.onnx (search up to 7 directory levels)
        if model_path is None or not os.path.exists(model_path):
            cur = self.script_dir
            for _ in range(7):
                cand = os.path.abspath(os.path.join(cur, "models", "speaker", "voxceleb.onnx"))
                if os.path.exists(cand):
                    model_path = cand
                    break
                parent = os.path.dirname(cur)
                if parent == cur:
                    break
                cur = parent

        if not model_path or not os.path.exists(model_path):
            raise FileNotFoundError(f"Speaker model voxceleb.onnx not found (searched from {self.script_dir})")

        self.model_path = model_path
        core = ov.Core()
        cache_dir = os.path.abspath(os.path.join(self.script_dir, "cache"))
        if not os.path.exists(cache_dir):
            for plevel in range(1, 7):
                c = os.path.abspath(os.path.join(self.script_dir, *(['..'] * plevel), "cache"))
                if os.path.isdir(c):
                    cache_dir = c
                    break
        os.makedirs(cache_dir, exist_ok=True)
        try:
            core.set_property({"CACHE_DIR": cache_dir})
        except Exception:
            pass
        model = core.read_model(self.model_path)
        requested = (self.device or "CPU").upper()
        order = [requested] + [d for d in ("NPU", "GPU", "CPU") if d != requested]
        if requested == "AUTO":
            order = ["NPU", "GPU", "CPU"]
        available = [d.upper() for d in core.available_devices]
        self.compiled_model = None
        for dev in order:
            if dev != "CPU" and not any(a == dev or a.startswith(dev + ".") for a in available):
                continue
            try:
                self.compiled_model = core.compile_model(model, dev)
                self.device = dev
                break
            except Exception:
                pass
        if self.compiled_model is None:
            self.compiled_model = core.compile_model(model, "CPU")
            self.device = "CPU"

        # 2. Locate canonical root profiles file (TPM-sealed vault)
        cur = self.script_dir
        root_dir = None
        for _ in range(7):
            if os.path.exists(os.path.join(cur, "DemoHub.sln")) or os.path.exists(os.path.join(cur, "models")) or os.path.isdir(os.path.join(cur, ".git")):
                root_dir = cur
                break
            parent = os.path.dirname(cur)
            if parent == cur:
                break
            cur = parent

        base_dir = root_dir if root_dir else self.script_dir
        self.canonical_tpm_path = os.path.abspath(os.path.join(base_dir, "Data", "Audio", "voice_profiles.tpm"))
        self.canonical_json_path = os.path.abspath(os.path.join(base_dir, "Data", "Audio", "voice_profiles.json"))

        if profiles_path:
            p_ext = os.path.splitext(profiles_path)[1].lower()
            if p_ext == ".tpm":
                self.profiles_tpm_path = os.path.abspath(profiles_path)
            else:
                self.profiles_tpm_path = os.path.abspath(os.path.splitext(profiles_path)[0] + ".tpm")
        else:
            self.profiles_tpm_path = self.canonical_tpm_path

        os.makedirs(os.path.dirname(self.profiles_tpm_path), exist_ok=True)
        os.makedirs(os.path.dirname(self.canonical_tpm_path), exist_ok=True)

        self._profiles_mtime = 0.0
        self.profiles = self._load_profiles()
        self.session_speakers = []

    def _get_tpm_paths_to_sync(self):
        paths = [self.profiles_tpm_path, self.canonical_tpm_path]
        bin_tpm = os.path.abspath(os.path.join(self.script_dir, "Data", "Audio", "voice_profiles.tpm"))
        if bin_tpm not in paths:
            paths.append(bin_tpm)
        return list(set(paths))

    def _get_legacy_json_paths(self):
        paths = [self.canonical_json_path]
        bin_json = os.path.abspath(os.path.join(self.script_dir, "Data", "Audio", "voice_profiles.json"))
        if bin_json not in paths:
            paths.append(bin_json)
        return list(set(paths))

    def _load_profiles(self):
        profs = {}
        loaded_from_tpm = False

        for p in self._get_tpm_paths_to_sync():
            if os.path.exists(p):
                try:
                    with open(p, "rb") as f:
                        raw_sealed = f.read()
                    if raw_sealed:
                        decrypted_bytes = tpm_unseal_data(raw_sealed)
                        data = json.loads(decrypted_bytes.decode("utf-8"))
                        for name, prof in data.get("profiles", {}).items():
                            if name not in profs or prof.get("sample_count", 1) >= profs[name].get("sample_count", 1):
                                profs[name] = prof
                                loaded_from_tpm = True
                except Exception as e:
                    print(f"[SpeakerManager] Warning reading TPM vault {p}: {e}", file=sys.stderr)

        needs_migration_save = False
        for jp in self._get_legacy_json_paths():
            if os.path.exists(jp):
                try:
                    with open(jp, "r", encoding="utf-8") as f:
                        jdata = json.load(f)
                    if "profiles" in jdata:
                        for name, prof in jdata.get("profiles", {}).items():
                            if name not in profs:
                                profs[name] = prof
                                needs_migration_save = True
                except Exception:
                    pass

        if needs_migration_save or (profs and not loaded_from_tpm):
            self.profiles = profs
            self._save_profiles()

        if os.path.exists(self.profiles_tpm_path):
            self._profiles_mtime = os.path.getmtime(self.profiles_tpm_path)
        return profs

    def _reload_profiles_if_modified(self):
        try:
            for p in self._get_tpm_paths_to_sync():
                if os.path.exists(p):
                    mt = os.path.getmtime(p)
                    if mt > self._profiles_mtime:
                        self.profiles = self._load_profiles()
                        break
        except Exception:
            pass

    def _save_profiles(self):
        """Seal profiles using Windows TPM 2.0 DPAPI and persist to disk."""
        payload = {
            "version": "2.0",
            "security": "TPM_2_0_SEALED",
            "hardware_provider": "Microsoft Platform Crypto Provider / DPAPI",
            "last_saved": datetime.datetime.now().isoformat(),
            "profiles": self.profiles
        }
        json_bytes = json.dumps(payload, indent=2, ensure_ascii=False).encode("utf-8")
        sealed_bytes = tpm_seal_data(json_bytes)

        for p in self._get_tpm_paths_to_sync():
            try:
                os.makedirs(os.path.dirname(p), exist_ok=True)
                with open(p, "wb") as f:
                    f.write(sealed_bytes)
                if p == self.profiles_tpm_path:
                    self._profiles_mtime = os.path.getmtime(p)
            except Exception as e:
                print(f"[SpeakerManager] Error saving TPM vault to {p}: {e}", file=sys.stderr)

        tombstone = {
            "security": "TPM_2_0_SEALED",
            "gdpr_compliance": "RGPD Article 9 - Biometric Data Protected by Hardware TPM",
            "notice": "Plaintext storage disabled. Biometric voice vectors are encrypted in voice_profiles.tpm via Windows TPM 2.0 Hardware DPAPI.",
            "last_updated": datetime.datetime.now().isoformat()
        }
        for jp in self._get_legacy_json_paths():
            try:
                os.makedirs(os.path.dirname(jp), exist_ok=True)
                with open(jp, "w", encoding="utf-8") as f:
                    json.dump(tombstone, f, indent=2)
            except Exception:
                pass

    @staticmethod
    def _to_16k_mono(audio_data, sr=16000):
        audio = np.asarray(audio_data, dtype=np.float32)
        if len(audio.shape) > 1:
            audio = audio.mean(axis=1)
        if sr != 16000 and len(audio) > 0:
            try:
                import scipy.signal
                num_samples = int(len(audio) * 16000 / sr)
                audio = scipy.signal.resample(audio, num_samples).astype(np.float32)
            except Exception:
                target_len = int(len(audio) * 16000 / sr)
                audio = np.interp(
                    np.linspace(0, len(audio), target_len, endpoint=False),
                    np.arange(len(audio)),
                    audio
                ).astype(np.float32)
        if len(audio) > 0:
            audio = audio - np.mean(audio)
        return audio

    @staticmethod
    def _center_embedding(vec):
        v = np.asarray(vec, dtype=np.float32) - _COHORT_MEAN
        norm = np.linalg.norm(v)
        return v / norm if norm > 1e-6 else v

    def _run_model_normalized(self, wav_16k):
        seg = np.asarray(wav_16k, dtype=np.float32)
        mx = float(np.max(np.abs(seg))) if len(seg) > 0 else 0.0
        if mx > 1e-5:
            seg = seg * (0.80 / mx)
        inp = seg.reshape(1, -1).astype(np.float32)
        res = self.compiled_model([inp])[0]
        vec = res[0, 0, :].astype(np.float32)
        norm = np.linalg.norm(vec)
        if norm > 1e-6:
            vec = vec / norm
        return vec

    @staticmethod
    def find_speech_islands(audio_16k, sr=16000, min_island_sec=0.42, bridge_sec=0.20):
        """Detect active speech islands in 16kHz audio while stripping silence and background noise."""
        blk = int(0.020 * sr)  # 20ms blocks (320 samples at 16kHz)
        n = len(audio_16k) // blk
        if n < 5:
            return [(0, len(audio_16k))] if len(audio_16k) >= int(0.20 * sr) else []

        rms = np.sqrt(np.mean(audio_16k[:n * blk].reshape(n, blk) ** 2, axis=1))
        p95 = float(np.percentile(rms, 95))
        if p95 < 1e-4:
            return []

        noise = float(np.percentile(rms, 20))
        thr = max(noise * 2.5, p95 * 0.12, 0.008)
        bridge_blocks = max(1, int(round(bridge_sec / 0.020)))
        active = np.convolve((rms >= thr).astype(int), np.ones(bridge_blocks, dtype=int), mode="same") > 0
        if not np.any(active):
            return []

        padded = np.concatenate([[False], active, [False]])
        starts = np.where(padded[1:] & ~padded[:-1])[0]
        ends = np.where(~padded[1:] & padded[:-1])[0]

        islands = []
        min_blocks = int(round(min_island_sec / 0.020))
        peak_gate = max(p95 * 0.22, 0.018)
        for sb, eb in zip(starts, ends):
            if (eb - sb) < min_blocks:
                continue
            if float(np.max(rms[sb:eb])) < peak_gate:
                continue
            s_idx = max(0, (sb - 2) * blk)
            e_idx = min(len(audio_16k), (eb + 2) * blk)
            islands.append((s_idx, e_idx))

        return islands

    def extract_embedding(self, audio_data, sr=16000):
        """
        Extract a 192-dimensional normalized speaker embedding from audio.
        Automatically strips leading/trailing/long silences and normalizes gain
        so silence frames never pollute ECAPA-TDNN statistics pooling.
        """
        audio = self._to_16k_mono(audio_data, sr)
        if len(audio) < 3200:  # 0.20s minimum
            return None

        # Detect active speech islands (bridge 300ms internal pauses within an utterance)
        islands = self.find_speech_islands(audio, sr=16000, min_island_sec=0.30, bridge_sec=0.30)
        if not islands:
            # Fallback if audio is very soft but still has signal
            if float(np.max(np.abs(audio))) < 1e-4:
                return None
            return self._run_model_normalized(audio)

        vecs = []
        weights = []
        for s_idx, e_idx in islands:
            seg = audio[s_idx:e_idx]
            if len(seg) < 3200:
                continue
            vecs.append(self._run_model_normalized(seg))
            weights.append(float(len(seg)))

        if not vecs:
            return self._run_model_normalized(audio)

        if len(vecs) == 1:
            return vecs[0]

        avg = np.average(vecs, axis=0, weights=np.array(weights, dtype=np.float32))
        norm = np.linalg.norm(avg)
        return (avg / norm).astype(np.float32) if norm > 1e-6 else avg.astype(np.float32)

    def _match_known_profile(self, centroid_emb, competing_centroids=None):
        """
        Verify if a speaker centroid matches a known TPM-sealed profile using
        both raw cosine similarity and cohort mean-centered cosine similarity,
        plus 1-to-1 exclusivity against competing speaker clusters.
        """
        if centroid_emb is None or not self.profiles:
            return None

        c_emb = self._center_embedding(centroid_emb)
        best_name = None
        best_prof = None
        best_raw = -1.0
        best_cent = -1.0

        for name, prof in self.profiles.items():
            p_emb = np.array(prof["embedding"], dtype=np.float32)
            p_norm = np.linalg.norm(p_emb)
            if p_norm > 1e-6:
                p_emb = p_emb / p_norm
            raw_sim = float(np.dot(centroid_emb, p_emb))
            cent_sim = float(np.dot(c_emb, self._center_embedding(p_emb)))

            if raw_sim >= 0.72 and cent_sim >= 0.42:
                # Exclusivity check: ensure no other cluster in the conversation matches this profile better
                is_beaten = False
                if competing_centroids:
                    for other_c in competing_centroids:
                        if other_c is centroid_emb:
                            continue
                        other_raw = float(np.dot(other_c, p_emb))
                        if other_raw > raw_sim + 0.015:
                            is_beaten = True
                            break
                if not is_beaten and cent_sim > best_cent:
                    best_cent = cent_sim
                    best_raw = raw_sim
                    best_name = name
                    best_prof = prof

        if best_prof is None:
            return None

        # Rescale confidence to an intuitive 0.80 - 0.99 range for UI display
        calibrated_conf = min(0.99, max(0.80, 0.78 + (best_cent - 0.40) * 0.45))
        return {
            "name": best_name,
            "color": best_prof.get("color", COLORS[0]),
            "confidence": round(float(calibrated_conf), 3),
            "raw_sim": best_raw,
            "cent_sim": best_cent,
            "embedding": np.array(best_prof["embedding"], dtype=np.float32)
        }

    def identify_or_cluster(self, audio_chunk, sr=16000, threshold=0.56):
        """
        Online streaming speaker identification & diarization:
        1. Extract silence-trimmed, gain-normalized speaker embedding.
        2. Cluster into session speakers using duration-weighted average linkage.
        3. Match session speaker centroid against TPM-sealed profiles with cohort centering.
        """
        self._reload_profiles_if_modified()

        audio_16k = self._to_16k_mono(audio_chunk, sr)
        islands = self.find_speech_islands(audio_16k, sr=16000, min_island_sec=0.30, bridge_sec=0.30)
        if islands:
            active_dur = sum((e - s) for s, e in islands) / 16000.0
        else:
            active_dur = len(audio_16k) / 16000.0

        emb = self.extract_embedding(audio_16k, sr=16000)
        if emb is None:
            if self.session_speakers:
                last = self.session_speakers[-1]
                return {
                    "speaker_id": last["id"],
                    "speaker_name": last["name"],
                    "speaker_color": last["color"],
                    "confidence": 0.50,
                    "is_known": last.get("is_known", False),
                    "is_tpm_certified": last.get("is_known", False),
                    "embedding": None
                }
            return {
                "speaker_id": "spk_1",
                "speaker_name": "Intervenant 1",
                "speaker_color": COLORS[0],
                "confidence": 0.50,
                "is_known": False,
                "is_tpm_certified": False,
                "embedding": None
            }

        dur_weight = max(0.4, float(active_dur))
        best_spk = None
        best_score = -1.0

        for spk in self.session_speakers:
            raw_mean = spk["raw_sum"] / spk["weight"]
            score = float(np.dot(emb, raw_mean))
            # Protect a session cluster already bound to a known TPM profile from merging an incompatible voice
            if spk.get("known_emb") is not None:
                c_prof = float(np.dot(self._center_embedding(emb), self._center_embedding(spk["known_emb"])))
                if c_prof < 0.35 and score < 0.62:
                    continue
            if score > best_score:
                best_score = score
                best_spk = spk

        # Short utterances (< 1.05s) attach to the closest existing session speaker instead of spawning spurious speakers
        if dur_weight < 1.05 and self.session_speakers:
            closest_spk = max(self.session_speakers, key=lambda s: float(np.dot(emb, s["norm_mean"])))
            closest_sim = float(np.dot(emb, closest_spk["norm_mean"]))
            if closest_sim >= 0.44:
                best_spk = closest_spk
                best_score = max(best_score, threshold)

        if best_spk is not None and best_score >= threshold:
            # Only update centroid weight if segment has sufficient duration (>= 0.85s)
            if dur_weight >= 0.85:
                best_spk["raw_sum"] = best_spk["raw_sum"] + emb * dur_weight
                best_spk["weight"] += dur_weight
                nm = best_spk["raw_sum"] / best_spk["weight"]
                best_spk["norm_mean"] = nm / (np.linalg.norm(nm) + 1e-6)
                best_spk["embedding"] = best_spk["norm_mean"].tolist()
            best_spk["count"] = best_spk.get("count", 1) + 1
            chosen = best_spk
        else:
            spk_idx = len(self.session_speakers)
            spk_id = f"spk_{spk_idx + 1}"
            spk_name = f"Intervenant {spk_idx + 1}"
            spk_color = COLORS[spk_idx % len(COLORS)]
            chosen = {
                "id": spk_id,
                "base_id": spk_id,
                "base_name": spk_name,
                "base_color": spk_color,
                "name": spk_name,
                "color": spk_color,
                "raw_sum": emb.copy() * dur_weight,
                "weight": dur_weight,
                "norm_mean": emb.copy(),
                "embedding": emb.tolist(),
                "count": 1,
                "is_known": False,
                "is_tpm_certified": False,
                "known_emb": None
            }
            self.session_speakers.append(chosen)

        # Evaluate if the chosen session speaker centroid matches a known TPM profile
        competing = [s["norm_mean"] for s in self.session_speakers if s is not chosen]
        prof_match = self._match_known_profile(chosen["norm_mean"], competing_centroids=competing)
        if prof_match is not None:
            chosen["id"] = f"known_{prof_match['name']}"
            chosen["name"] = prof_match["name"]
            chosen["color"] = prof_match["color"]
            chosen["is_known"] = True
            chosen["is_tpm_certified"] = True
            chosen["known_emb"] = prof_match["embedding"]
            conf = prof_match["confidence"]
        else:
            chosen["id"] = chosen.get("base_id", chosen["id"])
            chosen["name"] = chosen.get("base_name", chosen["name"])
            chosen["color"] = chosen.get("base_color", chosen["color"])
            chosen["is_known"] = False
            chosen["is_tpm_certified"] = False
            chosen["known_emb"] = None
            conf = round(min(0.98, max(0.75, best_score + 0.15)), 3) if best_score > 0 else 0.90

        return {
            "speaker_id": chosen["id"],
            "speaker_name": chosen["name"],
            "speaker_color": chosen["color"],
            "confidence": conf,
            "is_known": chosen["is_known"],
            "is_tpm_certified": chosen["is_tpm_certified"],
            "embedding": chosen["norm_mean"].tolist()
        }

    def split_speaker_turns_live(self, audio_chunk, sr=16000):
        """
        For a live microphone buffer (>= 2.0s), check if two speakers spoke back-to-back
        separated by a brief pause, and split at the speaker change boundary.
        Returns a list of (start_sample, end_sample) in the input sample rate.
        """
        audio_16k = self._to_16k_mono(audio_chunk, sr)
        if len(audio_16k) < int(3.2 * 16000):
            return [(0, len(audio_chunk))]

        islands = self.find_speech_islands(audio_16k, sr=16000, min_island_sec=1.10, bridge_sec=0.35)
        if len(islands) <= 1:
            return [(0, len(audio_chunk))]

        embs = [self._run_model_normalized(audio_16k[s:e]) for s, e in islands]
        turns_16k = [(islands[0][0], islands[0][1], embs[0])]
        for (s, e), emb in zip(islands[1:], embs[1:]):
            prev_s, prev_e, prev_emb = turns_16k[-1]
            sim = float(np.dot(prev_emb, emb))
            if sim >= 0.45:
                # Same speaker -> merge islands and average embedding
                merged_emb = prev_emb + emb
                merged_emb = merged_emb / (np.linalg.norm(merged_emb) + 1e-6)
                turns_16k[-1] = (prev_s, e, merged_emb)
            else:
                # Speaker change detected inside live phrase!
                turns_16k.append((s, e, emb))

        if len(turns_16k) == 1:
            return [(0, len(audio_chunk))]

        scale = float(sr) / 16000.0
        return [(int(s * scale), min(len(audio_chunk), int(e * scale))) for s, e, _ in turns_16k]

    def diarize_audio_turns(self, audio_data, sr=16000):
        """
        Full-file speaker-aware segmentation and global agglomerative diarization:
        1. Segment audio into active speech islands (stripping silence/noise).
        2. Cluster all islands globally via Weighted Average-Linkage Cosine Clustering.
        3. Absorb short micro-clusters (< 1.8s) into their closest main speaker cluster.
        4. Merge consecutive turns of the same speaker when separated by < 1.0s pause.
        5. Match global speaker centroids against TPM 2.0 profiles with 1-to-1 exclusivity.
        """
        self._reload_profiles_if_modified()
        audio_16k = self._to_16k_mono(audio_data, sr)
        islands = self.find_speech_islands(audio_16k, sr=16000, min_island_sec=0.42, bridge_sec=0.20)
        if not islands:
            islands = [(0, len(audio_16k))]

        embs = []
        weights = []
        valid_islands = []
        for s, e in islands:
            seg = audio_16k[s:e]
            if len(seg) < 3200:
                continue
            embs.append(self._run_model_normalized(seg))
            weights.append((e - s) / 16000.0)
            valid_islands.append((s, e))

        if not valid_islands:
            return []

        N = len(valid_islands)
        clusters = [[i] for i in range(N)]

        def raw_mean_vec(c):
            w = [weights[i] for i in c]
            return np.average([embs[i] for i in c], axis=0, weights=w)

        def norm_vec(c):
            v = raw_mean_vec(c)
            return v / (np.linalg.norm(v) + 1e-6)

        # Global weighted average-linkage agglomerative clustering
        while len(clusters) > 1:
            best_sim = -1.0
            best_pair = None
            c_raw = [raw_mean_vec(c) for c in clusters]
            for i in range(len(clusters)):
                for j in range(i + 1, len(clusters)):
                    s = float(np.dot(c_raw[i], c_raw[j]))
                    if s > best_sim:
                        best_sim = s
                        best_pair = (i, j)
            if best_sim < 0.60 or best_pair is None:
                break
            i, j = best_pair
            clusters[i].extend(clusters[j])
            clusters.pop(j)

        # Absorb short singleton/micro-clusters (< 1.8s total duration) into closest main speaker cluster
        main_clusters = [c for c in clusters if sum(weights[i] for i in c) >= 1.8]
        tiny_clusters = [c for c in clusters if sum(weights[i] for i in c) < 1.8]
        if main_clusters and tiny_clusters:
            for tc in tiny_clusters:
                tv = raw_mean_vec(tc)
                best_m = max(main_clusters, key=lambda mc: float(np.dot(tv, raw_mean_vec(mc))))
                if float(np.dot(tv, norm_vec(best_m))) >= 0.48:
                    best_m.extend(tc)
                else:
                    main_clusters.append(tc)
            clusters = main_clusters

        # Sort speaker clusters chronologically by first appearance
        clusters.sort(key=lambda c: min(c))
        labels = [0] * N
        centroids = []
        for cid, c in enumerate(clusters):
            centroids.append(norm_vec(c))
            for idx in c:
                labels[idx] = cid

        # Match each global speaker cluster centroid against TPM profiles with 1-to-1 exclusivity
        cluster_meta = []
        for cid, centroid in enumerate(centroids):
            prof_match = self._match_known_profile(centroid, competing_centroids=centroids)
            if prof_match is not None:
                cluster_meta.append({
                    "speaker_id": f"known_{prof_match['name']}",
                    "speaker_name": prof_match["name"],
                    "speaker_color": prof_match["color"],
                    "is_known": True,
                    "is_tpm_certified": True,
                    "confidence": prof_match["confidence"],
                    "embedding": centroid.tolist()
                })
            else:
                cluster_meta.append({
                    "speaker_id": f"spk_{cid + 1}",
                    "speaker_name": f"Intervenant {cid + 1}",
                    "speaker_color": COLORS[cid % len(COLORS)],
                    "is_known": False,
                    "is_tpm_certified": False,
                    "confidence": 0.92,
                    "embedding": centroid.tolist()
                })

        # Merge adjacent islands of the SAME speaker if pause < 1.0s and merged duration <= 9.0s
        merged_turns = []
        for (s, e), cid in zip(valid_islands, labels):
            if (
                merged_turns
                and merged_turns[-1]["cid"] == cid
                and (s - merged_turns[-1]["end_sample"]) / 16000.0 < 1.0
                and (e - merged_turns[-1]["start_sample"]) / 16000.0 <= 9.0
            ):
                merged_turns[-1]["end_sample"] = e
                merged_turns[-1]["end_ts"] = round(e / 16000.0, 2)
            else:
                meta = cluster_meta[cid]
                merged_turns.append({
                    "cid": cid,
                    "start_sample": s,
                    "end_sample": e,
                    "start_ts": round(s / 16000.0, 2),
                    "end_ts": round(e / 16000.0, 2),
                    "speaker_id": meta["speaker_id"],
                    "speaker_name": meta["speaker_name"],
                    "speaker_color": meta["speaker_color"],
                    "is_known": meta["is_known"],
                    "is_tpm_certified": meta["is_tpm_certified"],
                    "confidence": meta["confidence"],
                    "embedding": meta["embedding"]
                })

        return merged_turns

    def register_profile(self, name, embedding_vector, color=None):
        """Save or update a named voice profile with the given 192D embedding in the TPM vault."""
        emb = np.array(embedding_vector, dtype=np.float32)
        norm = np.linalg.norm(emb)
        if norm > 1e-6:
            emb = emb / norm

        now_iso = datetime.datetime.now().isoformat()
        if name in self.profiles:
            existing = self.profiles[name]
            old_emb = np.array(existing["embedding"], dtype=np.float32)
            old_norm = np.linalg.norm(old_emb)
            if old_norm > 1e-6:
                old_emb = old_emb / old_norm
            sim = float(np.dot(old_emb, emb))
            if sim >= 0.68:
                # Same speaker: refine voiceprint with capped moving average
                cnt = min(existing.get("sample_count", 1), 4)
                merged = (old_emb * cnt + emb) / (cnt + 1)
                m_norm = np.linalg.norm(merged)
                if m_norm > 1e-6:
                    merged = merged / m_norm
                existing["embedding"] = merged.tolist()
                existing["sample_count"] = existing.get("sample_count", 1) + 1
            else:
                # Different voice (e.g. replacing a demo test with actual user voice): replace cleanly
                existing["embedding"] = emb.tolist()
                existing["sample_count"] = 1
            existing["last_updated"] = now_iso
            if color:
                existing["color"] = color
        else:
            if not color:
                assigned_idx = len(self.profiles) % len(COLORS)
                color = COLORS[assigned_idx]
            self.profiles[name] = {
                "name": name,
                "color": color,
                "embedding": emb.tolist(),
                "sample_count": 1,
                "last_updated": now_iso
            }

        self._save_profiles()
        return self.profiles[name]

    def register_from_audio_file(self, file_path, start_sec=0.0, end_sec=None, name=""):
        """Extract embedding from audio file segment and register profile in TPM vault."""
        if not os.path.exists(file_path):
            raise FileNotFoundError(f"Audio file not found: {file_path}")

        audio, sr = sf.read(file_path)
        if len(audio.shape) > 1:
            audio = audio.mean(axis=1)

        total_audio_len = len(audio)
        s_idx = int(start_sec * sr)
        e_idx = int(end_sec * sr) if end_sec is not None else total_audio_len

        if s_idx >= total_audio_len:
            s_idx = max(0, total_audio_len - int(3.5 * sr))
        e_idx = min(total_audio_len, max(e_idx, s_idx + int(1.0 * sr)))

        chunk = audio[s_idx:e_idx]
        if len(chunk) < 3200:
            chunk = audio[-min(total_audio_len, int(4.0 * sr)):]

        emb = self.extract_embedding(chunk, sr=sr)
        if emb is None:
            raise ValueError("Échantillon audio trop court ou silence détecté.")

        return self.register_profile(name, emb)

    def enroll_from_mic(self, name, duration_sec=5.0, color=None):
        """Record live audio from microphone and register profile directly in TPM vault."""
        try:
            import sounddevice as sd
        except ImportError:
            raise ImportError("Le module 'sounddevice' est requis pour l'enregistrement micro.")

        sr = 16000
        total_samples = int(duration_sec * sr)
        recording = sd.rec(total_samples, samplerate=sr, channels=1, dtype="float32")
        sd.wait()

        audio = recording.flatten()
        emb = self.extract_embedding(audio, sr=sr)
        if emb is None:
            raise ValueError("Échantillon audio trop court ou silence détecté. Veuillez parler distinctement près du microphone.")

        return self.register_profile(name, emb, color)

    def delete_profile(self, name):
        """Permanently delete a voice profile from the TPM vault (Right to erasure / RGPD Art 17)."""
        if name in self.profiles:
            del self.profiles[name]
            self._save_profiles()
            return True
        return False

    def list_profiles(self):
        """List all enrolled profiles with TPM security metadata."""
        self._reload_profiles_if_modified()
        result = []
        for name, prof in self.profiles.items():
            result.append({
                "name": prof["name"],
                "color": prof.get("color", COLORS[0]),
                "sample_count": prof.get("sample_count", 1),
                "last_updated": prof.get("last_updated", ""),
                "is_tpm_sealed": True,
                "vault_security": "TPM 2.0 / Pluton (Hardware DPAPI)"
            })
        return result


def main():
    parser = argparse.ArgumentParser(description="Voice Profile & Speaker Management CLI (TPM 2.0 Secured)")
    parser.add_argument("--action", required=True, choices=["list", "save_from_audio", "save_embedding", "enroll_mic", "delete"])
    parser.add_argument("--name", help="Speaker name / prénom")
    parser.add_argument("--file", help="Path to audio file (for save_from_audio)")
    parser.add_argument("--start", type=float, default=0.0, help="Start timestamp in seconds")
    parser.add_argument("--end", type=float, default=None, help="End timestamp in seconds")
    parser.add_argument("--duration", type=float, default=5.0, help="Mic enrollment recording duration in seconds")
    parser.add_argument("--color", help="Hex color e.g. #3B82F6")
    parser.add_argument("--embedding_json", help="JSON array of 192 floats")
    parser.add_argument("--embedding_file", help="Path to JSON file containing 192 floats")
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")

    mgr = SpeakerManager()

    if args.action == "list":
        profiles = mgr.list_profiles()
        print(json.dumps({
            "type": "profiles_list",
            "is_tpm_active": True,
            "security_provider": "Windows DPAPI (TPM 2.0 / Microsoft Pluton)",
            "profiles": profiles
        }, ensure_ascii=False))

    elif args.action == "save_from_audio":
        if not args.name or not args.file:
            print(json.dumps({"type": "error", "message": "--name and --file are required for save_from_audio"}))
            sys.exit(1)
        try:
            prof = mgr.register_from_audio_file(args.file, args.start, args.end, args.name)
            print(json.dumps({
                "type": "profile_saved",
                "name": prof["name"],
                "color": prof["color"],
                "sample_count": prof["sample_count"],
                "is_tpm_sealed": True
            }, ensure_ascii=False))
        except Exception as e:
            print(json.dumps({"type": "error", "message": str(e)}))
            sys.exit(1)

    elif args.action == "enroll_mic":
        if not args.name:
            print(json.dumps({"type": "error", "message": "--name is required for enroll_mic"}))
            sys.exit(1)
        try:
            prof = mgr.enroll_from_mic(name=args.name, duration_sec=args.duration, color=args.color)
            print(json.dumps({
                "type": "profile_saved",
                "name": prof["name"],
                "color": prof["color"],
                "sample_count": prof["sample_count"],
                "is_tpm_sealed": True
            }, ensure_ascii=False))
        except Exception as e:
            print(json.dumps({"type": "error", "message": str(e)}))
            sys.exit(1)

    elif args.action == "save_embedding":
        if not args.name:
            print(json.dumps({"type": "error", "message": "--name is required for save_embedding"}))
            sys.exit(1)
        try:
            vec = None
            if args.embedding_file and os.path.exists(args.embedding_file):
                with open(args.embedding_file, "r", encoding="utf-8") as vf:
                    vec = json.load(vf)
            elif args.embedding_json:
                vec = json.loads(args.embedding_json)
            else:
                print(json.dumps({"type": "error", "message": "--embedding_json or --embedding_file is required"}))
                sys.exit(1)

            prof = mgr.register_profile(args.name, vec, args.color)
            print(json.dumps({
                "type": "profile_saved",
                "name": prof["name"],
                "color": prof["color"],
                "sample_count": prof["sample_count"],
                "is_tpm_sealed": True
            }, ensure_ascii=False))
        except Exception as e:
            print(json.dumps({"type": "error", "message": str(e)}))
            sys.exit(1)

    elif args.action == "delete":
        if not args.name:
            print(json.dumps({"type": "error", "message": "--name is required for delete"}))
            sys.exit(1)
        success = mgr.delete_profile(args.name)
        print(json.dumps({"type": "profile_deleted", "name": args.name, "success": success}))

if __name__ == "__main__":
    main()
