# Third-Party Notices

PodSlacker incorporates components from the following open-source projects. Each component is used under the terms of its respective license, reproduced below.

---

## .NET / C# Dependencies

### System.CommandLine
- **Version:** 2.0.7
- **Author:** .NET Foundation and contributors
- **License:** MIT
- **Source:** https://github.com/dotnet/command-line-api
- **Use:** Command-line argument parsing (options, subcommands, help generation)

```
The MIT License (MIT)
Copyright (c) .NET Foundation and contributors
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

### Microsoft.Extensions.AI / Microsoft.Extensions.AI.OpenAI
- **Version:** 10.5.1
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/dotnet/extensions
- **Use:** Provider-agnostic `IChatClient` abstraction for LLM calls (OpenAI, OpenRouter, Ollama, Azure OpenAI)

---

### Microsoft.Extensions.DependencyInjection / Hosting / Logging / Configuration / Options
- **Version:** 10.0.7
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/dotnet/runtime
- **Use:** Dependency injection container, generic host, logging infrastructure, JSON configuration binding

---

### OpenAI (.NET SDK)
- **Version:** 2.10.0
- **Author:** Microsoft and OpenAI contributors
- **License:** MIT
- **Source:** https://github.com/openai/openai-dotnet
- **Use:** `OpenAIClient`, `AudioClient`, `GeneratedSpeechVoice` — used for OpenAI TTS synthesis

---

### Markdig
- **Version:** 1.1.3
- **Author:** Alexandre Mutel (xoofx)
- **License:** BSD 2-Clause
- **Source:** https://github.com/xoofx/markdig
- **Use:** Converts LLM-generated Markdown summaries to HTML for embedding in the output page

```
Copyright (c) 2018-2019, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

### OpenCvSharp4 / OpenCvSharp4 platform runtime packages
- **Version:** 4.13.0.20260427 / 4.13.0.20260302
- **Author:** shimat
- **License:** Apache License 2.0 (binding); Apache License 2.0 (OpenCV itself)
- **Source:** https://github.com/shimat/opencvsharp
- **Use:** Seeks to timestamps in a remote video stream and captures JPEG frames for the HTML page gallery
- **Packages:** `OpenCvSharp4`, `OpenCvSharp4.runtime.win`, `OpenCvSharp4.runtime.ubuntu.22.04-x64`, `OpenCvSharp4.runtime.osx-arm64`, `OpenCvSharp4.runtime.osx-x64` (platform-specific native binaries selected at build time via MSBuild conditions)

```
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
```

---

### YoutubeExplode
- **Version:** 6.6.0
- **Author:** Oleksii Holub (Tyrrrz)
- **License:** GNU Lesser General Public License v3.0 (LGPL-3.0)
- **Source:** https://github.com/Tyrrrz/YoutubeExplode
- **Use:** Fetches video titles, closed captions with timestamps, and direct video stream URLs — replacing the previous yt-dlp subprocess dependency

> **LGPL-3.0 notice:** YoutubeExplode is used as an unmodified library linked dynamically at runtime. No modifications have been made to the YoutubeExplode source code. The full LGPL-3.0 license text is available at https://www.gnu.org/licenses/lgpl-3.0.html.

---

### KokoroSharp / KokoroSharp.CPU
- **Version:** 0.6.7
- **Author:** thewh1teagle
- **License:** MIT
- **Source:** https://github.com/thewh1teagle/kokoro-onnx
- **Use:** Local CPU-based text-to-speech synthesis using the Kokoro 82M ONNX model; provides `KokoroWavSynthesizer` and `KokoroVoiceManager`

---

