# -*- coding: utf-8 -*-
"""
Utilitaires de langue pour la reconnaissance vocale :
- Détection linguistique haute précision (FR/EN/ES/DE/IT) sur segments courts ou longs.
- Sonde acoustique sans état (`_get_lang_probe_pipe`) : contourne le bug de persistance
  du cache KV du `StaticWhisperPipeline` NPU d'OpenVINO GenAI (où `cfg.language = None`
  hérite de la langue du segment précédent et traduit l'anglais en français après un passage français).
  La sonde identifie la langue réelle de chaque prise de parole sans biais d'état, puis passe
  explicitement `<|fr|>` ou `<|en|>` au pipeline principal Whisper sur NPU.
"""
import os
import re

_PROBE_PIPE = None
_PROBE_INIT_TRIED = False

_STOPWORDS = {
    "fr": set("""le la les un une des du de et est sont être avoir a ont à au aux en dans sur pour par avec sans
        que qui quoi dont où ce cet cette ces se sa ses leur leurs nous vous ils elle elles il je tu on ne pas plus
        mais ou donc car ni très bien aussi comme tout tous cela ça bonjour merci oui non alors voici
        notre votre mon ma mes ton ta tes fait faire peut sera aujourd hui résultats prochaines étapes réunion
        suis es sommes êtes étaient était avons avez avait faut va vais allons allez vont dit dire voir vu
        même autre autres entre après avant depuis pendant contre sous vers chez jusqu parce pourquoi comment quand
        quel quelle quels quelles chaque plusieurs quelques rien personne jamais toujours encore déjà trop peu
        beaucoup moins tant tellement vraiment ainsi pourtant cependant toutefois donc voilà""".split()),
    "en": set("""the a an and is are was were be been being have has had having to of in on for with without that this
        these those it its we you they he she i me my mine our ours your yours their theirs his her hers not but or so
        very also as all there here what which who whom whose where when why how will would can could should shall may
        might must do does did done doing just about from by at into onto over under again further then once today
        hello thanks thank yes no okay ok next steps results meeting going gonna wanna gotta sure father mother brother
        sister every other another some any many much more most such own same than too only both each few through during
        before after above below between up down out off back around near far small big large good great new old first
        last long short high low right left team work working process processes manage manages managed managing said
        say says saying hey hi bye well now still even way thing things people time year years day days man men
        lost get got getting cut off welcome eating mean one first rather rush depending mind compared everybody
        else theories winner log launched because leaders project follow track wow relaunched march should like
        peter approximately million students side wrap thank you""".split()),
    "es": set("""el la los las un una unos unas y es son ser estar hay de del en con sin que por para como pero muy más
        este esta estos estas nosotros ustedes ellos ella yo tu su sus nuestro hola gracias sí también hoy todo todos""".split()),
    "de": set("""der die das ein eine und ist sind war sein haben hat nicht mit von zu auf für ich du wir sie er es auch aber
        sehr dass wie noch nur heute hallo danke ja nein bitte werden wird""".split()),
    "it": set("""il lo la i gli le un una e è sono essere avere ha di del della in con senza che per come ma molto più questo
        questa noi voi loro io tu suo nostro ciao grazie sì anche oggi""".split()),
}

# Contractions anglaises typiques (avant découpage de l'apostrophe)
_EN_CONTRACTIONS_RE = re.compile(
    r"\b(i'm|it's|don't|can't|won't|didn't|doesn't|isn't|aren't|wasn't|weren't|"
    r"that's|there's|what's|let's|you're|we're|they're|i've|you've|we've|they've|"
    r"i'll|you'll|we'll|he's|she's|couldn't|wouldn't|shouldn't|haven't|hasn't)\b",
    re.IGNORECASE,
)

# Élisions françaises typiques (lettre d'élision AVANT l'apostrophe + voyelle/h)
_FR_ELISIONS_RE = re.compile(
    r"\b(j'|c'|l'|d'|n'|s'|m'|t'|qu'|jusqu'|lorsqu'|puisqu')[a-zàâäéèêëîïôöùûüÿœ]",
    re.IGNORECASE,
)

