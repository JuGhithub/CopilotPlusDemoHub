import argparse
import json
import os
import sys
import time
import numpy as np
from PIL import Image
import openvino as ov
import openvino_genai as ovg

GENDER_STYLE_PROMPTS = {
    "male": {
        "cartoon": (
            "handsome adult man, masculine facial features, sharp defined jawline, "
            "stylish 3D animated cartoon male character, charming 3D animated movie hero, "
            "3D cartoon animation studio lighting, subsurface scattering skin, vibrant colors, 8k render, masterpiece"
        ),
        "anime": (
            "handsome male anime hero, young adult man, masculine features, defined jawline, "
            "studio ghibli aesthetic, anime movie art, warm soft cinematic lighting, clean sharp linework, masterpiece"
        ),
        "cyberpunk": (
            "handsome male cyberpunk character, adult man, masculine jawline, "
            "futuristic neon sci-fi animation, glowing tech details, volumetric lighting, 8k 3D render, cinematic masterpiece"
        ),
        "vintage_60s": (
            "authentic vintage 1960s color photograph portrait of a handsome adult man, "
            "classic 60s masculine hairstyle and suit, warm kodachrome film grain, sepia patina, analog photography, 1960s soft lighting, historical masterpiece"
        )
    },
    "female": {
        "cartoon": (
            "beautiful adult woman, feminine facial features, graceful smile, "
            "stylish 3D animated cartoon female character, charming 3D animated movie heroine, "
            "3D cartoon animation studio lighting, subsurface scattering skin, vibrant colors, 8k render, masterpiece"
        ),
        "anime": (
            "beautiful female anime heroine, young adult woman, feminine features, graceful expressive eyes, "
            "studio ghibli aesthetic, anime movie art, warm soft cinematic lighting, clean linework, masterpiece"
        ),
        "cyberpunk": (
            "beautiful female cyberpunk character, adult woman, feminine features, "
            "futuristic neon sci-fi animation, glowing tech details, volumetric lighting, 8k 3D render, cinematic masterpiece"
        ),
        "vintage_60s": (
            "authentic vintage 1960s color photograph portrait of an elegant adult woman, "
            "classic 60s retro hairstyle and fashion, warm kodachrome film grain, sepia patina, analog photography, 1960s soft lighting, historical masterpiece"
        )
    }
}

# Compatibility alias for pixar style
for _g in GENDER_STYLE_PROMPTS:
    GENDER_STYLE_PROMPTS[_g]["pixar"] = GENDER_STYLE_PROMPTS[_g]["cartoon"]

# Compatibility alias
STYLE_PROMPTS = GENDER_STYLE_PROMPTS["male"]

def detect_gender(image_path: str, raw_img: Image.Image) -> str:
    """Automatically detect gender to preserve facial likeness and prevent gender drift."""
    name_lower = os.path.basename(image_path).lower()
    if any(k in name_lower for k in ["homme", "man", "male", "garcon", "boy", "guy", "mr", "sir"]):
        return "male"
    if any(k in name_lower for k in ["femme", "woman", "female", "fille", "girl", "lady", "mme", "madame"]):
        return "female"

    try:
        import cv2
        arr = np.array(raw_img.convert("RGB"))
        h, w, _ = arr.shape
        # Lower face chin / jaw area: center horizontal, bottom 55% to 80%
        chin = arr[int(h * 0.55):int(h * 0.80), int(w * 0.35):int(w * 0.65)]
        gray_chin = cv2.cvtColor(chin, cv2.COLOR_RGB2GRAY)
        lap_chin = cv2.Laplacian(gray_chin, cv2.CV_64F).var()

        # Upper cheek smooth skin area
        cheek = arr[int(h * 0.40):int(h * 0.55), int(w * 0.25):int(w * 0.40)]
        gray_cheek = cv2.cvtColor(cheek, cv2.COLOR_RGB2GRAY)
        lap_cheek = cv2.Laplacian(gray_cheek, cv2.CV_64F).var()

        ratio = lap_chin / (lap_cheek + 1e-5)
        # Men with stubble or defined jawline have significantly higher texture variance
        if ratio >= 2.0 or lap_chin > 900:
            return "male"
        else:
            return "female"
    except Exception:
        return "male"

