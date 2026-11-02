#!/usr/bin/env python3
"""Builds a reference corpus of REAL human speech with ground-truth transcripts, in the harness corpus format.

Sources (public Hugging Face datasets, fetched through the datasets-server rows API; nothing large is downloaded):
  kespeech  TwinkStart/KeSpeech test split: Mandarin and 8 Mandarin sub-dialects (Zhongyuan, Southwestern, Northeastern,
            Ji-Lu, Jiang-Huai, Jiao-Liao, Lan-Yin, Beijing). Read speech. KeSpeech is free for academic use under its own
            agreement (https://github.com/tzyll/KeSpeech): keep the audio local, do not redistribute or commit it.
  ascend    CAiRE/ASCEND test split: spontaneous Mandarin-English code-switching conversations (CC-BY-SA-4.0).

  wenet     TwinkStart/WenetSpeech test_meeting (real meetings, one utterance per clip) and test_net (internet video,
            podcast, interview: consecutive sentences of one video), spontaneous speech with human transcripts and no
            punctuation. WenetSpeech is free for research; keep the audio local.

Consecutive utterances of one speaker are joined with natural pauses into dictation-length clips, so the pipeline's
segmentation and continuity are exercised too. Output: <out>/<source>/audio/<id>.wav (16 kHz mono) + <id>.timeline.json.
Run the harness on it with: Dictation.Harness run --corpus <out>/kespeech ...

Needs: ffmpeg, ffprobe. Default output is the user profile (VOICE_POC_REFERENCE overrides), never the repository.
"""
import argparse
import itertools
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

API = "https://datasets-server.huggingface.co/rows"
FFMPEG = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
FFPROBE = shutil.which("ffprobe") or "/opt/homebrew/bin/ffprobe"
LEAD_MS, GAP_MS, TAIL_MS = 300, 500, 900


def get(url, binary=False):
    """GET with backoff; the datasets-server rate-limits bursts (HTTP 429) and sometimes answers 5xx."""
    for attempt in range(8):
        try:
            with urllib.request.urlopen(url, timeout=120) as response:
                body = response.read()
            time.sleep(0.8)
            return body if binary else json.loads(body)
        except urllib.error.HTTPError as e:
            if e.code not in (429, 500, 502, 503, 504):
                raise
        except (urllib.error.URLError, TimeoutError):
            pass
        time.sleep(10 * (attempt + 1))
    raise RuntimeError("giving up on " + url[:120])


def rows(dataset, config, split, offset, length=100):
    query = urllib.parse.urlencode(dict(dataset=dataset, config=config, split=split, offset=offset, length=length))
    return get(f"{API}?{query}")["rows"]


def clean(text):
    """Drops annotation tags such as <SPOKEN_NOISE> and [UNK]: they mark non-speech or untranscribed audio, not words said."""
    return re.sub(r"<[A-Za-z_]+>|\[[A-Za-z_]+\]", "", text).strip()


def duration_ms(path):
    out = subprocess.run([FFPROBE, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                         check=True, capture_output=True, text=True).stdout
    return round(float(out) * 1000)


def silence(path, ms):
    subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono",
                    "-t", str(ms / 1000), "-c:a", "pcm_s16le", path], check=True)


def build_clip(clip_id, category, voice, utterances, out_dir, work):
    """utterances: list of (text, audio_url). Downloads, joins with pauses, writes wav + timeline."""
    os.makedirs(out_dir, exist_ok=True)
    lead, gap, tail = (os.path.join(work, n) for n in ("lead.wav", "gap.wav", "tail.wav"))
    for path, ms in ((lead, LEAD_MS), (gap, GAP_MS), (tail, TAIL_MS)):
        if not os.path.exists(path):
            silence(path, ms)

    listing, parts, cursor = [lead], [], LEAD_MS
    for index, (text, url) in enumerate(utterances):
        raw = os.path.join(work, f"{clip_id}-{index}.src")
        with open(raw, "wb") as f:
            f.write(get(url, binary=True))
        wav = os.path.join(work, f"{clip_id}-{index}.wav")
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", raw, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav], check=True)
        length = duration_ms(wav)
        parts.append(dict(text=clean(text), startMs=cursor, endMs=cursor + length))
        listing.append(wav)
        cursor += length
        if index < len(utterances) - 1:
            listing.append(gap)
            cursor += GAP_MS
    listing.append(tail)
    cursor += TAIL_MS

    list_file = os.path.join(work, f"{clip_id}.txt")
    with open(list_file, "w") as f:
        f.writelines(f"file '{p}'\n" for p in listing)
    wav_out = os.path.join(out_dir, f"{clip_id}.wav")
    subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", list_file, "-c:a", "pcm_s16le", wav_out], check=True)
    timeline = dict(id=clip_id, category=category, voice=voice, durationMs=duration_ms(wav_out), parts=parts,
                    protect=[], mustKeep=[], forbid=[])
    with open(os.path.join(out_dir, f"{clip_id}.timeline.json"), "w", encoding="utf-8") as f:
        json.dump(timeline, f, ensure_ascii=False, indent=2)
    print(f"built {clip_id}: {timeline['durationMs'] / 1000:.1f}s, {len(utterances)} utterances, voice {voice}")


