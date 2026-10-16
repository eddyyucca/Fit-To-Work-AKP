/*
 * Halaman aktivitas berjalan (Views/Gym/CheckOut.cshtml)
 * - Jam berjalan sejak mulai (dihitung dari server, tidak bergantung jam HP)
 * - Aktivitas ber-GPS: rekam titik GPS (watchPosition), simpan di localStorage (aman kalau halaman dimuat ulang),
 *   tampilkan jarak & pace langsung (logika sama dengan server: fitness-track.js), jaga layar tetap menyala (Wake Lock)
 * - Tombol Selesai -> konfirmasi -> titik GPS dikirim di input tersembunyi "track"; server menghitung ulang jaraknya
 * Konfigurasi: window.AKP_ACTIVITY = { sessionId, elapsedSeconds, usesGps, maxSpeedKmh }
 */
(function () {
    'use strict';

    const cfg = window.AKP_ACTIVITY;
    const T = window.AKP_TRACK;
    if (!cfg || !T) return;

    const $ = (id) => document.getElementById(id);
    const MIN_INTERVAL_MS = 3000;     // maksimal 1 titik per 3 detik
    const MAX_POINTS = 15000;         // sama dengan FitnessTrack.MaxPoints
    const KEY_PREFIX = 'akp-track-';
    const key = KEY_PREFIX + cfg.sessionId;

    // ---------------- Jam berjalan ----------------
    const startedAt = Date.now() - Number(cfg.elapsedSeconds || 0) * 1000;
    const elapsedSec = () => (Date.now() - startedAt) / 1000;
    const elElapsed = $('actElapsed');

    // ---------------- Penyimpanan titik (localStorage boleh gagal: mode privat / penyimpanan penuh) ----------------
    const store = {
        load() {
            try {
                const raw = window.localStorage.getItem(key);
                const arr = raw ? JSON.parse(raw) : [];
                return Array.isArray(arr) ? arr.filter((p) => Array.isArray(p) && p.length >= 4) : [];
            } catch (e) { return []; }
        },
        save(points) {
            try { window.localStorage.setItem(key, JSON.stringify(points)); return true; } catch (e) { return false; }
        },
        cleanupOthers() {
            try {
                const stale = [];
                for (let i = 0; i < window.localStorage.length; i++) {
                    const k = window.localStorage.key(i);
                    if (k && k.startsWith(KEY_PREFIX) && k !== key) stale.push(k);
                }
                stale.forEach((k) => window.localStorage.removeItem(k));
            } catch (e) { /* abaikan */ }
        }
    };

    let points = [];
    let lastStat = { distance: 0, gapSeconds: 0, used: 0 };
    let lastFixAt = 0;
    let lastAcc = null;
    let unsaved = 0;
    let lastSaveAt = 0;

    function recompute() {
        lastStat = T.compute(points, cfg.maxSpeedKmh);
        const dEl = $('actDistance');
        if (dEl) dEl.textContent = T.formatDistance(lastStat.distance);
        const pEl = $('actPace');
        if (pEl) pEl.textContent = T.formatPace(elapsedSec(), lastStat.distance) || '-';
        const cEl = $('confirmDistance');
        if (cEl) cEl.textContent = T.formatDistance(lastStat.distance);
    }

    function setGps(kind, text) {
        const el = $('gpsStatus');
        if (!el) return;
        const cls = { ok: 'alert-success', wait: 'alert-warning', bad: 'alert-danger' }[kind] || 'alert-secondary';
        el.className = `alert ${cls} py-2 small mb-2`;
        el.textContent = text;
    }

    function persist(force) {
        const now = Date.now();
        if (!force && unsaved < 5 && now - lastSaveAt < 15000) return;
        if (!store.save(points)) {
            const w = $('storageWarn');
            if (w) w.classList.remove('d-none');
        }
        unsaved = 0;
        lastSaveAt = now;
    }

    function onPosition(pos) {
        const c = pos.coords;
        const t = Number(pos.timestamp) || Date.now();
        lastFixAt = Date.now();
        lastAcc = c.accuracy;

        if (!(c.accuracy <= T.MAX_ACCURACY)) {
            setGps('wait', `Sinyal GPS lemah (±${Math.round(c.accuracy)} m). Menunggu sinyal lebih baik… Usahakan di tempat terbuka.`);
            return;
        }

        const last = points[points.length - 1];
        if (last && t - last[3] < MIN_INTERVAL_MS) {
            setGps('ok', `GPS aktif · akurasi ±${Math.round(c.accuracy)} m · ${points.length} titik`);
            return;
        }
        if (points.length >= MAX_POINTS) {
            setGps('wait', 'Batas rekaman tercapai. Silakan selesaikan aktivitas.');
            return;
        }

        points.push([
            Math.round(c.latitude * 1e6) / 1e6,
            Math.round(c.longitude * 1e6) / 1e6,
            Math.round(c.accuracy * 10) / 10,
            t
        ]);
        unsaved++;
        persist(false);
        recompute();
        setGps('ok', `GPS aktif · akurasi ±${Math.round(c.accuracy)} m · ${points.length} titik`);
    }

    function onError(err) {
        if (err && err.code === 1) {
            setGps('bad', 'Izin lokasi ditolak. Izinkan akses lokasi untuk situs ini di pengaturan browser, lalu muat ulang halaman. ' +
                'Aktivitas tetap bisa diselesaikan, tetapi jarak tidak tercatat.');
        } else {
            setGps('wait', 'Belum mendapat sinyal GPS. Pastikan GPS / Lokasi HP menyala…');
        }
    }

    // ---------------- Layar tetap menyala ----------------
    let wakeLock = null;
    async function requestWakeLock() {
        const info = $('wakeInfo');
        if (!('wakeLock' in navigator)) {
            if (info) {
                info.className = 'alert alert-warning py-2 small mb-2';
                info.innerHTML = '<i class="bi bi-exclamation-triangle me-1"></i>HP ini tidak bisa menjaga layar tetap menyala otomatis. ' +
                    '<strong>Jangan kunci layar</strong> dan perpanjang waktu layar mati di pengaturan HP, supaya GPS tetap merekam.';
            }
            return;
        }
        try {
            wakeLock = await navigator.wakeLock.request('screen');
            if (info) {
                info.className = 'alert alert-info py-2 small mb-2';
                info.innerHTML = '<i class="bi bi-brightness-high me-1"></i>Layar dijaga tetap menyala selama merekam. ' +
                    '<strong>Jangan kunci layar / pindah aplikasi</strong>, GPS berhenti kalau browser tidak tampil.';
            }
            wakeLock.addEventListener('release', () => { wakeLock = null; });
        } catch (e) {
            if (info) {
                info.className = 'alert alert-warning py-2 small mb-2';
                info.textContent = 'Layar tidak bisa dijaga tetap menyala (mode hemat baterai?). Jangan kunci layar selama merekam.';
            }
        }
    }

    let hiddenAt = null;
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
            hiddenAt = Date.now();
            persist(true);
            return;
        }
        if (cfg.usesGps && !wakeLock) requestWakeLock();
        if (hiddenAt && Date.now() - hiddenAt > T.GAP_SECONDS * 1000) {
            const gap = $('gapInfo');
            if (gap) {
                const menit = Math.max(1, Math.round((Date.now() - hiddenAt) / 60000));
                gap.textContent = `GPS sempat berhenti ±${menit} menit karena layar terkunci / aplikasi lain dibuka. ` +
                    'Jarak di jeda itu dihitung garis lurus.';
                gap.classList.remove('d-none');
            }
        }
        hiddenAt = null;
    });

    // ---------------- Tombol Selesai (konfirmasi tanpa dialog browser) ----------------
    const form = $('finishForm');
    const btnAsk = $('btnAskFinish');
    const panel = $('confirmPanel');
    const btnCancel = $('btnCancelFinish');

    if (btnAsk && panel) {
        btnAsk.addEventListener('click', () => {
            recompute();
            panel.classList.remove('d-none');
            btnAsk.classList.add('d-none');
        });
    }
    if (btnCancel && panel) {
        btnCancel.addEventListener('click', () => {
            panel.classList.add('d-none');
            btnAsk.classList.remove('d-none');
        });
    }
    if (form) {
        form.addEventListener('submit', () => {
            if (cfg.usesGps) {
                persist(true);
                const input = $('trackInput');
                if (input) input.value = JSON.stringify(points);
            }
            setTimeout(() => {
                form.querySelectorAll('button[type=submit]').forEach((b) => { b.disabled = true; });
            }, 0);
        });
    }

    // ---------------- Mulai ----------------
    function tick() {
        if (elElapsed) elElapsed.textContent = T.formatElapsed(elapsedSec());
        if (cfg.usesGps) {
            const pEl = $('actPace');
            if (pEl) pEl.textContent = T.formatPace(elapsedSec(), lastStat.distance) || '-';
            if (lastFixAt && Date.now() - lastFixAt > 30000) {
                setGps('wait', 'Sinyal GPS hilang lebih dari 30 detik. Pastikan layar tetap menyala dan GPS aktif…');
            }
        }
    }
    tick();
    setInterval(tick, 1000);

    if (cfg.usesGps) {
        store.cleanupOthers();
        points = store.load().sort((a, b) => a[3] - b[3]);
        recompute();
        if (!('geolocation' in navigator)) {
            setGps('bad', 'Browser ini tidak mendukung GPS. Aktivitas tetap bisa diselesaikan, tetapi jarak tidak tercatat.');
        } else if (!window.isSecureContext) {
            setGps('bad', 'GPS hanya bisa dipakai lewat alamat https://. Jarak tidak tercatat.');
        } else {
            setGps('wait', points.length ? `Melanjutkan rekaman (${points.length} titik tersimpan). Mencari sinyal GPS…` : 'Mencari sinyal GPS…');
            navigator.geolocation.watchPosition(onPosition, onError, { enableHighAccuracy: true, maximumAge: 0, timeout: 30000 });
        }
        requestWakeLock();
        window.addEventListener('pagehide', () => persist(true));
    }

    // Untuk pengujian
    window.AKP_ACTIVITY_STATE = () => ({ points: points.slice(), stat: lastStat, lastAcc });
})();
