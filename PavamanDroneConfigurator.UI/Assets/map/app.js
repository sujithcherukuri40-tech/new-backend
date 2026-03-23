/**
 * Pavaman Drone Map - CesiumJS Application
 * Production-ready real-time drone telemetry visualization
 * Uses OpenStreetMap (NO Cesium Ion required)
 * Features: 3D terrain, multiple view modes, waypoints, flight path tracking
 */

// ============================================
// ERROR HANDLING
// ============================================
window.addEventListener('error', function(e) {
    console.error('[CesiumMap] Global error:', e.error);
    notifyHost({ type: 'error', message: e.error?.message || 'Unknown error' });
});

window.addEventListener('unhandledrejection', function(e) {
    console.error('[CesiumMap] Unhandled promise rejection:', e.reason);
    notifyHost({ type: 'error', message: e.reason?.message || 'Promise rejection' });
});

// ============================================
// CONFIGURATION
// ============================================
const CONFIG = {
    defaultCenter: { lat: 20.5937, lon: 78.9629 }, // India
    maxFlightPathPoints: 10000,
    updateThrottleMs: 50,
    cameraFollowOffset: { x: 0, y: -50, z: 30 },
    topDownHeight: 200,
    droneModelScale: 2.0,
    defaultWaypointAltitude: 10,
    sprayWidthMeters: 4.0
};

// View modes
const ViewMode = {
    TOP_DOWN: 'topdown',
    CHASE_3D: 'chase3d',
    FREE_ROAM: 'free',
    FIRST_PERSON: 'fpv'
};

const MapMessageType = {
    MAP_READY: 'mapReady',
    ERROR: 'error',
    VIEW_MODE_CHANGED: 'viewModeChanged',
    FOLLOW_CHANGED: 'followChanged',
    WAYPOINT_PLACED: 'waypoint_placed',
    WAYPOINT_MOVED: 'waypoint_moved',
    WAYPOINT_DELETED: 'waypoint_deleted',
    HOME_PLACED: 'home_placed',
    LAND_PLACED: 'land_placed',
    ORBIT_PLACED: 'orbit_placed',
    SURVEY_BOUNDARY_COMPLETED: 'survey_boundary_completed'
};

// ============================================
// GLOBAL STATE
// ============================================
let viewer = null;
let droneEntity = null;
let droneModel = null;
let flightPathEntity = null;
let homeEntity = null;
let waypointEntities = [];
let missionPathEntity = null;
let flightPathPositions = [];
let isInitialized = false;
let currentViewMode = ViewMode.TOP_DOWN;
let isFollowing = true;
let lastDronePosition = null;
let lastDroneHeading = 0;
let lastDroneAltitude = 0;
let lastDronePitch = 0;
let lastDroneRoll = 0;
let homePosition = null;
let missionClickHandler = null;
let activePlanningTool = 'none';
let missionWaypoints = [];

// Survey polygon state
let surveyBoundaryPoints = [];
let surveyBoundaryEntities = [];
let surveyGridEntities = [];

// Spray overlay state
let sprayOverlayEntities = [];
let isSprayActive = false;
let lastSprayPosition = null;
let sprayWidthMeters = CONFIG.sprayWidthMeters;

// Waypoint drag state
let dragHandler = null;
let draggedWaypointIndex = null;

// Throttling
let lastUpdateTime = 0;

let droneHeadingLineEntity = null;

// ============================================
// ICON GENERATORS
// ============================================
function createHomeIconSvg() {
    const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" width="48" height="48">
        <defs>
            <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="0" dy="2" stdDeviation="2" flood-color="#000" flood-opacity="0.4"/>
            </filter>
        </defs>
        <polygon points="24,4 4,24 12,24 12,42 36,42 36,24 44,24" fill="#22C55E" stroke="#16A34A" stroke-width="2" filter="url(#shadow)"/>
        <rect x="18" y="28" width="12" height="14" fill="#16A34A"/>
        <rect x="20" y="30" width="8" height="10" fill="#86EFAC"/>
    </svg>`;
    return `data:image/svg+xml;base64,${btoa(svg)}`;
}

function createWaypointIcon(number, isCurrent) {
    const bgColor = isCurrent ? '#22C55E' : '#3B82F6';
    const borderColor = isCurrent ? '#16A34A' : '#2563EB';
    
    const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 36 50" width="36" height="50">
        <defs>
            <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="0" dy="2" stdDeviation="2" flood-color="#000" flood-opacity="0.5"/>
            </filter>
        </defs>
        <path d="M18 0 C8 0 0 8 0 18 C0 32 18 50 18 50 C18 50 36 32 36 18 C36 8 28 0 18 0 Z" 
              fill="${bgColor}" stroke="${borderColor}" stroke-width="2" filter="url(#shadow)"/>
        <circle cx="18" cy="18" r="12" fill="white"/>
        <text x="18" y="23" text-anchor="middle" font-family="Inter, Arial, sans-serif" 
              font-size="14" font-weight="bold" fill="${bgColor}">${number}</text>
    </svg>`;
    return `data:image/svg+xml;base64,${btoa(svg)}`;
}

function createLandIcon() {
    const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" width="48" height="48">
        <circle cx="24" cy="24" r="20" fill="#F59E0B" stroke="#D97706" stroke-width="2"/>
        <path d="M24 12 L24 28 M18 22 L24 28 L30 22" stroke="white" stroke-width="3" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
        <line x1="14" y1="34" x2="34" y2="34" stroke="white" stroke-width="3" stroke-linecap="round"/>
    </svg>`;
    return `data:image/svg+xml;base64,${btoa(svg)}`;
}

function createOrbitIcon() {
    const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" width="48" height="48">
        <circle cx="24" cy="24" r="18" fill="none" stroke="#8B5CF6" stroke-width="3" stroke-dasharray="8,4"/>
        <circle cx="24" cy="24" r="6" fill="#8B5CF6"/>
        <path d="M24 6 L28 12 L20 12 Z" fill="#8B5CF6"/>
    </svg>`;
    return `data:image/svg+xml;base64,${btoa(svg)}`;
}

