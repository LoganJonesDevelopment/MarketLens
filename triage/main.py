import logging
import os
import threading
from typing import Optional
from fastapi import FastAPI
from pydantic import BaseModel
from transformers import pipeline
import torch

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

MODEL_ID = os.environ.get("TRIAGE_MODEL", "MoritzLaurer/deberta-v3-large-zeroshot-v2.0")
DEVICE_SETTING = os.environ.get("TRIAGE_DEVICE", "auto").lower()

if DEVICE_SETTING == "cpu":
    DEVICE = -1
elif torch.cuda.is_available():
    DEVICE = 0
else:
    DEVICE = -1

MODEL_KWARGS = {"dtype": torch.float16} if DEVICE >= 0 else {}

EVENT_LABELS: dict[str, str] = {
    "earnings": "quarterly earnings report or financial results",
    "acquisition_disposition": "merger acquisition or divestiture",
    "material_agreement": "material business agreement or contract",
    "material_impairment": "asset impairment or writedown",
    "delisting": "stock delisting or exchange removal",
    "restatement": "financial restatement or accounting correction",
    "officer_change": "executive officer or director change",
    "vote_result": "shareholder vote result",
    "regulation_fd_disclosure": "regulation FD investor disclosure",
    "analyst_action": "stock analyst rating upgrade or downgrade or price target change",
    "product_launch": "new product launch or release",
    "litigation": "lawsuit litigation or legal proceeding",
    "regulatory_action": "regulatory action or government agency decision",
    "macro_release": "macroeconomic data release or central bank decision",
    "other_material_event": "other material corporate event",
    "production_guidance": "mine production guidance revision or output forecast change",
    "supply_disruption": "mine shutdown or supply disruption or force majeure or strike",
    "trade_restriction": "export ban or tariff or trade restriction on minerals or commodities",
}

LABEL_TO_KEY = {v: k for k, v in EVENT_LABELS.items()}
LABEL_LIST = list(EVENT_LABELS.values())

logger.info("loading %s on device %s", MODEL_ID, DEVICE)
classifier = pipeline(
    "zero-shot-classification",
    model=MODEL_ID,
    device=DEVICE,
    model_kwargs=MODEL_KWARGS,
)

# FastAPI runs sync routes in a threadpool; the pipeline shares one GPU model,
# so calls must be serialized.
classifier_lock = threading.Lock()


class ClassifyRequest(BaseModel):
    text: str
    threshold: float = 0.4


class ClassifyBatchRequest(BaseModel):
    texts: list[str]
    threshold: float = 0.4


class ClassifyResponse(BaseModel):
    event_type: Optional[str]
    confidence: float
    all_scores: dict[str, float]


app = FastAPI()


@app.get("/health")
def health():
    return {
        "status": "ok",
        "model": MODEL_ID,
        "device": DEVICE,
        "cuda": torch.cuda.is_available(),
        "gpu": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
    }


@app.post("/classify", response_model=ClassifyResponse)
def classify(req: ClassifyRequest):
    return classify_text(req.text, req.threshold)


@app.post("/classify-batch", response_model=list[ClassifyResponse])
def classify_batch(req: ClassifyBatchRequest):
    if not req.texts:
        return []

    with classifier_lock:
        result = classifier(
            req.texts,
            candidate_labels=LABEL_LIST,
            multi_label=False,
        )
    results = result if isinstance(result, list) else [result]
    return [to_response(r, req.threshold) for r in results]


def classify_text(text: str, threshold: float) -> ClassifyResponse:
    with classifier_lock:
        result = classifier(
            text,
            candidate_labels=LABEL_LIST,
            multi_label=False,
        )
    return to_response(result, threshold)


def to_response(result, threshold: float) -> ClassifyResponse:
    top_label = result["labels"][0]
    top_score = float(result["scores"][0])
    event_type = LABEL_TO_KEY[top_label] if top_score >= threshold else None
    return ClassifyResponse(
        event_type=event_type,
        confidence=top_score,
        all_scores={LABEL_TO_KEY[lbl]: float(s) for lbl, s in zip(result["labels"], result["scores"])},
    )
