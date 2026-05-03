/**
 * FjordWatch Leaflet bridge.
 *
 * Each map instance is keyed by elementId. The Blazor component owns the
 * lifecycle: calls createMap on first render, addOrUpdateVessel for each
 * incoming position, drawTrack on selection, and disposeMap on unmount.
 */
(function () {
    const maps = new Map(); // elementId -> { map, vesselLayer, trackLayer, markers, dotnetRef }

    const categoryColors = {
        Cargo: '#0d6efd',
        Tanker: '#dc3545',
        Fishing: '#198754',
        Passenger: '#fd7e14',
        HighSpeed: '#6f42c1',
        Tug: '#6c757d',
        Pleasure: '#20c997',
        Sailing: '#0dcaf0',
        Military: '#000000',
        SearchAndRescue: '#ffc107',
        Other: '#adb5bd',
        Unknown: '#adb5bd',
    };

    function colorFor(category) {
        return categoryColors[category] ?? categoryColors.Unknown;
    }

    window.fjordwatchMap = {
        createMap: function (elementId, dotnetRef, initialCenter, initialZoom) {
            if (maps.has(elementId)) {
                return;
            }
            const map = L.map(elementId, { preferCanvas: true }).setView(
                [initialCenter.latitude, initialCenter.longitude],
                initialZoom);

            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                attribution: '© OpenStreetMap contributors',
                maxZoom: 18,
            }).addTo(map);

            L.tileLayer('https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png', {
                attribution: '© OpenSeaMap contributors',
                maxZoom: 18,
            }).addTo(map);

            const vesselLayer = L.layerGroup().addTo(map);
            const trackLayer = L.layerGroup().addTo(map);

            const reportViewport = () => {
                const bounds = map.getBounds();
                dotnetRef.invokeMethodAsync('OnViewportChanged',
                    bounds.getWest(), bounds.getSouth(),
                    bounds.getEast(), bounds.getNorth(),
                    map.getZoom());
            };

            map.on('moveend', reportViewport);
            map.on('zoomend', reportViewport);
            // Fire once on init so the API gets the initial viewport.
            setTimeout(reportViewport, 0);

            maps.set(elementId, {
                map,
                vesselLayer,
                trackLayer,
                markers: new Map(),
                dotnetRef,
            });
        },

        addOrUpdateVessel: function (elementId, vessel) {
            const handle = maps.get(elementId);
            if (!handle) return;
            const existing = handle.markers.get(vessel.mmsi);
            const color = colorFor(vessel.category);
            const latlng = [vessel.latitude, vessel.longitude];

            if (existing) {
                existing.setLatLng(latlng);
                existing.setStyle({ color, fillColor: color });
                existing.bindTooltip(vessel.label ?? String(vessel.mmsi), { sticky: true });
            } else {
                const marker = L.circleMarker(latlng, {
                    radius: 5,
                    color,
                    fillColor: color,
                    fillOpacity: 0.8,
                    weight: 1,
                });
                marker.on('click', () => {
                    handle.dotnetRef.invokeMethodAsync('OnVesselClicked', vessel.mmsi);
                });
                marker.bindTooltip(vessel.label ?? String(vessel.mmsi), { sticky: true });
                marker.addTo(handle.vesselLayer);
                handle.markers.set(vessel.mmsi, marker);
            }
        },

        addOrUpdateVessels: function (elementId, vessels) {
            for (const v of vessels) {
                this.addOrUpdateVessel(elementId, v);
            }
        },

        drawTrack: function (elementId, geoJsonLineString) {
            const handle = maps.get(elementId);
            if (!handle) return;
            handle.trackLayer.clearLayers();
            if (!geoJsonLineString || !geoJsonLineString.geometry || !geoJsonLineString.geometry.coordinates.length) {
                return;
            }
            const latlngs = geoJsonLineString.geometry.coordinates.map(c => [c[1], c[0]]);
            const line = L.polyline(latlngs, { color: '#212529', weight: 2, opacity: 0.85 });
            line.addTo(handle.trackLayer);
            handle.map.fitBounds(line.getBounds(), { padding: [40, 40], maxZoom: 12 });
        },

        clearTrack: function (elementId) {
            const handle = maps.get(elementId);
            if (handle) {
                handle.trackLayer.clearLayers();
            }
        },

        clearVessels: function (elementId) {
            const handle = maps.get(elementId);
            if (handle) {
                handle.vesselLayer.clearLayers();
                handle.markers.clear();
            }
        },

        disposeMap: function (elementId) {
            const handle = maps.get(elementId);
            if (handle) {
                handle.map.remove();
                maps.delete(elementId);
            }
        },
    };
})();
