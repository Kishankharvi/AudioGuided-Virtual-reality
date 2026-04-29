"""
AGVR Rehab Session API Server
FastAPI backend that receives session data from the Unity VR app,
stores it as JSON files, and serves it to the web dashboard.
"""

import json
import os
import uuid
from datetime import datetime
from pathlib import Path
from typing import List, Optional

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse
from pydantic import BaseModel


# ── Data Models ──────────────────────────────────────────────

class ExerciseMetrics(BaseModel):
    exerciseName: str
    accuracy: float
    gripStrength: float
    repsCompleted: int
    targetReps: int
    duration: float
    startTimestamp: Optional[str] = ""
    endTimestamp: Optional[str] = ""


class SessionData(BaseModel):
    sessionId: Optional[str] = ""
    patientId: Optional[str] = ""
    startTimestamp: Optional[str] = ""
    endTimestamp: Optional[str] = ""
    overallAccuracy: float
    averageGripStrength: float
    totalDuration: float
    exercises: List[ExerciseMetrics] = []


class SessionSummary(BaseModel):
    sessionId: str
    patientId: str
    date: str
    overallAccuracy: float
    averageGripStrength: float
    totalDuration: float
    exerciseCount: int


class ImprovementData(BaseModel):
    dates: List[str]
    accuracies: List[float]
    gripStrengths: List[float]
    durations: List[float]


# ── App Setup ────────────────────────────────────────────────

