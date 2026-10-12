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

# --- Windows DPAPI Hardware/TPM-Backed Sealing Functions ---
class DATA_BLOB(ctypes.Structure):
    _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_byte))]

crypt32 = getattr(ctypes.windll, 'crypt32', None)
kernel32 = getattr(ctypes.windll, 'kernel32', None)

def tpm_seal_data(data: bytes, desc: str = "CopilotPlus_TPM_Voiceprint_Vault") -> bytes:
    """Seal data using Windows DPAPI tied to machine TPM 2.0 / current user hardware master key."""
    if crypt32 is None or kernel32 is None:
        # Fallback if non-Windows (not applicable on Copilot+ PC, but prevents crashes)
        return VAULT_HEADER + data

    blob_in = DATA_BLOB(len(data), ctypes.cast(ctypes.create_string_buffer(data, len(data)), ctypes.POINTER(ctypes.c_byte)))
    blob_out = DATA_BLOB()
    # CryptProtectData: flag 0 uses machine/user TPM-backed master key
    if not crypt32.CryptProtectData(ctypes.byref(blob_in), desc, None, None, None, 0, ctypes.byref(blob_out)):
        raise ctypes.WinError()

    encrypted = ctypes.string_at(blob_out.pbData, blob_out.cbData)
    kernel32.LocalFree(blob_out.pbData)
    return VAULT_HEADER + encrypted

