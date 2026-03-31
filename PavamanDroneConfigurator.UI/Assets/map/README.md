# Google Map Integration (`PavamanDroneConfigurator`)

This document explains how Google Maps is integrated into the app.

## 1) Where the integration lives

### Frontend (HTML/JS map)
- `PavamanDroneConfigurator.UI/Assets/map/google-map.html`
  - Loads Google Maps JS API.
  - Initializes the map (`initMap`).
  - Exposes JS functions called from C# (`window.updateDrone`, `window.setViewMode`, `window.setWaypoints`, etc.).
  - Sends events back to C# using `window.chrome.webview.postMessage(...)`.

### Avalonia WebView host
- `PavamanDroneConfigurator.UI/Controls/CesiumMapView.axaml.cs`
  - Creates and hosts `WebView2`.
  - Locates and navigates to `google-map.html`.
  - Receives JS messages in `OnWebMessageReceived`.
  - Executes JS using `_webView.ExecuteScriptAsync(...)`.
  - Queues pending telemetry until map is ready.

### Page-level wiring
- `PavamanDroneConfigurator.UI/Views/LiveMapPage.axaml`
  - Places `<controls:CesiumMapView x:Name="CesiumMap" />` in the layout.
- `PavamanDroneConfigurator.UI/Views/LiveMapPage.axaml.cs`
  - Connects `LiveMapPageViewModel` events to map control methods.
  - Pushes telemetry updates to map via `UpdateDronePosition(...)`.

### ViewModel source of map data
- `PavamanDroneConfigurator.UI/ViewModels/LiveMapPageViewModel.cs`
  - Emits events like position updates, view mode changes, mission tool changes.

## 2) Asset deployment

Map files under `Assets/map` are copied to output by `PavamanDroneConfigurator.UI.csproj`:

- `<None Update="Assets\map\**">`
- `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`

So `google-map.html` is available at runtime from the app output folder.

## 3) Runtime flow (end-to-end)

1. `LiveMapPage` loads.
2. `CesiumMapView` initializes `WebView2`.
3. `CesiumMapView` navigates to `google-map.html`.
4. JS map initializes and posts `{ type: "mapReady" }`.
5. C# receives `mapReady`, marks `_mapReady = true`, and flushes pending telemetry update.
6. Telemetry/view events from `LiveMapPageViewModel` call map methods:
   - position ? `updateDrone(...)`
   - view mode ? `setViewMode(...)`
   - follow/recenter/zoom/mission tools/waypoints/geofence/spray overlay similarly.
7. JS user actions (waypoint/home/land/orbit/survey boundary) are posted back to C# and raised as typed events.

## 4) C# -> JS API contract

The main JS methods exposed in `google-map.html`:

- `updateDrone(lat, lng, alt, heading, pitch, roll, isArmed, groundSpeed, verticalSpeed)`
- `setViewMode(mode)`
- `toggleFollow()`
- `centerOnDrone(animate)`
- `setMapType(type)`
- `setMissionTool(tool)`
- `setWaypoints(waypoints)`
- `setCurrentWaypoint(index)`
- `deleteWaypoint(index)`
- `setGeofence(center, radius, maxAlt)`
- `renderSurveyGrid(gridWaypoints, sprayWidth)`
- `setSprayActive(active, width)`
- clear/reset helpers (`clearFlightPath`, `clearWaypoints`, etc.)

`CesiumMapView` calls these via `ExecuteScriptAsync`.

## 5) JS -> C# message contract

JS posts message JSON with `type` values such as:

- `mapReady`
- `viewModeChanged`
- `followChanged`
- `waypoint_placed`
- `waypoint_moved`
- `waypoint_deleted`
- `home_placed`
- `land_placed`
- `orbit_placed`
- `survey_boundary_completed`
- `error`

`CesiumMapView.OnWebMessageReceived` parses these and raises strongly-typed C# events.

## 6) Google API key location

The Google Maps script is loaded in:
- `PavamanDroneConfigurator.UI/Assets/map/google-map.html`

Current script tag format:

- `https://maps.googleapis.com/maps/api/js?key=...&callback=initMap`

## 7) Troubleshooting checklist

- Verify `WebView2 Runtime` is installed (checked in `CesiumMapView.InitializeWebView2Async`).
- Ensure `Assets/map/google-map.html` is copied to output.
- Check debug output from:
  - `[GoogleMapView] ...` (C# host logs)
  - `[GoogleMap] ...` (JS console logs)
- Confirm JS sends `mapReady`; until then telemetry updates are queued.
- If map fails to load, check network/API key restrictions and browser console errors.

## 8) Notes

- Class name `CesiumMapView` is legacy naming; implementation is Google Maps-based.
- There is fallback probing for `Assets/map/index.html` if `google-map.html` is not found.
