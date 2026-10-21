import argparse
import json
import os
import sys
import time
from math import gcd
import numpy as np
import soundfile as sf
try:
    import scipy.signal
except Exception:
    scipy = None
import openvino_genai as ovg
for _lvl in range(1, 6):
    _cand_root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *(['..'] * _lvl)))
    if os.path.exists(os.path.join(_cand_root, "lang_utils.py")) and _cand_root not in sys.path:
        sys.path.insert(0, _cand_root)
        break
from speaker_service import SpeakerManager
from lang_utils import detect_language, lang_code, whisper_translate_to_english, smart_transcribe_turn, is_hallucinated_or_junk


def load_whisper_and_speaker(model_dir_arg, requested_device):
    load_start = time.perf_counter()
    script_dir = os.path.dirname(os.path.abspath(__file__))
    model_path = model_dir_arg
    if not os.path.isabs(model_path) or not os.path.exists(os.path.join(model_path, "openvino_encoder_model.xml")):
        cur = script_dir
        for _ in range(6):
            for folder in ("whisper-small-fp16-ov", "whisper-base-fp16-ov", "whisper-tiny-fp16-ov"):
                cand = os.path.abspath(os.path.join(cur, "models", folder))
                if os.path.isdir(cand) and os.path.exists(os.path.join(cand, "openvino_encoder_model.xml")):
                    model_path = cand
                    break
            else:
                parent = os.path.dirname(cur)
                if parent == cur:
                    break
                cur = parent
                continue
            break

    cache_dir = os.path.abspath(os.path.join(script_dir, "cache"))
    if not os.path.exists(cache_dir):
        for parent_level in range(1, 6):
            c = os.path.abspath(os.path.join(script_dir, *(['..'] * parent_level), "cache"))
            if os.path.isdir(c):
                cache_dir = c
                break
    os.makedirs(cache_dir, exist_ok=True)

    target_device = requested_device
    pipe = None
    try:
        pipe = ovg.WhisperPipeline(model_path, target_device, CACHE_DIR=cache_dir)
    except Exception as e_initial:
        fallback_devices = ["GPU", "CPU"] if target_device == "NPU" else ["CPU"]
        for fb in fallback_devices:
            try:
                pipe = ovg.WhisperPipeline(model_path, fb, CACHE_DIR=cache_dir)
                target_device = fb
                break
            except Exception:
                pass
        if pipe is None:
            raise e_initial

    spk_mgr = None
    try:
        spk_mgr = SpeakerManager()
    except Exception as e:
        print(json.dumps({"type": "warning", "message": f"Could not initialize SpeakerManager: {e}"}), flush=True)

    effective_device = target_device
    is_fallback = requested_device.upper() not in effective_device.upper()
    load_time_ms = int((time.perf_counter() - load_start) * 1000)
    return pipe, spk_mgr, os.path.basename(model_path), requested_device, effective_device, is_fallback, load_time_ms


