import argparse
import json
import os
import sys
import time
import numpy as np
import soundfile as sf
import openvino_genai as ovg
from speaker_service import SpeakerManager
from lang_utils import detect_language, lang_code, whisper_translate_to_english

def main():
    parser = argparse.ArgumentParser(description="Real OpenVINO Whisper Transcription & Speaker Diarization Service")
    parser.add_argument("--file", required=True, help="Path to audio file (WAV, MP3, FLAC, etc.)")
    parser.add_argument("--device", default="NPU", choices=["NPU", "GPU", "CPU"], help="Hardware accelerator")
    parser.add_argument("--language", default="<|fr|>", help="Language token, e.g. <|fr|> or <|en|>")
    parser.add_argument("--model_dir", default="models/whisper-small-fp16-ov", help="Path to OpenVINO Whisper model")
    parser.add_argument("--translate_to", default="none", help="Langue cible de traduction Whisper ('en') ou 'none'. Les autres langues sont traduites côté application (LLM).")
    args = parser.parse_args()
    translate_to = (args.translate_to or "none").strip().lower()

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

        requested_device = args.device
        target_device = args.device
        pipe = None
        try:
            pipe = ovg.WhisperPipeline(model_path, target_device, CACHE_DIR=cache_dir)
        except Exception as e_initial:
            fallback_devices = ["GPU", "CPU"] if target_device == "NPU" else ["CPU"]
            for fb in fallback_devices:
                try:
                    pipe = ovg.WhisperPipeline(model_path, fb)
                    target_device = fb
                    args.device = fb
                    break
                except Exception:
                    pass
            if pipe is None:
                raise e_initial

        effective_device = target_device
        is_fallback = requested_device.upper() not in effective_device.upper()
        load_time_ms = int((time.perf_counter() - load_start) * 1000)
        print(json.dumps({
            "type": "model_loaded",
            "device": effective_device,
            "requested_device": requested_device,
            "effective_device": effective_device,
            "fallback": is_fallback,
            "model": os.path.basename(model_path),
            "load_time_ms": load_time_ms
        }), flush=True)
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Error loading model on {args.device}: {str(e)}"}), flush=True)
        return

    # 4. Transcribe with Speaker-Aware Turn Diarization
    infer_start = time.perf_counter()
    try:
        config = pipe.get_generation_config()
        if args.language and args.language.lower() not in ("auto", "<|auto|>", "none", ""):
            config.language = args.language if args.language.startswith("<|") else f"<|{args.language}|>"
        else:
            config.language = None
        config.task = "transcribe"
        forced_lang = lang_code(config.language)

        turns = spk_mgr.diarize_audio_turns(audio, sr=16000) if spk_mgr is not None else []
        accumulated_text = ""
        lang_votes = []

        if turns:
            config.return_timestamps = False
            hallucinations = {"...", "…", "sous-titres réalisés par", "merci d'avoir regardé cette vidéo", "sous-titres", "merci."}
            for turn in turns:
                s_idx = turn["start_sample"]
                e_idx = turn["end_sample"]
                if e_idx <= s_idx:
                    continue
                turn_audio = audio[s_idx:e_idx]
                res = pipe.generate(turn_audio.tolist(), config)
                text = str(res).strip()
                if not text or text.lower() in hallucinations:
                    continue

                accumulated_text += " " + text
                chunk_lang = forced_lang if forced_lang else detect_language(text, fallback=lang_votes[-1] if lang_votes else "fr")
                lang_votes.append(chunk_lang)

                translation = None
                translation_lang = None
                if translate_to == "en" and chunk_lang != "en":
                    translation = whisper_translate_to_english(pipe, turn_audio.tolist(), chunk_lang)
                    if translation:
                        translation_lang = "en"

                print(json.dumps({
                    "type": "chunk",
                    "start_ts": turn["start_ts"],
                    "end_ts": turn["end_ts"],
                    "speaker_id": turn["speaker_id"],
                    "speaker_name": turn["speaker_name"],
                    "speaker_color": turn["speaker_color"],
                    "is_known": turn["is_known"],
                    "is_tpm_certified": turn["is_tpm_certified"],
                    "confidence": turn["confidence"],
                    "embedding": turn["embedding"],
                    "detected_language": chunk_lang,
                    "translation": translation,
                    "translation_lang": translation_lang,
                    "text": text
                }, ensure_ascii=False), flush=True)

            infer_time_ms = int((time.perf_counter() - infer_start) * 1000)
            detected_lang = forced_lang if forced_lang else detect_language(accumulated_text, fallback=lang_votes[0] if lang_votes else "fr")
        else:
            config.return_timestamps = True
            results = pipe.generate(audio.tolist(), config)
            infer_time_ms = int((time.perf_counter() - infer_start) * 1000)
            full_text_guess = " ".join(c.text for c in results.chunks) if hasattr(results, "chunks") and results.chunks else str(results)
            detected_lang = forced_lang if forced_lang else detect_language(full_text_guess, fallback="fr")

            if hasattr(results, "chunks") and results.chunks:
                for chunk in results.chunks:
                    text = chunk.text.strip()
                    if not text:
                        continue
                    accumulated_text += " " + text
                    chunk_lang = forced_lang if forced_lang else detect_language(text, fallback=detected_lang)
                    translation = None
                    translation_lang = None
                    if translate_to == "en" and chunk_lang != "en":
                        ts_i = max(0, int(chunk.start_ts * 16000))
                        te_i = min(len(audio), int(chunk.end_ts * 16000))
                        if te_i > ts_i:
                            translation = whisper_translate_to_english(pipe, audio[ts_i:te_i].tolist(), chunk_lang)
                            if translation:
                                translation_lang = "en"

                    print(json.dumps({
                        "type": "chunk",
                        "start_ts": round(chunk.start_ts, 2),
                        "end_ts": round(chunk.end_ts, 2),
                        "speaker_id": "spk_1",
                        "speaker_name": "Intervenant 1",
                        "speaker_color": "#3B82F6",
                        "is_known": False,
                        "is_tpm_certified": False,
                        "confidence": 0.90,
                        "embedding": None,
                        "detected_language": chunk_lang,
                        "translation": translation,
                        "translation_lang": translation_lang,
                        "text": text
                    }, ensure_ascii=False), flush=True)

        speed_factor = round(audio_duration_sec / max(0.01, (infer_time_ms / 1000.0)), 2)
        print(json.dumps({
            "type": "complete",
            "device": effective_device,
            "requested_device": requested_device,
            "effective_device": effective_device,
            "fallback": is_fallback,
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
