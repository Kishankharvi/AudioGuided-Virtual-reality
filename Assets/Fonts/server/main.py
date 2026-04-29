"""
FastAPI backend for AGVRSystem VR Hand Rehabilitation app.
Stores session data in SQLite, provides REST endpoints matching Unity APIManager.

Run: uvicorn main:app --host 0.0.0.0 --port 8000
"""

import json
import os
import sqlite3
from contextlib import asynccontextmanager
from typing import List

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from models import (
    ExerciseMetricModel,
    HealthResponse,
    PatientSessionsResponse,
    SessionDataModel,
    SessionResponse,
)

DB_PATH = os.environ.get("AGVR_DB_PATH", "agvr_sessions.db")


def init_db():
    """Create tables if they don't exist."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS sessions (
            session_id TEXT PRIMARY KEY,
            patient_id TEXT NOT NULL,
            start_timestamp TEXT NOT NULL,
            end_timestamp TEXT NOT NULL,
            overall_accuracy REAL NOT NULL,
            average_grip_strength REAL NOT NULL,
            total_duration REAL NOT NULL,
            exercises_json TEXT NOT NULL,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
    """)
    cursor.execute("""
        CREATE INDEX IF NOT EXISTS idx_patient_id ON sessions(patient_id)
    """)
    conn.commit()
    conn.close()


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_db()
    yield


app = FastAPI(
    title="AGVRSystem Session API",
    description="REST API for VR Hand Rehabilitation session storage",
    version="1.0.0",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/api/health", response_model=HealthResponse)
async def health_check():
    """Health check endpoint."""
    return HealthResponse(status="ok")


@app.post("/api/session", response_model=SessionResponse, status_code=201)
async def create_session(session: SessionDataModel):
    """
    Store a completed rehabilitation session.
    Called by Unity APIManager.PostSession().
    """
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()

    try:
        # Check for duplicate session ID
        cursor.execute("SELECT 1 FROM sessions WHERE session_id = ?", (session.sessionId,))
        if cursor.fetchone():
            conn.close()
            raise HTTPException(
                status_code=409,
                detail=f"Session {session.sessionId} already exists"
            )

        exercises_json = json.dumps([e.model_dump() for e in session.exercises])

        cursor.execute(
            """
            INSERT INTO sessions
            (session_id, patient_id, start_timestamp, end_timestamp,
             overall_accuracy, average_grip_strength, total_duration, exercises_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                session.sessionId,
                session.patientId,
                session.startTimestamp,
                session.endTimestamp,
                session.overallAccuracy,
                session.averageGripStrength,
                session.totalDuration,
                exercises_json,
            ),
        )
        conn.commit()
    except HTTPException:
        raise
    except Exception as e:
        conn.close()
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        conn.close()

    return SessionResponse(
        message="Session stored successfully",
        sessionId=session.sessionId
    )


@app.get("/api/patient/{patient_id}", response_model=PatientSessionsResponse)
async def get_patient_sessions(patient_id: str):
    """
    Retrieve all sessions for a given patient.
    Returns sessions ordered by start timestamp descending.
    """
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()

    cursor.execute(
        """
        SELECT * FROM sessions
        WHERE patient_id = ?
        ORDER BY start_timestamp DESC
        """,
        (patient_id,),
    )
    rows = cursor.fetchall()
    conn.close()

    sessions: List[SessionDataModel] = []
    for row in rows:
        exercises = [
            ExerciseMetricModel(**e)
            for e in json.loads(row["exercises_json"])
        ]
        sessions.append(
            SessionDataModel(
                sessionId=row["session_id"],
                patientId=row["patient_id"],
                startTimestamp=row["start_timestamp"],
                endTimestamp=row["end_timestamp"],
                overallAccuracy=row["overall_accuracy"],
                averageGripStrength=row["average_grip_strength"],
                totalDuration=row["total_duration"],
                exercises=exercises,
            )
        )

    return PatientSessionsResponse(
        patientId=patient_id,
        sessionCount=len(sessions),
        sessions=sessions,
    )