def apply_vintage_60s_filter(img: Image.Image) -> Image.Image:
    """Enhance vintage 1960s analog look with warm Kodachrome tint and subtle analog patina."""
    try:
        arr = np.array(img, dtype=np.float32)
        # Kodachrome 60s warm tone curve: boost warm channels, soften deep blues, add subtle vintage patina
        r = arr[:, :, 0] * 1.05 + 6.0
        g = arr[:, :, 1] * 0.98 + 3.0
        b = arr[:, :, 2] * 0.88 - 2.0
        vintage_arr = np.clip(np.dstack([r, g, b]), 0, 255).astype(np.uint8)
        return Image.fromarray(vintage_arr)
    except Exception:
        return img

def find_model_dir(base_dir, model_name="lcm_dreamshaper_8bit"):
    cur = base_dir
    for _ in range(7):
        cand = os.path.abspath(os.path.join(cur, "models", model_name))
        if os.path.isdir(cand) and os.path.exists(os.path.join(cand, "model_index.json")):
            return cand
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return None

def crop_center_square(img: Image.Image) -> Image.Image:
    w, h = img.size
    min_dim = min(w, h)
    left = (w - min_dim) // 2
    top = (h - min_dim) // 2
    right = left + min_dim
    bottom = top + min_dim
    return img.crop((left, top, right, bottom))