// ============================================
// DRONE 3D MODEL - Quadcopter shape using primitives
// ============================================
function createDroneModelUri(isArmed) {
    const baseColor = isArmed ? '#EF4444' : '#3B82F6';
    const outlineColor = isArmed ? '#FCA5A5' : '#93C5FD';

    // Use KFT logo image from app assets (fallback to simple SVG badge)
    const logoPath = '../Images/KFT%20Logo.png';

    const fallbackSvg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
        <circle cx="50" cy="50" r="34" fill="white" stroke="${baseColor}" stroke-width="6"/>
        <text x="50" y="57" text-anchor="middle" font-family="Arial, sans-serif" font-size="22" font-weight="700" fill="${baseColor}">KFT</text>
        <polygon points="50,6 43,22 57,22" fill="${outlineColor}"/>
    </svg>`;

    return window._kftLogoFailed ? `data:image/svg+xml;base64,${btoa(fallbackSvg)}` : logoPath;
}

function createSatelliteImageryProvider() {
    return new Cesium.UrlTemplateImageryProvider({
        url: 'https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
        credit: 'Esri World Imagery',
        maximumLevel: 18 // ✅ High resolution satellite imagery
    });
}

function createRoadImageryProvider() {
    return new Cesium.OpenStreetMapImageryProvider({
        url: 'https://tile.openstreetmap.org/',
        credit: 'OpenStreetMap Contributors',
        maximumLevel: 19
    });
}

function createHybridLabelProvider() {
    return new Cesium.UrlTemplateImageryProvider({
        url: 'https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}',
        credit: 'Esri Labels',
        maximumLevel: 18
    });
}

// ✅ NEW: Add Blue Marble base layer for when satellite tiles don't load
function createBlueMarbleProvider() {
    return new Cesium.TileMapServiceImageryProvider({
        url: Cesium.buildModuleUrl('Assets/Textures/NaturalEarthII'),
        credit: 'Natural Earth II',
        maximumLevel: 5
    });
}

function applyMapType(type) {
    if (!viewer) return;

    const imageryLayers = viewer.imageryLayers;
    imageryLayers.removeAll();

    try {
        switch (type) {
            case 'satellite':
                imageryLayers.addImageryProvider(createSatelliteImageryProvider());
                break;

            case 'hybrid':
                imageryLayers.addImageryProvider(createSatelliteImageryProvider());
                imageryLayers.addImageryProvider(createHybridLabelProvider());
                break;

            case 'terrain':
            case 'roadmap':
            default:
                imageryLayers.addImageryProvider(createRoadImageryProvider());
                break;
        }
    } catch (err) {
        console.warn('[CesiumMap] Failed to apply requested map type, falling back to OSM:', err);
        imageryLayers.removeAll();
        imageryLayers.addImageryProvider(createRoadImageryProvider());
    }

    viewer.scene.requestRender();
}

function updateDroneHeadingLine(lat, lon, alt, heading) {
    if (!viewer || !droneEntity) return;

    const headingRad = Cesium.Math.toRadians(heading || 0);
    const noseLengthMeters = 20;
    const latRad = Cesium.Math.toRadians(lat);

    const deltaLat = (noseLengthMeters * Math.cos(headingRad)) / 111320;
    const deltaLon = (noseLengthMeters * Math.sin(headingRad)) / (111320 * Math.max(Math.cos(latRad), 0.0001));

    const start = Cesium.Cartesian3.fromDegrees(lon, lat, alt);
    const end = Cesium.Cartesian3.fromDegrees(lon + deltaLon, lat + deltaLat, alt);

    if (!droneHeadingLineEntity) {
        droneHeadingLineEntity = viewer.entities.add({
            id: 'droneHeadingLine',
            polyline: {
                positions: [start, end],
                width: 3,
                material: Cesium.Color.RED.withAlpha(0.95),
                clampToGround: false
            }
        });
    } else {
        droneHeadingLineEntity.polyline.positions = [start, end];
    }
}

// ============================================
// INITIALIZATION - Using OpenStreetMap (NO Ion)
// ============================================
async function initializeCesium() {
    try {
        console.log('[CesiumMap] ========================================');
        console.log('[CesiumMap] Starting Cesium initialization...');
        console.log('[CesiumMap] Cesium version:', Cesium.VERSION);
        console.log('[CesiumMap] ========================================');
        
        // Create viewer WITHOUT Cesium Ion - use OpenStreetMap
        viewer = new Cesium.Viewer('cesiumContainer', {
            // NO terrain - use simple ellipsoid for better performance
            terrainProvider: new Cesium.EllipsoidTerrainProvider(),
            
            // Start with reliable satellite tiles - ✅ CHANGED: Added error handling
            imageryProvider: false, // ✅ We'll add imagery providers manually after viewer creation
            
            // Disable base layer picker since we're using OSM
            baseLayerPicker: false,
            
            // Disable unnecessary UI elements
            animation: false,
            timeline: false,
            fullscreenButton: false,
            vrButton: false,
            geocoder: false,
            homeButton: false,
            infoBox: false,
            sceneModePicker: false,
            selectionIndicator: false,
            navigationHelpButton: false,
            navigationInstructionsInitiallyVisible: false,
            creditContainer: document.createElement('div'),
            
            // Performance settings
            requestRenderMode: false,
            maximumRenderTimeChange: Infinity,
            targetFrameRate: 60,
            
            // 3D-only mode
            scene3DOnly: true,
            
            // Disable shadows for performance
            shadows: false
        });

        console.log('[CesiumMap] Viewer created successfully');

        // ✅ NEW: Add imagery with error handling
        try {
            const satelliteProvider = createSatelliteImageryProvider();
            viewer.imageryLayers.addImageryProvider(satelliteProvider);
            console.log('[CesiumMap] Satellite imagery added');
        } catch (err) {
            console.warn('[CesiumMap] Failed to load satellite imagery, trying OpenStreetMap:', err);
            try {
                const osmProvider = createRoadImageryProvider();
                viewer.imageryLayers.addImageryProvider(osmProvider);
                console.log('[CesiumMap] OpenStreetMap imagery added as fallback');
            } catch (err2) {
                console.error('[CesiumMap] All imagery providers failed:', err2);
                // Viewer will show with default globe color
            }
        }

        // Configure scene
        const scene = viewer.scene;
        scene.globe.enableLighting = false;
        scene.globe.depthTestAgainstTerrain = false;
        scene.globe.showGroundAtmosphere = true; // ✅ CHANGED: Enable atmosphere for better visibility
        scene.skyAtmosphere.show = true; // ✅ CHANGED: Show sky atmosphere
        scene.backgroundColor = Cesium.Color.fromCssColorString('#000814'); // ✅ CHANGED: Darker space background
        scene.globe.baseColor = Cesium.Color.fromCssColorString('#2C3E50'); // ✅ CHANGED: Better ocean color
        if (scene.sun) {
            scene.sun.show = false;
        }
        if (scene.moon) {
            scene.moon.show = false;
        }
        if (scene.skyBox) {
            scene.skyBox.show = true; // ✅ CHANGED: Show skybox for better space appearance
        }
        scene.fog.enabled = false;
        
        // Disable HDR for better performance
        scene.highDynamicRange = false;
        
        // Enable FXAA anti-aliasing
        if (scene.postProcessStages && scene.postProcessStages.fxaa) {
            scene.postProcessStages.fxaa.enabled = true;
        }

        console.log('[CesiumMap] Scene configured');

        // Set initial camera position (India) - ✅ CHANGED: Lower altitude for better view
        viewer.camera.setView({
            destination: Cesium.Cartesian3.fromDegrees(
                CONFIG.defaultCenter.lon, 
                CONFIG.defaultCenter.lat, 
                1500000 // ✅ CHANGED: 1500km instead of 3000km - better initial view
            ),
            orientation: {
                heading: 0,
                pitch: Cesium.Math.toRadians(-45), // ✅ CHANGED: Angled view instead of straight down
                roll: 0
            }
        });

        console.log('[CesiumMap] Camera positioned');

        // Ensure initial map type is satellite
        applyMapType('satellite');

        // Add keyboard controls
        setupKeyboardControls();
        setupMissionPlannerInteractions();
        setupWaypointDragHandler();

        // Hide loading overlay
        setTimeout(() => {
            const loadingOverlay = document.getElementById('loading-overlay');
            if (loadingOverlay) {
                loadingOverlay.classList.add('hidden');
                console.log('[CesiumMap] Loading overlay hidden');
            }
        }, 1000);

        isInitialized = true;
        console.log('[CesiumMap] ========================================');
        console.log('[CesiumMap] Initialization complete - Ready for telemetry');
        console.log('[CesiumMap] ========================================');
        
        // Notify C# that map is ready
        notifyHost({ type: MapMessageType.MAP_READY });

    } catch (error) {
        console.error('[CesiumMap] Initialization failed:', error);
        console.error('[CesiumMap] Error stack:', error.stack);
        showLoadingError(error.message);
        notifyHost({ type: MapMessageType.ERROR, message: `Map init failed: ${error.message}` });
    }
}

function showLoadingError(message) {
    const overlay = document.getElementById('loading-overlay');
    if (overlay) {
        overlay.innerHTML = `
            <div style="color: #EF4444; text-align: center; padding: 20px;">
                <div style="font-size: 48px; margin-bottom: 16px;">⚠️</div>
                <div style="font-size: 18px; font-weight: 600;">Map Loading Failed</div>
                <div style="font-size: 14px; color: #94A3B8; margin-top: 8px;">${message}</div>
                <div style="font-size: 12px; color: #64748B; margin-top: 16px;">Check browser console (F12) for details</div>
                <button onclick="location.reload()" style="margin-top: 20px; padding: 10px 24px; background: #3B82F6; color: white; border: none; border-radius: 8px; cursor: pointer; font-size: 14px;">
                    Retry
                </button>
            </div>
        `;
    }
}

function notifyHost(message) {
    try {
        if (!message || typeof message !== 'object' || !message.type) {
            return;
        }

        console.log('[CesiumMap] Notifying host:', JSON.stringify(message));
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        } else {
            console.warn('[CesiumMap] WebView2 postMessage not available');
        }
    } catch (e) {
        console.error('[CesiumMap] Host notification error:', e.message);
    }
}

// ============================================
// MISSION PLANNING INTERACTIONS
// ============================================
function setupMissionPlannerInteractions() {
    if (!viewer || missionClickHandler) return;

    missionClickHandler = new Cesium.ScreenSpaceEventHandler(viewer.scene.canvas);

    missionClickHandler.setInputAction((click) => {
        if (activePlanningTool === 'none') return;

        const ray = viewer.camera.getPickRay(click.position);
        if (!ray) return;

        const cartesian = viewer.scene.globe.pick(ray, viewer.scene);
        if (!cartesian) return;

        const cartographic = Cesium.Cartographic.fromCartesian(cartesian);
        const lat = Cesium.Math.toDegrees(cartographic.latitude);
        const lon = Cesium.Math.toDegrees(cartographic.longitude);

        if (!isValidCoordinate(lat, lon)) return;

        switch (activePlanningTool) {
            case 'waypoint': {
                const index = missionWaypoints.length;
                const altitude = Math.max(lastDroneAltitude || CONFIG.defaultWaypointAltitude, 5);
                missionWaypoints.push({ lat, lon, alt: altitude, isCurrent: false });
                setWaypoints(missionWaypoints);
                notifyHost({ type: MapMessageType.WAYPOINT_PLACED, lat, lon, alt: altitude, index });
                console.log(`[CesiumMap] Waypoint placed: ${lat}, ${lon}, ${altitude}`);
                break;
            }
            case 'home':
                setHomePosition(lat, lon);
                notifyHost({ type: MapMessageType.HOME_PLACED, lat, lon });
                console.log(`[CesiumMap] Home position set: ${lat}, ${lon}`);
                break;
            case 'land':
                notifyHost({ type: MapMessageType.LAND_PLACED, lat, lon });
                console.log(`[CesiumMap] Land position set: ${lat}, ${lon}`);
                break;
            case 'orbit':
                notifyHost({ type: MapMessageType.ORBIT_PLACED, lat, lon, radius: 20 });
                console.log(`[CesiumMap] Orbit position set: ${lat}, ${lon}`);
                break;
            case 'survey':
                addSurveyBoundaryPoint(lat, lon);
                console.log(`[CesiumMap] Survey boundary point added: ${lat}, ${lon}`);
                break;
        }
    }, Cesium.ScreenSpaceEventType.LEFT_CLICK);

    // Double-click to complete survey polygon
    missionClickHandler.setInputAction(() => {
        if (activePlanningTool === 'survey' && surveyBoundaryPoints.length >= 3) {
            completeSurveyBoundary();
        }
    }, Cesium.ScreenSpaceEventType.LEFT_DOUBLE_CLICK);

    // Right-click to cancel current tool
    missionClickHandler.setInputAction(() => {
        if (activePlanningTool === 'survey') {
            clearSurveyBoundary();
        }
        setMissionTool('none');
    }, Cesium.ScreenSpaceEventType.RIGHT_CLICK);
}

function setupWaypointDragHandler() {
    if (!viewer || dragHandler) return;

    dragHandler = new Cesium.ScreenSpaceEventHandler(viewer.scene.canvas);

    dragHandler.setInputAction((click) => {
        const picked = viewer.scene.pick(click.position);
        if (Cesium.defined(picked) && picked.id && typeof picked.id.id === 'string' && picked.id.id.startsWith('waypoint_')) {
            const idParts = picked.id.id.split('_');
            if (idParts.length === 2 && !idParts[1].includes('line')) {
                draggedWaypointIndex = parseInt(idParts[1]);
                viewer.scene.screenSpaceCameraController.enableRotate = false;
                viewer.scene.screenSpaceCameraController.enableTranslate = false;
            }
        }
    }, Cesium.ScreenSpaceEventType.LEFT_DOWN);

    dragHandler.setInputAction((movement) => {
        if (draggedWaypointIndex !== null && draggedWaypointIndex < missionWaypoints.length) {
            const cartesian = viewer.camera.pickEllipsoid(movement.endPosition, viewer.scene.globe.ellipsoid);
            if (cartesian) {
                const cartographic = Cesium.Cartographic.fromCartesian(cartesian);
                const lat = Cesium.Math.toDegrees(cartographic.latitude);
                const lon = Cesium.Math.toDegrees(cartographic.longitude);
                
                if (isValidCoordinate(lat, lon)) {
                    missionWaypoints[draggedWaypointIndex].lat = lat;
                    missionWaypoints[draggedWaypointIndex].lon = lon;
                    setWaypoints(missionWaypoints);
                }
            }
        }
    }, Cesium.ScreenSpaceEventType.MOUSE_MOVE);

    dragHandler.setInputAction(() => {
        if (draggedWaypointIndex !== null) {
            const wp = missionWaypoints[draggedWaypointIndex];
            if (wp) {
                notifyHost({
                    type: MapMessageType.WAYPOINT_MOVED,
                    index: draggedWaypointIndex,
                    lat: wp.lat,
                    lon: wp.lon
                });
                console.log(`[CesiumMap] Waypoint moved: ${wp.lat}, ${wp.lon}`);
            }
            draggedWaypointIndex = null;
            viewer.scene.screenSpaceCameraController.enableRotate = true;
            viewer.scene.screenSpaceCameraController.enableTranslate = true;
        }
    }, Cesium.ScreenSpaceEventType.LEFT_UP);
}

function setMissionTool(tool) {
    const allowed = new Set(['none', 'waypoint', 'home', 'survey', 'orbit', 'rtl', 'land']);
    activePlanningTool = allowed.has((tool || '').toLowerCase()) ? tool.toLowerCase() : 'none';

    const container = document.getElementById('cesiumContainer');
    if (container) {
        container.style.cursor = activePlanningTool === 'none' ? 'default' : 'crosshair';
    }

    if (activePlanningTool === 'survey') {
        clearSurveyBoundary();
    }

    console.log(`[CesiumMap] Mission tool: ${activePlanningTool}`);
}

// ============================================
// SURVEY BOUNDARY & GRID
// ============================================
function addSurveyBoundaryPoint(lat, lon) {
    surveyBoundaryPoints.push({ lat, lon });

    // Add marker for this point
    const entity = viewer.entities.add({
        id: `survey_point_${surveyBoundaryPoints.length - 1}`,
        position: Cesium.Cartesian3.fromDegrees(lon, lat, 0),
        point: {
            pixelSize: 10,
            color: Cesium.Color.YELLOW,
            outlineColor: Cesium.Color.BLACK,
            outlineWidth: 2,
            heightReference: Cesium.HeightReference.CLAMP_TO_GROUND
        }
    });
    surveyBoundaryEntities.push(entity);

    // Draw polygon outline so far
    if (surveyBoundaryPoints.length >= 2) {
        updateSurveyBoundaryPolyline();
    }

    viewer.scene.requestRender();
}

function updateSurveyBoundaryPolyline() {
    // Remove existing polyline
    const existing = viewer.entities.getById('survey_boundary_line');
    if (existing) viewer.entities.remove(existing);

    const positions = surveyBoundaryPoints.map(p => Cesium.Cartesian3.fromDegrees(p.lon, p.lat, 0));
    // Close the polygon visually
    if (surveyBoundaryPoints.length >= 3) {
        positions.push(positions[0]);
    }

    viewer.entities.add({
        id: 'survey_boundary_line',
        polyline: {
            positions: positions,
            width: 2,
            material: new Cesium.PolylineDashMaterialProperty({
                color: Cesium.Color.YELLOW,
                dashLength: 12
            }),
            clampToGround: true
        }
    });
}

function completeSurveyBoundary() {
    if (surveyBoundaryPoints.length < 3) return;

    // Draw filled polygon
    const positions = surveyBoundaryPoints.flatMap(p => [p.lon, p.lat]);
    viewer.entities.add({
        id: 'survey_boundary_polygon',
        polygon: {
            hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
            material: Cesium.Color.YELLOW.withAlpha(0.15),
            outline: true,
            outlineColor: Cesium.Color.YELLOW,
            outlineWidth: 2,
            height: 0,
            heightReference: Cesium.HeightReference.CLAMP_TO_GROUND
        }
    });

    // Notify host with boundary data
    notifyHost({
        type: MapMessageType.SURVEY_BOUNDARY_COMPLETED,
        boundary: surveyBoundaryPoints,
        vertexCount: surveyBoundaryPoints.length
    });

    setMissionTool('none');
    console.log(`[CesiumMap] Survey boundary completed with ${surveyBoundaryPoints.length} vertices`);
}

function clearSurveyBoundary() {
    surveyBoundaryEntities.forEach(e => viewer.entities.remove(e));
    surveyBoundaryEntities = [];
    surveyBoundaryPoints = [];

    const line = viewer.entities.getById('survey_boundary_line');
    if (line) viewer.entities.remove(line);

    const polygon = viewer.entities.getById('survey_boundary_polygon');
    if (polygon) viewer.entities.remove(polygon);

    clearSurveyGrid();
}

function clearSurveyGrid() {
    surveyGridEntities.forEach(e => viewer.entities.remove(e));
    surveyGridEntities = [];
}

function renderSurveyGrid(gridWaypoints, sprayWidth) {
    clearSurveyGrid();

    if (!gridWaypoints || gridWaypoints.length < 2) return;

    // Draw spray lanes
    for (let i = 0; i < gridWaypoints.length - 1; i += 2) {
        const start = gridWaypoints[i];
        const end = gridWaypoints[i + 1];

        // Lane center line (green)
        const lineEntity = viewer.entities.add({
            id: `survey_lane_${i}`,
            polyline: {
                positions: Cesium.Cartesian3.fromDegreesArrayHeights([
                    start.lon, start.lat, start.alt,
                    end.lon, end.lat, end.alt
                ]),
                width: 3,
                material: Cesium.Color.LIMEGREEN
            }
        });
        surveyGridEntities.push(lineEntity);

        // Spray swath corridor
        const corridorEntity = viewer.entities.add({
            id: `survey_swath_${i}`,
            corridor: {
                positions: Cesium.Cartesian3.fromDegreesArray([
                    start.lon, start.lat, end.lon, end.lat
                ]),
                width: sprayWidth,
                material: Cesium.Color.LIMEGREEN.withAlpha(0.15),
                height: start.alt
            }
        });
        surveyGridEntities.push(corridorEntity);
    }

    // Draw turnaround segments (cyan dashed)
    for (let i = 1; i < gridWaypoints.length - 1; i += 2) {
        const turnEntity = viewer.entities.add({
            id: `survey_turn_${i}`,
            polyline: {
                positions: Cesium.Cartesian3.fromDegreesArrayHeights([
                    gridWaypoints[i].lon, gridWaypoints[i].lat, gridWaypoints[i].alt,
                    gridWaypoints[i + 1].lon, gridWaypoints[i + 1].lat, gridWaypoints[i + 1].alt
                ]),
                width: 2,
                material: new Cesium.PolylineDashMaterialProperty({
                    color: Cesium.Color.CYAN,
                    dashLength: 12
                })
            }
        });
        surveyGridEntities.push(turnEntity);
    }

    viewer.scene.requestRender();
    console.log(`[CesiumMap] Survey grid rendered with ${gridWaypoints.length} waypoints`);
}

// ============================================
// SPRAY OVERLAY
// ============================================
function setSprayActive(active, width) {
    isSprayActive = active;
    if (width) sprayWidthMeters = width;
    if (!active) lastSprayPosition = null;
    console.log(`[CesiumMap] Spray ${active ? 'ON' : 'OFF'}, width: ${sprayWidthMeters}m`);
}

function updateSprayTrail(lat, lon, heading) {
    if (!isSprayActive || !isValidCoordinate(lat, lon)) return;

    const current = { lat, lon };

    if (lastSprayPosition) {
        const halfWidth = sprayWidthMeters / 2;
        const perpAngle = (heading || 0) + 90;

        const corners = [
            offsetPoint(lastSprayPosition, perpAngle, halfWidth),
            offsetPoint(lastSprayPosition, perpAngle, -halfWidth),
            offsetPoint(current, perpAngle, -halfWidth),
            offsetPoint(current, perpAngle, halfWidth)
        ];

        const positions = corners.flatMap(c => [c.lon, c.lat]);
        const entity = viewer.entities.add({
            id: `spray_${Date.now()}`,
            polygon: {
                hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
                material: Cesium.Color.LIMEGREEN.withAlpha(0.3),
                height: 0,
                outline: false,
                heightReference: Cesium.HeightReference.CLAMP_TO_GROUND
            }
        });
        sprayOverlayEntities.push(entity);
    }

    lastSprayPosition = current;
}

function clearSprayOverlay() {
    sprayOverlayEntities.forEach(e => viewer.entities.remove(e));
    sprayOverlayEntities = [];
    lastSprayPosition = null;
}

function offsetPoint(point, angleDeg, distanceMeters) {
    const angleRad = Cesium.Math.toRadians(angleDeg);
    const latRad = Cesium.Math.toRadians(point.lat);
    const deltaLat = (distanceMeters * Math.cos(angleRad)) / 111320;
    const deltaLon = (distanceMeters * Math.sin(angleRad)) / (111320 * Math.max(Math.cos(latRad), 0.0001));
    return { lat: point.lat + deltaLat, lon: point.lon + deltaLon };
}

// ============================================
// DRONE POSITION & ATTITUDE UPDATE
// ============================================
function updateDrone(lat, lon, alt, heading, pitch, roll, isArmed, groundSpeed, verticalSpeed) {
    if (!viewer || !isInitialized) {
        console.warn('[CesiumMap] updateDrone called but viewer not ready');
        return;
    }

    // Throttle updates
    const now = Date.now();
    if (now - lastUpdateTime < CONFIG.updateThrottleMs) {
        return;
    }
    lastUpdateTime = now;

    try {
        // Validate coordinates
        if (!isValidCoordinate(lat, lon)) {
            console.warn(`[CesiumMap] Invalid coordinates: ${lat}, ${lon}`);
            return;
        }

        // Only log every 50th update to reduce spam
        if (!window._droneUpdateCount) window._droneUpdateCount = 0;
        window._droneUpdateCount++;
        
        if (window._droneUpdateCount % 50 === 0) {
            console.log(`[CesiumMap] Drone update #${window._droneUpdateCount}: ${lat.toFixed(6)}, ${lon.toFixed(6)}, ${alt.toFixed(1)}m, ${heading.toFixed(0)}°`);
        }

        const position = Cesium.Cartesian3.fromDegrees(lon, lat, alt);
        const headingRad = Cesium.Math.toRadians(heading || 0);
        const pitchRad = Cesium.Math.toRadians(pitch || 0);
        const rollRad = Cesium.Math.toRadians(roll || 0);
        
        const hpr = new Cesium.HeadingPitchRoll(headingRad, pitchRad, rollRad);
        const orientation = Cesium.Transforms.headingPitchRollQuaternion(position, hpr);

        // Update state
        lastDronePosition = position;
        lastDroneHeading = heading;
        lastDroneAltitude = alt;
        lastDronePitch = pitch;
        lastDroneRoll = roll;

        // Set home on first valid position
        if (!homePosition && isValidCoordinate(lat, lon)) {
            setHomePosition(lat, lon);
        }

        // Create or update drone entity
        if (!droneEntity) {
            console.log('[CesiumMap] Creating drone entity');
            droneEntity = viewer.entities.add({
                id: 'drone',
                position: position,
                orientation: orientation,
                billboard: {
                    image: createDroneModelUri(isArmed),
                    width: 56,
                    height: 56,
                    verticalOrigin: Cesium.VerticalOrigin.CENTER,
                    horizontalOrigin: Cesium.HorizontalOrigin.CENTER,
                    rotation: 0,
                    alignedAxis: Cesium.Cartesian3.UNIT_Z,
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
                },
                label: createDroneLabel(alt, groundSpeed, verticalSpeed, isArmed)
            });

            // If KFT logo fails to load from local path, switch to SVG fallback
            if (typeof Image !== 'undefined' && !window._kftLogoChecked) {
                window._kftLogoChecked = true;
                const img = new Image();
                img.onload = () => { window._kftLogoFailed = false; };
                img.onerror = () => {
                    window._kftLogoFailed = true;
                    if (droneEntity?.billboard) {
                        droneEntity.billboard.image = createDroneModelUri(isArmed);
                    }
                };
                img.src = '../Images/KFT%20Logo.png';
            }
        } else {
            droneEntity.position = position;
            droneEntity.orientation = orientation;
            droneEntity.billboard.image = createDroneModelUri(isArmed);
            droneEntity.billboard.rotation = 0;
            updateDroneLabel(droneEntity.label, alt, groundSpeed, verticalSpeed, isArmed);
        }

        updateDroneHeadingLine(lat, lon, alt, heading);
        addToFlightPath(lon, lat, alt, isArmed);

        // Update spray trail if spraying
        if (isSprayActive) {
            updateSprayTrail(lat, lon, heading);
        }

        if (isFollowing) {
            updateCameraFollow(position, headingRad, pitchRad);
        }

        viewer.scene.requestRender();

    } catch (error) {
        console.error('[CesiumMap] ? Error updating drone:', error);
        notifyHost({ type: 'error', message: `Update drone failed: ${error.message}` });
    }
}

