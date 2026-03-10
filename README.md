# TaskFlow — Personal Task Manager

Outlook-style calendar task manager. Two Docker containers (React frontend + C# API), SQLite persistence.

---

## Quick Start

```bash
docker compose up --build
```

Open **http://localhost:5173**. Done.

Data is stored in a named Docker volume (`taskflow-data`) — survives container rebuilds, restarts, and image updates.

---

## Architecture

```
Browser → http://localhost:5173
            │
            ▼
  ┌─────────────────────┐
  │  frontend container  │  Vite / Node 20
  │  port 5173           │  Proxies /api → backend
  └────────┬────────────┘
           │ http://backend:8080
           ▼
  ┌─────────────────────┐
  │  backend container   │  ASP.NET Core 8
  │  port 8080           │  EF Core + SQLite
  └────────┬────────────┘
           │
           ▼
     taskflow-data (named Docker volume) ← survives rebuilds
```

---

## Database schema

Three tables:

| Table                 | Purpose                                              |
|-----------------------|------------------------------------------------------|
| `Tasks`               | All tasks; self-referencing ParentId for subtasks    |
| `RecurringCompletions`| Per-day completion records for recurring tasks       |
| `AppMeta`             | Singleton row: last rollover timestamp + UI config   |

Schema is applied automatically on startup via `db.Database.Migrate()` — no manual steps.

---

## Data backup & restore

```bash
# Backup — copy the db file out of the named volume onto your host
docker run --rm -v taskflow_taskflow-data:/data -v $(pwd):/backup alpine \
  cp /data/taskflow.db /backup/taskflow-backup.db

# Restore — copy a backup back into the volume
docker run --rm -v taskflow_taskflow-data:/data -v $(pwd):/backup alpine \
  cp /backup/taskflow-backup.db /data/taskflow.db
docker compose restart backend
```

The volume is only ever deleted if you explicitly run:
```bash
docker volume rm taskflow_taskflow-data   # ← destroys all data, be careful
```

---

## Commands

```bash
docker compose up --build       # build and start
docker compose up -d --build    # start in background
docker compose down             # stop
docker compose logs -f backend  # stream backend logs
```

---

## Local development (no Docker)

```bash
# Terminal 1 — backend
cd backend
# For local dev, DataDirectory defaults to %AppData%\TodoApp (Windows) or ~/.config/TodoApp (Linux)
dotnet run

# Terminal 2 — frontend
cd frontend
npm install
# Change vite.config.js proxy target to http://localhost:8080
npm run dev
```

---

## Project structure

```
todo-app/
├── docker-compose.yml

├── backend/
│   ├── Dockerfile
│   ├── TodoApp.csproj             ← EF Core + SQLite packages
│   ├── Program.cs                 ← registers DbContextFactory, runs migrations
│   ├── Data/
│   │   └── AppDbContext.cs        ← EF Core DbContext, model config, DateOnly converters
│   ├── Migrations/                ← auto-applied on startup
│   ├── Models/Models.cs           ← TodoTask, RecurringCompletion, AppMeta entities
│   ├── Controllers/TasksController.cs
│   └── Services/
│       ├── DataStore.cs           ← EF Core queries (replaces JSON store)
│       ├── TaskService.cs
│       └── RolloverService.cs
└── frontend/
    ├── Dockerfile
    ├── vite.config.js
    └── src/
        ├── components/
        │   ├── App.jsx + App.css
        │   ├── CalendarViews.jsx
        │   ├── DayColumn.jsx
        │   ├── TaskCard.jsx
        │   └── CreateTaskModal.jsx
        ├── api.js
        └── constants.js
```