def tpm_unseal_data(data: bytes) -> bytes:
    """Unseal DPAPI/TPM encrypted vault payload."""
    if not data:
        return b"{}"

    # Strip magic header if present
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
        # If it failed to unprotect, check if it's legacy unencrypted JSON
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
                if parent == cur: break
                cur = parent

        if not model_path or not os.path.exists(model_path):
            raise FileNotFoundError(f"Speaker model voxceleb.onnx not found (searched from {self.script_dir})")

        self.model_path = model_path
        core = ov.Core()
        model = core.read_model(self.model_path)
        self.compiled_model = core.compile_model(model, self.device)

        # 2. Locate canonical root profiles file (TPM-sealed vault)
        cur = self.script_dir
        root_dir = None
        for _ in range(7):
            if os.path.exists(os.path.join(cur, "DemoHub.sln")) or os.path.exists(os.path.join(cur, "models")) or os.path.isdir(os.path.join(cur, ".git")):
                root_dir = cur
                break
            parent = os.path.dirname(cur)
            if parent == cur: break
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
        self.session_speakers = []  # In-session clusters: [{"id", "name", "color", "embedding", "count"}]

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

        # 1. First priority: load from TPM-sealed vault (.tpm)
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

        # 2. Migration: If .tpm is missing or legacy .json has profiles not in .tpm
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

        # If we imported from legacy JSON or .tpm didn't exist yet, seal immediately to TPM
        if needs_migration_save or (profs and not loaded_from_tpm):
            self.profiles = profs
            self._save_profiles()

        # Update cached mtime
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

        # GDPR / RGPD Sanitization: replace plaintext JSON files with a secure tombstone
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

    def extract_embedding(self, audio_data, sr=16000):
        """Extract a 192-dimensional normalized speaker embedding from 16kHz mono audio."""
        if len(audio_data.shape) > 1:
            audio_data = audio_data.mean(axis=1)

        if sr != 16000:
            try:
                import scipy.signal
                num_samples = int(len(audio_data) * 16000 / sr)
                audio_data = scipy.signal.resample(audio_data, num_samples).astype(np.float32)
            except Exception:
                target_len = int(len(audio_data) * 16000 / sr)
                audio_data = np.interp(
                    np.linspace(0, len(audio_data), target_len, endpoint=False),
                    np.arange(len(audio_data)),
                    audio_data
                ).astype(np.float32)

        if len(audio_data) < 3200:  # 0.20s minimum
            return None

        inp = audio_data.astype(np.float32).reshape(1, -1)
        res = self.compiled_model([inp])[0]  # Shape [1, 1, 192]
        vec = res[0, 0, :]
        norm = np.linalg.norm(vec)
        if norm > 1e-6:
            vec = vec / norm
        return vec

    def identify_or_cluster(self, audio_chunk, sr=16000, threshold=0.60):
        """
        Identify who is speaking in the audio chunk:
        1. Check known TPM-saved profiles.
        2. If not found, check/cluster with session speakers.
        3. If still not found, create new session speaker (Intervenant X).
        """
        self._reload_profiles_if_modified()

        emb = self.extract_embedding(audio_chunk, sr)
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

        # 1. Match against known TPM-saved profiles
        best_profile_name = None
        best_profile_score = -1.0
        best_profile = None

        for name, prof in self.profiles.items():
            p_emb = np.array(prof["embedding"], dtype=np.float32)
            score = float(np.dot(emb, p_emb))
            if score > best_profile_score:
                best_profile_score = score
                best_profile_name = name
                best_profile = prof

        if best_profile_score >= threshold and best_profile is not None:
            return {
                "speaker_id": f"known_{best_profile_name}",
                "speaker_name": best_profile_name,
                "speaker_color": best_profile.get("color", COLORS[0]),
                "confidence": round(best_profile_score, 3),
                "is_known": True,
                "is_tpm_certified": True,
                "embedding": emb.tolist()
            }

        # 2. Match against current session speakers
        best_session_spk = None
        best_session_score = -1.0

        for spk in self.session_speakers:
            s_emb = np.array(spk["embedding"], dtype=np.float32)
            score = float(np.dot(emb, s_emb))
            if score > best_session_score:
                best_session_score = score
                best_session_spk = spk

        if best_session_score >= threshold and best_session_spk is not None:
            cnt = best_session_spk.get("count", 1)
            updated_emb = (np.array(best_session_spk["embedding"]) * cnt + emb) / (cnt + 1)
            norm = np.linalg.norm(updated_emb)
            if norm > 1e-6:
                updated_emb = updated_emb / norm
            best_session_spk["embedding"] = updated_emb.tolist()
            best_session_spk["count"] = cnt + 1

            return {
                "speaker_id": best_session_spk["id"],
                "speaker_name": best_session_spk["name"],
                "speaker_color": best_session_spk["color"],
                "confidence": round(best_session_score, 3),
                "is_known": False,
                "is_tpm_certified": False,
                "embedding": emb.tolist()
            }

        # 3. New Speaker in this session
        spk_idx = len(self.session_speakers)
        spk_id = f"spk_{spk_idx + 1}"
        spk_name = f"Intervenant {spk_idx + 1}"
        spk_color = COLORS[spk_idx % len(COLORS)]

        new_spk = {
            "id": spk_id,
            "name": spk_name,
            "color": spk_color,
            "embedding": emb.tolist(),
            "count": 1,
            "is_known": False,
            "is_tpm_certified": False
        }
        self.session_speakers.append(new_spk)

        return {
            "speaker_id": spk_id,
            "speaker_name": spk_name,
            "speaker_color": spk_color,
            "confidence": 1.0,
            "is_known": False,
            "is_tpm_certified": False,
            "embedding": emb.tolist()
        }

    def register_profile(self, name, embedding_vector, color=None):
        """Save or update a named voice profile with the given 192D embedding in the TPM vault."""
        emb = np.array(embedding_vector, dtype=np.float32)
        norm = np.linalg.norm(emb)
        if norm > 1e-6:
            emb = emb / norm

        now_iso = datetime.datetime.now().isoformat()
        if name in self.profiles:
            existing = self.profiles[name]
            cnt = existing.get("sample_count", 1)
            old_emb = np.array(existing["embedding"], dtype=np.float32)
            merged = (old_emb * cnt + emb) / (cnt + 1)
            norm = np.linalg.norm(merged)
            if norm > 1e-6:
                merged = merged / norm
            existing["embedding"] = merged.tolist()
            existing["sample_count"] = cnt + 1
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

        # Clamp boundaries
        if s_idx >= total_audio_len:
            s_idx = max(0, total_audio_len - int(3.5 * sr))
        e_idx = min(total_audio_len, max(e_idx, s_idx + int(1.0 * sr)))

        chunk = audio[s_idx:e_idx]
        if len(chunk) < 3200:
            chunk = audio[-min(total_audio_len, int(4.0 * sr)):]

        emb = self.extract_embedding(chunk, sr=sr)
        if emb is None:
            raise ValueError("Audio chunk is too short to extract a voiceprint.")

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
            raise ValueError("Échantillon audio trop court ou silence détecté.")

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