function createDroneLabel(alt, groundSpeed, verticalSpeed, isArmed) {
    const vsSign = verticalSpeed >= 0 ? '↑' : '↓';
    
    return {
        text: `▲ ${alt.toFixed(1)}m  ${vsSign} ${Math.abs(verticalSpeed || 0).toFixed(1)}m/s  → ${(groundSpeed || 0).toFixed(1)}m/s`,
        font: '13px Inter, sans-serif',
        fillColor: Cesium.Color.WHITE,
        outlineColor: Cesium.Color.BLACK,
        outlineWidth: 2,
        style: Cesium.LabelStyle.FILL_AND_OUTLINE,
        verticalOrigin: Cesium.VerticalOrigin.TOP,
        pixelOffset: new Cesium.Cartesian2(0, 40),
        showBackground: true,
        backgroundColor: new Cesium.Color(0.1, 0.1, 0.1, 0.85),
        backgroundPadding: new Cesium.Cartesian2(10, 6),
        disableDepthTestDistance: Number.POSITIVE_INFINITY
    };
}

function updateDroneLabel(label, alt, groundSpeed, verticalSpeed, isArmed) {
    if (!label) return;
    const vsSign = verticalSpeed >= 0 ? '↑' : '↓';
    label.text = `▲ ${alt.toFixed(1)}m  ${vsSign} ${Math.abs(verticalSpeed || 0).toFixed(1)}m/s  → ${(groundSpeed || 0).toFixed(1)}m/s`;
}

