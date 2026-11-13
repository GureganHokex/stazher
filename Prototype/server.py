"""InGameSurface: a loopback-only campaign console. No external advertising services."""
from __future__ import annotations

import csv
import io
import json
import os
import re
import sqlite3
import uuid
import base64
import shutil
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from contextlib import contextmanager
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse, Response
from pydantic import BaseModel, Field
from starlette.middleware.trustedhost import TrustedHostMiddleware

ROOT = Path(__file__).resolve().parent
DATA = Path(os.environ.get("IGS_DATA", ROOT / "data"))
MEDIA = DATA / "media"
DATA.mkdir(parents=True, exist_ok=True)
MEDIA.mkdir(exist_ok=True)
DB = DATA / "demo.sqlite3"
PLACEMENTS = {"office_coffee": "Кофейная зона", "office_screen": "Экран в офисе", "street_board": "Городской щит"}
LIMIT = 100 * 1024 * 1024
DEMO_ID = "10000000000040008000000000000001"
PIXEL_EVENTS = ("impression", "creativeView", "start", "firstQuartile", "midpoint", "thirdQuartile", "complete", "error")
GIF = base64.b64decode("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")


@contextmanager
def connection():
    db = sqlite3.connect(DB, timeout=5)
    db.row_factory = sqlite3.Row
    try:
        with db:
            yield db
    finally:
        db.close()


def initialize():
    with connection() as db:
        db.executescript("""
        CREATE TABLE IF NOT EXISTS campaigns(id TEXT PRIMARY KEY, name TEXT NOT NULL,
          kind TEXT NOT NULL, media TEXT NOT NULL, theme TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1);
        CREATE TABLE IF NOT EXISTS placements(id TEXT PRIMARY KEY, campaign TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY, session TEXT NOT NULL,
          placement TEXT NOT NULL, creative TEXT NOT NULL, kind TEXT NOT NULL,
          seconds REAL NOT NULL, client_time TEXT NOT NULL, received TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS events_group ON events(placement, creative);
        CREATE TABLE IF NOT EXISTS media_info(creative TEXT PRIMARY KEY, duration REAL NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS pixels(ad TEXT NOT NULL, kind TEXT NOT NULL, creative TEXT NOT NULL,
          placement TEXT NOT NULL, session TEXT NOT NULL, received TEXT NOT NULL, error TEXT NOT NULL,
          PRIMARY KEY(ad,kind));
        CREATE TABLE IF NOT EXISTS ad_responses(id TEXT PRIMARY KEY, creative TEXT NOT NULL, placement TEXT NOT NULL, session TEXT NOT NULL);
        """)
        for cid, name, theme in [("igs", "IGS · Статичный резерв", "igs")]:
            db.execute("INSERT OR IGNORE INTO campaigns VALUES (?,?,'builtin','',?,1)", (cid, name, theme))
        demo = ROOT / "creative" / "IGS-demo-15s.mp4"
        default = "igs"
        if demo.is_file() and demo.stat().st_size > 1000:
            target = MEDIA / (DEMO_ID + ".mp4")
            if not target.exists():
                shutil.copyfile(demo, target)
            db.execute("INSERT OR IGNORE INTO campaigns VALUES (?,?,'vast',?,'igs',1)", (DEMO_ID, "IGS · Видеокреатив 15 секунд", target.name))
            db.execute("INSERT OR IGNORE INTO media_info VALUES (?,15,1280,720)", (DEMO_ID,))
            default = DEMO_ID
        for pid in PLACEMENTS:
            db.execute("INSERT OR IGNORE INTO placements VALUES (?,?)", (pid, default))


initialize()
app = FastAPI(title="InGameSurface local demo", docs_url=None, redoc_url=None, openapi_url=None)
app.add_middleware(TrustedHostMiddleware, allowed_hosts=["127.0.0.1", "localhost", "testserver"])


@app.middleware("http")
async def local_writes(request: Request, call_next):
    if request.method not in ("GET", "HEAD", "OPTIONS"):
        origin = request.headers.get("origin")
        if request.headers.get("x-igs-demo") != "local" or (origin and origin not in ("http://127.0.0.1:8765", "http://localhost:8765")):
            return JSONResponse({"detail": "Local demo requests only"}, status_code=403)
    response = await call_next(request)
    response.headers["X-Content-Type-Options"] = "nosniff"
    if request.url.path.startswith("/api/"):
        response.headers["Cache-Control"] = "no-store"
    return response


@app.get("/")
def dashboard():
    return FileResponse(ROOT / "index.html")


@app.get("/api/config")
def config():
    with connection() as db:
        rows = db.execute("SELECT p.id AS placement, c.* FROM placements p JOIN campaigns c ON p.campaign=c.id ORDER BY p.id").fetchall()
    return {"schemaVersion": 1, "placements": [
        {"placementId": r["placement"], "creativeId": r["id"], "title": r["name"], "kind": r["kind"],
         "mediaPath": "/vast/" + r["placement"] + ".xml" if r["kind"] == "vast" else "/media/" + r["media"] if r["media"] else "",
         "theme": r["theme"], "enabled": bool(r["enabled"])} for r in rows]}


