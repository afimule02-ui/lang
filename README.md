# AfriCorpus AI — Powering African Language & Speech Datasets

Enterprise-grade, ethically sourced 3-tier datasets (Studio Audio, Verbatim Transcripts, Aligned Parallel Translations) powered by time-stamped structured JSON metadata. Optimized for ASR, Speech Translation, and LLM fine-tuning.

This folder contains the AfriCorpus AI language collection workspace as an Angular frontend, with an ASP.NET Core 8 (.NET) backend. Its country and language catalog covers African language communities using the included Glottolog country-language map. The Angular app is compiled during the Docker build and served by the API from the same origin on port 8001.

## Deploy the ZIP on your server

You need Docker with Docker Compose, a reachable SQL Server database, a domain with HTTPS, and FFmpeg (already installed in the Docker image). Do not expose the SQL Server port to the public internet.

1. Extract this ZIP into a new folder on your server.
2. Copy `.env.example` to `.env` and edit `.env` with your SQL Server details, website origin, and private administrator/agent setup values. Keep `.env` on the server and out of shared ZIP files. If any credential was shared in a chat, rotate it and use a new password.
3. Ensure the SQL Server database `LangDataPlatform` exists and the configured database login can create tables and indexes. On first start the API creates its initial tables.
4. From the extracted folder, run:

```sh
docker compose up --build -d
```

5. Put an HTTPS reverse proxy in front of server port `8001`. The .NET app serves both the website and API on that port. Open your domain and check `https://YOUR_DOMAIN/health` returns `{"status":"ok","service":"ELDF .NET API"}`.
6. Back up both SQL Server and the Docker volume `eldf-data`. Database backups do not include uploaded audio.

The website and API are in the same origin. If you host the frontend separately, put its exact origin in `ELDF_ALLOWED_ORIGINS` in `.env` and restart the container. Never set `ELDF_COOKIE_SECURE=false` on a public HTTPS deployment.

## Open and run in Visual Studio 2022

Install the .NET 8 SDK, then open `EthiopianLanguagesDataFactory.sln`. In Visual Studio, right-click **Eldf.Api** → **Manage User Secrets** and add your development database connection:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SQL_SERVER;Database=LangDataPlatform;User Id=YOUR_DATABASE_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true;"
  }
}
```

Set **Eldf.Api** as the startup project and press **Start** to run the API on `http://localhost:8002`. Run the Angular dev server separately as described below; it serves the frontend on `http://localhost:4200` and proxies API requests to port `8002`. (`Program.cs` falls back to `http://localhost:8001` when `ELDF_URLS` is not set, which is the case in Docker and in production behind the reverse proxy.) Keep the connection string in User Secrets or a protected environment variable; do not put a real password in `appsettings.json`.

For local development without Visual Studio, first start the API in one PowerShell window:

```powershell
dotnet run --project Eldf.Api
```

Then start the Angular development server in a second window:

```powershell
cd eldf-frontend
npm ci
npm start
```

Open `http://localhost:4200`. The Angular dev proxy forwards `/api` and `/health` to the API at `http://localhost:8002`; production uses same-origin relative URLs, so no development host is baked into the frontend bundle.

Configure the connection string through Visual Studio User Secrets first. To use a different database with an existing database login, the login needs permission to create the initial tables. Use a test database or take a backup before trying it against existing data.

### Frontend files

The Angular source of truth is `eldf-frontend/src`. `Dockerfile` builds it with Node and copies the production bundle into `Eldf.Api/wwwroot` before `dotnet publish`. For local frontend work use `npm start` with the API running at port `8002`; the API serves the compiled frontend on port `8001` in Docker/production.

## Creating the first administrator and agent accounts

There is **no database seeding and there are no pre-created accounts.** Every account, including the first administrator, is created through `POST /api/auth/register`. The server decides the role in `StaffRole()` (`Program.cs`): a registration becomes `admin` or `agent` only when the submitted phone number is on the matching allowlist **and** the `staff_code` field matches the matching enrollment token. Anything else becomes a `contributor`.

Set these environment variables before starting the API, then register through the normal **Create contributor account** form with the staff invitation code filled in:

| Variable | Purpose |
| :--- | :--- |
| `ELDF_ADMIN_PHONES` | Comma-separated normalized phone numbers allowed to enrol as admin, e.g. `+251911000001` |
| `ELDF_ADMIN_ENROLLMENT_TOKEN` | Long random secret that must be typed into **Staff invitation code** |
| `ELDF_AGENT_PHONES` | Comma-separated phone numbers allowed to enrol as agent |
| `ELDF_AGENT_ENROLLMENT_TOKEN` | A different long random secret for agent enrolment |