function isValidCoordinate(lat, lon) {
    return Math.abs(lat) > 0.0001 && Math.abs(lon) > 0.0001 &&
           lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
}

// ============================================
// FLIGHT PATH
// ============================================
function addToFlightPath(lon, lat, alt, isArmed) {
    const newPos = Cesium.Cartesian3.fromDegrees(lon, lat, alt);
    
    // Distance threshold check
    if (flightPathPositions.length > 0) {
        const lastPos = flightPathPositions[flightPathPositions.length - 1].position;
        const distance = Cesium.Cartesian3.distance(lastPos, newPos);
        if (distance < 0.5) return; // Less than 0.5m, skip
    }

    flightPathPositions.push({
        position: newPos,
        isArmed: isArmed,
        timestamp: Date.now()
    });

    // Limit path length
    while (flightPathPositions.length > CONFIG.maxFlightPathPoints) {
        flightPathPositions.shift();
    }

    // Rebuild flight path with gradient coloring
    if (flightPathPositions.length >= 2) {
        rebuildFlightPath();
    }
}

function rebuildFlightPath() {
    // Remove existing path
    if (flightPathEntity) {
        viewer.entities.remove(flightPathEntity);
    }

    const positions = flightPathPositions.map(p => p.position);
    
    // Determine if currently armed for path color
    const isArmed = flightPathPositions.length > 0 ? 
        flightPathPositions[flightPathPositions.length - 1].isArmed : false;
    
    const pathColor = isArmed ? 
        Cesium.Color.fromCssColorString('#EF4444').withAlpha(0.9) :
        Cesium.Color.fromCssColorString('#3B82F6').withAlpha(0.9);

    flightPathEntity = viewer.entities.add({
        id: 'flightPath',
        polyline: {
            positions: positions,
            width: 4,
            material: new Cesium.PolylineGlowMaterialProperty({
                glowPower: 0.3,
                taperPower: 0.5,
                color: pathColor
            }),
            clampToGround: false,
            depthFailMaterial: new Cesium.PolylineGlowMaterialProperty({
                glowPower: 0.2,
                color: pathColor.withAlpha(0.5)
            })
        }
    });
}

