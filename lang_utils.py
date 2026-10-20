# -*- coding: utf-8 -*-
"""
Utilitaires de langue pour la reconnaissance vocale (détection FR/EN/ES/DE/IT + traduction Whisper).

Contexte : dans openvino_genai, `WhisperPipeline.generate()` transcrit bien dans la langue parlée quand
`config.language = None`, mais il n'expose PAS la langue détectée (`results.languages` vaut None).
On déduit donc la langue du texte transcrit (mots outils très fréquents), ce qui est fiable pour une phrase.
"""
import re

_STOPWORDS = {
    "fr": set("""le la les un une des du de d l et est sont être avoir a ont à au aux en dans sur pour par avec sans
        que qui quoi dont où ce cet cette ces se sa son ses leur leurs nous vous ils elle elles il je tu on ne pas plus
        mais ou donc car ni très bien aussi comme tout tous cela ça c n s j m t y bonjour merci oui non alors voici
        notre votre mon ma mes ton ta tes fait faire peut être sera aujourd hui résultats prochaines étapes réunion""".split()),
    "en": set("""the a an and is are was were be been have has had to of in on for with without that this these those it its
        we you they he she i me my our your their his her not but or so very also as all there here what which who where
        will would can could should do does did just about from by at today hello thanks yes no okay next steps results
        meeting going""".split()),
    "es": set("""el la los las un una unos unas y es son ser estar hay de del en con sin que por para como pero muy más
        este esta estos estas nosotros ustedes ellos ella yo tu su sus nuestro hola gracias sí también hoy""".split()),
    "de": set("""der die das ein eine und ist sind war sein haben hat nicht mit von zu auf für ich du wir sie er es auch aber
        sehr dass wie noch nur heute hallo danke ja nein bitte werden wird""".split()),
    "it": set("""il lo la i gli le un una e è sono essere avere ha di del della in con senza che per come ma molto più questo
        questa noi voi loro io tu suo nostro ciao grazie sì anche oggi""".split()),
}

_WORD_RE = re.compile(r"[a-zA-ZÀ-ÿ']+")


def detect_language(text, fallback="fr"):
    """Retourne le code langue ('fr', 'en', 'es', 'de', 'it') le plus probable pour `text`."""
    words = [w.lower().replace("'", " ").strip() for w in _WORD_RE.findall(text or "")]
    tokens = []
    for w in words:
        tokens.extend(w.split())
    if not tokens:
        return fallback
    scores = {}
    for lang, sw in _STOPWORDS.items():
        scores[lang] = sum(1 for t in tokens if t in sw)
    # Indices orthographiques discriminants
    low = (text or "").lower()
    if re.search(r"[éèêàçùûôîï]", low):
        scores["fr"] += 1
    if re.search(r"[ñ¿¡]", low):
        scores["es"] += 2
    if re.search(r"[äöüß]", low):
        scores["de"] += 2
    best = max(scores, key=lambda k: scores[k])
    if scores[best] == 0:
        return fallback
    return best


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
    """Traduit un extrait audio vers l'anglais avec la tâche native 'translate' de Whisper.

    Retourne le texte anglais, ou None si la traduction n'est pas applicable / échoue.
    """
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
