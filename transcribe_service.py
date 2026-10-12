import argparse
import json
import os
import sys
import time
import numpy as np
import soundfile as sf
import openvino_genai as ovg
from speaker_service import SpeakerManager

def main():
    parser = argparse.ArgumentParser(description="Real OpenVINO Whisper Transcription & Speaker Diarization Service")
    parser.add_argument("--file", required=True, help="Path to audio file (WAV, MP3, FLAC, etc.)")
    parser.add_argument("--device", default="NPU", choices=["NPU", "GPU", "CPU"], help="Hardware accelerator")
    parser.add_argument("--language", default="<|fr|>", help="Language token, e.g. <|fr|> or <|en|>")
    parser.add_argument("--model_dir", default="models/whisper-small-fp16-ov", help="Path to OpenVINO Whisper model")
    args = parser.parse_args()

    # Ensure UTF-8 output
    sys.stdout.reconfigure(encoding="utf-8")

    if not os.path.exists(args.file):
        print(json.dumps({"type": "error", "message": f"Audio file not found: {args.file}"}), flush=True)
        return

    # 1. Load Audio
    try:
        audio, sr = sf.read(args.file)
        if len(audio.shape) > 1:
            audio = audio.mean(axis=1)  # Convert to mono
        if sr != 16000:
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
        else:
            audio = audio.astype(np.float32)

        audio_duration_sec = len(audio) / 16000.0
        print(json.dumps({"type": "audio_info", "duration_sec": round(audio_duration_sec, 2), "sample_rate": 16000}), flush=True)
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Error reading audio file: {str(e)}"}), flush=True)
        return

    # 2. Initialize Speaker Manager
    spk_mgr = None
    try:
        spk_mgr = SpeakerManager()
    except Exception as e:
        print(json.dumps({"type": "warning", "message": f"Could not initialize SpeakerManager: {e}"}), flush=True)

    # 3. Initialize Whisper on Selected Silicon (NPU / GPU / CPU)
    load_start = time.perf_counter()
    try:
        model_path = args.model_dir
        if not os.path.isabs(model_path) or not os.path.exists(os.path.join(model_path, "openvino_encoder_model.xml")):
            script_dir = os.path.dirname(os.path.abspath(__file__))
            cur = script_dir
            for _ in range(6):
                cand_small = os.path.abspath(os.path.join(cur, "models", "whisper-small-fp16-ov"))
                if os.path.isdir(cand_small) and os.path.exists(os.path.join(cand_small, "openvino_encoder_model.xml")):
                    model_path = cand_small
                    break
                cand_base = os.path.abspath(os.path.join(cur, "models", "whisper-base-fp16-ov"))
                if os.path.isdir(cand_base) and os.path.exists(os.path.join(cand_base, "openvino_encoder_model.xml")):
                    model_path = cand_base
                    break
                cand_tiny = os.path.abspath(os.path.join(cur, "models", "whisper-tiny-fp16-ov"))
                if os.path.isdir(cand_tiny) and os.path.exists(os.path.join(cand_tiny, "openvino_encoder_model.xml")):
                    model_path = cand_tiny
                    break
                parent = os.path.dirname(cur)
                if parent == cur:
                    break
                cur = parent
        cache_dir = os.path.abspath(os.path.join(script_dir, "cache"))
        if not os.path.exists(cache_dir):
            for parent_level in range(1, 6):
                c = os.path.abspath(os.path.join(script_dir, *(['..'] * parent_level), "cache"))
                if os.path.isdir(c):
                    cache_dir = c
                    break
        os.makedirs(cache_dir, exist_ok=True)

        pipe = ovg.WhisperPipeline(model_path, args.device, CACHE_DIR=cache_dir)
        load_time_ms = int((time.perf_counter() - load_start) * 1000)
        print(json.dumps({
            "type": "model_loaded",
            "device": args.device,
            "model": os.path.basename(model_path),
            "load_time_ms": load_time_ms
        }), flush=True)
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Error loading model on {args.device}: {str(e)}"}), flush=True)
        return

    # 4. Transcribe with Timestamps & Speaker Diarization
    infer_start = time.perf_counter()
    try:
        config = pipe.get_generation_config()
        if args.language and args.language.lower() not in ("auto", "<|auto|>", "none", ""):
            config.language = args.language if args.language.startswith("<|") else f"<|{args.language}|>"
        else:
            config.language = None
        config.return_timestamps = True
        config.task = "transcribe"

        results = pipe.generate(audio.tolist(), config)
        infer_time_ms = int((time.perf_counter() - infer_start) * 1000)
        det = getattr(results, "language", None)
        detected_lang = str(det).replace("<|", "").replace("|>", "").strip() if det else ("fr" if config.language and "fr" in str(config.language) else "en")

        accumulated_text = ""
        if hasattr(results, "chunks") and results.chunks:
            for chunk in results.chunks:
                text = chunk.text.strip()
                if not text:
                    continue
                accumulated_text += " " + text

                spk_id = "spk_1"
                spk_name = "Intervenant 1"
                spk_color = "#3B82F6"
                is_known = False
                confidence = 1.0
                embedding = None

                if spk_mgr is not None:
                    s_idx = max(0, int(chunk.start_ts * 16000))
                    e_idx = min(len(audio), int(chunk.end_ts * 16000))
                    if e_idx > s_idx:
                        chunk_audio = audio[s_idx:e_idx]
                        spk_info = spk_mgr.identify_or_cluster(chunk_audio, sr=16000)
                        spk_id = spk_info["speaker_id"]
                        spk_name = spk_info["speaker_name"]
                        spk_color = spk_info["speaker_color"]
                        is_known = spk_info.get("is_known", False)
                        is_tpm_certified = spk_info.get("is_tpm_certified", False)
                        confidence = spk_info.get("confidence", 1.0)
                        embedding = spk_info.get("embedding", None)

                print(json.dumps({
                    "type": "chunk",
                    "start_ts": round(chunk.start_ts, 2),
                    "end_ts": round(chunk.end_ts, 2),
                    "speaker_id": spk_id,
                    "speaker_name": spk_name,
                    "speaker_color": spk_color,
                    "is_known": is_known,
                    "is_tpm_certified": is_tpm_certified,
                    "confidence": confidence,
                    "embedding": embedding,
                    "text": text
                }), flush=True)
        else:
            accumulated_text = str(results).strip()
            spk_id = "spk_1"
            spk_name = "Intervenant 1"
            spk_color = "#3B82F6"
            is_known = False
            embedding = None
            if spk_mgr is not None:
                spk_info = spk_mgr.identify_or_cluster(audio, sr=16000)
                spk_id = spk_info["speaker_id"]
                spk_name = spk_info["speaker_name"]
                spk_color = spk_info["speaker_color"]
                is_known = spk_info.get("is_known", False)
                embedding = spk_info.get("embedding", None)

            print(json.dumps({
                "type": "chunk",
                "start_ts": 0.0,
                "end_ts": round(audio_duration_sec, 2),
                "speaker_id": spk_id,
                "speaker_name": spk_name,
                "speaker_color": spk_color,
                "is_known": is_known,
                "embedding": embedding,
                "text": accumulated_text
            }), flush=True)

        speed_factor = round(audio_duration_sec / max(0.01, (infer_time_ms / 1000.0)), 2)
        print(json.dumps({
            "type": "complete",
            "device": args.device,
            "full_text": accumulated_text.strip(),
            "infer_time_ms": infer_time_ms,
            "speed_factor_raw": speed_factor,
            "speed_factor": f"{speed_factor}x",
            "detected_language": detected_lang
        }), flush=True)

    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Inference error on {args.device}: {str(e)}"}), flush=True)

if __name__ == "__main__":
    main()