// ============================================
// HOME POSITION
// ============================================
function setHomePosition(lat, lon) {
    if (homeEntity) {
        viewer.entities.remove(homeEntity);
    }

    const existingCircle = viewer.entities.getById('homeCircle');
    if (existingCircle) {
        viewer.entities.remove(existingCircle);
    }

    homePosition = { lat, lon };
    const position = Cesium.Cartesian3.fromDegrees(lon, lat, 0);

    homeEntity = viewer.entities.add({
        id: 'home',
        position: position,
        billboard: {
            image: createHomeIconSvg(),
            width: 44,
            height: 44,
            verticalOrigin: Cesium.VerticalOrigin.BOTTOM,
            horizontalOrigin: Cesium.HorizontalOrigin.CENTER,
            disableDepthTestDistance: Number.POSITIVE_INFINITY
        },
        label: {
            text: 'HOME',
            font: '11px Inter, sans-serif',
            fillColor: Cesium.Color.WHITE,
            outlineColor: Cesium.Color.BLACK,
            outlineWidth: 2,
            style: Cesium.LabelStyle.FILL_AND_OUTLINE,
            verticalOrigin: Cesium.VerticalOrigin.TOP,
            pixelOffset: new Cesium.Cartesian2(0, 5),
            showBackground: true,
            backgroundColor: new Cesium.Color(0.1, 0.5, 0.2, 0.85),
            backgroundPadding: new Cesium.Cartesian2(8, 4),
            disableDepthTestDistance: Number.POSITIVE_INFINITY
        }
    });

    viewer.entities.add({
        id: 'homeCircle',
        position: position,
        ellipse: {
            semiMajorAxis: 8,
            semiMinorAxis: 8,
            material: Cesium.Color.fromCssColorString('#22C55E').withAlpha(0.4),
            outline: true,
            outlineColor: Cesium.Color.WHITE,
            outlineWidth: 2,
            height: 0,
            heightReference: Cesium.HeightReference.CLAMP_TO_GROUND
        }
    });

    console.log(`[CesiumMap] Home position set: ${lat.toFixed(6)}, ${lon.toFixed(6)}`);
}