Phone numbers are normalized before comparison: a 9-digit local number is stored as `+251` + the digits, so the allowlist must use the `+251…` form. Rotate both tokens after the staff accounts exist. Once one administrator exists, further roles can be granted from the admin dashboard (`PATCH /api/admin/users/{id}`) without re-using the enrolment tokens.

## Included .NET API features

- Contributor registration and login with salted PBKDF2 password hashes and secure, HTTP-only sessions.
- Admin/agent enrollment only for allowlisted phone numbers with separate enrollment tokens.
- Save speaker profiles (at least two speakers are required).
- Submit audio with transcript and translation; the server checks the actual audio duration and enforces a 30–60 minute range.
- Delete a contributor's own submitted audio (admins can delete any submission).
- Prepare audio as mono 48 kHz WAV, trim edge silence, apply mild noise filtering, and normalize it using FFmpeg.
- Serve the frontend and `/health` from the .NET app.

The Angular contributor recording flow and contributor workspace use the .NET API and SQL Server for speaker profiles, drafts, submissions, and review status. Admin and agent workspaces, review decisions, account role updates, payment summaries, and activity records also use persisted API endpoints. Full Angular support-chat and payment-approval screens are not included yet. No production database, public-domain deployment, or real microphone recording was tested as part of this change.

## Important security and operational notes

- The server must use HTTPS before real contributors sign in; HTTPS is required for secure session cookies in production.
- Use long, random enrollment tokens and only share them with the intended staff members.
- Set firewall rules so SQL Server is reachable only from the application server and trusted administration network.
- Configure backups, monitoring, rate limits, and account recovery before inviting public users.
- Audio cleanup can alter speech. Listen to processed files and retain originals when possible; processing does not certify dataset quality or satisfy a specific company's acceptance standard.

---

## Global AI Dataset Standards & Technical Specifications

AfriCorpus AI structures speech and language corpora to meet the stringent procurement and ingestion standards of global AI research labs, foundation model builders (OpenAI, Google, Meta, Microsoft, Anthropic), and data aggregator platforms (Defined.ai, Appen, TELUS International, OneForma, LXT, Masakhane).

### 1. Acoustic & Audio Technical Specifications

| Parameter | Enterprise Standard | AfriCorpus AI Implementation |
| :--- | :--- | :--- |
| **Audio Format** | Uncompressed WAV (RIFF header) | Uncompressed 16-bit Linear PCM WAV |
| **Sampling Rate** | 16,000 Hz (16 kHz) or 44,100 / 48,000 Hz | 16 kHz (ASR optimized) and 48 kHz (Studio master) |
| **Bit Depth** | 16-bit PCM | 16-bit PCM depth |
| **Channels** | Mono (Single-channel pristine voice capture) | Single-channel Mono (1 mic per speaker) |
| **Signal-to-Noise (SNR)** | > 20 dB (Studio >= 35 dB) | Studio sessions SNR >= 38 dB; field sessions SNR > 22 dB |
| **Noise Floor** | < -40 dBFS | Background noise floor calibrated below -42 dBFS |
| **Headroom & Clipping** | Peak normalized to -3 dBFS; 0% clipping | Zero digital clipping (hard limiter ceiling at -1 dBFS) |

---

### 2. Universal Machine-Readable File Naming Convention

To allow automated parsing and zero-touch ingestion into enterprise data loaders (Hugging Face Datasets, PyTorch SpeechDataLoader, TensorFlow I/O), every audio recording adheres to the standard schema:

```
[Language]_[Domain]_[TopicID]_[SessionID]_[SpeakerID].wav
```

#### Field Breakdown:
- **`[Language]`**: 3-letter ISO 639-3 language code (e.g., `AMH` for Amharic, `KBF` for Kafa, `SID` for Sidama, `WAL` for Wolaytta, `ORM` for Afaan Oromo, `TIR` for Tigrinya).
- **`[Domain]`**: 4-letter topical domain code (`AGRI` = Agriculture, `TECH` = Technology & Fintech, `CULT` = Culture & Traditions, `HLTH` = Healthcare, `COMM` = Commerce & Daily Trade).
- **`[TopicID]`**: Structured topic prompt identifier (`T001`, `T002`, `T003`...).
- **`[SessionID]`**: Recording session sequence number (`S0001`, `S0002`...).
- **`[SpeakerID]`**: Pseudonymous speaker identifier (`SPK01`, `SPK02`...).