@app.get("/api/campaigns")
def campaigns():
    with connection() as db:
        return {"campaigns": [dict(r) for r in db.execute("SELECT * FROM campaigns ORDER BY rowid")], "placements": PLACEMENTS}


class Assignment(BaseModel):
    placementId: Literal["office_coffee", "office_screen", "street_board"]
    creativeId: str = Field(min_length=1, max_length=80)


@app.post("/api/assign")
def assign(body: Assignment):
    with connection() as db:
        if not db.execute("SELECT 1 FROM campaigns WHERE id=?", (body.creativeId,)).fetchone():
            raise HTTPException(404, "Креатив не найден")
        db.execute("UPDATE placements SET campaign=? WHERE id=?", (body.creativeId, body.placementId))
    return {"ok": True}


class Switch(BaseModel):
    creativeId: str = Field(max_length=80)
    enabled: bool


@app.post("/api/enabled")
def enabled(body: Switch):
    with connection() as db:
        cursor = db.execute("UPDATE campaigns SET enabled=? WHERE id=?", (int(body.enabled), body.creativeId))
        if not cursor.rowcount:
            raise HTTPException(404, "Креатив не найден")
    return {"ok": True}


@app.post("/api/upload")
async def upload(request: Request, filename: str = "creative.mp4", title: str = "Мой креатив", duration: float = 0, width: int = 1280, height: int = 720):
    suffix = Path(filename).suffix.lower()
    if suffix not in (".mp4", ".png", ".jpg", ".jpeg") or not 1 <= len(title.strip()) <= 100:
        raise HTTPException(400, "Нужны MP4, PNG или JPG и название до 100 символов")
    if suffix == ".mp4" and not (0 < duration <= 3600 and 1 <= width <= 8192 and 1 <= height <= 8192):
        raise HTTPException(400, "Нужны длительность и размеры видео; браузер определяет их автоматически")
    cid = uuid.uuid4().hex
    tmp = MEDIA / (cid + ".tmp")
    size, head = 0, b""
    try:
        with tmp.open("wb") as stream:
            async for chunk in request.stream():
                size += len(chunk)
                if size > LIMIT:
                    raise HTTPException(413, "Максимальный размер — 100 МБ")
                if len(head) < 32:
                    head = (head + chunk)[:32]
                stream.write(chunk)
        valid = (suffix == ".mp4" and head[4:8] == b"ftyp") or (suffix == ".png" and head.startswith(b"\x89PNG\r\n\x1a\n")) or (suffix in (".jpg", ".jpeg") and head.startswith(b"\xff\xd8\xff"))
        if not valid:
            raise HTTPException(400, "Содержимое не соответствует формату файла")
        media_name = cid + suffix
        tmp.replace(MEDIA / media_name)
        with connection() as db:
            db.execute("INSERT INTO campaigns VALUES (?,?,?,?,?,1)", (cid, title.strip(), "vast" if suffix == ".mp4" else "image", media_name, ""))
            if suffix == ".mp4":
                db.execute("INSERT INTO media_info VALUES (?,?,?,?)", (cid, duration, width, height))
        return {"id": cid, "name": title.strip()}
    finally:
        tmp.unlink(missing_ok=True)


@app.get("/media/{name}")
def media(name: str):
    if not re.fullmatch(r"[a-f0-9]{32}\.(mp4|png|jpg|jpeg)", name) or not (MEDIA / name).is_file():
        raise HTTPException(404)
    return FileResponse(MEDIA / name)


@app.get("/vast/{placement}.xml")
def vast(placement: str, session: uuid.UUID, creative: str = ""):
    if placement not in PLACEMENTS:
        raise HTTPException(404)
    root = ET.Element("VAST", version="4.3")
    with connection() as db:
        row = db.execute("SELECT c.*,m.duration,m.width,m.height FROM placements p JOIN campaigns c ON p.campaign=c.id JOIN media_info m ON m.creative=c.id WHERE p.id=? AND c.kind='vast' AND c.enabled=1", (placement,)).fetchone()
        if row is None or (creative and creative != row['id']):
            return Response(ET.tostring(root), media_type="application/xml", headers={"Cache-Control": "no-store"})
        ad_id = uuid.uuid4().hex
        db.execute("INSERT INTO ad_responses VALUES (?,?,?,?)", (ad_id, row['id'], placement, str(session)))
    def element(parent, name, value=None, **attrs):
        node = ET.SubElement(parent, name, attrs)
        if value is not None:
            node.text = str(value)
        return node
    def pixel(event):
        suffix = "&code=[ERRORCODE]" if event == "error" else ""
        return "http://127.0.0.1:8765/pixel/" + event + ".gif?ad=" + ad_id + "&cb=[CACHEBUSTING]" + suffix
    inline = element(element(root, "Ad", id=row['id']), "InLine")
    element(inline, "AdSystem", "In Game Surface local demo", version="0.1")
    element(inline, "AdTitle", row['name'])
    element(inline, "AdServingId", ad_id)
    element(inline, "Impression", pixel("impression"))
    element(inline, "Error", pixel("error"))
    item = element(element(inline, "Creatives"), "Creative", id=row['id'])
    element(item, "UniversalAdId", row['id'], idRegistry="igs.local")
    linear = element(item, "Linear")
    ms = round(row['duration'] * 1000)
    element(linear, "Duration", f"{ms//3600000:02}:{ms//60000%60:02}:{ms//1000%60:02}.{ms%1000:03}")
    tracking = element(linear, "TrackingEvents")
    for event in PIXEL_EVENTS:
        if event not in ("impression", "error"):
            element(tracking, "Tracking", pixel(event), event=event)
    element(element(linear, "MediaFiles"), "MediaFile", "http://127.0.0.1:8765/media/" + row['media'], delivery="progressive", type="video/mp4", width=str(row['width']), height=str(row['height']), scalable="true", maintainAspectRatio="true")
    return Response(ET.tostring(root, encoding="utf-8", xml_declaration=True), media_type="application/xml", headers={"Cache-Control": "no-store"})


