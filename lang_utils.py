# -*- coding: utf-8 -*-
"""
Utilitaires de langue pour la reconnaissance vocale :
- Détection linguistique haute précision (FR/EN/ES/DE/IT) sur segments courts ou longs.
- Rattrapage en temps réel (Auto-Correction à la volée) quand Whisper bascule par erreur
  en anglais sur un segment court alors que l'intervenant parle français (ou inversement).
- Traduction native Whisper vers l'anglais.
"""
import re

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
        sister every other another some any many much more most such own same than too onlyboth each few through during
        before after above below betweenup down out off back around near far small big large good great new old first
        last long short high low right left team work working process processes manage manages managed managing said
        say says saying hey hi bye well now still even back way thing things people time year years day days man men""".split()),
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

    # Morphologie anglaise courante (-ing, th-, wh-, -ck, sh-) quand aucun accent français n'est présent
    if fr_accents == 0:
        en_morph = len(re.findall(r"\b(\w+ing|th\w+|wh\w+|\w+ck|\w+sh|\w+ght)\b", low))
        if en_morph:
            scores["en"] += 0.8 * en_morph

    return scores


def detect_language(text, fallback="fr"):
    """Retourne le code langue ('fr', 'en', 'es', 'de', 'it') correspondant réellement aux mots de `text`."""
    scores = score_languages(text)
    best = max(scores, key=lambda k: scores[k])
    if scores[best] <= 0.0:
        return fallback
    # En cas d'égalité stricte entre 'fr' et 'en', départager par la langue de repli
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
    Transcrit un segment audio et corrige à la volée les erreurs de détection de langue de Whisper :
    - Si `forced_lang` est défini (ex: 'fr'), décode directement dans cette langue.
    - Si l'utilisateur est en mode Auto (`forced_lang is None`) :
      1. Sur les segments courts (< 2.6s) où le détecteur interne de Whisper bascule souvent à tort
         sur son biais anglais (70% du pré-entraînement), ou lorsque la langue détectée dans le texte
         transcrit contredit la langue dominante de la session (`expected_lang`),
      2. On relance immédiatement le décodage sur le NPU en forçant `expected_lang` (ex: `<|fr|>`),
         et on compare la cohérence linguistique des deux sorties pour garder la vraie langue parlée !
    Retourne `(text, detected_lang, was_retried)`.
    """
    audio_list = audio_samples.tolist() if hasattr(audio_samples, "tolist") else list(audio_samples)
    duration_sec = len(audio_list) / 16000.0

    cfg = pipe.get_generation_config()
    cfg.task = "transcribe"
    cfg.return_timestamps = False

    if forced_lang:
        cfg.language = lang_token(forced_lang)
        res = pipe.generate(audio_list, cfg)
        text = str(res).strip()
        return text, forced_lang, False

    # Mode Auto-détection :
    cfg.language = None
    res_auto = pipe.generate(audio_list, cfg)
    text_auto = str(res_auto).strip()
    if not text_auto:
        return "", expected_lang or "fr", False

    lang_auto = detect_language(text_auto, fallback=expected_lang or "fr")
    scores_auto = score_languages(text_auto)

    # Si la langue détectée diffère de la langue attendue (ex: Whisper sort de l'anglais sur une phrase
    # française courte ou bruitée), on vérifie à la volée en décodant avec `expected_lang` :
    target_check = expected_lang or "fr"
    if lang_auto != target_check:
        try:
            cfg_retry = pipe.get_generation_config()
            cfg_retry.task = "transcribe"
            cfg_retry.return_timestamps = False
            cfg_retry.language = lang_token(target_check)
            res_retry = pipe.generate(audio_list, cfg_retry)
            text_retry = str(res_retry).strip()

            if text_retry and not is_hallucinated_or_junk(text_retry):
                lang_retry = detect_language(text_retry, fallback=target_check)
                scores_retry = score_languages(text_retry)

                # Si le re-décodage produit du vrai texte dans la langue attendue (ex: français cohérent)
                # et que le segment était court (< 3.8s) ou que le score dans la langue attendue est supérieur
                # ou égal au score auto, c'était bien un faux switch de Whisper -> on corrige à la volée !
                if lang_retry == target_check and (
                    duration_sec < 3.8 or scores_retry[target_check] >= scores_auto[lang_auto] * 0.75
                ):
                    return text_retry, target_check, True
        except Exception:
            pass

    return text_auto, lang_auto, False


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