### Kokoro ONNX Model Weights
- **Version:** v0.2.0
- **Author:** hexgrad
- **License:** Apache License 2.0
- **Source:** https://huggingface.co/hexgrad/Kokoro-82M
- **Model file:** `kokoro.onnx` (~320 MB, downloaded automatically on first run from https://github.com/taylorchu/kokoro-onnx)
- **Use:** Neural network weights for the Kokoro TTS engine

---

### Octokit
- **Version:** 14.0.0
- **Author:** GitHub and contributors
- **License:** MIT
- **Source:** https://github.com/octokit/octokit.net
- **Use:** Creates and updates GitHub repositories and files via the GitHub REST API for the optional GitHub Pages publishing feature

---

### Microsoft.ML.OnnxRuntime
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/microsoft/onnxruntime
- **Use:** Transitive dependency of KokoroSharp; provides the ONNX inference runtime used to run the Kokoro model

---

### Microsoft.AspNetCore.OpenApi
- **Version:** 10.0.7
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/dotnet/aspnetcore
- **Use:** Maps the OpenAPI (`/openapi/v1.json`) endpoint in `PodSlacker.ApiService` for development-time API exploration

---

### Microsoft.Extensions.Http.Resilience
- **Version:** 10.5.0
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/dotnet/extensions
- **Use:** Adds standard HTTP retry, timeout, and circuit-breaker policies to all `HttpClient` instances registered in `PodSlacker.ServiceDefaults`

---

### Microsoft.Extensions.ServiceDiscovery
- **Version:** 10.5.0
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/dotnet/extensions
- **Use:** Resolves logical service names (e.g. `http://podslacker-api`) to actual endpoints using environment variables injected by .NET Aspire's orchestrator

---

### OpenTelemetry for .NET
- **Versions:** 1.15.x (see individual packages below)
- **Author:** OpenTelemetry Authors
- **License:** Apache License 2.0
- **Source:** https://github.com/open-telemetry/opentelemetry-dotnet
- **Packages:**
  - `OpenTelemetry.Extensions.Hosting` 1.15.3 — integrates OTel with the .NET generic host lifetime
  - `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3 — exports traces, metrics, and logs via OTLP to the Aspire Dashboard
  - `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2 — automatic tracing for ASP.NET Core request handling
  - `OpenTelemetry.Instrumentation.Http` 1.15.1 — automatic tracing for outbound `HttpClient` calls
  - `OpenTelemetry.Instrumentation.Runtime` 1.15.1 — .NET runtime metrics (GC, thread pool, etc.)
- **Use:** Provides distributed tracing, structured logging, and metrics across `PodSlacker.ApiService` and `PodSlacker.Web`, surfaced in the Aspire Dashboard

```
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
```

---

### .NET Aspire Hosting packages
- **Version:** 13.2.4
- **Author:** Microsoft
- **License:** MIT
- **Source:** https://github.com/dotnet/aspire
- **Packages:**
  - `Aspire.Hosting.AppHost` — core orchestration APIs (`DistributedApplication`, `AddProject`, `WithReference`)
  - `Aspire.Hosting.Orchestration.{win-x64,osx-arm64,osx-x64,linux-x64}` — DCP (Developer Control Plane) binary that launches and supervises the orchestrated processes; platform-specific package selected at build time
  - `Aspire.Dashboard.Sdk.{win-x64,osx-arm64,osx-x64,linux-x64}` — self-contained Aspire Dashboard web app that displays traces, logs, and resource status; platform-specific package selected at build time
- **Use:** `PodSlacker.AppHost` uses these packages to start `PodSlacker.ApiService` and `PodSlacker.Web` together in development, wire service discovery between them, and open the Aspire Dashboard

---

## Python Dependencies (python/ directory)

The original Python implementation in the `python/` subdirectory uses the following packages, each installable via `pip`:

| Package | License | Purpose |
|---|---|---|
| `youtube-transcript-api` | MIT | Fetches YouTube captions |
| `openai` | MIT | LLM chat completions + TTS synthesis |
| `requests` | Apache 2.0 | HTTP sessions with cookie support |
| `yt-dlp` | Unlicense | Resolves video stream URLs |
| `opencv-python-headless` | MIT / Apache 2.0 | Frame capture from video streams |
| `markdown` | BSD | Markdown-to-HTML rendering |
| `kokoro` | Apache 2.0 | Local Kokoro TTS (Python binding) |
| `torch` | BSD | PyTorch — required by kokoro |
| `soundfile` / `scipy` | BSD / BSD | WAV file writing |

---

## Model Weights and Data

### Kokoro Voice Pack
- Bundled inside the `KokoroSharp.CPU` NuGet package
- **License:** Apache License 2.0
- **Source:** https://huggingface.co/hexgrad/Kokoro-82M

---

*This file was last updated: May 2026 (added OpenTelemetry, Microsoft.Extensions.Http.Resilience, Microsoft.Extensions.ServiceDiscovery, Microsoft.AspNetCore.OpenApi, .NET Aspire Hosting packages, and cross-platform OpenCvSharp4 runtime packages). If you believe any attribution is missing or incorrect, please open an issue.*