def kespeech(out, per_dialect, utterances_per_clip, window_step, work):
    """Picks runs of consecutive utterances by one speaker with one dialect label, spread over the test split."""
    candidates = {}
    for offset in range(0, 19723, window_step):
        batch = rows("TwinkStart/KeSpeech", "default", "test", offset)
        key = lambda r: (r["row"]["ID"].split("_")[0], r["row"]["Dialect"])
        for (speaker, dialect), group in itertools.groupby(batch, key=key):
            group = list(group)
            if len(group) >= utterances_per_clip:
                candidates.setdefault(dialect, []).append((speaker, group[:utterances_per_clip]))
        print(f"scanned offset {offset}: " + ", ".join(f"{d} {len(c)}" for d, c in sorted(candidates.items())), flush=True)

    out_dir = os.path.join(out, "kespeech", "audio")
    for dialect, runs in sorted(candidates.items()):
        seen, chosen = set(), []
        for speaker, group in runs:
            if speaker not in seen:
                seen.add(speaker)
                chosen.append(group)
        step = max(1, len(chosen) // per_dialect)
        for number, group in enumerate(chosen[::step][:per_dialect], start=1):
            utterances = [(r["row"]["Text"], r["row"]["audio"][0]["src"]) for r in group]
            build_clip(f"clip{number}@{dialect}", "real-accent", dialect, utterances, out_dir, work)


def ascend(out, clips, window_step, work):
    """Picks stretches of one conversation session containing mixed Chinese-English speech, 20 to 45 seconds long."""
    chosen = []
    for offset in range(0, 1315, window_step):
        batch = rows("CAiRE/ASCEND", "main", "test", offset)
        for session, group in itertools.groupby(batch, key=lambda r: r["row"]["session_id"]):
            group = [r for r in group if r["row"]["duration"] >= 1.0]
            run, total = [], 0.0
            for r in group:
                run.append(r)
                total += r["row"]["duration"]
                if total >= 20:
                    langs = {x["row"]["language"] for x in run}
                    if len(langs) > 1 or "mixed" in langs:
                        chosen.append(run)
                    run, total = [], 0.0
        print(f"scanned offset {offset}: {len(chosen)} candidate clips", flush=True)
        if len(chosen) >= clips * 3:
            break

    step = max(1, len(chosen) // clips)
    out_dir = os.path.join(out, "ascend", "audio")
    for number, run in enumerate(chosen[::step][:clips], start=1):
        utterances = [(r["row"]["transcription"], r["row"]["audio"][0]["src"]) for r in run]
        build_clip(f"clip{number}@code-switch", "real-codeswitch", "code-switch", utterances, out_dir, work)


def wenet(out, meeting_clips, net_clips, window_step, work):
    dataset = "TwinkStart/WenetSpeech"
    meeting = []
    for offset in range(0, 8370, 100):
        meeting += [r for r in rows(dataset, "default", "test_meeting", offset) if r["row"]["end_time"] - r["row"]["begin_time"] >= 7]
    out_dir = os.path.join(out, "wenet-meeting", "audio")
    step = max(1, len(meeting) // meeting_clips)
    for number, r in enumerate(meeting[::step][:meeting_clips], start=1):
        build_clip(f"clip{number}@meeting", "real-meeting", "meeting", [(r["row"]["text"], r["row"]["audio"][0]["src"])], out_dir, work)

    out_dir = os.path.join(out, "wenet-net", "audio")
    chosen, seen = [], set()
    for offset in range(0, 24774, window_step):
        batch = rows(dataset, "default", "test_net", offset)
        video = lambda r: r["row"]["sid"].rsplit("_S", 1)[0]
        for name, group in itertools.groupby(batch, key=video):
            group = list(group)
            run, total = [], 0.0
            for r in group:
                run.append(r)
                total += r["row"]["end_time"] - r["row"]["begin_time"]
                if total >= 30:
                    break
            if total >= 30 and name not in seen:
                seen.add(name)
                chosen.append(run)
        print(f"scanned offset {offset}: {len(chosen)} candidate clips", flush=True)
    step = max(1, len(chosen) // net_clips)
    for number, run in enumerate(chosen[::step][:net_clips], start=1):
        utterances = [(r["row"]["text"], r["row"]["audio"][0]["src"]) for r in run]
        build_clip(f"clip{number}@net-video", "real-video", "net-video", utterances, out_dir, work)


def main():
    default_out = os.environ.get("VOICE_POC_REFERENCE") or os.path.join(os.path.expanduser("~"), "VoicePoc", "reference-corpus")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=default_out)
    ap.add_argument("--source", choices=["kespeech", "ascend", "wenet", "all"], default="all")
    ap.add_argument("--per-dialect", type=int, default=3, help="kespeech clips per dialect label")
    ap.add_argument("--utterances", type=int, default=6, help="kespeech utterances per clip")
    ap.add_argument("--ascend-clips", type=int, default=8)
    ap.add_argument("--meeting-clips", type=int, default=20)
    ap.add_argument("--net-clips", type=int, default=10)
    ap.add_argument("--window-step", type=int, default=700, help="spacing of the scanned 100-row windows; smaller finds more speakers")
    args = ap.parse_args()

    if os.path.abspath(args.out).startswith(os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))):
        sys.exit("Refusing to write real speech inside the repository; choose --out in the user profile.")
    with tempfile.TemporaryDirectory() as work:
        if args.source in ("kespeech", "all"):
            kespeech(args.out, args.per_dialect, args.utterances, args.window_step, work)
        if args.source in ("wenet", "all"):
            wenet(args.out, args.meeting_clips, args.net_clips, args.window_step, work)
        if args.source in ("ascend", "all"):
            ascend(args.out, args.ascend_clips, max(100, args.window_step // 7), work)
    print("done:", args.out)


if __name__ == "__main__":
    main()