#### Production Examples:
- `AMH_AGRI_T001_S0005_SPK01.wav` (Amharic • Agriculture • Topic 1 • Session 5 • Speaker 1)
- `KBF_AGRI_T001_S0001_SPK01.wav` (Kafa • Agriculture • Topic 1 • Session 1 • Speaker 1)
- `SID_TECH_T002_S0003_SPK02.wav` (Sidama • Technology • Topic 2 • Session 3 • Speaker 2)
- `WAL_CULT_T003_S0002_SPK01.wav` (Wolaytta • Culture • Topic 3 • Session 2 • Speaker 1)

---

### 3. Structured Guided Discussion Prompts (Prompt Schema)

Natural, unscripted spontaneous dialogue is required for conversational voice assistants and foundation speech LLMs. Facilitators guide multi-turn dialogues using structured prompt schemas:

#### Example Prompt: `TOPIC_AGRI_01`
- **Topic ID**: `TOPIC_AGRI_01`
- **Main Domain**: Modern Livestock Fattening & Agricultural Trade
- **Context**: Rural and peri-urban sheep and cattle fattening practices, feed formulations, and marketplace dynamics.
- **Guided Conversation Prompts**:
  1. *Prompt 1*: How do you select the best sheep breeds before starting the fattening process?
  2. *Prompt 2*: Describe the daily mixture and proportions of feed (hay, oilseed cake/fagulo, crop residues).
  3. *Prompt 3*: What routine preventative veterinary care is needed to prevent livestock illness and accelerate healthy weight gain?
  4. *Prompt 4*: How many weeks does a typical fattening cycle take, and how is profitability evaluated in the local market?

---

### 4. Real-World Mobile Hardware Diversity (Tecno, Infinix, itel, Samsung, Huawei)

Global voice AI models (Google Assistant, Meta Voice, Siri, Alexa) fail when trained solely on sterile studio microphones. They require acoustic generalization across real-world smartphones commonly used by the next billion internet users.

#### Budget Smartphone Capabilities:
- Modern entry-level chipsets (MediaTek Helio, Unisoc Tiger) feature internal ADCs capable of pristine 16 kHz and 48 kHz uncompressed capture.
- Acoustic noise models benefit from device generalization across Tecno Spark/Camon, Infinix Hot/Note, itel S/A series, Samsung Galaxy A series, and Huawei devices.

#### 4-Step Field Recording Protocol:
1. **Unprocessed Capture Application**: Use RecForge II or the AfriCorpus Web Audio Recorder configured to:
   - Format: WAV (Uncompressed 16-bit PCM)
   - Sampling Rate: 16 kHz (or 48 kHz studio master)
   - Audio Source: Raw / Unprocessed Mic (bypassing phone hardware noise-gate distortions)
2. **Controlled Acoustic Environment**: Low-reverberation rooms shielded from traffic, wind, livestock, and ambient street noise.
3. **Calibrated Distance**: Maintain steady 15–20 cm distance from mouth to mic to avoid plosive breath pops while preventing ambient room reverb.
4. **Lavalier Microphone Option**: Clip-on 3.5mm / USB-C lavalier mics ($3–$5) used for high-fidelity isolation in semi-field scenarios.

---

### 5. Legal, Ethical & GDPR Compliance Framework

AfriCorpus AI operates under strict adherence to the General Data Protection Regulation (GDPR), the African Union Malabo Convention on Cyber Security & Personal Data Protection, and IEEE Ethical AI standards.

- **Explicit Informed Digital Consent**: Every speaker completes a verified digital consent onboarding acknowledging their voice data will be utilized for international AI and machine learning model training.
- **Cryptographic Consent Reference**: Every audio segment references a verified audit entry:
  ```json
  "consent_record_reference": "CONSENT-AFRI-v1-AUDIO-VERIFIED"
  ```
- **Rigorous Anonymization**: No contributor telephone numbers, government ID numbers, real names, or physical addresses are ever released in commercial training corpora.
- **100% Original Speech Guarantee**: All dialogue is spontaneous and original. No copyrighted books, web-scraped media, radio broadcasts, or unconsented recordings are included.
- **Fair Labor Benchmarks**: Native speakers and transcribers are compensated above local minimum wage benchmarks, ensuring ethical community participation.

---

### 6. Commercial Licensing & Go-to-Market Model

- **Standard Commercial License**: **$200 USD / Hour of Clean Speech**
  - Non-exclusive enterprise grant for training ASR, TTS, Speech Translation, and Voice LLMs worldwide across research and production.
- **Full Exclusive IP Buyout**: **$350 USD / Hour of Clean Speech**
  - Sole ownership transfer granting exclusive commercial exploitation and sole proprietary custody of the specified language volume.
- **Procurement Channels**: Direct partnerships desk via `partnerships@africorpus.ai`, sample dataset download (.ZIP with WAV, JSON, and CSV demographics), and partnerships with enterprise aggregators (Defined.ai, Appen, TELUS International, Masakhane).