@app.get("/pixel/{event}.gif")
def pixel(event: str, request: Request, ad: uuid.UUID, code: str = ""):
    # Local client marker keeps third-party pages from generating test impressions.
    if request.headers.get("x-igs-demo") != "local":
        raise HTTPException(403)
    if event not in PIXEL_EVENTS or len(code) > 8:
        raise HTTPException(400)
    with connection() as db:
        row = db.execute("SELECT * FROM ad_responses WHERE id=?", (ad.hex,)).fetchone()
        if row is None:
            raise HTTPException(404)
        db.execute("INSERT OR IGNORE INTO pixels VALUES (?,?,?,?,?,?,?)", (ad.hex, event, row['creative'], row['placement'], row['session'], datetime.now(timezone.utc).isoformat(), code))
    return Response(GIF, media_type="image/gif", headers={"Cache-Control": "no-store, no-cache, must-revalidate"})


@app.get("/api/pixels")
def pixel_report():
    with connection() as db:
        return {"rows": [dict(r) for r in db.execute("SELECT placement, creative, kind, COUNT(*) AS hits FROM pixels GROUP BY placement,creative,kind ORDER BY placement,creative,kind")],
                "recent": [dict(r) for r in db.execute("SELECT kind,placement,session,received,error FROM pixels ORDER BY rowid DESC LIMIT 30")]}


class AdEvent(BaseModel):
    eventId: uuid.UUID
    sessionId: uuid.UUID
    placementId: Literal["office_coffee", "office_screen", "street_board"]
    creativeId: str = Field(min_length=1, max_length=80)
    kind: Literal["loaded", "impression", "view_time", "error", "video_start", "video_25", "video_50", "video_75", "video_complete"]
    seconds: float = Field(default=0, ge=0, le=10, allow_inf_nan=False)
    timestamp: datetime


class Batch(BaseModel):
    events: list[AdEvent] = Field(max_length=100)


@app.post("/api/events")
def events(body: Batch):
    accepted = []
    with connection() as db:
        now = datetime.now(timezone.utc).isoformat()
        for e in body.events:
            if e.kind != "view_time" and e.seconds != 0:
                raise HTTPException(422, "seconds is only valid for view_time")
            db.execute("INSERT OR IGNORE INTO events VALUES (?,?,?,?,?,?,?,?)", (str(e.eventId), str(e.sessionId), e.placementId, e.creativeId, e.kind, e.seconds, e.timestamp.isoformat(), now))
            accepted.append(str(e.eventId))
    return {"accepted": accepted}


def report_rows():
    with connection() as db:
        return [dict(r) for r in db.execute("""SELECT e.placement, e.creative, COALESCE(c.name,e.creative) AS name,
          COUNT(DISTINCT session) AS sessions, SUM(e.kind='loaded') AS loads,
          SUM(e.kind='impression') AS impressions, ROUND(SUM(CASE WHEN e.kind='view_time' THEN seconds ELSE 0 END),2) AS visible_seconds,
          SUM(e.kind='video_start') AS video_starts, SUM(e.kind='video_complete') AS video_completes,
          SUM(e.kind='error') AS errors FROM events e LEFT JOIN campaigns c ON e.creative=c.id
          GROUP BY e.placement,e.creative ORDER BY e.placement,e.creative""")]


@app.get("/api/report")
def report():
    return {"rows": report_rows(), "rules": "Демо: ≥50% поверхности видно, ≥1% кадра, угол ≤60°, 2 секунды непрерывно; один показ на креатив/место/сеанс. Не сертифицированная методика."}


@app.get("/api/export.csv")
def export():
    buf = io.StringIO()
    fields = ["placement", "creative", "name", "sessions", "loads", "impressions", "visible_seconds", "video_starts", "video_completes", "errors"]
    writer = csv.DictWriter(buf, fieldnames=fields)
    writer.writeheader()
    for row in report_rows():
        writer.writerow({k: ("'" + v if isinstance(v, str) and v.startswith(("=", "+", "-", "@")) else v) for k, v in row.items()})
    return Response("\ufeff" + buf.getvalue(), media_type="text/csv; charset=utf-8", headers={"Content-Disposition": 'attachment; filename="igs-report.csv"'})


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8765, access_log=False)
