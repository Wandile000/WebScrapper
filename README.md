# WebScrapper

A C# (.NET 8) console tool that scans candidate company websites, scores them
against "poorly built site" heuristics, and writes the URLs of likely leads to
a text file so you can cold call / email them with a redesign pitch.

All scan history is persisted to a local SQLite database so you don't re-hit
the same hosts and can filter / query results later.

## What it checks

The scorer flags signals that typically correlate with an outdated site:

- No HTTPS
- Missing or legacy doctype (HTML 4 / XHTML transitional)
- No mobile viewport meta tag
- Obsolete tags: `<font>`, `<center>`, `<marquee>`, `<blink>`, `<frameset>`, `<applet>`, ...
- Flash `<object>` / `<embed>` still embedded
- Nested `<table>` layouts
- Heavy use of presentational attributes (`bgcolor`, `align`, ...)
- Excessive inline `style=""` attributes
- No CSS at all / no external stylesheet
- Missing meta description / Open Graph tags / favicon
- Images missing `alt` text
- Stale footer copyright year (3+ years old)
- Slow response, tiny HTML, no contact link

Each signal contributes points. A configurable threshold (`--threshold`,
default `30`) decides what ends up in the leads file.

## Project layout

```
WebScrapper.sln
src/WebScrapper/
  WebScrapper.csproj
  Program.cs            - CLI entry, orchestration
  CliOptions.cs         - argument parsing
  SiteFetcher.cs        - HttpClient wrapper (2 MB cap, decompression, TLS)
  PoorSiteScorer.cs     - heuristics
  ScraperDb.cs          - SQLite persistence (scans + failures)
  DdgDiscovery.cs       - optional DuckDuckGo HTML search discovery
seeds.example.txt       - sample seed file
```

## Build

```
dotnet build -c Release
```

## Run

Scan a list of candidate URLs from a file:

```
dotnet run --project src/WebScrapper -- --seeds seeds.example.txt
```

Discover candidates via DuckDuckGo and scan them:

```
dotnet run --project src/WebScrapper -- \
    --search "plumber cape town" \
    --search "electrician johannesburg" \
    --search-max 30 \
    --threshold 35
```

Combine both, raise concurrency, use a custom output file:

```
dotnet run --project src/WebScrapper -- \
    --seeds prospects.txt \
    --search "accountant durban" \
    --concurrency 16 \
    --output leads/today.txt
```

### All options

| Flag | Default | Meaning |
|---|---|---|
| `--seeds <file>` | – | URL list file (one per line, `#` comments). Repeatable. |
| `--search "<query>"` | – | DDG HTML search query. Repeatable. |
| `--search-max <n>` | `25` | Max results per query. |
| `--threshold <n>` | `30` | Minimum score to qualify as a lead. |
| `--concurrency <n>` | `8` | Parallel HTTP workers. |
| `--timeout <sec>` | `15` | Per-request timeout. |
| `--output <file>` | `output/leads.txt` | Where to write the lead list. |
| `--db <file>` | `data/scraper.db` | SQLite database path. |
| `-h`, `--help` | – | Help. |

## Output

The leads file is tab-separated and sorted by score (highest first):

```
# Poorly-built site candidates. Generated 2026-04-23T09:42:00Z.
# Threshold: 30. 12 hit(s) of 87 scanned.
# Format: <score>	<url>	<reasons>
72	http://some-old-plumber.co.za/	+20 no viewport meta; +15 no doctype; ...
48	https://acme-services.example/	+10 no https; +12 no css at all; ...
```

The SQLite DB has two tables:

- `scans` — every successful scan (url, host, score, reasons, elapsed, status, scanned_at)
- `failures` — fetch errors (timeouts, non-HTML responses, DNS, etc.)

Query it however you like:

```
sqlite3 data/scraper.db "SELECT score, url FROM scans WHERE score >= 40 ORDER BY score DESC LIMIT 20;"
```

## Notes & etiquette

- Only the homepage of each candidate is fetched; no deep crawling.
- A descriptive User-Agent is sent.
- Response bodies are capped at 2 MB; non-HTML responses are skipped.
- You are responsible for respecting robots.txt, site terms, and local laws
  (e.g. POPIA / GDPR) when you use the output list for outreach.