def main():
    parser = argparse.ArgumentParser(description="Copilot+ AI Avatar & 3D Cartoon Stylization Service")
    parser.add_argument("--image", required=True, help="Input portrait image path")
    parser.add_argument("--output", required=True, help="Output image destination path")
    style_choices = ["cartoon", "pixar", "anime", "cyberpunk", "vintage_60s", "comic"]
    parser.add_argument("--style", default="cartoon", choices=style_choices, help="Style preset")
    parser.add_argument("--gender", default="auto", choices=["auto", "male", "female"], help="Gender preservation guidance")
    parser.add_argument("--strength", type=float, default=0.45, help="Transformation strength (0.30 to 0.75)")
    parser.add_argument("--steps", type=int, default=4, help="Inference steps (default: 4 for LCM)")
    parser.add_argument("--guidance", type=float, default=1.5, help="Guidance scale (default: 1.5)")
    parser.add_argument("--device", default="NPU", choices=["NPU", "GPU", "CPU"], help="Target compute silicon")
    parser.add_argument("--model_dir", default=None, help="Explicit path to LCM model")
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")
    script_dir = os.path.dirname(os.path.abspath(__file__))

    # 1. Resolve Model Directory based on device
    model_path = args.model_dir
    cache_dir = None
    if not model_path or not os.path.exists(model_path):
        if args.device == "NPU":
            model_path = find_model_dir(script_dir, "lcm_dreamshaper_static")
            if not model_path:
                model_path = find_model_dir(script_dir, "lcm_dreamshaper_8bit")
        else:
            model_path = find_model_dir(script_dir, "lcm_dreamshaper_8bit")
            if not model_path:
                model_path = find_model_dir(script_dir, "lcm_dreamshaper_static")

    if not model_path or not os.path.exists(model_path):
        print(json.dumps({
            "type": "error",
            "message": "Modèle LCM introuvable (lcm_dreamshaper_static ou lcm_dreamshaper_8bit)."
        }), flush=True)
        sys.exit(1)

    if args.device == "NPU":
        cache_candidate = os.path.abspath(os.path.join(os.path.dirname(model_path), "npu_cache"))
        os.makedirs(cache_candidate, exist_ok=True)
        cache_dir = cache_candidate

    # 2. Check input image
    inp_img_path = os.path.abspath(args.image)
    if not os.path.exists(inp_img_path):
        print(json.dumps({
            "type": "error",
            "message": f"Image source introuvable : {inp_img_path}"
        }), flush=True)
        sys.exit(1)

    out_img_path = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(out_img_path), exist_ok=True)

    # 3. Load input image and preprocess (Center-crop square to 512x512)
    try:
        raw_img = Image.open(inp_img_path).convert("RGB")
        square_img = crop_center_square(raw_img)
        resized_img = square_img.resize((512, 512), Image.Resampling.LANCZOS)
        img_array = np.array(resized_img)[None]  # Shape [1, 512, 512, 3]
        input_tensor = ov.Tensor(img_array)
    except Exception as e:
        print(json.dumps({
            "type": "error",
            "message": f"Erreur de lecture de l'image source : {e}"
        }), flush=True)
        sys.exit(1)

    # 4. Load OpenVINO Image2ImagePipeline
    print(json.dumps({
        "type": "loading",
        "device": args.device,
        "model": os.path.basename(model_path)
    }), flush=True)

    t_load_start = time.perf_counter()
    try:
        if args.device == "NPU" and cache_dir:
            pipe = ovg.Image2ImagePipeline(model_path, "NPU", **{"CACHE_DIR": cache_dir})
        else:
            pipe = ovg.Image2ImagePipeline(model_path, args.device)
    except Exception as e:
        # Fallback to GPU or CPU if initialization fails
        fallback_done = False
        if args.device == "NPU":
            try:
                print(json.dumps({"type": "info", "message": f"NPU non disponible, repli sur GPU ({e})"}), flush=True)
                gpu_model = find_model_dir(script_dir, "lcm_dreamshaper_8bit") or model_path
                pipe = ovg.Image2ImagePipeline(gpu_model, "GPU")
                args.device = "GPU"
                fallback_done = True
            except Exception:
                pass

        if not fallback_done and args.device in ["GPU", "NPU"]:
            try:
                pipe = ovg.Image2ImagePipeline(model_path, "CPU")
                args.device = "CPU"
                fallback_done = True
            except Exception as e2:
                print(json.dumps({"type": "error", "message": f"Échec de chargement : {e2}"}), flush=True)
                sys.exit(1)
        elif not fallback_done:
            print(json.dumps({"type": "error", "message": f"Échec de chargement : {e}"}), flush=True)
            sys.exit(1)

    load_time_ms = int((time.perf_counter() - t_load_start) * 1000)

    # 5. Determine Subject Gender & Generate Image
    effective_gender = args.gender
    if effective_gender == "auto":
        effective_gender = detect_gender(inp_img_path, raw_img)

    target_style = "vintage_60s" if args.style == "comic" else args.style
    gender_dict = GENDER_STYLE_PROMPTS.get(effective_gender, GENDER_STYLE_PROMPTS["male"])
    prompt = gender_dict.get(target_style, gender_dict.get("cartoon"))

    print(json.dumps({
        "type": "generating",
        "style": target_style,
        "gender": effective_gender,
        "device": args.device,
        "load_time_ms": load_time_ms,
        "strength": round(args.strength, 2),
        "steps": args.steps
    }), flush=True)

    t_gen_start = time.perf_counter()
    try:
        res = pipe.generate(
            prompt=prompt,
            image=input_tensor,
            strength=float(args.strength),
            num_inference_steps=int(args.steps),
            guidance_scale=float(args.guidance)
        )
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Erreur lors de la génération IA : {e}"}), flush=True)
        sys.exit(1)

    infer_time_ms = int((time.perf_counter() - t_gen_start) * 1000)

    # 6. Save Output Image
    try:
        out_img = Image.fromarray(res.data[0])
        if target_style == "vintage_60s":
            out_img = apply_vintage_60s_filter(out_img)
        out_img.save(out_img_path, quality=95)
    except Exception as e:
        print(json.dumps({"type": "error", "message": f"Erreur de sauvegarde de l'image : {e}"}), flush=True)
        sys.exit(1)

    print(json.dumps({
        "type": "complete",
        "output": out_img_path,
        "device": args.device,
        "style": target_style,
        "gender": effective_gender,
        "load_time_ms": load_time_ms,
        "infer_time_ms": infer_time_ms,
        "steps": args.steps,
        "strength": args.strength
    }, ensure_ascii=False), flush=True)

if __name__ == "__main__":
    main()