// ============================================
// WAYPOINTS
// ============================================
function setWaypoints(waypoints) {
    clearWaypointsInternal();

    if (!waypoints || waypoints.length === 0) {
        missionWaypoints = [];
        return;
    }

    missionWaypoints = waypoints.map(w => ({ ...w }));

    waypoints.forEach((wp, index) => {
        const position = Cesium.Cartesian3.fromDegrees(wp.lon, wp.lat, wp.alt || 50);
        const isCurrent = wp.isCurrent || false;
        
        const entity = viewer.entities.add({
            id: `waypoint_${index}`,
            position: position,
            billboard: {
                image: createWaypointIcon(index + 1, isCurrent),
                width: 36,
                height: 50,
                verticalOrigin: Cesium.VerticalOrigin.BOTTOM,
                horizontalOrigin: Cesium.HorizontalOrigin.CENTER,
                disableDepthTestDistance: Number.POSITIVE_INFINITY
            },
            label: {
                text: `${(wp.alt || 50).toFixed(0)}m`,
                font: '11px Inter, sans-serif',
                fillColor: Cesium.Color.WHITE,
                outlineColor: Cesium.Color.BLACK,
                outlineWidth: 2,
                style: Cesium.LabelStyle.FILL_AND_OUTLINE,
                verticalOrigin: Cesium.VerticalOrigin.BOTTOM,
                pixelOffset: new Cesium.Cartesian2(30, -25),
                showBackground: true,
                backgroundColor: new Cesium.Color(0, 0, 0, 0.7),
                backgroundPadding: new Cesium.Cartesian2(6, 3),
                disableDepthTestDistance: Number.POSITIVE_INFINITY
            }
        });
        
        waypointEntities.push(entity);

        const groundPosition = Cesium.Cartesian3.fromDegrees(wp.lon, wp.lat, 0);
        const lineEntity = viewer.entities.add({
            id: `waypoint_line_${index}`,
            polyline: {
                positions: [groundPosition, position],
                width: 2,
                material: new Cesium.PolylineDashMaterialProperty({
                    color: Cesium.Color.YELLOW.withAlpha(0.7),
                    dashLength: 8
                })
            }
        });
        waypointEntities.push(lineEntity);
    });

    if (waypoints.length >= 2) {
        createMissionPath(waypoints);
    }

    viewer.scene.requestRender();
    console.log(`[CesiumMap] Added ${waypoints.length} waypoints`);
}

