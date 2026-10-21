import argparse
import collections
import json
import os
import queue
import sys
import threading
import time
import numpy as np
import sounddevice as sd
import soundfile as sf
import openvino_genai as ovg
for _lvl in range(1, 6):
    _cand_root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *(['..'] * _lvl)))
    if os.path.exists(os.path.join(_cand_root, "lang_utils.py")) and _cand_root not in sys.path:
        sys.path.insert(0, _cand_root)
        break
from speaker_service import SpeakerManager
from lang_utils import detect_language, lang_code, whisper_translate_to_english, smart_transcribe_turn, is_hallucinated_or_junk

def main():
    parser = argparse.ArgumentParser(description="Real-Time Streaming Speech Transcription & Diarization")
    parser.add_argument("--output", required=True, help="Path to output WAV file")
    parser.add_argument("--device", default="NPU", choices=["NPU", "GPU", "CPU"], help="Whisper compute device")
    parser.add_argument("--language", default="<|fr|>", help="Language token (<|fr|>, <|en|>, etc.)")
    parser.add_argument("--model_dir", default="models/whisper-small-fp16-ov", help="Path to OpenVINO Whisper model")
    parser.add_argument("--samplerate", type=int, default=16000, help="Audio sample rate (default 16000)")
    parser.add_argument("--translate_to", default="none", help="Langue cible de traduction Whisper ('en') ou 'none'. Les autres langues sont traduites côté application (LLM).")
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")

    out_path = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    script_dir = os.path.dirname(os.path.abspath(__file__))

    # 1. Initialize Whisper Pipeline
    load_start = time.perf_counter()
    model_path = args.model_dir
    if not os.path.isabs(model_path) or not os.path.exists(os.path.join(model_path, "openvino_encoder_model.xml")):
        cur = script_dir
        found = False
        for _ in range(6):
            # small (meilleur) puis base puis tiny : on prend le premier modèle réellement installé
            for name in ("whisper-small-fp16-ov", "whisper-base-fp16-ov", "whisper-tiny-fp16-ov"):
                cand = os.path.abspath(os.path.join(cur, "models", name))
                if os.path.isdir(cand) and os.path.exists(os.path.join(cand, "openvino_encoder_model.xml")):
                    model_path = cand
                    found = True
                    break
            if found:
                break
            parent = os.path.dirname(cur)
            if parent == cur: break
            cur = parent

    cache_dir = os.path.abspath(os.path.join(script_dir, "cache"))
    if not os.path.exists(cache_dir):
        for plevel in range(1, 6):
            c = os.path.abspath(os.path.join(script_dir, *(['..'] * plevel), "cache"))
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
        # Fallback automatique matériel universel si le NPU ou le GPU n'est pas disponible (ex: AMD, Qualcomm)
        print(json.dumps({"type": "info", "message": f"Périphérique '{target_device}' non disponible sous OpenVINO ({e_initial}). Repli fluide en cours..."}, ensure_ascii=False), flush=True)
        fallback_devices = ["GPU", "CPU"] if target_device == "NPU" else ["CPU"]
        for fb in fallback_devices:
            try:
                pipe = ovg.WhisperPipeline(model_path, fb)
                target_device = fb
                args.device = fb
                print(json.dumps({"type": "info", "message": f"Whisper chargé avec succès sur le matériel alternatif '{fb}'."}, ensure_ascii=False), flush=True)
                break
            except Exception:
                pass
        if pipe is None:
            print(json.dumps({"type": "error", "message": f"Échec d'initialisation Whisper : {e_initial}"}, ensure_ascii=False), flush=True)
            sys.exit(1)

    effective_device = target_device
    is_fallback = requested_device.upper() not in effective_device.upper()

    try:
        config = pipe.get_generation_config()
        if args.language and args.language.lower() not in ("auto", "<|auto|>", "none", ""):
            config.language = args.language if args.language.startswith("<|") else f"<|{args.language}|>"
        else:
            config.language = None
        config.task = "transcribe"
        config.return_timestamps = False
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Configuration Whisper : {e}"}, ensure_ascii=False), flush=True)
        sys.exit(1)

    load_time_ms = int((time.perf_counter() - load_start) * 1000)

    # 2. Initialize Speaker Manager
    spk_mgr = None
    try:
        spk_mgr = SpeakerManager()
    except Exception as e:
        print(json.dumps({"type": "warning", "message": f"SpeakerManager init: {e}"}), flush=True)

    # 3. Audio file writer
    try:
        wav_file = sf.SoundFile(
            out_path,
            mode="w",
            samplerate=args.samplerate,
            channels=1,
            subtype="PCM_16"
        )
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Could not create WAV file: {e}"}), flush=True)
        sys.exit(1)

    # 4. State & Communication
    stop_event = threading.Event()
    audio_queue = queue.Queue()
    total_captured_samples = 0
    lock = threading.Lock()

    # VAD & Streaming Settings
    FRAME_SIZE = 1600  # 100ms at 16kHz
    PRE_SPEECH_FRAMES = 3   # 300ms pre-buffer
    SILENCE_TRIGGER_FRAMES = 7  # 700ms pause triggers phrase end (avoids chopping mid-sentence breaths)
    MAX_PHRASE_SAMPLES = int(args.samplerate * 6.5)  # 6.5s max phrase length
    MIN_PHRASE_SAMPLES = int(args.samplerate * 0.85)  # 0.85s min phrase length
    SPEECH_RMS_THRESHOLD = 0.014  # Minimum energy for speech
    session_lang_votes = collections.deque(["fr", "fr"], maxlen=7)

    # Ring buffer for pre-speech frames
    pre_speech_ring = collections.deque(maxlen=PRE_SPEECH_FRAMES)

    print(json.dumps({
        "type": "live_ready",
        "device": effective_device,
        "requested_device": requested_device,
        "effective_device": effective_device,
        "fallback": is_fallback,
        "model": os.path.basename(model_path),
        "load_time_ms": load_time_ms,
        "output": out_path
    }), flush=True)

    def audio_callback(indata, frames, time_info, status):
        # indata is (frames, 1) float32
        wav_file.write(indata)
        audio_queue.put(indata.copy().flatten())

    try:
        stream = sd.InputStream(
            samplerate=args.samplerate,
            channels=1,
            dtype="float32",
            blocksize=FRAME_SIZE,
            callback=audio_callback
        )
        stream.start()
    except Exception as e:
        wav_file.close()
        print(json.dumps({"type": "error", "message": f"Microphone stream error: {e}"}), flush=True)
        sys.exit(1)

    translate_state = {"to": (args.translate_to or "none").strip().lower()}

    # Stdin thread for clean stop
    def stdin_listener():
        while not stop_event.is_set():
            try:
                line = sys.stdin.readline()
                if not line:
                    stop_event.set()
                    break
                cmd = line.strip().lower()
                if cmd in ("stop", "quit", "exit"):
                    try:
                        stream.stop()
                    except Exception:
                        pass
                    stop_event.set()
                    break
                elif cmd.startswith("translate "):
                    translate_state["to"] = cmd.split(maxsplit=1)[1].strip().lower()
                    print(json.dumps({"type": "translate_updated", "translate_to": translate_state["to"]}), flush=True)
                elif cmd.startswith("language "):
                    parts = line.strip().split(maxsplit=1)
                    if len(parts) > 1:
                        new_lang = parts[1].strip()
                        if new_lang.lower() in ("auto", "<|auto|>", "none", ""):
                            config.language = None
                        else:
                            config.language = new_lang if new_lang.startswith("<|") else f"<|{new_lang}|>"
                        print(json.dumps({"type": "language_updated", "language": str(config.language)}), flush=True)
            except Exception:
                stop_event.set()
                break

    t_stdin = threading.Thread(target=stdin_listener, daemon=True)
    t_stdin.start()

    # Main VAD & Ingestion Loop
    in_speech = False
    silence_count = 0
    speech_frames = []
    phrase_start_sample = 0
    accumulated_samples = 0
    last_volume_emit = time.time()

    def process_and_emit_segment(samples_list, start_sample, end_sample):
        if not samples_list:
            return
        full_chunk = np.concatenate(samples_list).astype(np.float32)
        if len(full_chunk) < MIN_PHRASE_SAMPLES:
            return

        sub_turns = [(0, len(full_chunk))]
        if spk_mgr is not None:
            try:
                sub_turns = spk_mgr.split_speaker_turns_live(full_chunk, sr=args.samplerate)
            except Exception:
                sub_turns = [(0, len(full_chunk))]

        for s_off, e_off in sub_turns:
            audio_chunk = full_chunk[s_off:e_off]
            if len(audio_chunk) < int(args.samplerate * 0.35):
                continue

            start_sec = round((start_sample + s_off) / float(args.samplerate), 2)
            end_sec = round((start_sample + e_off) / float(args.samplerate), 2)

            t0 = time.perf_counter()
            detected_lang = "fr"
            try:
                forced = lang_code(config.language)
                expected_lang = collections.Counter(session_lang_votes).most_common(1)[0][0] if session_lang_votes else "fr"
                text, detected_lang, _ = smart_transcribe_turn(pipe, audio_chunk, forced_lang=forced, expected_lang=expected_lang)
                if not forced and text and not is_hallucinated_or_junk(text):
                    session_lang_votes.append(detected_lang)
                    if (end_sec - start_sec) >= 2.8:
                        session_lang_votes.append(detected_lang)
            except Exception:
                text = ""

            infer_ms = int((time.perf_counter() - t0) * 1000)

            clean_text = text.strip()
            if is_hallucinated_or_junk(clean_text):
                continue

            spk_id = "spk_1"
            spk_name = "Intervenant 1"
            spk_color = "#3B82F6"
            is_known = False
            is_tpm_certified = False
            confidence = 1.0
            embedding = None

            if spk_mgr is not None:
                try:
                    spk_info = spk_mgr.identify_or_cluster(audio_chunk, sr=args.samplerate)
                    spk_id = spk_info["speaker_id"]
                    spk_name = spk_info["speaker_name"]
                    spk_color = spk_info["speaker_color"]
                    is_known = spk_info.get("is_known", False)
                    is_tpm_certified = spk_info.get("is_tpm_certified", False)
                    confidence = spk_info.get("confidence", 1.0)
                    embedding = spk_info.get("embedding", None)
                except Exception:
                    is_tpm_certified = False

            translation = None
            translation_lang = None
            if translate_state["to"] == "en" and detected_lang != "en":
                translation = whisper_translate_to_english(pipe, audio_chunk.tolist(), detected_lang)
                if translation:
                    translation_lang = "en"

            print(json.dumps({
                "type": "live_segment",
                "device": effective_device,
                "requested_device": requested_device,
                "effective_device": effective_device,
                "fallback": is_fallback,
                "start_ts": start_sec,
                "end_ts": end_sec,
                "speaker_id": spk_id,
                "speaker_name": spk_name,
                "speaker_color": spk_color,
                "is_known": is_known,
                "is_tpm_certified": is_tpm_certified,
                "confidence": confidence,
                "embedding": embedding,
                "infer_ms": infer_ms,
                "detected_language": detected_lang,
                "translation": translation,
                "translation_lang": translation_lang,
                "text": clean_text
            }, ensure_ascii=False), flush=True)

    while not stop_event.is_set() or not audio_queue.empty():
        try:
            frame = audio_queue.get(timeout=0.1)
        except queue.Empty:
            if stop_event.is_set():
                break
            continue

        frame_samples = len(frame)
        accumulated_samples += frame_samples
        rms = float(np.sqrt(np.mean(frame**2))) if frame_samples > 0 else 0.0

        # Periodic volume & state emit every 300ms
        now = time.time()
        if now - last_volume_emit >= 0.3:
            dur_sec = round(accumulated_samples / float(args.samplerate), 1)
            print(json.dumps({
                "type": "speech_state",
                "is_speaking": in_speech,
                "duration_sec": dur_sec,
                "level": round(rms, 3)
            }), flush=True)
            last_volume_emit = now

        is_voice = (rms >= SPEECH_RMS_THRESHOLD)

        if is_voice:
            if not in_speech:
                in_speech = True
                phrase_start_sample = max(0, accumulated_samples - frame_samples - (len(pre_speech_ring) * FRAME_SIZE))
                # Add pre-speech frames for smooth consonant capture
                speech_frames = list(pre_speech_ring)
            silence_count = 0
            speech_frames.append(frame)

            # Check if phrase duration exceeded max cap (4.5s)
            current_phrase_len = sum(len(f) for f in speech_frames)
            if current_phrase_len >= MAX_PHRASE_SAMPLES:
                process_and_emit_segment(speech_frames, phrase_start_sample, accumulated_samples)
                speech_frames = []
                in_speech = False
                silence_count = 0
        else:
            pre_speech_ring.append(frame)
            if in_speech:
                silence_count += 1
                speech_frames.append(frame)

                # Silence pause reached -> complete phrase
                if silence_count >= SILENCE_TRIGGER_FRAMES:
                    process_and_emit_segment(speech_frames, phrase_start_sample, accumulated_samples)
                    speech_frames = []
                    in_speech = False
                    silence_count = 0

    # Flush final remaining speech buffer upon stop
    if in_speech and speech_frames:
        process_and_emit_segment(speech_frames, phrase_start_sample, accumulated_samples)

    # Cleanup
    try:
        stream.stop()
        stream.close()
    except Exception:
        pass

    try:
        wav_file.flush()
        wav_file.close()
    except Exception:
        pass

    final_duration = round(accumulated_samples / float(args.samplerate), 2)
    print(json.dumps({
        "type": "live_stopped",
        "file": out_path,
        "duration_sec": final_duration
    }), flush=True)

if __name__ == "__main__":
    main()