def transcribe_file(file_path, language_arg, translate_to_arg, pipe, spk_mgr, model_name, requested_device, effective_device, is_fallback, load_time_ms):
    translate_to = (translate_to_arg or "none").strip().lower()

    if not file_path or not os.path.exists(file_path):
        print(json.dumps({"type": "error", "message": f"Audio file not found: {file_path}"}), flush=True)
        return

    try:
        audio, sr = sf.read(file_path)
        if len(audio.shape) > 1:
            audio = audio.mean(axis=1)
        if sr != 16000:
            try:
                g = gcd(16000, int(sr))
                audio = scipy.signal.resample_poly(audio, 16000 // g, int(sr) // g).astype(np.float32)
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

    print(json.dumps({
        "type": "model_loaded",
        "device": effective_device,
        "requested_device": requested_device,
        "effective_device": effective_device,
        "fallback": is_fallback,
        "model": model_name,
        "load_time_ms": load_time_ms
    }), flush=True)

    infer_start = time.perf_counter()
    try:
        if spk_mgr is not None:
            try:
                spk_mgr.profiles = spk_mgr._load_profiles()
                spk_mgr.session_speakers = []
            except Exception:
                pass

        config = pipe.get_generation_config()
        if language_arg and language_arg.lower() not in ("auto", "<|auto|>", "none", ""):
            config.language = language_arg if language_arg.startswith("<|") else f"<|{language_arg}|>"
        else:
            config.language = None
        config.task = "transcribe"
        forced_lang = lang_code(config.language)

        turns = spk_mgr.diarize_audio_turns(audio, sr=16000) if spk_mgr is not None else []
        accumulated_text = ""
        lang_votes = []

        if turns:
            config.return_timestamps = False
            for turn in turns:
                s_idx = turn["start_sample"]
                e_idx = turn["end_sample"]
                if e_idx <= s_idx:
                    continue
                turn_audio = audio[s_idx:e_idx]
                expected_lang = lang_votes[-1] if lang_votes else "fr"
                text, chunk_lang, _ = smart_transcribe_turn(pipe, turn_audio, forced_lang=forced_lang, expected_lang=expected_lang)
                if is_hallucinated_or_junk(text):
                    continue

                accumulated_text += " " + text
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
        print(json.dumps({"type": "error", "message": f"Inference error on {effective_device}: {str(e)}"}), flush=True)


def main():
    parser = argparse.ArgumentParser(description="Real OpenVINO Whisper Transcription & Speaker Diarization Service")
    parser.add_argument("--file", required=False, help="Path to audio file (WAV, MP3, FLAC, etc.)")
    parser.add_argument("--daemon", action="store_true", help="Keep Whisper pipeline warm in memory and accept JSON jobs on stdin")
    parser.add_argument("--device", default="NPU", choices=["NPU", "GPU", "CPU"], help="Hardware accelerator")
    parser.add_argument("--language", default="<|fr|>", help="Language token, e.g. <|fr|> or <|en|>")
    parser.add_argument("--model_dir", default="models/whisper-small-fp16-ov", help="Path to OpenVINO Whisper model")
    parser.add_argument("--translate_to", default="none", help="Langue cible de traduction Whisper ('en') ou 'none'.")
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")

    try:
        pipe, spk_mgr, model_name, requested_device, effective_device, is_fallback, load_time_ms = load_whisper_and_speaker(args.model_dir, args.device)
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Error loading model on {args.device}: {str(e)}"}), flush=True)
        return

    if args.daemon:
        print(json.dumps({
            "type": "daemon_ready",
            "device": effective_device,
            "requested_device": requested_device,
            "effective_device": effective_device,
            "fallback": is_fallback,
            "model": model_name,
            "load_time_ms": load_time_ms
        }), flush=True)

        first_job_load_ms = 0
        for raw_line in sys.stdin:
            line = raw_line.strip()
            if not line:
                continue
            if line.lower() in ("stop", "quit", "exit"):
                break
            try:
                job = json.loads(line)
                job_file = job.get("file", "")
                job_lang = job.get("language", args.language)
                job_tr = job.get("translate_to", args.translate_to)
                transcribe_file(
                    job_file,
                    job_lang,
                    job_tr,
                    pipe,
                    spk_mgr,
                    model_name,
                    requested_device,
                    effective_device,
                    is_fallback,
                    first_job_load_ms
                )
            except Exception as ex:
                print(json.dumps({"type": "error", "message": str(ex)}), flush=True)
    else:
        if not args.file:
            print(json.dumps({"type": "error", "message": "Missing --file argument"}), flush=True)
            return
        transcribe_file(
            args.file,
            args.language,
            args.translate_to,
            pipe,
            spk_mgr,
            model_name,
            requested_device,
            effective_device,
            is_fallback,
            load_time_ms
        )


if __name__ == "__main__":
    main()
