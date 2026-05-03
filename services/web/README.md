# web (Blazor WebAssembly)

Static SPA served by nginx in production. Map of Norwegian vessels via Leaflet
(OpenStreetMap base + OpenSeaMap nautical overlay), real-time updates over
SignalR, vessel detail + 24-hour track on click.

## Stack

- **Blazor WebAssembly** (.NET 9).
- **MudBlazor** for layout, app bar, side panel, snackbar.
- **Leaflet 1.9** via JS interop in `wwwroot/js/leaflet-interop.js`.
- **SignalR client** (`Microsoft.AspNetCore.SignalR.Client`) with automatic
  reconnect against `/hubs/vessels`.
- **nginx 1.27 alpine** runtime image with brotli/gzip compression and
  long-lived cache headers on `/_framework/`.

## Run locally

The web app talks to the core API at `PublicApiBaseUrl` (default
`http://localhost:8080`). With the full stack up:

```bash
make up
open http://localhost:5000
```

For frontend-only iteration without rebuilding the container:

```bash
cd services/web/FjordWatch.Web
dotnet watch run
```

## Build

```bash
cd services/web/FjordWatch.Web
dotnet build -c Release
```

The Docker build does the same `dotnet publish` and copies the resulting
`wwwroot/` into nginx.

## Layout

```
services/web/
├── FjordWatch.Web/
│   ├── Components/         (MapView, VesselSidePanel, LegendPanel, ConnectionStatus)
│   ├── Layout/             (MainLayout)
│   ├── Pages/              (Home, About)
│   ├── Models/             (DTOs mirroring core-api/Contracts/*)
│   ├── Services/           (ApiClient, VesselsHubClient)
│   ├── wwwroot/
│   │   ├── css/app.css
│   │   ├── js/leaflet-interop.js
│   │   └── index.html
│   └── Program.cs
├── nginx.conf
└── Dockerfile
```
