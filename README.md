# TaskFlow — Personal Task Manager

Outlook-style calendar task manager. Three Docker containers (React frontend + C# ASP.NET Core API + PostgreSQL), data persisted in a named Docker volume.

---

## Quick Start

```bash
docker compose up --build
```

Open **http://localhost:5173**. Done.

For LAN access from other devices use `http://<host-ip>:5173`.

Data is stored in the `taskflow-data` named Docker volume — survives container rebuilds, restarts, and image updates.

---

## Architecture

```
Browser → http://localhost:5173
            │
            ▼
  ┌─────────────────────┐
  │  frontend (Node 20)  │  Vite + React 18
  │  port 5173           │  Proxies /api → backend
  └────────┬────────────┘
           │ http://backend:8080
           ▼
  ┌─────────────────────┐
  │  backend (.NET 8)    │  ASP.NET Core Web API
  │  port 8080           │  EF Core + Npgsql
  └────────┬────────────┘
           │
           ▼
  ┌─────────────────────┐
  │  db (Postgres 16)    │
  │  port 5432           │  taskflow-data volume
  └─────────────────────┘
```

Migrations run automatically on startup via `db.Database.Migrate()`.

---

## Database schema

| Table                | Purpose                                                              |
|----------------------|----------------------------------------------------------------------|
| `Tasks`              | All tasks; self-referencing `ParentId` for subtasks                  |
| `TaskCompletions`    | Per-day completion records for all tasks (source of truth)           |
| `Epics`              | Named colour-coded groups that tasks can belong to                   |
| `DayScheduleBlocks`  | Day planner time blocks (6:00–24:00, 30-min slots)                   |
| `AppMeta`            | Singleton row: last rollover timestamp + level visibility config     |

### Tasks columns

| Column                 | Type               | Notes                                              |
|------------------------|--------------------|----------------------------------------------------|
| `Id`                   | text PK            | GUID                                               |
| `Title`                | text               |                                                    |
| `Description`          | text?              |                                                    |
| `Level`                | text               | `Daily` · `Weekly` · `Monthly` · `Yearly`          |
| `Priority`             | text               | `High` · `Average` · `Low`                         |
| `IsCompleted`          | bool               | Denormalised cache — source of truth is `TaskCompletions` |
| `IsMissed`             | bool               | Set when a task is rolled over (original stays, copy moves forward) |
| `ScheduledDate`        | date?              | null for recurring tasks                           |
| `OriginalScheduledDate`| date?              | Set on first rollover                              |
| `SortOrder`            | int                | Drag-and-drop order within a day                   |
| `RolloverCount`        | int                | How many times this task has been rolled over      |
| `ParentId`             | text? FK           | Self-reference for subtasks (cascade delete)       |
| `RecurrenceMask`       | int                | Bitmask Mon=1…Sun=64; 0 = non-recurring            |
| `EpicId`               | text? FK           | FK → `Epics`, SetNull on epic delete               |
| `CreatedAt`            | timestamptz        |                                                    |
| `UpdatedAt`            | timestamptz        |                                                    |

### TaskCompletions columns

| Column        | Type        | Notes                              |
|---------------|-------------|------------------------------------|
| `Id`          | int identity|                                    |
| `TaskId`      | text FK     | Cascade delete                     |
| `Date`        | date        | The day this completion applies to |
| `CompletedAt` | timestamptz |                                    |

Unique index on `(TaskId, Date)`.

### Epics columns

| Column      | Type        |
|-------------|-------------|
| `Id`        | text PK     |
| `Title`     | text        |
| `Color`     | text        | Hex colour e.g. `#6366f1` |
| `CreatedAt` | timestamptz |

### DayScheduleBlocks columns

| Column         | Type   | Notes                        |
|----------------|--------|------------------------------|
| `Id`           | text PK|                              |
| `Date`         | date   |                              |
| `TaskId`       | text?  | FK → Tasks, SetNull on delete |
| `Label`        | text   |                              |
| `StartMinutes` | int    | Minutes since midnight        |
| `EndMinutes`   | int    | Minutes since midnight        |

---

## Migrations

Migrations are generated locally and committed — the Docker build does **not** run `dotnet ef` at build time.

```bash
# Generate a new migration (run from repo root)
cd backend
dotnet ef migrations add <MigrationName>
# Commit the generated files, then deploy
```

---

## Data backup & restore

```bash
# Backup
docker exec taskflow-db pg_dump -U taskflow taskflow > backup.sql

# Restore
docker exec -i taskflow-db psql -U taskflow taskflow < backup.sql
```

> **Never** run `docker compose down -v` unless you intend to wipe all data — the `-v` flag deletes the named volume.

---

## API endpoints

| Method | Endpoint                        | Description                              |
|--------|---------------------------------|------------------------------------------|
| GET    | `/api/tasks`                    | All tasks (including completedDates)     |
| POST   | `/api/tasks`                    | Create task                              |
| PUT    | `/api/tasks/{id}`               | Update task                              |
| POST   | `/api/tasks/{id}/toggle`        | Toggle complete → returns all tasks      |
| DELETE | `/api/tasks/{id}`               | Delete (cascades subtasks)               |
| POST   | `/api/tasks/{id}/move`          | Move to new date `{ "newDate": "..." }`  |
| POST   | `/api/tasks/reorder`            | Reorder `["id1","id2",...]`              |
| GET    | `/api/tasks/export?format=json` | Export all tasks as JSON                 |
| GET    | `/api/tasks/export?format=csv`  | Export all tasks as CSV                  |
| POST   | `/api/tasks/import`             | Import .json or .csv file                |
| GET    | `/api/config`                   | Level visibility config                  |
| PUT    | `/api/config`                   | Update config                            |
| GET    | `/api/epics`                    | All epics                                |
| POST   | `/api/epics`                    | Create epic                              |
| PUT    | `/api/epics/{id}`               | Update epic                              |
| DELETE | `/api/epics/{id}`               | Delete epic (unlinks tasks)              |
| GET    | `/api/schedule?date=yyyy-MM-dd` | Day planner blocks for a date            |
| POST   | `/api/schedule`                 | Create block                             |
| PUT    | `/api/schedule/{id}`            | Update block                             |
| DELETE | `/api/schedule/{id}`            | Delete block                             |

---

## Docker commands

```bash
docker compose up --build        # build and start
docker compose up -d --build     # start in background
docker compose down              # stop (data preserved)
docker compose down -v           # stop AND delete all data ⚠️
docker compose logs -f backend   # stream backend logs
docker compose logs -f frontend  # stream frontend logs
```

---

## Local development (no Docker)

Requires .NET 8 SDK, Node 20, and a local Postgres instance.

```bash
# Terminal 1 — backend
cd backend
# Update appsettings.json connection string to point at local Postgres
dotnet run

# Terminal 2 — frontend
cd frontend
npm install
# In vite.config.js change proxy target to http://localhost:8080
npm run dev
```

---

## Project structure

```
todo-app/
├── docker-compose.yml
├── backend/
│   ├── Dockerfile
│   ├── TodoApp.csproj               ← Npgsql.EF + EF.Design packages
│   ├── Program.cs                   ← Migrate() with retry loop, CORS *
│   ├── appsettings.json             ← Postgres connection string
│   ├── Data/AppDbContext.cs
│   ├── Models/Models.cs             ← TodoTask, TaskCompletion, Epic, DayScheduleBlock, AppMeta
│   ├── Controllers/
│   │   └── TasksController.cs       ← TasksController, BackupController, EpicsController, DayScheduleController
│   ├── Migrations/                  ← generated locally, committed, auto-applied on startup
│   └── Services/
│       ├── DataStore.cs
│       ├── TaskService.cs
│       └── RolloverService.cs       ← midnight rollover: missed copy + IsMissed flag
└── frontend/
    ├── Dockerfile
    ├── vite.config.js               ← proxy /api → http://backend:8080
    ├── package.json
    └── src/
        ├── api.js                   ← tasksApi, configApi, backupApi, epicsApi, scheduleApi
        ├── constants.js             ← LEVELS, PRIORITIES, recurrence helpers, isCompletedOnDate
        └── components/
            ├── App.jsx + App.css
            ├── CalendarViews.jsx    ← DayView, WeekView, MonthView, YearView
            ├── DayColumn.jsx        ← per-day task list with DnD reorder
            ├── TaskCard.jsx         ← task card, subtasks, rollover trail
            ├── CreateTaskModal.jsx  ← create + edit modal, epic picker
            ├── EpicsPanel.jsx       ← collapsible epics sidebar with progress bars
            ├── DayPlanner.jsx       ← 06:00–24:00 time grid, drag/resize blocks
            └── StatsPanel.jsx
```