function createMissionPath(waypoints) {
    if (missionPathEntity) {
        viewer.entities.remove(missionPathEntity);
    }

    const positions = waypoints.map(wp => 
        Cesium.Cartesian3.fromDegrees(wp.lon, wp.lat, wp.alt || 50)
    );

    missionPathEntity = viewer.entities.add({
        id: 'missionPath',
        polyline: {
            positions: positions,
            width: 3,
            material: new Cesium.PolylineArrowMaterialProperty(
                Cesium.Color.YELLOW.withAlpha(0.8)
            ),
            clampToGround: false
        }
    });
}

function clearWaypointsInternal() {
    waypointEntities.forEach(entity => {
        viewer.entities.remove(entity);
    });
    waypointEntities = [];
    
    if (missionPathEntity) {
        viewer.entities.remove(missionPathEntity);
        missionPathEntity = null;
    }
}

function clearWaypoints() {
    clearWaypointsInternal();
    missionWaypoints = [];
}

function deleteWaypoint(index) {
    if (index >= 0 && index < missionWaypoints.length) {
        missionWaypoints.splice(index, 1);
        setWaypoints(missionWaypoints);
        notifyHost({ type: MapMessageType.WAYPOINT_DELETED, index });
    }
}

function setCurrentWaypoint(index) {
    missionWaypoints.forEach((wp, i) => {
        wp.isCurrent = (i === index);
    });
    setWaypoints(missionWaypoints);
    viewer.scene.requestRender();
}

// ============================================
// CAMERA CONTROL
// ============================================
function updateCameraFollow(position, heading, pitch) {
    if (!viewer || !position) return;

    switch (currentViewMode) {
        case ViewMode.TOP_DOWN:
            updateTopDownView(position);
            break;
        case ViewMode.CHASE_3D:
            updateChaseView(position, heading);
            break;
        case ViewMode.FIRST_PERSON:
            updateFirstPersonView(position, heading, pitch);
            break;
    }
}

function updateTopDownView(position) {
    const cartographic = Cesium.Cartographic.fromCartesian(position);
    const height = Math.max(cartographic.height + CONFIG.topDownHeight, 100);
    
    viewer.camera.setView({
        destination: Cesium.Cartesian3.fromRadians(
            cartographic.longitude,
            cartographic.latitude,
            height
        ),
        orientation: {
            heading: 0,
            pitch: Cesium.Math.toRadians(-90),
            roll: 0
        }
    });
}

function updateChaseView(position, heading) {
    const cartographic = Cesium.Cartographic.fromCartesian(position);
    
    // Calculate camera position behind and above drone
    const offsetDistance = 80; // meters behind
    const offsetHeight = 40; // meters above
    
    const offsetLon = cartographic.longitude - Math.sin(heading) * (offsetDistance / 111319.5 / Math.cos(cartographic.latitude));
    const offsetLat = cartographic.latitude - Math.cos(heading) * (offsetDistance / 111319.5);
    
    viewer.camera.setView({
        destination: Cesium.Cartesian3.fromRadians(
            offsetLon,
            offsetLat,
            cartographic.height + offsetHeight
        ),
        orientation: {
            heading: heading,
            pitch: Cesium.Math.toRadians(-25), // Looking down at drone
            roll: 0
        }
    });
}

function updateFirstPersonView(position, heading, pitch) {
    viewer.camera.setView({
        destination: position,
        orientation: {
            heading: heading,
            pitch: Cesium.Math.toRadians(pitch - 10), // Slight forward tilt
            roll: 0
        }
    });
}

function centerOnDrone(animate = true) {
    if (!droneEntity || !viewer || !lastDronePosition) return;

    const cartographic = Cesium.Cartographic.fromCartesian(lastDronePosition);
    const height = Math.max(cartographic.height + 150, 250);

    if (animate) {
        viewer.camera.flyTo({
            destination: Cesium.Cartesian3.fromRadians(
                cartographic.longitude,
                cartographic.latitude,
                height
            ),
            orientation: {
                heading: Cesium.Math.toRadians(lastDroneHeading),
                pitch: Cesium.Math.toRadians(-45),
                roll: 0
            },
            duration: 1.5,
            easingFunction: Cesium.EasingFunction.QUADRATIC_IN_OUT
        });
    } else {
        viewer.camera.setView({
            destination: Cesium.Cartesian3.fromRadians(
                cartographic.longitude,
                cartographic.latitude,
                height
            ),
            orientation: {
                heading: Cesium.Math.toRadians(lastDroneHeading),
                pitch: Cesium.Math.toRadians(-45),
                roll: 0
            }
        });
    }
}

// ============================================
// VIEW MODE CONTROL
// ============================================
function setViewMode(mode) {
    currentViewMode = mode;
    
    switch (mode) {
        case ViewMode.TOP_DOWN:
            isFollowing = true;
            if (lastDronePosition) {
                updateTopDownView(lastDronePosition);
            }
            console.log('[CesiumMap] View mode: Top-Down');
            break;
            
        case ViewMode.CHASE_3D:
            isFollowing = true;
            if (lastDronePosition) {
                updateChaseView(lastDronePosition, Cesium.Math.toRadians(lastDroneHeading));
            }
            console.log('[CesiumMap] View mode: 3D Chase');
            break;
            
        case ViewMode.FIRST_PERSON:
            isFollowing = true;
            if (lastDronePosition) {
                updateFirstPersonView(lastDronePosition, 
                    Cesium.Math.toRadians(lastDroneHeading), 
                    lastDronePitch);
            }
            console.log('[CesiumMap] View mode: First Person');
            break;
            
        case ViewMode.FREE_ROAM:
            isFollowing = false;
            console.log('[CesiumMap] View mode: Free Roam');
            break;
    }
    
    notifyHost({ type: MapMessageType.VIEW_MODE_CHANGED, mode: mode });
    viewer.scene.requestRender();
}

