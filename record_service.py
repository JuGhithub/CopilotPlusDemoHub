import argparse
import json
import os
import sys
import threading
import time
import numpy as np
import sounddevice as sd
import soundfile as sf

def main():
    parser = argparse.ArgumentParser(description="Real Microphone Audio Recording Service")
    parser.add_argument("--output", required=True, help="Destination WAV file path")
    parser.add_argument("--samplerate", type=int, default=16000, help="Audio sample rate (default 16000 Hz)")
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")

    out_path = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    stop_event = threading.Event()
    total_samples = 0
    lock = threading.Lock()
    current_level = 0.0

    try:
        dev_info = sd.query_devices(kind="input")
        dev_name = dev_info.get("name", "Default Microphone")
    except Exception:
        dev_name = "Default Microphone"

    try:
        wav_file = sf.SoundFile(
            out_path,
            mode="w",
            samplerate=args.samplerate,
            channels=1,
            subtype="PCM_16"
        )
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Could not create audio file: {e}"}), flush=True)
        sys.exit(1)

    def audio_callback(indata, frames, time_info, status):
        nonlocal total_samples, current_level
        if status:
            pass
        # indata is (frames, channels) float32
        wav_file.write(indata)
        with lock:
            total_samples += frames
            rms = float(np.sqrt(np.mean(np.square(indata))))
            current_level = rms

    try:
        stream = sd.InputStream(
            samplerate=args.samplerate,
            channels=1,
            dtype="float32",
            callback=audio_callback
        )
        stream.start()
    except Exception as e:
        wav_file.close()
        print(json.dumps({"type": "error", "message": f"Could not start microphone stream: {e}"}), flush=True)
        sys.exit(1)

    print(json.dumps({
        "type": "recording_started",
        "file": out_path,
        "device": dev_name,
        "samplerate": args.samplerate
    }), flush=True)

    # Thread to read stdin for stop signal
    def stdin_listener():
        while not stop_event.is_set():
            try:
                line = sys.stdin.readline()
                if not line:
                    stop_event.set()
                    break
                cmd = line.strip().lower()
                if cmd in ("stop", "quit", "exit"):
                    stop_event.set()
                    break
            except Exception:
                stop_event.set()
                break

    t_listener = threading.Thread(target=stdin_listener, daemon=True)
    t_listener.start()

    # Progress loop
    try:
        while not stop_event.is_set():
            time.sleep(0.4)
            with lock:
                dur = total_samples / float(args.samplerate)
                lvl = current_level
            print(json.dumps({
                "type": "recording_progress",
                "duration_sec": round(dur, 1),
                "level": round(lvl, 3)
            }), flush=True)
    except KeyboardInterrupt:
        stop_event.set()

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

    final_duration = total_samples / float(args.samplerate)
    print(json.dumps({
        "type": "recording_stopped",
        "file": out_path,
        "duration_sec": round(final_duration, 2)
    }), flush=True)

if __name__ == "__main__":
    main()
