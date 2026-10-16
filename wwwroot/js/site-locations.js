/*
 * Lokasi Site (admin) - peta Leaflet
 * - mode 'overview' (Index): semua lokasi site + lingkaran radius.
 * - mode 'edit' (Tambah/Ubah): klik peta / geser penanda untuk memilih titik, radius ikut berubah,
 *   tempel koordinat dari Google Maps, "Lokasi saya", "IP saya". Lokasi lain tampil samar (abu-abu).
 * Peta dasar: citra satelit (Esri) & OpenStreetMap, bisa dipilih di pojok kanan atas.
 */
(function () {
    'use strict';

    const cfg = window.AKP_SITES || { sites: [] };
    const mapEl = document.getElementById('siteMap');
    if (!mapEl || !window.L) return;

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const COLOR = { active: '#1a8fd8', inactive: '#6c757d', edit: '#22b3a4' };

    L.Icon.Default.imagePath = 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/';

    const satelit = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
        maxZoom: 19, attribution: 'Citra &copy; Esri'
    });
    const jalan = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19, attribution: '&copy; OpenStreetMap contributors'
    });

    const map = L.map(mapEl, { layers: [satelit] }).setView([-1.5, 117], 5);   // default: Kalimantan
    L.control.layers({ 'Satelit': satelit, 'Peta jalan': jalan }).addTo(map);
    L.control.scale({ imperial: false }).addTo(map);

    // Lokasi yang sudah terdaftar
    const bounds = [];
    (cfg.sites || []).forEach((s) => {
        const faded = cfg.mode === 'edit';
        const color = faded ? COLOR.inactive : (s.aktif ? COLOR.active : COLOR.inactive);
        const circle = L.circle([s.lat, s.lng], {
            radius: s.radius, color, weight: 2, fillOpacity: faded ? 0.08 : 0.15, dashArray: s.aktif ? null : '6 6'
        }).addTo(map);
        circle.bindTooltip(`${esc(s.nama)}${s.aktif ? '' : ' (nonaktif)'} · radius ${s.radius} m`);
        if (!faded) L.circleMarker([s.lat, s.lng], { radius: 4, color, fillOpacity: 1 }).addTo(map);
        bounds.push(circle.getBounds());
    });

    if (cfg.mode !== 'edit') {
        if (bounds.length) map.fitBounds(bounds.reduce((a, b) => a.extend(b)), { padding: [30, 30], maxZoom: 16 });
        return;
    }

    // ======================= Mode edit =======================
    const $ = (id) => document.getElementById(id);
    const latInput = $('Latitude');
    const lngInput = $('Longitude');
    const radiusInput = $('RadiusMeter');
    const radiusRange = $('radiusRange');

    let marker = null;
    let circle = null;

    const currentRadius = () => {
        const r = parseInt(radiusInput.value, 10);
        return Number.isFinite(r) && r > 0 ? r : 500;
    };

    function setPoint(lat, lng, pan) {
        lat = Math.round(lat * 1e6) / 1e6;
        lng = Math.round(lng * 1e6) / 1e6;
        latInput.value = lat.toFixed(6);
        lngInput.value = lng.toFixed(6);

        if (!marker) {
            marker = L.marker([lat, lng], { draggable: true, autoPan: true }).addTo(map);
            marker.on('drag', (e) => {
                const p = e.target.getLatLng();
                circle.setLatLng(p);
            });
            marker.on('dragend', (e) => {
                const p = e.target.getLatLng();
                setPoint(p.lat, p.lng, false);
            });
            circle = L.circle([lat, lng], { radius: currentRadius(), color: COLOR.edit, weight: 2, fillOpacity: 0.18 }).addTo(map);
        } else {
            marker.setLatLng([lat, lng]);
            circle.setLatLng([lat, lng]);
        }
        if (pan) map.fitBounds(circle.getBounds(), { padding: [40, 40], maxZoom: 17 });
    }

    function setRadius(r, fromRange) {
        if (!fromRange) radiusRange.value = Math.min(Math.max(r, 50), 5000);
        else radiusInput.value = r;
        if (circle) circle.setRadius(r);
    }

    map.on('click', (e) => setPoint(e.latlng.lat, e.latlng.lng, false));
    radiusRange.addEventListener('input', () => setRadius(parseInt(radiusRange.value, 10), true));
    radiusInput.addEventListener('input', () => setRadius(currentRadius(), false));

    // Ketik manual lat/long
    [latInput, lngInput].forEach((el) => el.addEventListener('change', () => {
        const lat = parseFloat(latInput.value), lng = parseFloat(lngInput.value);
        if (Number.isFinite(lat) && Number.isFinite(lng)) setPoint(lat, lng, true);
    }));

    // Tempel "-0.502106, 117.153709" dari Google Maps
    function applyPaste() {
        const m = $('pasteCoord').value.match(/(-?\d{1,2}(?:\.\d+)?)\s*[,; ]\s*(-?\d{1,3}(?:\.\d+)?)/);
        if (!m) {
            toastr.warning('Format koordinat tidak dikenali. Contoh: -0.502106, 117.153709');
            return;
        }
        const lat = parseFloat(m[1]), lng = parseFloat(m[2]);
        if (Math.abs(lat) > 90 || Math.abs(lng) > 180) {
            toastr.warning('Koordinat di luar jangkauan.');
            return;
        }
        setPoint(lat, lng, true);
        $('pasteCoord').value = '';
    }
    $('btnPaste').addEventListener('click', applyPaste);
    $('pasteCoord').addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); applyPaste(); }
    });

    // Lokasi browser admin (berguna kalau admin sedang di site)
    $('btnMyLocation').addEventListener('click', () => {
        if (!('geolocation' in navigator)) {
            toastr.error('Browser ini tidak bisa membaca lokasi.');
            return;
        }
        toastr.info('Membaca lokasi Anda…', null, { timeOut: 2000 });
        navigator.geolocation.getCurrentPosition(
            (pos) => {
                setPoint(pos.coords.latitude, pos.coords.longitude, true);
                toastr.success(`Lokasi terbaca (akurasi ±${Math.round(pos.coords.accuracy)} m).`);
            },
            () => toastr.error('Lokasi tidak bisa dibaca. Pastikan izin lokasi browser diaktifkan.'),
            { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 });
    });

    const btnMyIp = $('btnMyIp');
    if (btnMyIp) {
        btnMyIp.addEventListener('click', () => {
            const input = $('IpPublik');
            const list = input.value.split(/[,;\s]+/).filter(Boolean);
            if (!list.includes(btnMyIp.dataset.ip)) list.push(btnMyIp.dataset.ip);
            input.value = list.join(', ');
        });
    }

    // Wajib ada titik sebelum simpan
    $('siteForm').addEventListener('submit', (e) => {
        if (!latInput.value || !lngInput.value) {
            e.preventDefault();
            toastr.warning('Klik peta untuk menentukan titik lokasi site terlebih dahulu.');
        }
    });

    // Titik awal: data yang sedang diubah, atau pusatkan ke lokasi lain yang sudah ada
    const lat0 = parseFloat(latInput.value), lng0 = parseFloat(lngInput.value);
    if (Number.isFinite(lat0) && Number.isFinite(lng0)) {
        setPoint(lat0, lng0, true);
        setRadius(currentRadius(), false);
    } else if (bounds.length) {
        map.fitBounds(bounds.reduce((a, b) => a.extend(b)), { padding: [30, 30], maxZoom: 15 });
    }
})();
