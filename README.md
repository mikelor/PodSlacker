# PodSlacker

> Turn any YouTube video into a podcast episode — automatically.

PodSlacker fetches a video's transcript, uses an LLM to write a structured summary and a podcast script, synthesises audio via text-to-speech, captures key-moment screenshots, and packages everything into a self-contained HTML page. Optionally publishes to GitHub Pages.

Built on **.NET 10 / C# 14** with no Python, no ffmpeg, and no yt-dlp required. Kokoro local TTS is the default — **no API key needed for audio**.

---

## Features

- **No extra runtimes.** Pure .NET 10 — install the SDK and you're done.
- **Local TTS by default.** [Kokoro](https://github.com/thewh1teagle/kokoro-onnx) runs on CPU, produces high-quality 24 kHz WAV, and requires no API key. Switch to OpenAI TTS with one flag.
- **Any OpenAI-compatible LLM.** Works with OpenRouter, Ollama, Groq, Azure OpenAI, Together AI — or vanilla OpenAI. Each pipeline step can use a different provider.
- **Three ways to run.** CLI locally, CLI pointing at a remote API service, or a full Blazor web UI — all sharing the same pipeline core.
- **Aspire orchestration.** Start the API service, web UI, and dashboard in one command with .NET Aspire. No ports to manage.
- **Docker support.** Multi-stage Dockerfiles for both services and a `docker-compose.yml` for one-command local container runs. Cross-platform: OpenCvSharp runtime packages are selected automatically per OS.
- **No yt-dlp dependency.** Transcripts, metadata, and video stream URLs are all resolved natively via [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode).
- **No ffmpeg dependency.** MP3 segments are concatenated as raw bytes; WAV files are written with a hand-written RIFF header.
- **Self-contained HTML output.** Audio and screenshots are base64-embedded — the page opens in any browser, offline, with no web server.
- **GitHub Pages publishing.** One flag uploads the page and enables Pages automatically.
- **Prompt-driven.** All LLM instructions live in plain-text files you can edit freely, with a three-tier fallback so the tool always works out of the box.

### Web UI features

- **Job history sidebar.** The home page shows all in-memory jobs in a live-polling sidebar — titles, status badges, progress, and direct links. Refreshes every 5 seconds automatically.
- **LLM model selector.** Choose from a curated list of free OpenRouter models (Llama 3.3 70B, Gemini 2.0 Flash, DeepSeek R1, Mistral, Qwen, Phi-4, and more) directly in the submit form.
- **Dialogue / Monologue toggle.** Switch between a two-host conversation and a solo narration before submitting.
- **Snapshot count slider.** Control how many key-moment screenshots are captured (0–12) from the submit form.
- **Smart download filenames.** Completed SlackCasts download as `video_title_<jobid>.html` rather than a bare UUID.
- **Live video title.** The video title appears on the job status page as soon as the pipeline fetches it — before any LLM steps begin.
- **Copy job URL.** One-click copy button on the job status page puts the full URL on your clipboard with a "✓ Copied!" flash.
- **PodSlacker CTA banner.** Every generated HTML page includes a branded "Try PodSlacker now" banner at the top, linking to `https://podslacker.com`.

---

## Solution Structure

```
PodSlacker/
├── docker-compose.yml            # Run API + Web as containers with one command
└── src/
    ├── PodSlacker.sln
    ├── .dockerignore             # Shared ignore rules for both Docker build contexts
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

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and an LLM API key (see [LLM Providers](#llm-providers) — OpenRouter has a free tier).

```bash
# 1. Clone and build
git clone https://github.com/your-username/PodSlacker.git
cd PodSlacker/src
dotnet build PodSlacker.sln -c Release

# 2. Set your LLM API key (OpenRouter used in podslacker.json by default)
export OPENROUTER_API_KEY=sk-or-...

# 3. Run it — Kokoro TTS is the default, so no audio API key is needed
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

Files written to `outputs/`:

| File | Contents |
|---|---|
| `*_transcript.txt` | Timestamped captions `[MM:SS] text` |
| `*_summary.md` | LLM summary + full podcast script |
| `*_podcast.wav` | Stitched audio (Kokoro) or `*_podcast.mp3` (OpenAI TTS) |
| `*_frame_01.jpg` … | Key-moment screenshots (6 by default) |
| `*_page.html` | Self-contained page with summary, gallery, and audio player |

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

Aspire starts the API service and the web UI together, assigns ports automatically, and opens a dashboard showing live logs, traces, and resource status.

```bash
dotnet run --project src/PodSlacker.AppHost
```

The Aspire dashboard opens at **http://localhost:18888**. The web UI URL is shown in the dashboard under `podslacker-web` — click it to open the browser interface where you can paste a YouTube URL and track the job in real time.

> **Note:** Do not set `PODSLACKER_SERVICE_URL` when using Aspire. The AppHost clears it automatically and wires the web UI to the API via service discovery.

### Option B — Manual startup

Start the API service first, then point the CLI or web UI at it.

```bash
# Terminal 1 — start the API
export OPENROUTER_API_KEY=sk-or-...
export PODSLACKER_API_KEY=my-secret   # optional — enables X-Api-Key auth
# export YOUTUBE_API_KEY=AIza...      # optional — only needed if the default key stops working
dotnet run --project src/PodSlacker.ApiService

# Terminal 2 — use the CLI in remote mode
export PODSLACKER_SERVICE_URL=http://localhost:5100   # use the HTTP port shown at startup
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
    "github_token_value": "ghp_..."
  }
}
```

All `config` fields are optional — omitted fields use the server's compiled defaults. The server always overrides `output_dir` (uses a temp directory) and ignores `publish_github`.

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

Once the Aspire host or Docker Compose stack is running, open the web UI (default `http://localhost:5200`) in any browser.

### Submit form

The home page is split into two columns: the submit form on the left and a live job history sidebar on the right.

**YouTube URL** — paste any YouTube link and press Enter or click **🎙️ Generate Podcast**.

**Format** — choose between *🎭 Dialogue* (Alex & Jordan, two voices) or *🎤 Monologue* (Alex solo). Maps to `hosts: 2` and `hosts: 1` in the config.

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
# 1. Create a .env file next to docker-compose.yml with your API keys
echo "OPENROUTER_API_KEY=sk-or-..."  >> .env
echo "PODSLACKER_API_KEY=my-secret" >> .env   # optional auth

# 2. Build and start both services
docker compose up --build

# Web UI → http://localhost:5200
# API    → http://localhost:5100
```

`docker compose down` stops the containers but preserves the `kokoro-model` volume so the 320 MB ONNX model doesn't need to re-download. Add `--volumes` to remove it too.

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
| `ASPNETCORE_ENVIRONMENT` | Both | Defaults to `Development`; set to `Production` for hardened deployments |

### OpenCV cross-platform support

`PodSlacker.Core.csproj` selects the correct OpenCvSharp4 native runtime package at build time via MSBuild conditions:

| Platform | Package |
|---|---|
| Windows | `OpenCvSharp4.runtime.win` |
| Linux (Ubuntu 22.04) | `OpenCvSharp4.runtime.ubuntu.22.04-x64` |
| macOS Apple Silicon | `OpenCvSharp4.runtime.osx-arm64` |
| macOS Intel | `OpenCvSharp4.runtime.osx-x64` |

The Linux image also installs the required system libraries (`libglib2.0-0`, `libgtk2.0-0`, `libavcodec-extra`, etc.) via `apt-get` in the Dockerfile's runtime stage.

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

### 2. Fix the Web port (recommended)

Aspire assigns a new port every run, which means you'd have to re-point the tunnel each time. Avoid that by pinning the Web UI to a fixed port in `AppHost/Program.cs`:

```csharp
builder
    .AddProject<Projects.PodSlacker_Web>("podslacker-web")
    .WithReference(apiService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("PODSLACKER_SERVICE_URL", "")
    .WithHttpEndpoint(port: 5200, name: "http")   // fixed port — tunnel target never changes
    .WithExternalHttpEndpoints();
```

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

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0 or later | The only system-level dependency |
| LLM API key | — | OpenRouter, OpenAI, or any compatible provider |
| OpenAI API key *(optional)* | — | Only needed when `--tts-engine openai` |
| [Kokoro ONNX model](https://github.com/taylorchu/kokoro-onnx) (~320 MB) | auto-downloaded | Downloaded once on first Kokoro TTS run |

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
  "host1_name": "MIKE",
  "host2_name": "JORDAN",
  "num_frames": 6,

  "_llm": "Use any OpenAI-compatible endpoint",
  "llm_base_url": "https://openrouter.ai/api/v1",
  "llm_model": "openrouter/auto:free",
  "llm_api_key_env": "OPENROUTER_API_KEY",

  "_tts": "kokoro = local, free. openai = cloud, requires API key.",
  "tts_engine": "kokoro",
  "voice_host1": "am_michael",
  "voice_host2": "af_heart",
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
  "github_branch": "gh-pages"
}
```

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
| `--host1-name NAME` | `MIKE` | Name for the first (or only) host |
| `--host2-name NAME` | `JORDAN` | Name for the second host |
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
| `--voice-host1 VOICE` | `am_michael` | Voice for host 1 (Kokoro or OpenAI name) |
| `--voice-host2 VOICE` | `af_heart` | Voice for host 2 (Kokoro or OpenAI name) |

Voices used in the default config:

| Engine | Host | Voice | Style |
|---|---|---|---|
| Kokoro | Host 1 | `am_michael` | American English male |
| Kokoro | Host 2 | `af_heart` | American English female |
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

**Kokoro model management.** The 320 MB `kokoro.onnx` model is downloaded once to `AppContext.BaseDirectory` via a streamed HTTP download with 10%-bucket progress logging, using an atomic `.download → rename` pattern so a cancelled download never leaves a corrupt file.

**Embedded prompts.** All four system prompts are embedded resources in `PodSlacker.Core.dll`, so the CLI, the web API, and any future clients share the same canonical prompts with no file-copying during deployment.

**NuGet-based Aspire (no workload).** `PodSlacker.AppHost` uses `Aspire.Hosting.AppHost` and the platform-specific DCP/Dashboard NuGet packages directly, avoiding the deprecated `IsAspireHost` workload property that triggers `NETSDK1228` on .NET 10.

---

## Python Version

The original Python implementation lives in [`python/`](python/). It has a `--tts-engine kokoro` flag and supports the same basic pipeline but requires Python 3.10+, `pip` packages, and optionally `yt-dlp`. The .NET version supersedes it but the Python code is preserved for reference.

---

## Acknowledgements

PodSlacker is built on top of excellent open-source work. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the full list of packages, their authors, and their licenses.

---

## License

MIT — see [LICENSE](LICENSE).
