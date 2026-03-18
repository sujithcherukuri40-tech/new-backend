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
    droneModelScale: 2.0
};

// View modes
const ViewMode = {
    TOP_DOWN: 'topdown',
    CHASE_3D: 'chase3d',
    FREE_ROAM: 'free',
    FIRST_PERSON: 'fpv'
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

// Throttling
let lastUpdateTime = 0;

// ============================================
// DRONE 3D MODEL - Quadcopter shape using primitives
// ============================================
function createDroneModelUri(isArmed) {
    const baseColor = isArmed ? '#EF4444' : '#3B82F6';
    const glowColor = isArmed ? '#DC2626' : '#2563EB';
    
    // Create a simple quadcopter SVG for billboard
    const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
        <defs>
            <filter id="glow" x="-50%" y="-50%" width="200%" height="200%">
                <feGaussianBlur stdDeviation="3" result="coloredBlur"/>
                <feMerge>
                    <feMergeNode in="coloredBlur"/>
                    <feMergeNode in="SourceGraphic"/>
                </feMerge>
            </filter>
            <linearGradient id="bodyGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" style="stop-color:${baseColor};stop-opacity:1" />
                <stop offset="100%" style="stop-color:${glowColor};stop-opacity:1" />
            </linearGradient>
        </defs>
        <!-- Arms -->
        <line x1="20" y1="20" x2="80" y2="80" stroke="${baseColor}" stroke-width="6" filter="url(#glow)"/>
        <line x1="80" y1="20" x2="20" y2="80" stroke="${baseColor}" stroke-width="6" filter="url(#glow)"/>
        <!-- Motors -->
        <circle cx="20" cy="20" r="12" fill="${baseColor}" filter="url(#glow)"/>
        <circle cx="80" cy="20" r="12" fill="${baseColor}" filter="url(#glow)"/>
        <circle cx="20" cy="80" r="12" fill="${baseColor}" filter="url(#glow)"/>
        <circle cx="80" cy="80" r="12" fill="${baseColor}" filter="url(#glow)"/>
        <!-- Props (spinning effect) -->
        <circle cx="20" cy="20" r="15" fill="none" stroke="rgba(255,255,255,0.4)" stroke-width="2" stroke-dasharray="8,4"/>
        <circle cx="80" cy="20" r="15" fill="none" stroke="rgba(255,255,255,0.4)" stroke-width="2" stroke-dasharray="8,4"/>
        <circle cx="20" cy="80" r="15" fill="none" stroke="rgba(255,255,255,0.4)" stroke-width="2" stroke-dasharray="8,4"/>
        <circle cx="80" cy="80" r="15" fill="none" stroke="rgba(255,255,255,0.4)" stroke-width="2" stroke-dasharray="8,4"/>
        <!-- Center body -->
        <circle cx="50" cy="50" r="18" fill="url(#bodyGrad)" filter="url(#glow)"/>
        <circle cx="50" cy="50" r="10" fill="white"/>
        <circle cx="50" cy="50" r="5" fill="${baseColor}"/>
        <!-- Heading indicator (front) -->
        <polygon points="50,25 45,38 55,38" fill="white" filter="url(#glow)"/>
    </svg>`;
    
    return `data:image/svg+xml;base64,${btoa(svg)}`;
}

// Home icon SVG
function createHomeIconSvg() {
    const svg = `
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" width="48" height="48">
    <defs>
        <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
            <feDropShadow dx="0" dy="2" stdDeviation="2" flood-opacity="0.5"/>
        </filter>
    </defs>
    <circle cx="24" cy="24" r="20" fill="#22C55E" stroke="white" stroke-width="3" filter="url(#shadow)"/>
    <path d="M24 12 L36 22 L36 36 L28 36 L28 26 L20 26 L20 36 L12 36 L12 22 Z" fill="white"/>
</svg>`;
    return `data:image/svg+xml;base64,${btoa(svg)}`;
}

// Waypoint icon generator
function createWaypointIcon(number, isCurrent = false) {
    const bgColor = isCurrent ? '#F59E0B' : '#FBBF24';
    const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 56" width="40" height="56">
        <defs>
            <filter id="wp-shadow" x="-20%" y="-10%" width="140%" height="120%">
                <feDropShadow dx="0" dy="3" stdDeviation="2" flood-opacity="0.4"/>
            </filter>
        </defs>
        <path d="M20 0 C9 0 0 9 0 20 C0 35 20 56 20 56 C20 56 40 35 40 20 C40 9 31 0 20 0 Z" 
              fill="${bgColor}" filter="url(#wp-shadow)"/>
        <circle cx="20" cy="20" r="14" fill="white"/>
        <text x="20" y="25" text-anchor="middle" font-size="14" font-weight="bold" fill="${bgColor}">${number}</text>
    </svg>`;
    return `data:image/svg+xml;base64,${btoa(svg)}`;
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
            
            // Use OpenStreetMap imagery (free, no API key)
            imageryProvider: new Cesium.OpenStreetMapImageryProvider({
                url: 'https://tile.openstreetmap.org/'
            }),
            
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

        // Configure scene
        const scene = viewer.scene;
        scene.globe.enableLighting = false;
        scene.globe.depthTestAgainstTerrain = false;
        scene.fog.enabled = true;
        scene.fog.density = 0.0001;
        
        // Disable HDR for better performance
        scene.highDynamicRange = false;
        
        // Enable FXAA anti-aliasing
        if (scene.postProcessStages && scene.postProcessStages.fxaa) {
            scene.postProcessStages.fxaa.enabled = true;
        }

        console.log('[CesiumMap] Scene configured');

        // Set initial camera position (India)
        viewer.camera.setView({
            destination: Cesium.Cartesian3.fromDegrees(
                CONFIG.defaultCenter.lon, 
                CONFIG.defaultCenter.lat, 
                3000000
            ),
            orientation: {
                heading: 0,
                pitch: Cesium.Math.toRadians(-90),
                roll: 0
            }
        });

        console.log('[CesiumMap] Camera positioned');

        // Add keyboard controls
        setupKeyboardControls();

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
        console.log('[CesiumMap] ? Initialization complete - Ready for telemetry');
        console.log('[CesiumMap] ========================================');
        
        // Notify C# that map is ready
        notifyHost({ type: 'mapReady' });

    } catch (error) {
        console.error('[CesiumMap] ? Initialization failed:', error);
        console.error('[CesiumMap] Error stack:', error.stack);
        showLoadingError(error.message);
        notifyHost({ type: 'error', message: `Map init failed: ${error.message}` });
    }
}