function toggleFollow() {
    isFollowing = !isFollowing;
    if (isFollowing && lastDronePosition) {
        updateCameraFollow(lastDronePosition, 
            Cesium.Math.toRadians(lastDroneHeading), 
            lastDronePitch);
    }
    notifyHost({ type: MapMessageType.FOLLOW_CHANGED, following: isFollowing });
    return isFollowing;
}

// ============================================
// MAP CONTROLS
// ============================================
function setMapType(type) {
    if (!viewer) return;
    applyMapType(type);
    console.log(`[CesiumMap] Map type: ${type}`);
}

function clearFlightPath() {
    flightPathPositions = [];
    if (flightPathEntity) {
        viewer.entities.remove(flightPathEntity);
        flightPathEntity = null;
    }
    if (homeEntity) {
        viewer.entities.remove(homeEntity);
        homeEntity = null;
    }
    const homeCircle = viewer.entities.getById('homeCircle');
    if (homeCircle) {
        viewer.entities.remove(homeCircle);
    }
    if (droneHeadingLineEntity) {
        viewer.entities.remove(droneHeadingLineEntity);
        droneHeadingLineEntity = null;
    }
    homePosition = null;
    clearSprayOverlay();
    viewer.scene.requestRender();
    console.log('[CesiumMap] Flight path cleared');
}

function zoomIn() {
    if (!viewer) return;
    viewer.camera.zoomIn(viewer.camera.positionCartographic.height * 0.3);
}

function zoomOut() {
    if (!viewer) return;
    viewer.camera.zoomOut(viewer.camera.positionCartographic.height * 0.3);
}

function resetView() {
    if (!viewer) return;
    
    if (homePosition) {
        viewer.camera.flyTo({
            destination: Cesium.Cartesian3.fromDegrees(
                homePosition.lon, 
                homePosition.lat, 
                500
            ),
            orientation: {
                heading: 0,
                pitch: Cesium.Math.toRadians(-60),
                roll: 0
            },
            duration: 2
        });
    } else {
        viewer.camera.flyTo({
            destination: Cesium.Cartesian3.fromDegrees(
                CONFIG.defaultCenter.lon, 
                CONFIG.defaultCenter.lat, 
                3000000
            ),
            duration: 2
        });
    }
}

// ============================================
// KEYBOARD CONTROLS
// ============================================
function setupKeyboardControls() {
    document.addEventListener('keydown', (e) => {
        switch (e.key) {
            case '1':
                setViewMode(ViewMode.TOP_DOWN);
                break;
            case '2':
                setViewMode(ViewMode.CHASE_3D);
                break;
            case '3':
                setViewMode(ViewMode.FIRST_PERSON);
                break;
            case '4':
                setViewMode(ViewMode.FREE_ROAM);
                break;
            case 'f':
            case 'F':
                toggleFollow();
                break;
            case 'c':
            case 'C':
                centerOnDrone(true);
                break;
            case 'h':
            case 'H':
                if (homePosition) {
                    viewer.camera.flyTo({
                        destination: Cesium.Cartesian3.fromDegrees(
                            homePosition.lon, 
                            homePosition.lat, 
                            200
                        ),
                        duration: 1.5
                    });
                }
                break;
            case 'Escape':
                setMissionTool('none');
                break;
        }
    });
}

// ============================================
// GEOFENCE
// ============================================
function setGeofence(center, radius, maxAlt) {
    const existing = viewer.entities.getById('geofence');
    if (existing) viewer.entities.remove(existing);
    
    const existingAlt = viewer.entities.getById('geofenceAlt');
    if (existingAlt) viewer.entities.remove(existingAlt);

    if (!center || !radius) return;

    viewer.entities.add({
        id: 'geofence',
        position: Cesium.Cartesian3.fromDegrees(center.lon, center.lat, 0),
        ellipse: {
            semiMajorAxis: radius,
            semiMinorAxis: radius,
            material: Cesium.Color.RED.withAlpha(0.1),
            outline: true,
            outlineColor: Cesium.Color.RED.withAlpha(0.8),
            outlineWidth: 3,
            height: 0,
            heightReference: Cesium.HeightReference.CLAMP_TO_GROUND
        }
    });

    if (maxAlt) {
        viewer.entities.add({
            id: 'geofenceAlt',
            position: Cesium.Cartesian3.fromDegrees(center.lon, center.lat, maxAlt / 2),
            cylinder: {
                length: maxAlt,
                topRadius: radius,
                bottomRadius: radius,
                material: Cesium.Color.RED.withAlpha(0.05),
                outline: true,
                outlineColor: Cesium.Color.RED.withAlpha(0.3),
                numberOfVerticalLines: 16
            }
        });
    }

    console.log(`[CesiumMap] Geofence set: radius=${radius}m, maxAlt=${maxAlt}m`);
}

// ============================================
// TELEMETRY OVERLAY (HUD)
// ============================================
function updateTelemetryHud(telemetry) {
    // Update on-map HUD elements if present
    const hudAlt = document.getElementById('hud-alt');
    const hudSpd = document.getElementById('hud-spd');
    const hudHdg = document.getElementById('hud-hdg');
    
    if (hudAlt && telemetry.altitude !== undefined) {
        hudAlt.textContent = telemetry.altitude.toFixed(1);
    }
    if (hudSpd && telemetry.groundSpeed !== undefined) {
        hudSpd.textContent = telemetry.groundSpeed.toFixed(1);
    }
    if (hudHdg && telemetry.heading !== undefined) {
        hudHdg.textContent = telemetry.heading.toFixed(0);
    }
}

// ============================================
// INITIALIZATION
// ============================================

document.addEventListener('DOMContentLoaded', initializeCesium);

// ============================================
// EXPOSE API TO C#
// ============================================
window.updateDrone = updateDrone;
window.centerOnDrone = centerOnDrone;
window.clearFlightPath = clearFlightPath;
window.setMapType = setMapType;
window.setViewMode = setViewMode;
window.toggleFollow = toggleFollow;
window.zoomIn = zoomIn;
window.zoomOut = zoomOut;
window.resetView = resetView;
window.setHomePosition = setHomePosition;
window.setWaypoints = setWaypoints;
window.setCurrentWaypoint = setCurrentWaypoint;
window.clearWaypoints = clearWaypoints;
window.deleteWaypoint = deleteWaypoint;
window.setGeofence = setGeofence;
window.updateTelemetryHud = updateTelemetryHud;
window.setMissionTool = setMissionTool;
window.renderSurveyGrid = renderSurveyGrid;
window.clearSurveyGrid = clearSurveyGrid;
window.clearSurveyBoundary = clearSurveyBoundary;
window.setSprayActive = setSprayActive;
window.clearSprayOverlay = clearSprayOverlay;

// Constants for C#
window.ViewMode = ViewMode;
window.MapMessageType = MapMessageType;