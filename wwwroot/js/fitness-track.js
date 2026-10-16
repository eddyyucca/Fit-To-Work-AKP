/*
 * Logika jarak GPS aktivitas kebugaran - SAMA PERSIS dengan Services/FitnessTrack.cs (server)
 * supaya jarak yang tampil di HP saat merekam = jarak yang tersimpan.
 * Kalau aturan di sini diubah, ubah juga di FitnessTrack.cs (dan sebaliknya).
 *
 * Titik: [lat, lng, akurasiMeter, unixMs]
 */
(function (root) {
    'use strict';

    const MAX_ACCURACY = 50;     // titik dengan akurasi lebih buruk dari ini dibuang
    const MIN_STEP = 5;          // perpindahan < max(5 m, 0.75 x akurasi) diabaikan (getaran GPS saat diam)
    const MIN_STEP_FACTOR = 0.75;
    const SMOOTH_POINTS = 3;     // posisi = rata-rata maks. 3 titik terakhir...
    const SMOOTH_WINDOW_MS = 10000; // ...dalam 10 detik
    const GAP_SECONDS = 60;      // jeda antar titik > 60 detik = GPS sempat berhenti
    const REANCHOR_AFTER = 5;    // 5 lonjakan berturut-turut -> acuan dipindah ke titik terbaru
    const DEFAULT_MAX_SPEED = 50;

    function distance(lat1, lng1, lat2, lng2) {
        const rad = (d) => d * Math.PI / 180;
        const dLat = rad(lat2 - lat1);
        const dLng = rad(lng2 - lng1);
        const a = Math.sin(dLat / 2) * Math.sin(dLat / 2)
            + Math.cos(rad(lat1)) * Math.cos(rad(lat2)) * Math.sin(dLng / 2) * Math.sin(dLng / 2);
        return 2 * 6371000 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    /** points harus urut waktu. Hasil: { distance, used, dropped, gapSeconds, accepted } */
    function compute(points, maxSpeedKmh) {
        const maxSpeed = maxSpeedKmh > 0 ? maxSpeedKmh : DEFAULT_MAX_SPEED;
        const accepted = [];
        let buffer = [];
        let dist = 0, gap = 0, dropped = 0, rejectRun = 0;
        let last = null, prevT = null;

        for (const p of points) {
            const [, , acc, t] = p;
            if (acc > MAX_ACCURACY) { dropped++; continue; }

            if (prevT !== null && (t - prevT) / 1000 > GAP_SECONDS) gap += (t - prevT) / 1000;
            prevT = t;

            // Posisi dihaluskan: rata-rata maks. 3 titik terakhir dalam 10 detik
            buffer.push(p);
            buffer = buffer.filter((q) => t - q[3] <= SMOOTH_WINDOW_MS);
            if (buffer.length > SMOOTH_POINTS) buffer = buffer.slice(buffer.length - SMOOTH_POINTS);
            const sm = [avg(buffer, 0), avg(buffer, 1), acc, t];

            if (!last) { last = sm; accepted.push(sm); continue; }

            const dt = (t - last[3]) / 1000;
            if (dt <= 0) { buffer.pop(); continue; }

            const d = distance(last[0], last[1], sm[0], sm[1]);
            if (d < Math.max(MIN_STEP, acc * MIN_STEP_FACTOR)) { rejectRun = 0; continue; }

            if (d / dt * 3.6 > maxSpeed) {
                dropped++;
                buffer.pop();
                if (++rejectRun >= REANCHOR_AFTER) {
                    buffer = [p];
                    last = p;
                    accepted.push(p);
                    rejectRun = 0;
                }
                continue;
            }

            rejectRun = 0;
            dist += d;
            last = sm;
            accepted.push(sm);
        }

        return { distance: Math.round(dist), used: accepted.length, dropped, gapSeconds: Math.round(gap), accepted };
    }

    // Rata-rata berurutan (sama dengan Enumerable.Average di C#)
    function avg(arr, i) {
        let sum = 0;
        for (const q of arr) sum += q[i];
        return sum / arr.length;
    }

    /** Encoded polyline (format Google, 5 desimal) -> [[lat, lng], ...] */
    function decodePolyline(str) {
        const out = [];
        if (!str) return out;
        let index = 0, lat = 0, lng = 0;
        const next = () => {
            let result = 0, shift = 0, b;
            do {
                if (index >= str.length) return null;
                b = str.charCodeAt(index++) - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            return (result & 1) ? ~(result >> 1) : (result >> 1);
        };
        while (index < str.length) {
            const dLat = next();
            const dLng = next();
            if (dLat === null || dLng === null) break;
            lat += dLat;
            lng += dLng;
            out.push([lat / 1e5, lng / 1e5]);
        }
        return out;
    }

    function formatDistance(m) {
        if (m == null) return '-';
        m = Math.max(0, Math.round(m));
        return m < 1000 ? `${m} m` : `${(m / 1000).toFixed(2)} km`;
    }

    /** detik & meter -> "6:05 /km" (null kalau jarak < 100 m) */
    function formatPace(seconds, meters) {
        if (!seconds || !meters || meters < 100) return null;
        const perKm = Math.round(seconds / (meters / 1000));
        if (perKm > 99 * 60) return null;
        return `${Math.floor(perKm / 60)}:${String(perKm % 60).padStart(2, '0')} /km`;
    }

    function formatElapsed(seconds) {
        seconds = Math.max(0, Math.floor(seconds));
        const h = Math.floor(seconds / 3600), m = Math.floor((seconds % 3600) / 60), s = seconds % 60;
        const mm = String(m).padStart(2, '0'), ss = String(s).padStart(2, '0');
        return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
    }

    const api = { MAX_ACCURACY, GAP_SECONDS, distance, compute, decodePolyline, formatDistance, formatPace, formatElapsed };
    root.AKP_TRACK = api;
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