function showLoadingError(message) {
    const overlay = document.getElementById('loading-overlay');
    if (overlay) {
        overlay.innerHTML = `
            <div style="color: #EF4444; text-align: center; padding: 20px;">
                <div style="font-size: 48px; margin-bottom: 16px;">??</div>
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
                    width: 64,
                    height: 64,
                    verticalOrigin: Cesium.VerticalOrigin.CENTER,
                    horizontalOrigin: Cesium.HorizontalOrigin.CENTER,
                    rotation: -headingRad,
                    alignedAxis: Cesium.Cartesian3.UNIT_Z,
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
                },
                label: createDroneLabel(alt, groundSpeed, verticalSpeed, isArmed)
            });
        } else {
            droneEntity.position = position;
            droneEntity.orientation = orientation;
            droneEntity.billboard.image = createDroneModelUri(isArmed);
            droneEntity.billboard.rotation = -headingRad;
            
            // Update label
            updateDroneLabel(droneEntity.label, alt, groundSpeed, verticalSpeed, isArmed);
        }

        // Add to flight path
        addToFlightPath(lon, lat, alt, isArmed);

        // Update camera if following
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
    const vsSign = verticalSpeed >= 0 ? '?' : '?';
    
    return {
        text: `? ${alt.toFixed(1)}m  ${vsSign} ${Math.abs(verticalSpeed || 0).toFixed(1)}m/s  ? ${(groundSpeed || 0).toFixed(1)}m/s`,
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
    const vsSign = verticalSpeed >= 0 ? '?' : '?';
    label.text = `? ${alt.toFixed(1)}m  ${vsSign} ${Math.abs(verticalSpeed || 0).toFixed(1)}m/s  ? ${(groundSpeed || 0).toFixed(1)}m/s`;
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

    // Add ground circle marker
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
    // Clear existing waypoints
    clearWaypoints();

    if (!waypoints || waypoints.length === 0) return;

    // Add waypoint markers
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
                text: `${(wp.alt || 50).toFixed(0)}m\n(${(wp.groundAlt || wp.alt || 50).toFixed(0)}m)`,
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

        // Add vertical line from waypoint to ground
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

    // Create mission path
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

function clearWaypoints() {
    waypointEntities.forEach(entity => {
        viewer.entities.remove(entity);
    });
    waypointEntities = [];
    
    if (missionPathEntity) {
        viewer.entities.remove(missionPathEntity);
        missionPathEntity = null;
    }
}

function setCurrentWaypoint(index) {
    // Update waypoint appearance
    waypointEntities.forEach((entity, i) => {
        if (entity.billboard && i < waypointEntities.length / 2) {
            entity.billboard.image = createWaypointIcon(i + 1, i === index);
        }
    });
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
        // FREE_ROAM does nothing - user controls camera
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
            pitch: Cesium.Math.toRadians(-90), // Straight down
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
    
    notifyHost({ type: 'viewModeChanged', mode: mode });
    viewer.scene.requestRender();
}

function toggleFollow() {
    isFollowing = !isFollowing;
    if (isFollowing && lastDronePosition) {
        updateCameraFollow(lastDronePosition, 
            Cesium.Math.toRadians(lastDroneHeading), 
            lastDronePitch);
    }
    notifyHost({ type: 'followChanged', following: isFollowing });
    return isFollowing;
}

// ============================================
// MAP CONTROLS
// ============================================
function setMapType(type) {
    if (!viewer) return;

    const imageryLayers = viewer.imageryLayers;
    imageryLayers.removeAll();

    switch (type) {
        case 'satellite':
            // Use ESRI World Imagery (free, no API key)
            imageryLayers.addImageryProvider(new Cesium.ArcGisMapServerImageryProvider({
                url: 'https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer'
            }));
            break;
            
        case 'terrain':
        case 'roadmap':
        default:
            // Use OpenStreetMap
            imageryLayers.addImageryProvider(new Cesium.OpenStreetMapImageryProvider({
                url: 'https://tile.openstreetmap.org/'
            }));
            break;
            
        case 'hybrid':
            // ESRI imagery + labels
            imageryLayers.addImageryProvider(new Cesium.ArcGisMapServerImageryProvider({
                url: 'https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer'
            }));
            break;
    }

    console.log(`[CesiumMap] Map type: ${type}`);
    viewer.scene.requestRender();
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
    // Remove home circle
    const homeCircle = viewer.entities.getById('homeCircle');
    if (homeCircle) {
        viewer.entities.remove(homeCircle);
    }
    homePosition = null;
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
        }
    });
}

// ============================================
// GEOFENCE
// ============================================
function setGeofence(center, radius, maxAlt) {
    // Remove existing geofence
    const existing = viewer.entities.getById('geofence');
    if (existing) {
        viewer.entities.remove(existing);
    }
    const existingAlt = viewer.entities.getById('geofenceAlt');
    if (existingAlt) {
        viewer.entities.remove(existingAlt);
    }

    if (!center || !radius) return;

    // Ground circle
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

    // Altitude cylinder (if max altitude specified)
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
    // This can be used to update an HTML overlay for telemetry
    // For now, the drone label handles basic telemetry display
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
window.setGeofence = setGeofence;
window.updateTelemetryHud = updateTelemetryHud;

// View mode constants for C#
window.ViewMode = ViewMode;