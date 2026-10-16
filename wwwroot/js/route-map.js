/*
 * Peta rute aktivitas kebugaran (Leaflet 1.9.4 + fitness-track.js untuk decode polyline)
 * - AKP_ROUTE.render(el, polyline)          : gambar rute di elemen (dipakai halaman selesai aktivitas)
 * - AKP_ROUTE.bindModal(urlTemplate)        : klik elemen [data-route-id] -> ambil JSON rute -> tampil di #routeModal
 *   urlTemplate berisi "__ID__", mis. "/Admin/Gym/Route/__ID__"
 *   JSON: { judul, sub, polyline, ringkasan: [[label, nilai], ...], catatan }
 */
(function () {
    'use strict';

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    function render(el, polyline) {
        if (!el) return null;
        const pts = window.AKP_TRACK ? window.AKP_TRACK.decodePolyline(polyline) : [];
        if (!window.L || pts.length < 2) {
            el.innerHTML = '<div class="h-100 d-flex align-items-center justify-content-center text-muted small p-3 text-center">' +
                (pts.length < 2 ? 'Rute GPS tidak tersedia.' : 'Peta tidak dapat dimuat (periksa koneksi internet).') + '</div>';
            return null;
        }
        if (el._akpMap) { el._akpMap.remove(); el._akpMap = null; }
        el.innerHTML = '';

        const map = L.map(el, { zoomControl: true, attributionControl: true });
        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; OpenStreetMap'
        }).addTo(map);

        const line = L.polyline(pts, { color: '#1a8fd8', weight: 5, opacity: .9 }).addTo(map);
        L.circleMarker(pts[0], { radius: 7, color: '#fff', weight: 2, fillColor: '#4caf50', fillOpacity: 1 })
            .bindTooltip('Mulai').addTo(map);
        L.circleMarker(pts[pts.length - 1], { radius: 7, color: '#fff', weight: 2, fillColor: '#dc3545', fillOpacity: 1 })
            .bindTooltip('Selesai').addTo(map);
        map.fitBounds(line.getBounds(), { padding: [20, 20] });
        el._akpMap = map;
        return map;
    }

    function bindModal(urlTemplate) {
        const modalEl = document.getElementById('routeModal');
        if (!modalEl || !window.bootstrap) return;
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        const mapEl = document.getElementById('routeMap');
        let pending = null;

        // Leaflet perlu ukuran elemen yang sudah tampil -> gambar setelah modal terbuka penuh
        modalEl.addEventListener('shown.bs.modal', () => {
            if (pending) {
                render(mapEl, pending.polyline);
                pending = null;
            } else if (mapEl._akpMap) {
                mapEl._akpMap.invalidateSize();
            }
        });

        document.addEventListener('click', async (ev) => {
            const btn = ev.target.closest('[data-route-id]');
            if (!btn) return;
            ev.preventDefault();
            const id = encodeURIComponent(btn.getAttribute('data-route-id'));

            document.getElementById('routeTitle').textContent = 'Memuat rute…';
            document.getElementById('routeSub').textContent = '';
            document.getElementById('routeStats').innerHTML = '';
            document.getElementById('routeNote').textContent = '';
            mapEl.innerHTML = '<div class="h-100 d-flex align-items-center justify-content-center"><div class="spinner-border text-primary"></div></div>';
            modal.show();

            try {
                const res = await fetch(urlTemplate.replace('__ID__', id), { headers: { 'Accept': 'application/json' } });
                if (!res.ok || !(res.headers.get('content-type') || '').includes('json')) throw new Error(String(res.status));
                const d = await res.json();
                document.getElementById('routeTitle').textContent = d.judul || 'Rute aktivitas';
                document.getElementById('routeSub').textContent = d.sub || '';
                document.getElementById('routeStats').innerHTML = (d.ringkasan || []).map(([label, value]) => `
                    <div class="col-6 col-md-3">
                        <div class="border rounded p-2 h-100">
                            <div class="small text-muted">${esc(label)}</div>
                            <div class="fw-semibold">${esc(value)}</div>
                        </div>
                    </div>`).join('');
                document.getElementById('routeNote').textContent = d.catatan || '';
                if (modalEl.classList.contains('show')) render(mapEl, d.polyline);
                else pending = d;
            } catch (e) {
                document.getElementById('routeTitle').textContent = 'Rute tidak dapat dimuat';
                mapEl.innerHTML = '<div class="h-100 d-flex align-items-center justify-content-center text-muted small">Coba lagi beberapa saat.</div>';
            }
        });
    }

    window.AKP_ROUTE = { render, bindModal };
})();