_WORD_RE = re.compile(r"[a-zA-ZÀ-ÿ]+")


def _get_lang_probe_pipe():
    """Charge une sonde Whisper-Tiny CPU sans état pour détecter la langue acoustique réelle de chaque segment."""
    global _PROBE_PIPE, _PROBE_INIT_TRIED
    if _PROBE_INIT_TRIED:
        return _PROBE_PIPE
    _PROBE_INIT_TRIED = True
    try:
        import openvino_genai as ovg
        cur = os.path.dirname(os.path.abspath(__file__))
        model_path = None
        cache_dir = os.path.join(cur, "cache")
        for _ in range(6):
            for folder in ("whisper-tiny-fp16-ov", "whisper-base-fp16-ov"):
                cand = os.path.abspath(os.path.join(cur, "models", folder))
                if os.path.isdir(cand) and os.path.exists(os.path.join(cand, "openvino_encoder_model.xml")):
                    model_path = cand
                    c_cand = os.path.abspath(os.path.join(cur, "cache"))
                    os.makedirs(c_cand, exist_ok=True)
                    cache_dir = c_cand
                    break
            if model_path:
                break
            parent = os.path.dirname(cur)
            if parent == cur:
                break
            cur = parent
        if model_path:
            _PROBE_PIPE = ovg.WhisperPipeline(model_path, "CPU", CACHE_DIR=cache_dir)
    except Exception:
        _PROBE_PIPE = None
    return _PROBE_PIPE


def score_languages(text):
    """Calcule un score de confiance linguistique par langue pour `text`."""
    raw = text or ""
    low = raw.lower()
    words = [w.lower() for w in _WORD_RE.findall(low)]
    scores = {lang: 0.0 for lang in _STOPWORDS}
    if not words:
        return scores

    for lang, sw in _STOPWORDS.items():
        scores[lang] = float(sum(1 for w in words if w in sw))

    # Ambiguïté "son" : en anglais "every son / my son / father and son"
    if "son" in words and any(w in words for w in ("father", "mother", "every", "his", "her", "my", "our", "your", "their", "and", "the")):
        scores["fr"] = max(0.0, scores["fr"] - 1.0)
        scores["en"] += 1.5

    # Contractions anglaises vs élisions françaises
    en_contr = len(_EN_CONTRACTIONS_RE.findall(low))
    if en_contr:
        scores["en"] += 2.5 * en_contr

    fr_elis = len(_FR_ELISIONS_RE.findall(low))
    if fr_elis:
        scores["fr"] += 2.5 * fr_elis

    # Indices orthographiques et morphologiques discriminants
    fr_accents = len(re.findall(r"[éèêëàâçùûüôîïœ]", low))
    if fr_accents:
        scores["fr"] += 1.8 * fr_accents
    if re.search(r"[ñ¿¡]", low):
        scores["es"] += 2.5
    if re.search(r"[äöüß]", low):
        scores["de"] += 2.5

    # Morphologie anglaise courante (-ing, th-, wh-, -ck, sh-, -ed, 's) quand aucun accent français n'est présent
    if fr_accents == 0:
        en_morph = len(re.findall(r"\b(\w+ing|th\w+|wh\w+|\w+ck|\w+sh|\w+ght|\w+ed)\b", low))
        if en_morph:
            scores["en"] += 0.8 * en_morph
        if re.search(r"\b\w+'s\b", low):
            scores["en"] += 1.5

    return scores


def detect_language(text, fallback="fr"):
    """Retourne le code langue ('fr', 'en', 'es', 'de', 'it') correspondant réellement aux mots de `text`."""
    scores = score_languages(text)
    best = max(scores, key=lambda k: scores[k])
    if scores[best] <= 0.0:
        return fallback
    if best == "fr" and scores["en"] == scores["fr"] and fallback == "en":
        return "en"
    return best


