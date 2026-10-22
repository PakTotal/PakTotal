# PakTotal

VirusTotal but for `.pak` (non-IoStore) or `.ucas` + `.utoc` + `.pak` (IoStore) files.

Scans for known suspicious or malicious code; some modules are public (committed to this repo) and others are private (only hosted on the PakTotal server) due to being undisclosed to the public and are not fixed by Epic or its partners.

Limit of 100MB per scan (all uploaded files combined) (some arbitrary number I picked right now), 10 requests per minute per visitor.

## API

`GET /api/v1/info`
- Return version info about the PakTotal API, PakTotal.Sdk and CUE4Parse

`GET /api/v1/engine-versions`
- Fetch the list of engine versions from the CUE4Parse EGame enum

`POST /api/v1/scans`
- Returns 202 with an id

`GET /api/v1/scans/{id}`
- Returns the status or result of a request

## Build and host (with only public modules) locally

Local dev: `dotnet run --project PakTotal`

Update ThirdParty deps: `git submodule update --init`

Docker: `docker compose up --build`

## Deployment

Docker: `docker compose up -d --build`

Check logs: `docker compose logs -f`

## Looking up a scan hash

Looking up scan hash's result in the db: `docker compose cp paktotal:/app/data ./paktotal-data`
then `dotnet run tools/ScanLookup.cs -- <hash> --db ./paktotal-data/paktotal.db`
