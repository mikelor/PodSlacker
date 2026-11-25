# PodSlacker

> Turn any YouTube video into a podcast episode — automatically.

PodSlacker fetches a video's transcript, uses an LLM to write a structured summary and a podcast script, synthesises audio via text-to-speech, captures key-moment screenshots, and packages everything into a self-contained HTML page. Optionally publishes to GitHub Pages.

Built on **.NET 11 / C# 15** with no Python, no ffmpeg, and no yt-dlp required. Kokoro local TTS is the default — **no API key needed for audio**.

---

## Features

- **No extra runtimes.** Pure .NET 11 — install the SDK and you're done.
- **Local TTS by default.** [Kokoro](https://github.com/thewh1teagle/kokoro-onnx) runs on CPU, produces high-quality 24 kHz WAV, and requires no API key. Switch to OpenAI TTS with one flag.
- **Any OpenAI-compatible LLM.** Works with OpenRouter, Ollama, Groq, Azure OpenAI, Together AI — or vanilla OpenAI. Each pipeline step can use a different provider.
- **Three ways to run.** CLI locally, CLI pointing at a remote API service, or a full Blazor web UI — all sharing the same pipeline core.
- **Aspire orchestration.** Start the API service, web UI, and dashboard in one command with .NET Aspire. No ports to manage.
- **One-command cloud deploy.** A script and a GitHub workflow publish the API and web UI to [Fly.io](https://fly.io).
- **Docker support.** Multi-stage Dockerfiles for both services and a `docker-compose.yml` for one-command local container runs. Cross-platform: OpenCvSharp runtime packages are selected automatically per OS.
- **No yt-dlp dependency.** Transcripts, metadata, and video stream URLs are all resolved natively via [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode).
- **No ffmpeg dependency.** MP3 segments are concatenated as raw bytes; WAV files are written with a hand-written RIFF header.
- **Self-contained HTML output.** Audio and screenshots are base64-embedded — the page opens in any browser, offline, with no web server.
- **GitHub Pages publishing.** One flag uploads the page and enables Pages automatically.
- **Prompt-driven.** All LLM instructions live in plain-text files you can edit freely, with a three-tier fallback so the tool always works out of the box.

### Web UI features

- **Batch submit.** Paste several YouTube URLs, one per line, and track them together on a batch page.
- **Job history sidebar.** The home page shows all in-memory jobs in a live-polling sidebar — titles, status badges, progress, and direct links. Refreshes every 5 seconds automatically.
- **LLM model selector.** Choose from a curated list of free OpenRouter models (Llama 3.3 70B, Gemini 2.0 Flash, DeepSeek R1, Mistral, Qwen, Phi-4, and more) directly in the submit form.
- **Dialogue / Monologue toggle.** Switch between a two-host conversation and a solo narration, and name the hosts, before submitting.
- **Snapshot count slider.** Control how many key-moment screenshots are captured (0–12) from the submit form.
- **Smart download filenames.** Completed SlackCasts download as `video_title_<jobid>.html` rather than a bare UUID.
- **Live video title.** The video title appears on the job status page as soon as the pipeline fetches it — before any LLM steps begin.
- **Copy job URL.** One-click copy button on the job status page puts the full URL on your clipboard with a "✓ Copied!" flash.
- **PodSlacker CTA banner.** Every generated HTML page includes a branded "Try PodSlacker now" banner at the top, linking to `https://podslacker.com`.

---

## Solution Structure

```
PodSlacker/
├── .env.example                  # Template for local settings — copy to .env (git-ignored)
├── docker-compose.yml            # Run API + Web as containers with one command
├── .github/workflows/
│   └── fly-deploy.yml            # Manual "Fly Deploy" workflow
├── scripts/
│   ├── Set-PodSlackerEnv.ps1     # Loads .env into your PowerShell session
│   └── fly-deploy.ps1            # Creates, configures and deploys the Fly.io apps
└── src/
    ├── PodSlacker.sln
    ├── Directory.Build.props     # Shared build settings (target framework, nullable, implicit usings)
    ├── Directory.Packages.props  # Central Package Management — all NuGet versions live here
    ├── .dockerignore             # Shared ignore rules for both Docker build contexts
    ├── fly.api.toml              # Fly.io config for the API service
    ├── fly.web.toml              # Fly.io config for the web UI
    ├── PodSlacker.Core/          # Pipeline logic, models, services — shared by all clients
    ├── PodSlacker.Cli/           # Command-line client (local or remote mode)
    ├── PodSlacker.ApiService/
    │   ├── Dockerfile            # Multi-stage image with OpenCV native deps
    │   └── ...                   # ASP.NET Core Minimal API — runs the pipeline as a web service
    ├── PodSlacker.Web/
    │   ├── Dockerfile            # Lightweight Blazor Server image (no native deps)
    │   └── ...                   # Blazor Server web UI — submits jobs and polls for results
    ├── PodSlacker.ServiceDefaults/ # Shared health checks, OTEL, and service discovery wiring
    └── PodSlacker.AppHost/       # .NET Aspire host — orchestrates ApiService + Web together
```

---

## Quick Start

### What you need

| Requirement | Notes |
|---|---|
| [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) | The only thing to install for local runs |
| An LLM API key | [OpenRouter](https://openrouter.ai/keys) has a free tier and is the default. Any OpenAI-compatible provider works — see [LLM Providers](#llm-providers) |
| [Fly CLI](https://fly.io/docs/flyctl/install/) *(optional)* | Only for [publishing to Fly.io](#3-publish-to-flyio) |

Audio needs no key: Kokoro TTS runs locally and downloads its ~320 MB model once, on the first job.

### 1. Clone and add your key

```powershell
git clone https://github.com/mikelor/podslacker.git
cd podslacker
Copy-Item .env.example .env
```

Open `.env` and set `OPENROUTER_API_KEY`. The file is git-ignored, so the key never reaches GitHub. The other entries are optional — see [Settings](#settings).

### 2. Run the web app with Aspire

```powershell
./scripts/Set-PodSlackerEnv.ps1
dotnet run --project src/PodSlacker.AppHost
```

The first command loads `.env` into the current terminal; the second starts the API service, the web UI and the Aspire dashboard together. Then open:

- **Web UI:** http://localhost:5200 — paste a YouTube URL and click **Generate Podcast**.
- **Aspire dashboard:** use the `http://localhost:18888/login?t=...` link printed in the terminal for logs, traces and resource status.

Run both commands in the same terminal: the AppHost passes the keys it finds in its own environment on to the API service. More detail is under [Running as a Web Service](#running-as-a-web-service).

### 3. Publish to Fly.io

With the [Fly CLI](https://fly.io/docs/flyctl/install/) installed and the same `.env` in place:

```powershell
fly auth login
./scripts/fly-deploy.ps1 -Setup -Secrets -Deploy
```

That creates the two Fly apps and their volumes, pushes your keys as Fly secrets, builds the images on Fly, and deploys. When it finishes the site is at **https://podslacker-web.fly.dev**. Read [Deploying to Fly.io](#deploying-to-flyio) first: the web UI is public and has no login.

### Or just use the CLI

No web service needed — the CLI runs the whole pipeline in-process:

```powershell
./scripts/Set-PodSlackerEnv.ps1
dotnet run --project src/PodSlacker.Cli -c Release -- generate "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

Files written to `outputs/`:

| File | Contents |
|---|---|
| `*_transcript.txt` | Timestamped captions `[MM:SS] text` |
| `*_summary.md` | LLM summary + full podcast script |
| `*_podcast.wav` | Stitched audio (Kokoro) or `*_podcast.mp3` (OpenAI TTS) |
| `*_frame_01.jpg` … | Key-moment screenshots (6 by default) |
| `*_page.html` | Self-contained page with summary, gallery, and audio player |

The remaining CLI examples in this README use bash syntax and are run from the `src/` folder.

### Settings

Every setting is an environment variable. For local runs put them in `.env` and load them with `./scripts/Set-PodSlackerEnv.ps1` (add `-Persist` to save them to your Windows user profile). On macOS or Linux, `set -a; source .env; set +a` does the same job.

| Variable | Needed? | Purpose |
|---|---|---|
| `OPENROUTER_API_KEY` | **Required** (default setup) | LLM key for the summary, script and key-moment steps |
| `OPENAI_API_KEY` | Only for OpenAI | Needed when using OpenAI TTS or OpenAI as the LLM provider |
| `GITHUB_TOKEN` | Only for publishing | PAT with `repo` + `pages` scopes, used by "Publish to GitHub Pages" when no token is typed into the form |
| `YOUTUBE_API_KEY` | No | Overrides the built-in key used by a transcript fallback |
| `PODSLACKER_API_KEY` | No | Requires an `X-Api-Key` header on the API service. Leave unset for local development |
| `PODSLACKER_SERVICE_URL` | Leave unset with Aspire | Points the CLI or web UI at a running API service. Aspire wires this up itself |

### Want a fully free, fully local run?

```bash
# Start Ollama with any model
ollama run llama3.2

# Run with local LLM + local Kokoro TTS — zero API keys
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --llm-base-url http://localhost:11434/v1 \
  --llm-model llama3.2 \
  --llm-api-key-env OPENAI_API_KEY
```

### Or use OpenAI for everything

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --llm-api-key-env OPENAI_API_KEY \
  --llm-model gpt-4o \
  --tts-engine openai
```

---

## Running as a Web Service

PodSlacker can run as a persistent REST API that accepts jobs from either the CLI or the Blazor web UI. There are two ways to start it.

### Option A — .NET Aspire (recommended for development)

Aspire starts the API service and the web UI together and opens a dashboard showing live logs, traces, and resource status.

```powershell
./scripts/Set-PodSlackerEnv.ps1
dotnet run --project src/PodSlacker.AppHost
```

- The **web UI** is at http://localhost:5200.
- The **Aspire dashboard** is at http://localhost:18888. It asks for a token; the terminal prints a ready-made `login?t=...` link.
- The **API service** gets a dynamic port, shown in the dashboard under `podslacker-api`.

Things to know:

- **Keys come from the AppHost's environment.** `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `YOUTUBE_API_KEY` and `GITHUB_TOKEN` are read when the AppHost starts and passed to the API service, so load `.env` first and restart the AppHost after changing a key.
- **Do not set `PODSLACKER_SERVICE_URL`.** The AppHost clears it and wires the web UI to the API through service discovery.
- **Use a normal Debug run.** `dotnet run` hands off to the Aspire CLI, which builds and launches the Debug configuration; `-c Release --no-build` fails to find the executable.
- **Missing key?** The API logs a warning at startup for each key that is not set, and jobs fail at the summary step with a 401.

### Option B — Manual startup

Start the API service first, then point the CLI or web UI at it.

```bash
# Terminal 1 — start the API on a fixed port
export OPENROUTER_API_KEY=sk-or-...
export PODSLACKER_API_KEY=my-secret   # optional — enables X-Api-Key auth
# export YOUTUBE_API_KEY=AIza...      # optional — only needed if the default key stops working
dotnet run --project src/PodSlacker.ApiService -- --urls http://localhost:5100

# Terminal 2 — use the CLI in remote mode
export PODSLACKER_SERVICE_URL=http://localhost:5100
export PODSLACKER_API_KEY=my-secret                   # must match the server's value if set
dotnet run --project src/PodSlacker.Cli -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ"

# Or start the web UI pointing at the running API
export PODSLACKER_SERVICE_URL=http://localhost:5100
dotnet run --project src/PodSlacker.Web
```

### API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/jobs` | Submit a new job; returns `202 Accepted` with a job ID |
| `GET` | `/api/jobs` | List all in-memory jobs, newest first |
| `GET` | `/api/jobs/{id}` | Poll job status (Queued / Running / Completed / Failed) |
| `GET` | `/api/jobs/{id}/page` | Download the generated HTML page once completed |
| `GET` | `/api/github-folders` | List top-level folders on the GitHub Pages branch (`repo`, `branch`, `token` query parameters) |
| `GET` | `/health` | Health check (no auth required) |
| `GET` | `/alive` | Liveness check (no auth required) |

#### Authentication

Set `PODSLACKER_API_KEY` on the API service to require an `X-Api-Key` header on all requests. If the variable is unset the API runs without authentication (fine for local dev).

#### POST /api/jobs body

```json
{
  "video_url": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
  "config": {
    "hosts": 2,
    "num_frames": 6,
    "llm_model": "meta-llama/llama-3.3-70b-instruct:free",
    "llm_base_url": "https://openrouter.ai/api/v1",
    "tts_engine": "kokoro",
    "publish_github": true,
    "github_repo": "podslacker-pages",
    "github_folder": "google-cloud-next",
    "github_token_value": "ghp_..."
  }
}
```

All `config` fields are optional — omitted fields use the server's compiled defaults. The server always overrides `output_dir` (it uses a temp directory). `publish_github` is honoured: the job publishes with `github_token_value` if supplied, otherwise with the server's `GITHUB_TOKEN`. `github_folder` places the published files in a subfolder of the Pages branch.

#### GET /api/jobs/{id} response

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "video_url": "https://...",
  "title": "Never Gonna Give You Up",
  "status": "Completed",
  "message": "Done!",
  "percent": 100,
  "error": null,
  "page_url": "http://localhost:5100/api/jobs/3fa85f64-.../page",
  "git_hub_pages_url": "https://username.github.io/podslacker-pages/never_gonna_give_you_up_dQw4w9WgXcQ_page.html"
}
```

`status` is one of `Queued`, `Running`, `Completed`, `Failed`. `title` is populated as soon as the pipeline fetches it from YouTube (typically within the first few seconds). `page_url` is populated (pointing at `/api/jobs/{id}/page`) only when status is `Completed`. `git_hub_pages_url` is populated only when `publish_github` was `true` and publishing succeeded.

The download filename for `/api/jobs/{id}/page` is derived from the video title — e.g. `never_gonna_give_you_up_3fa85f64.html` — rather than a bare UUID.

---

## Web UI Walkthrough

Once the Aspire host or Docker Compose stack is running, open the web UI (`http://localhost:5200` in both cases) in any browser.

### Submit form

The home page is split into two columns: the submit form on the left and a live job history sidebar on the right.

**YouTube URL(s)** — paste one YouTube link, or several with one per line, and click **🎙️ Generate Podcast**. A single URL opens that job's status page; several URLs open a batch page that tracks them all, refreshing every 5 seconds.

**Format** — choose between *🎭 Dialogue* (two hosts, two voices) or *🎤 Monologue* (one host). Maps to `hosts: 2` and `hosts: 1` in the config.

**Host names** — default to JORDAN and MIKE; change them to rename the speakers in the script.

**AI Model** — select the LLM used for summarisation and script generation. All options are free via OpenRouter:

| Model | Notes |
|---|---|
| Llama 3.3 70B | Recommended — best balance of quality and speed |
| Gemini 2.0 Flash | Very fast; good for shorter videos |
| DeepSeek R1 | Strong reasoning; slower |
| Mistral Small 3.1 24B | Compact and fast |
| Qwen 3 235B | Largest model; highest quality, slowest |
| Phi-4 14B | Lightweight; good for quick tests |
| OpenRouter Auto | Routes to the best available free model automatically |

**Key-Moment Snapshots** — drag the slider from 0 (none) to 12 to control how many screenshots are captured from the video.

### Job history sidebar

The sidebar polls `GET /api/jobs` every 5 seconds and shows all in-memory jobs with animated status badges. Click any row to jump to that job's status page. Jobs are held in memory only — they reset when the API service restarts.

### Publishing to GitHub Pages

Check **Publish to GitHub Pages** in the submit form to automatically push the finished SlackCast to a public GitHub Pages site when the pipeline completes.

**Repository name** — defaults to `podslacker-pages`. The repository is created automatically if it doesn't exist.

**Folder** — optionally publish into a subfolder of the Pages branch. The list shows the folders already there; choose **＋ New folder…** to create one.

**Personal Access Token** — paste a GitHub PAT with `repo` and `pages` scopes directly into the form. This overrides any `GITHUB_TOKEN` set on the server, so you can publish to different accounts without restarting the service. Leave the field blank to use the server's `GITHUB_TOKEN` environment variable instead. [Create a token ↗](https://github.com/settings/tokens)

When the job completes, the status page shows both a **⬇️ Download Your SlackCast** button and a **🌐 View on GitHub Pages** link. Published jobs also show a 🌐 icon in the home page sidebar.

#### Setting up your GitHub Pages repository

PodSlacker creates the repository and `gh-pages` branch automatically on first publish, but GitHub requires Pages to be enabled manually once before the site goes live.

1. After your first publish, go to `https://github.com/<your-username>/podslacker-pages/settings/pages`
2. Under **Build and deployment → Source**, select **Deploy from a branch**
3. Set the branch to `gh-pages` and the folder to `/ (root)`, then click **Save**

Your SlackCasts will then be publicly accessible at:

```
https://<your-username>.github.io/podslacker-pages/<video-title>_page.html
```

Each subsequent publish pushes a new file to the same branch — existing pages are never overwritten. For full GitHub Pages documentation see [docs.github.com/pages](https://docs.github.com/en/pages).

### Job status page

While the pipeline runs, the status page shows the video title (populated within seconds of job creation), a progress bar, and the current pipeline step. When the job completes, click **⬇️ Download Your SlackCast** to save the self-contained HTML page. Use the **⎘ Copy URL** button to copy a shareable link to the job.

### Generated SlackCast page

The downloaded HTML file is fully self-contained (audio and images are base64-embedded). It opens in any browser with no internet required. Every page includes a branded **Try PodSlacker now ↗** banner at the top.

---

## Running with Docker

Docker Compose is the quickest way to run both services as containers without installing the .NET SDK.

```bash
# 1. Create a .env file next to docker-compose.yml and fill in your API keys
cp .env.example .env

# 2. Build and start both services
docker compose up --build

# Web UI → http://localhost:5200
# API    → http://localhost:5100
```

`docker compose down` stops the containers but preserves the `kokoro-model` volume so the 320 MB ONNX model doesn't need to re-download. Add `--volumes` to remove it too.

> **Upgrading from an older checkout?** The volume used to be mounted at `/app`, which also captured the application binaries. Run `docker compose down --volumes` once so a stale volume is not reused.

### Building images individually

Both Dockerfiles use the `src/` directory as their build context:

```bash
cd src

# API service
docker build -f PodSlacker.ApiService/Dockerfile -t podslacker-api .

# Web UI
docker build -f PodSlacker.Web/Dockerfile -t podslacker-web .
```

### Environment variables

| Variable | Service | Description |
|---|---|---|
| `OPENROUTER_API_KEY` | API | LLM API key (or whichever provider you use) |
| `OPENAI_API_KEY` | API | Required only when using OpenAI TTS |
| `PODSLACKER_API_KEY` | API + Web | Enables `X-Api-Key` auth on the API; must match in both containers |
| `PODSLACKER_SERVICE_URL` | Web | Set automatically by Docker Compose to `http://podslacker-api:8080` |
| `YOUTUBE_API_KEY` | API | YouTube internal WEB client key used by transcript fallback Tier 3. A safe public default is compiled in; only set this if the default stops working. |
| `GITHUB_TOKEN` | API | GitHub Personal Access Token with `repo` + `pages` scopes. Used as a server-wide fallback when "Publish to GitHub Pages" is enabled but no token is entered in the web form. [Create one ↗](https://github.com/settings/tokens) |
| `KOKORO_MODEL_DIR` | API | Folder where the Kokoro model is stored. The Docker image sets it to `/data/kokoro`; Docker Compose and Fly mount a volume at `/data`. When unset (local runs) the model sits next to the executable. |
| `ASPNETCORE_ENVIRONMENT` | Both | Defaults to `Development`; set to `Production` for hardened deployments |

### OpenCV cross-platform support

`PodSlacker.Core.csproj` selects the correct OpenCvSharp4 native runtime package at build time via MSBuild conditions:

| Platform | Package |
|---|---|
| Windows | `OpenCvSharp4.runtime.win` |
| Linux x64 | `OpenCvSharp4.official.runtime.linux-x64` |
| macOS Apple Silicon | `OpenCvSharp4.runtime.osx-arm64` |
| macOS Intel | `OpenCvSharp4.runtime.osx-x64` |

> **macOS caveat:** the two macOS package IDs above could not be found on nuget.org when the packages were last updated (November 2026), so restore may fail on a Mac. Windows and Linux are unaffected.

The Linux image also installs the required system libraries (`libglib2.0-0`, `libgtk2.0-0`, `libavcodec-extra`, etc.) via `apt-get` in the Dockerfile's runtime stage.

---

## Deploying to Fly.io

PodSlacker runs on [Fly.io](https://fly.io) as two apps:

| App | Config | Exposure | Machine |
|---|---|---|---|
| `podslacker-api` | `src/fly.api.toml` | Private: no public ports, reachable only at `podslacker-api.internal:8080` | Always on, 2 GB, with a 1 GB volume for the Kokoro model |
| `podslacker-web` | `src/fly.web.toml` | Public: `https://podslacker-web.fly.dev` | Stops when idle, starts on the next request |

The API is always on because jobs run in the background and are held in memory; an auto-stopping machine would kill them mid-pipeline.

### First deploy

Prerequisites: a Fly.io account, the [Fly CLI](https://fly.io/docs/flyctl/install/), and a `.env` file in the repo root with at least `OPENROUTER_API_KEY` (copy `.env.example`). Docker is not needed; images are built on Fly.

```powershell
fly auth login
./scripts/fly-deploy.ps1 -Setup -Secrets -Deploy
```

When it finishes, open https://podslacker-web.fly.dev. The first request may take a few seconds while the web machine starts, and the first job downloads the Kokoro model to the API's volume.

`-Setup` creates the apps and volumes if they are missing, `-Secrets` pushes values from `.env` to Fly over stdin, and `-Deploy` builds on Fly's remote builders and releases the API, then the web UI. Each switch can be run on its own and is safe to repeat:

```powershell
./scripts/fly-deploy.ps1 -Deploy             # redeploy both apps
./scripts/fly-deploy.ps1 -Deploy -App web    # just the web UI
./scripts/fly-deploy.ps1 -Secrets            # after rotating a key in .env
```

### Secrets

| Secret | Set on | Notes |
|---|---|---|
| `OPENROUTER_API_KEY` | API | Required |
| `OPENAI_API_KEY` | API | Only for OpenAI TTS or LLM |
| `YOUTUBE_API_KEY` | API | Optional override |
| `PODSLACKER_API_KEY` | API + Web | Optional; must match on both. The API is already private, so this only guards against other apps in the same Fly organisation. |
| `GITHUB_TOKEN` | API | **Not pushed by default.** The web UI has no login, so a server-side token lets any visitor publish to your GitHub Pages. Pass `-IncludeGitHubToken` only if the site is otherwise protected. |

### Deploying from GitHub Actions

The **Fly Deploy** workflow (`.github/workflows/fly-deploy.yml`) is manual-trigger: run it from the Actions tab and choose `both`, `api`, or `web`. It needs one repository secret:

```powershell
fly tokens create org --name podslacker-github    # copy the token it prints
gh secret set FLY_API_TOKEN                        # paste it when prompted
```

### Things to know

- **The web UI is public and unauthenticated.** Anyone with the URL can submit jobs, which spend your LLM credits and CPU time.
- **Deploying the API drops in-flight jobs.** Its single machine restarts and the job list is in memory.
- **CLI remote mode** needs a tunnel, since the API has no public address: `fly proxy 5100:8080 --app podslacker-api`, then set `PODSLACKER_SERVICE_URL=http://localhost:5100`.
- **Useful commands:** `fly logs --app podslacker-api`, `fly status --app podslacker-web`, `fly secrets list --app podslacker-api`.
- **App names are global on Fly.** If `podslacker-api` or `podslacker-web` is taken, change `app` in both `fly.*.toml` files, the `.internal` hostname in `fly.web.toml`, and the names in `scripts/fly-deploy.ps1`.

---

## Exposing the Web UI via Dev Tunnels

Dev Tunnels creates a secure public HTTPS URL that forwards to your locally-running web UI. This lets you share or access PodSlacker from any browser without deploying anything to a server.

Only the **Web** project needs to be exposed. The ApiService is called server-side over `localhost` and never needs to be reachable from the internet.

```
Internet browser
      │  HTTPS  (e.g. https://abc123-5200.usw2.devtunnels.ms)
      ▼
Dev Tunnel ──────────────► PodSlacker.Web  (localhost:5200)
                                  │
                                  │  http://localhost:5100  (internal only)
                                  ▼
                           PodSlacker.ApiService
```

### 1. Install and authenticate (one-time)

```bash
# Windows
winget install Microsoft.devtunnel

# macOS
brew install --cask devtunnel

# Log in with your Microsoft or GitHub account
devtunnel user login
```

### 2. Check the Web port

The web UI listens on port **5200** on every run (set in `PodSlacker.Web/Properties/launchSettings.json`), so the tunnel target never changes. To use a different port, change `applicationUrl` there.

### 3. Start your services

```bash
dotnet run --project src/PodSlacker.AppHost
```

### 4. Host the tunnel

```bash
# Quick one-off — public URL is printed, tunnel closes when you Ctrl+C
devtunnel host --port-number 5200 --allow-anonymous

# Or create a persistent named tunnel so the URL stays the same between sessions
devtunnel create podslacker --allow-anonymous
devtunnel port create podslacker --port-number 5200
devtunnel host podslacker
```

The public URL will look like `https://abc123-5200.usw2.devtunnels.ms`. Share it or bookmark it — everything works including the Blazor interactive UI and the download endpoint.

`--allow-anonymous` makes the URL accessible to anyone who has the link. Without it, Dev Tunnels requires visitors to sign in with a Microsoft account, which is useful if you want to restrict access.

> **How Blazor works through the tunnel.** Blazor Server's interactive features rely on a WebSocket connection from the browser back to the server. The `UseForwardedHeaders()` middleware added to `PodSlacker.Web/Program.cs` tells ASP.NET Core to trust the `X-Forwarded-Proto` header that Dev Tunnels injects, so the app knows it's running behind HTTPS and produces the correct `wss://` upgrade URL. Without this the Blazor circuit silently fails and button clicks do nothing.

---

## CLI Remote Mode

When `PODSLACKER_SERVICE_URL` is set the CLI automatically delegates to the remote API instead of running the pipeline locally. All the usual `generate` flags (LLM provider, TTS engine, voices, etc.) are forwarded to the server in the request body.

```bash
export PODSLACKER_SERVICE_URL=http://localhost:5100
export PODSLACKER_API_KEY=my-secret   # if server requires auth

dotnet run --project PodSlacker.Cli -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --output-file ~/podcasts/episode.html
```

The CLI submits the job, polls every 5 seconds printing progress, then downloads the finished HTML page. `--output-file` controls where the HTML is saved (defaults to `podslacker_<jobId>.html` in the current directory).

---

## Building

```bash
cd src
dotnet build PodSlacker.sln -c Release
```

To publish a self-contained CLI executable:

```bash
# Windows x64
dotnet publish PodSlacker.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish PodSlacker.Cli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# macOS Apple Silicon
dotnet publish PodSlacker.Cli -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

---

## Configuration File

`podslacker.json` (next to the executable, or in the working directory) is loaded automatically. CLI flags always take precedence. Keys starting with `_` are treated as comments; `null` values fall through to the compiled default.

```json
{
  "_comment": "PodSlacker defaults — CLI flags override everything here",

  "output_dir": "outputs",
  "hosts": 2,
  "host1_name": "JORDAN",
  "host2_name": "MIKE",
  "num_frames": 6,

  "_llm": "Use any OpenAI-compatible endpoint",
  "llm_base_url": "https://openrouter.ai/api/v1",
  "llm_model": "openrouter/auto:free",
  "llm_api_key_env": "OPENROUTER_API_KEY",

  "_tts": "kokoro = local, free. openai = cloud, requires API key.",
  "tts_engine": "kokoro",
  "voice_host1": "af_heart",
  "voice_host2": "am_michael",
  "tts_model": "tts-1",
  "tts_api_key_env": "OPENAI_API_KEY",

  "_prompts": "Create these files to override the embedded defaults.",
  "summary_prompt": "prompts/summary.txt",
  "dialogue_prompt": "prompts/dialogue.txt",
  "monologue_prompt": "prompts/monologue.txt",
  "key_moments_prompt": "prompts/key_moments.txt",

  "_github": "Set publish_github to true and provide a GITHUB_TOKEN",
  "publish_github": false,
  "github_repo": "podslacker-pages",
  "github_token_env": "GITHUB_TOKEN",
  "github_branch": "gh-pages",
  "github_folder": "",
  "github_embed_assets": false
}
```

`github_folder` publishes into a subfolder of the Pages branch. `github_embed_assets` controls how published pages carry their media: `false` (default) uploads audio and images as separate files next to a small HTML page, `true` base64-embeds them into one large file. Both are config-file settings; there is no CLI flag for them.

Use `--config /path/to/custom.json` to load a different file.

---

## CLI Reference

```
podslacker generate <url> [options]
```

### General

| Flag | Default | Description |
|---|---|---|
| `--output-dir, -o DIR` | `outputs` | Directory for all generated files (local mode only) |
| `--output-file FILE` | `podslacker_<id>.html` | Where to save the HTML in remote mode |
| `--hosts {1,2}` | `2` | `1` = solo monologue, `2` = two-host dialogue |
| `--host1-name NAME` | `JORDAN` | Name for the first (or only) host |
| `--host2-name NAME` | `MIKE` | Name for the second host |
| `--no-audio` | off | Skip TTS synthesis |
| `--no-page` | off | Skip HTML page generation |
| `--reuse-summary` | off | Skip LLM calls if `*_summary.md` already exists |
| `--num-frames N` | `6` | Key-moment screenshots to capture; `0` to disable |
| `--config FILE` | auto | Path to a JSON config file |

### YouTube Access

| Flag | Default | Description |
|---|---|---|
| `--cookies FILE` | none | Netscape `cookies.txt` for age-restricted videos |
| `--proxy URL` | none | Proxy URL, e.g. `http://user:pass@host:port` |

### LLM Provider

| Flag | Default | Description |
|---|---|---|
| `--llm-model MODEL` | `openrouter/auto:free` | Model identifier |
| `--llm-base-url URL` | `https://openrouter.ai/api/v1` | Base URL for the chat completions endpoint |
| `--llm-api-key-env VAR` | `OPENROUTER_API_KEY` | Env var holding the API key |

To use plain OpenAI instead, pass `--llm-base-url` *(omit for OpenAI)*, `--llm-model gpt-4o`, and `--llm-api-key-env OPENAI_API_KEY`.

Each pipeline step (summary, script generation, key-moment identification) can override these independently using `--summary-model`, `--summary-base-url`, `--summary-api-key-env`, and equivalents for `--script-*` and `--key-moments-*`.

### TTS Engine

| Flag | Default | Description |
|---|---|---|
| `--tts-engine {kokoro,openai}` | `kokoro` | TTS backend |

**Voice selection** (`--voice-host1`, `--voice-host2`)

The same two flags work for both engines — just use the right voice name for whichever engine is active.

| Flag | Default | Description |
|---|---|---|
| `--voice-host1 VOICE` | `af_heart` | Voice for host 1 (Kokoro or OpenAI name) |
| `--voice-host2 VOICE` | `am_michael` | Voice for host 2 (Kokoro or OpenAI name) |

Defaults come from the `podslacker.json` that ships next to the CLI. Example voices per engine:

| Engine | Host | Voice | Style |
|---|---|---|---|
| Kokoro | Host 1 | `af_heart` | American English female |
| Kokoro | Host 2 | `am_michael` | American English male |
| OpenAI | Host 1 | `onyx` | Deep, authoritative male |
| OpenAI | Host 2 | `nova` | Warm, conversational female |

Full voice lists: [Kokoro voices (KokoroSharp)](https://github.com/thewh1teagle/kokoro-onnx) · [OpenAI TTS voices](https://platform.openai.com/docs/guides/text-to-speech)

**OpenAI TTS model** (ignored when using Kokoro):

| Flag | Default | Description |
|---|---|---|
| `--tts-model MODEL` | `tts-1` | `tts-1` (faster) or `tts-1-hd` (higher quality) |
| `--tts-api-key-env VAR` | `OPENAI_API_KEY` | Env var for the TTS API key |

### Prompt Overrides

| Flag | Default | Description |
|---|---|---|
| `--summary-prompt FILE` | embedded | Custom system prompt for the summary step |
| `--dialogue-prompt FILE` | embedded | Custom prompt for two-host dialogue |
| `--monologue-prompt FILE` | embedded | Custom prompt for solo narration |
| `--key-moments-prompt FILE` | embedded | Custom prompt for key-moment selection |

Prompts are resolved in this order: CLI flag → `prompts/` folder next to the executable → embedded resource in `PodSlacker.Core.dll`.

### GitHub Pages

| Flag | Default | Description |
|---|---|---|
| `--publish-github` | off | Publish the HTML page to GitHub Pages |
| `--github-repo REPO` | `podslacker-pages` | Repository name (created if missing) |
| `--github-token-env VAR` | `GITHUB_TOKEN` | Env var for a PAT with `repo` + `pages` scopes |
| `--github-branch BRANCH` | `gh-pages` | Branch used as the Pages source |

---

## LLM Providers

Any OpenAI-compatible endpoint works. Set `--llm-base-url` and `--llm-api-key-env` (or the equivalents in `podslacker.json`):

| Provider | `llm_base_url` | Example model |
|---|---|---|
| OpenAI | *(omit)* | `gpt-4o`, `gpt-4o-mini` |
| OpenRouter | `https://openrouter.ai/api/v1` | `google/gemini-2.0-flash-001`, `anthropic/claude-3-5-sonnet` |
| Ollama (local) | `http://localhost:11434/v1` | `llama3.2`, `mistral`, `phi4` |
| Groq | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` |
| Azure OpenAI | `https://<resource>.openai.azure.com/` | `gpt-4o` |
| Together AI | `https://api.together.xyz/v1` | `meta-llama/Llama-3-70b-chat-hf` |

---

## Usage Examples

**Regenerate audio without re-running the LLM:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --reuse-summary
```

**British voices:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --voice-host1 bm_george \
  --voice-host2 bf_emma
```

**Solo monologue, no frames:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --hosts 1 --num-frames 0
```

**Publish to GitHub Pages:**
```bash
export GITHUB_TOKEN=ghp_...
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --publish-github
```

**Use different models per step (summary on Claude, everything else on Gemini):**
```bash
export OPENROUTER_API_KEY=sk-or-...
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --llm-base-url https://openrouter.ai/api/v1 \
  --llm-model google/gemini-2.0-flash-001 \
  --llm-api-key-env OPENROUTER_API_KEY \
  --summary-model anthropic/claude-3-5-sonnet \
  --summary-api-key-env OPENROUTER_API_KEY
```

**Custom host names and output folder:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --host1-name SAM --host2-name TAYLOR \
  --output-dir ~/podcasts
```

**Submit to a remote API service and save the result:**
```bash
export PODSLACKER_SERVICE_URL=http://localhost:5100
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --output-file ~/podcasts/episode.html
```

---

## Architecture

### Pipeline

The pipeline runs seven stages in sequence, reporting progress via an `IProgress<PipelineProgress>` sink. `PodSlacker.Core` owns all pipeline logic and is shared by the CLI, the API service, and any future client.

```
YouTube URL
    │
    ▼
┌──────────────────────────────────────┐
│  1. Fetch title                       │  YoutubeExplode → oEmbed fallback
└──────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────┐
│  2. Fetch transcript                  │  YoutubeExplode ClosedCaptions
└──────────────────────────────────────┘  (HTTP fallback tiers: watch-page JSON,
    │  writes *_transcript.txt             YouTubei API, timedtext XML)
    ▼
┌──────────────────────────────────────┐
│  3. LLM — summary + podcast script   │  IChatClient (any OpenAI-compatible)
└──────────────────────────────────────┘
    │  writes *_summary.md
    ▼
┌──────────────────────────────────────┐
│  4. TTS audio                         │  Kokoro (local WAV) or OpenAI (MP3)
└──────────────────────────────────────┘
    │  writes *_podcast.wav / .mp3
    ▼
┌──────────────────────────────────────┐
│  5. Frame capture                     │  LLM identifies timestamps
└──────────────────────────────────────┘  YoutubeExplode stream URL → OpenCvSharp
    │  writes *_frame_01.jpg …
    ▼
┌──────────────────────────────────────┐
│  6. HTML page                         │  Markdig renders markdown; audio +
└──────────────────────────────────────┘  images are base64-embedded
    │  writes *_page.html
    ▼
┌──────────────────────────────────────┐
│  7. GitHub Pages (optional)           │  Octokit creates/updates repo + branch,
└──────────────────────────────────────┘  enables Pages
    │
    ▼
https://<user>.github.io/<repo>/*_page.html
```

### Multi-tier deployment

```
┌─────────────────────┐     ┌─────────────────────┐
│   PodSlacker.Cli    │     │   PodSlacker.Web     │
│  (terminal client)  │     │  (Blazor Server UI)  │
└────────┬────────────┘     └──────────┬───────────┘
         │  PODSLACKER_SERVICE_URL       │  Aspire service discovery
         │  (manual) or Aspire           │  → http://podslacker-api
         │                               │
         └──────────────┬────────────────┘
                        │ REST  POST /api/jobs
                        │      GET  /api/jobs/{id}
                        │      GET  /api/jobs/{id}/page
                        ▼
         ┌──────────────────────────────┐
         │    PodSlacker.ApiService     │
         │  ASP.NET Core Minimal API    │
         │  • In-memory job store       │
         │  • Background PipelineRunner │
         │  • 1-hour job TTL eviction   │
         └──────────────┬───────────────┘
                        │
                        ▼
         ┌──────────────────────────────┐
         │      PodSlacker.Core         │
         │  Pipeline + services (shared)│
         └──────────────────────────────┘
```

When running locally (no `PODSLACKER_SERVICE_URL`), the CLI calls `PodSlacker.Core` directly in-process. When `PODSLACKER_SERVICE_URL` is set (or when the Blazor web UI is used), the pipeline runs inside `PodSlacker.ApiService` and the caller just polls for the result.

### Key design decisions

**`Microsoft.Extensions.AI` for LLM abstraction.** The `IChatClient` interface means OpenAI, OpenRouter, Ollama, and Azure OpenAI all work without any changes to service code. The caller wires up the concrete client; the pipeline never sees a provider name.

**Per-step LLM clients.** Summary, script generation, and key-moment identification each get their own `IChatClient`, built from their own base URL and API key env var. When a step's settings match the base config, the same client instance is reused.

**`IServiceScopeFactory` in background tasks.** `PipelineRunner` injects `IServiceScopeFactory` rather than `IServiceProvider` directly. Each background job creates its own DI scope, so scoped services (including `PodSlackerPipeline`) are resolved and disposed correctly without outliving the HTTP request scope that created the job.

**Env-var key references.** API keys are never passed as CLI arguments — only the *name* of the environment variable is passed. Secrets stay out of shell history and process listings.

**No ffmpeg.** MP3 is a frame-based format; segments concatenate cleanly as raw bytes. WAV files for Kokoro are written with a hand-crafted 44-byte RIFF/WAVE PCM header — no audio library required.

**Kokoro model management.** The 320 MB `kokoro.onnx` model is downloaded once to `KOKORO_MODEL_DIR` (or `AppContext.BaseDirectory` when that is unset) via a streamed HTTP download with 10%-bucket progress logging, using an atomic `.download → rename` pattern so a cancelled download never leaves a corrupt file.

**Embedded prompts.** All four system prompts are embedded resources in `PodSlacker.Core.dll`, so the CLI, the web API, and any future clients share the same canonical prompts with no file-copying during deployment.

**NuGet-based Aspire (no workload).** `PodSlacker.AppHost` uses `Aspire.Hosting.AppHost`, `Aspire.Hosting.Dotnet` and the platform-specific DCP/Dashboard NuGet packages directly, avoiding the deprecated `IsAspireHost` workload property that triggers `NETSDK1228` on .NET 10 and later.

---

## Python Version

The original Python implementation lives in [`python/`](python/). It has a `--tts-engine kokoro` flag and supports the same basic pipeline but requires Python 3.10+, `pip` packages, and optionally `yt-dlp`. The .NET version supersedes it but the Python code is preserved for reference.

---

## Acknowledgements

PodSlacker is built on top of excellent open-source work. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the full list of packages, their authors, and their licenses.

---

## License

MIT — see [LICENSE](LICENSE).