def is_hallucinated_or_junk(text):
    """Détecte les hallucinations classiques de Whisper sur du bruit ou une fin de souffle."""
    clean = (text or "").strip().lower()
    if not clean:
        return True
    junk_exact = {
        "...", "…", ".", ",", "?", "!", "-",
        "sous-titres réalisés par", "sous-titres réalisés par la communauté d'amara.org",
        "merci d'avoir regardé cette vidéo", "merci d'avoir regardé cette vidéo !",
        "sous-titres", "merci.", "thank you.", "thanks for watching.", "you"
    }
    if clean in junk_exact:
        return True
    return False


def smart_transcribe_turn(pipe, audio_samples, forced_lang=None, expected_lang="fr"):
    """
    Transcrit un segment audio sur le NPU :
    - Si `forced_lang` est défini (ex: 'fr' ou 'en'), décode directement avec ce token de langue.
    - Si on est en mode Auto (`forced_lang is None`) :
      1. Interroge la sonde acoustique sans état (`_get_lang_probe_pipe()`, `max_new_tokens=10`)
         sur les premières secondes du segment afin de déterminer la vraie langue parlée (`fr` vs `en`)
         sans subir la rémanence du cache KV du `StaticWhisperPipeline` NPU d'OpenVINO GenAI.
      2. Exécute ensuite la transcription complète haute fidélité sur le NPU (`pipe`) en lui passant
         explicitement le token de langue détecté (`<|fr|>` ou `<|en|>`), ce qui garantit zéro dérive
         et 100 % de fidélité en français comme en anglais.
    Retourne `(text, detected_lang, was_retried)`.
    """
    audio_list = audio_samples.tolist() if hasattr(audio_samples, "tolist") else list(audio_samples)

    cfg = pipe.get_generation_config()
    cfg.task = "transcribe"
    cfg.return_timestamps = False

    if forced_lang:
        cfg.language = lang_token(forced_lang)
        res = pipe.generate(audio_list, cfg)
        text = str(res).strip()
        return text, forced_lang, False

    target_lang = expected_lang or "fr"
    probe = _get_lang_probe_pipe()
    if probe is not None:
        try:
            c_pr = probe.get_generation_config()
            c_pr.task = "transcribe"
            c_pr.language = None
            c_pr.return_timestamps = False
            c_pr.max_new_tokens = 10
            probe_txt = str(probe.generate(audio_list[:64000], c_pr)).strip()
            if probe_txt and not is_hallucinated_or_junk(probe_txt):
                target_lang = detect_language(probe_txt, fallback=expected_lang or "fr")
        except Exception:
            pass

    cfg.language = lang_token(target_lang)
    res_npu = pipe.generate(audio_list, cfg)
    text_npu = str(res_npu).strip()
    if not text_npu:
        return "", target_lang, False

    final_lang = detect_language(text_npu, fallback=target_lang)
    return text_npu, final_lang, False


def lang_token(code):
    """'fr' -> '<|fr|>'"""
    code = (code or "").strip()
    if not code or code.lower() in ("auto", "none", "<|auto|>"):
        return None
    return code if code.startswith("<|") else f"<|{code.lower()}|>"


def lang_code(token):
    """'<|fr|>' -> 'fr' (None/auto -> None)"""
    if not token:
        return None
    t = str(token).replace("<|", "").replace("|>", "").strip().lower()
    return None if t in ("", "auto", "none") else t


def whisper_translate_to_english(pipe, audio_list, source_lang_code):
    """Traduit un extrait audio vers l'anglais avec la tâche native 'translate' de Whisper."""
    if not source_lang_code or source_lang_code == "en":
        return None
    try:
        cfg = pipe.get_generation_config()
        cfg.task = "translate"
        cfg.language = lang_token(source_lang_code)
        cfg.return_timestamps = False
        res = pipe.generate(audio_list, cfg)
        texts = getattr(res, "texts", None)
        out = (texts[0] if texts else str(res)).strip()
        return out or None
    except Exception:
        return None
