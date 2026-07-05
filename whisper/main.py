import asyncio
import logging
import os
import re
import subprocess
import tempfile
from typing import Optional
import httpx
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from faster_whisper import WhisperModel

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

WHISPER_MODEL = os.environ.get("WHISPER_MODEL", "large-v3")
WHISPER_DEVICE = os.environ.get("WHISPER_DEVICE", "cuda")

compute_type = "float16" if WHISPER_DEVICE == "cuda" else "int8"

model = WhisperModel(
    WHISPER_MODEL,
    device=WHISPER_DEVICE,
    compute_type=compute_type,
)


class TranscribeRequest(BaseModel):
    audio_url: str
    language: Optional[str] = None


class Segment(BaseModel):
    index: int
    start: float
    end: float
    text: str


class TranscribeResponse(BaseModel):
    language: str
    duration: float
    segments: list[Segment]


app = FastAPI()


@app.get("/health")
def health():
    return {
        "status": "ok",
        "model": WHISPER_MODEL,
        "device": WHISPER_DEVICE,
    }


_YOUTUBE_RE = re.compile(
    r"https?://(?:www\.)?(?:youtube\.com/watch\?.*v=|youtu\.be/)([a-zA-Z0-9_\-]+)",
    re.IGNORECASE,
)

# Firefox snap profile path — yt-dlp reads stored cookies so it can bypass
# YouTube's proof-of-origin requirement without a live browser session.
_FIREFOX_PROFILE = "/root/.mozilla/firefox"
_FIREFOX_SNAP_PROFILE = "/root/snap/firefox/common/.mozilla/firefox"
_NODE_BIN = os.environ.get("NODE_BIN", "/usr/bin/node")


def _is_streaming_url(url: str) -> bool:
    lower = url.lower()
    return lower.endswith(".m3u8") or ".m3u8?" in lower or "prog_index" in lower


def _is_youtube_url(url: str) -> bool:
    return bool(_YOUTUBE_RE.search(url))


def _ytdlp_extract_audio_url(youtube_url: str) -> str:
    """
    Resolve a YouTube URL to a direct audio stream URL using yt-dlp.
    Tries cookie-based auth from Firefox snap profile if available, then
    falls back to unauthenticated extraction (works for public videos with
    a JS runtime).
    """
    cmd_base = ["yt-dlp", "-f", "bestaudio", "--get-url", "--no-playlist"]

    # EJS remote component for YouTube n-challenge solver. Downloaded once and cached
    # at ~/.cache/yt-dlp/. Required when node v18 is the runtime (v22+ solves inline).
    remote_components_args = ["--remote-components", "ejs:github"]

    # Prefer Firefox snap profile (populated in the container if volume-mounted)
    for profile_path in [_FIREFOX_SNAP_PROFILE, _FIREFOX_PROFILE]:
        if os.path.isdir(profile_path):
            try:
                result = subprocess.run(
                    cmd_base + [
                        "--cookies-from-browser", f"firefox:{profile_path}",
                        "--js-runtimes", f"node:{_NODE_BIN}",
                    ] + remote_components_args + [youtube_url],
                    capture_output=True,
                    text=True,
                    timeout=120,
                )
                if result.returncode == 0:
                    direct_url = result.stdout.strip().splitlines()[0]
                    if direct_url.startswith("http"):
                        return direct_url
                logger.warning(
                    "yt-dlp with cookies from %s failed (exit %d): %s",
                    profile_path, result.returncode, result.stderr[:300],
                )
            except subprocess.TimeoutExpired:
                logger.warning(
                    "yt-dlp with cookies from %s timed out after 120s", profile_path
                )
            except Exception as exc:
                logger.warning(
                    "yt-dlp with cookies from %s failed: %s", profile_path, exc
                )

    # No profile available — try without cookies (works for some public videos)
    result = subprocess.run(
        cmd_base + ["--js-runtimes", f"node:{_NODE_BIN}"] + remote_components_args + [youtube_url],
        capture_output=True,
        text=True,
        timeout=120,
    )
    if result.returncode == 0:
        direct_url = result.stdout.strip().splitlines()[0]
        if direct_url.startswith("http"):
            return direct_url

    raise RuntimeError(
        f"yt-dlp could not extract audio URL for {youtube_url}: {result.stderr[:300]}"
    )