app = FastAPI(title="AGVR Rehab Dashboard API", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

DATA_DIR = Path(__file__).parent / "data"
DATA_DIR.mkdir(exist_ok=True)

FRONTEND_DIR = Path(__file__).parent.parent / "frontend"


# ── Helpers ──────────────────────────────────────────────────

def load_all_sessions() -> List[dict]:
    """Load all session JSON files sorted by date (newest first)."""
    sessions = []
    for f in DATA_DIR.glob("*.json"):
        try:
            with open(f, "r") as fp:
                sessions.append(json.load(fp))
        except Exception:
            continue
    sessions.sort(key=lambda s: s.get("startTimestamp", ""), reverse=True)
    return sessions


# ── API Routes ───────────────────────────────────────────────

@app.post("/api/session", status_code=201)
async def create_session(data: SessionData):
    """Receive session data from Unity VR app."""
    if not data.sessionId:
        data.sessionId = str(uuid.uuid4())[:8]

    if not data.startTimestamp:
        data.startTimestamp = datetime.utcnow().isoformat()

    filename = f"{data.sessionId}.json"
    filepath = DATA_DIR / filename

    with open(filepath, "w") as f:
        json.dump(data.model_dump(), f, indent=2)

    return {"status": "created", "sessionId": data.sessionId}


@app.get("/api/sessions", response_model=List[SessionSummary])
async def list_sessions():
    """List all sessions with summary info."""
    sessions = load_all_sessions()
    summaries = []
    for s in sessions:
        summaries.append(SessionSummary(
            sessionId=s.get("sessionId", "?"),
            patientId=s.get("patientId", "unknown"),
            date=s.get("startTimestamp", "")[:10] or "unknown",
            overallAccuracy=s.get("overallAccuracy", 0),
            averageGripStrength=s.get("averageGripStrength", 0),
            totalDuration=s.get("totalDuration", 0),
            exerciseCount=len(s.get("exercises", [])),
        ))
    return summaries


@app.get("/api/session/{session_id}")
async def get_session(session_id: str):
    """Get full session data by ID."""
    filepath = DATA_DIR / f"{session_id}.json"
    if not filepath.exists():
        raise HTTPException(status_code=404, detail="Session not found")

    with open(filepath, "r") as f:
        return json.load(f)


@app.get("/api/improvement")
async def get_improvement():
    """Get improvement trend data across all sessions."""
    sessions = load_all_sessions()
    sessions.reverse()  # oldest first for chart

    return ImprovementData(
        dates=[s.get("startTimestamp", "")[:10] or f"Session {i+1}"
               for i, s in enumerate(sessions)],
        accuracies=[s.get("overallAccuracy", 0) for s in sessions],
        gripStrengths=[s.get("averageGripStrength", 0) for s in sessions],
        durations=[s.get("totalDuration", 0) for s in sessions],
    )


@app.get("/api/stats")
async def get_stats():
    """Get aggregate statistics."""
    sessions = load_all_sessions()
    if not sessions:
        return {
            "totalSessions": 0,
            "avgAccuracy": 0,
            "avgGrip": 0,
            "totalTime": 0,
            "bestAccuracy": 0,
            "latestImprovement": 0,
        }

    accuracies = [s.get("overallAccuracy", 0) for s in sessions]
    grips = [s.get("averageGripStrength", 0) for s in sessions]
    durations = [s.get("totalDuration", 0) for s in sessions]

    improvement = 0
    if len(accuracies) >= 2:
        improvement = accuracies[0] - accuracies[1]  # newest - previous

    return {
        "totalSessions": len(sessions),
        "avgAccuracy": sum(accuracies) / len(accuracies),
        "avgGrip": sum(grips) / len(grips),
        "totalTime": sum(durations),
        "bestAccuracy": max(accuracies),
        "latestImprovement": improvement,
    }


# ── Serve Frontend ───────────────────────────────────────────

if FRONTEND_DIR.exists():
    app.mount("/static", StaticFiles(directory=str(FRONTEND_DIR)), name="frontend")

    @app.get("/")
    async def serve_frontend():
        return FileResponse(str(FRONTEND_DIR / "index.html"))


# ── Seed Demo Data ───────────────────────────────────────────

@app.on_event("startup")
async def seed_demo_data():
    """Create demo sessions if data directory is empty."""
    if any(DATA_DIR.glob("*.json")):
        return

    demo_sessions = [
        {
            "sessionId": "demo-001",
            "patientId": "patient-A",
            "startTimestamp": "2026-03-28T10:00:00",
            "endTimestamp": "2026-03-28T10:12:00",
            "overallAccuracy": 62.5,
            "averageGripStrength": 45.0,
            "totalDuration": 720,
            "exercises": [
                {"exerciseName": "Grip Hold", "accuracy": 58.0, "gripStrength": 42.0,
                 "repsCompleted": 6, "targetReps": 10, "duration": 180,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Finger Tapping", "accuracy": 65.0, "gripStrength": 48.0,
                 "repsCompleted": 8, "targetReps": 10, "duration": 150,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Precision Pinching", "accuracy": 55.0, "gripStrength": 40.0,
                 "repsCompleted": 5, "targetReps": 10, "duration": 200,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Finger Spreading", "accuracy": 70.0, "gripStrength": 50.0,
                 "repsCompleted": 7, "targetReps": 10, "duration": 120,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Thumb Opposition", "accuracy": 64.0, "gripStrength": 45.0,
                 "repsCompleted": 6, "targetReps": 10, "duration": 70,
                 "startTimestamp": "", "endTimestamp": ""},
            ],
        },
        {
            "sessionId": "demo-002",
            "patientId": "patient-A",
            "startTimestamp": "2026-03-31T10:00:00",
            "endTimestamp": "2026-03-31T10:14:00",
            "overallAccuracy": 71.2,
            "averageGripStrength": 52.0,
            "totalDuration": 840,
            "exercises": [
                {"exerciseName": "Grip Hold", "accuracy": 68.0, "gripStrength": 50.0,
                 "repsCompleted": 8, "targetReps": 10, "duration": 200,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Finger Tapping", "accuracy": 74.0, "gripStrength": 55.0,
                 "repsCompleted": 9, "targetReps": 10, "duration": 160,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Precision Pinching", "accuracy": 65.0, "gripStrength": 48.0,
                 "repsCompleted": 7, "targetReps": 10, "duration": 210,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Finger Spreading", "accuracy": 78.0, "gripStrength": 55.0,
                 "repsCompleted": 8, "targetReps": 10, "duration": 140,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Thumb Opposition", "accuracy": 71.0, "gripStrength": 52.0,
                 "repsCompleted": 7, "targetReps": 10, "duration": 130,
                 "startTimestamp": "", "endTimestamp": ""},
            ],
        },
        {
            "sessionId": "demo-003",
            "patientId": "patient-A",
            "startTimestamp": "2026-04-03T10:00:00",
            "endTimestamp": "2026-04-03T10:15:00",
            "overallAccuracy": 82.8,
            "averageGripStrength": 61.0,
            "totalDuration": 900,
            "exercises": [
                {"exerciseName": "Grip Hold", "accuracy": 80.0, "gripStrength": 60.0,
                 "repsCompleted": 9, "targetReps": 10, "duration": 190,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Finger Tapping", "accuracy": 88.0, "gripStrength": 65.0,
                 "repsCompleted": 10, "targetReps": 10, "duration": 170,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Precision Pinching", "accuracy": 76.0, "gripStrength": 55.0,
                 "repsCompleted": 8, "targetReps": 10, "duration": 220,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Finger Spreading", "accuracy": 86.0, "gripStrength": 63.0,
                 "repsCompleted": 9, "targetReps": 10, "duration": 160,
                 "startTimestamp": "", "endTimestamp": ""},
                {"exerciseName": "Thumb Opposition", "accuracy": 84.0, "gripStrength": 62.0,
                 "repsCompleted": 9, "targetReps": 10, "duration": 160,
                 "startTimestamp": "", "endTimestamp": ""},
            ],
        },
    ]

    for session in demo_sessions:
        filepath = DATA_DIR / f"{session['sessionId']}.json"
        with open(filepath, "w") as f:
            json.dump(session, f, indent=2)

    print(f"[Seed] Created {len(demo_sessions)} demo sessions")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