@app.post("/transcribe", response_model=TranscribeResponse)
async def transcribe(req: TranscribeRequest):
    if _is_youtube_url(req.audio_url):
        return await _transcribe_youtube(req)
    if _is_streaming_url(req.audio_url):
        return await _transcribe_streaming(req)
    return await _transcribe_download(req)


async def _transcribe_youtube(req: TranscribeRequest) -> TranscribeResponse:
    loop = asyncio.get_running_loop()
    try:
        direct_url = await loop.run_in_executor(
            None, _ytdlp_extract_audio_url, req.audio_url
        )
    except RuntimeError as exc:
        raise HTTPException(status_code=422, detail=str(exc))

    # Delegate to the streaming path — yt-dlp returns a signed CDN URL that
    # faster-whisper can read directly as a stream.
    streaming_req = TranscribeRequest(audio_url=direct_url, language=req.language)
    try:
        return await _transcribe_streaming(streaming_req)
    except Exception as exc:
        # Fall back to download if the CDN URL is not directly streamable
        logger.warning(
            "streaming transcription of yt-dlp URL failed, falling back to download: %s", exc
        )
        return await _transcribe_download(streaming_req)


async def _transcribe_streaming(req: TranscribeRequest) -> TranscribeResponse:
    loop = asyncio.get_running_loop()

    if _is_streaming_url(req.audio_url):
        # PyAV (faster-whisper's reader) cannot demux HLS playlists. Pull through
        # ffmpeg to a local mono 16k WAV first, then transcribe from disk.
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
            tmp_path = tmp.name
        try:
            def _ffmpeg():
                return subprocess.run(
                    [
                        "ffmpeg", "-y", "-loglevel", "error",
                        "-i", req.audio_url,
                        "-vn", "-ac", "1", "-ar", "16000",
                        "-f", "wav", tmp_path,
                    ],
                    capture_output=True, text=True, timeout=1800,
                )
            result = await loop.run_in_executor(None, _ffmpeg)
            if result.returncode != 0:
                raise HTTPException(
                    status_code=422,
                    detail=f"ffmpeg failed to fetch HLS stream: {result.stderr[:300]}",
                )

            def _run():
                segments_raw, info = model.transcribe(
                    tmp_path,
                    language=req.language,
                    vad_filter=True,
                    beam_size=5,
                )
                return list(segments_raw), info

            segments_list, info = await loop.run_in_executor(None, _run)
        finally:
            try: os.unlink(tmp_path)
            except OSError: pass
    else:
        def _run():
            segments_raw, info = model.transcribe(
                req.audio_url,
                language=req.language,
                vad_filter=True,
                beam_size=5,
            )
            return list(segments_raw), info

        segments_list, info = await loop.run_in_executor(None, _run)

    segments = [
        Segment(index=i, start=round(s.start, 3), end=round(s.end, 3), text=s.text.strip())
        for i, s in enumerate(segments_list)
    ]
    return TranscribeResponse(
        language=info.language,
        duration=round(info.duration, 3),
        segments=segments,
    )


async def _transcribe_download(req: TranscribeRequest) -> TranscribeResponse:
    loop = asyncio.get_running_loop()
    with tempfile.NamedTemporaryFile(suffix=".audio", delete=False) as tmp:
        tmp_path = tmp.name

    try:
        async with httpx.AsyncClient(timeout=600.0, follow_redirects=True) as client:
            async with client.stream("GET", req.audio_url) as resp:
                if resp.status_code != 200:
                    raise HTTPException(
                        status_code=422,
                        detail=f"Failed to download audio: HTTP {resp.status_code}",
                    )
                with open(tmp_path, "wb") as f:
                    async for chunk in resp.aiter_bytes(chunk_size=65536):
                        f.write(chunk)

        def _run():
            segments_raw, info = model.transcribe(
                tmp_path,
                language=req.language,
                vad_filter=True,
                beam_size=5,
            )
            return list(segments_raw), info

        segments_list, info = await loop.run_in_executor(None, _run)

        segments = [
            Segment(index=i, start=round(s.start, 3), end=round(s.end, 3), text=s.text.strip())
            for i, s in enumerate(segments_list)
        ]
        return TranscribeResponse(
            language=info.language,
            duration=round(info.duration, 3),
            segments=segments,
        )
    finally:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
