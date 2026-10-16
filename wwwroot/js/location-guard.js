/*
 * Kunci lokasi (FTW & Gym) - sisi browser
 * - Membaca posisi GPS HP (navigator.geolocation), mengisi input tersembunyi geoLat/geoLng/geoAcc,
 *   lalu menanyakan ke server (/Location/Check) apakah posisi ini di area site.
 * - Mode "gps": tombol kirim dikunci sampai lokasi lolos. Mode "wifi": GPS hanya dicatat, tidak wajib.
 * - Posisi dibaca ulang otomatis kalau sudah lebih dari 3 menit saat tombol kirim ditekan.
 * - Server tetap mengecek ulang saat form dikirim; script ini hanya supaya karyawan tahu lebih awal.
 * - Halaman bisa melewati kunci untuk pilihan tertentu (mis. jenis aktivitas tanpa kunci lokasi):
 *   window.AKP_LOCATION_SKIP(true|false) -> kotak lokasi disembunyikan & tombol tidak dikunci.
 *   <form data-submit-hold="1"> -> tombol tetap terkunci apa pun hasil lokasinya (diatur halaman).
 * Catatan: lokasi browser hanya jalan di HTTPS (lewat Cloudflare) atau http://localhost.
 */
(function () {
    'use strict';

    const box = document.getElementById('locationStatus');
    if (!box) return;                                   // mode off: kunci lokasi belum aktif
    const form = box.closest('form');
    if (!form) return;

    const mode = box.dataset.mode;                      // 'gps' | 'wifi'
    const checkUrl = box.dataset.checkUrl;
    const $ = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const submitButtons = () => form.querySelectorAll('button[type="submit"], input[type="submit"]');
    const MAX_AGE_MS = 3 * 60 * 1000;

    let fixAt = 0;           // waktu posisi terakhir terbaca
    let allowed = mode === 'wifi';
    let reading = false;
    let submitAfterRead = false;

    function show(kind, icon, html, retry) {
        box.classList.remove('alert-secondary', 'alert-success', 'alert-danger', 'alert-warning');
        box.classList.add('alert-' + kind);
        $('locationIcon').textContent = icon;
        $('locationText').innerHTML = html;
        $('locationRetry').classList.toggle('d-none', !retry);
    }

    const skip = () => form.dataset.locationSkip === '1';
    // data-submit-hold="1": halaman sendiri menahan tombol (mis. jenis aktivitas belum dipilih)
    const held = () => form.dataset.submitHold === '1';

    function lock(locked) {
        if (skip()) locked = false;
        if (held()) locked = true;
        submitButtons().forEach((b) => {
            b.disabled = locked;
            b.classList.toggle('disabled', locked);
        });
    }

    function setInputs(pos) {
        $('geoLat').value = pos ? pos.coords.latitude.toFixed(6) : '';
        $('geoLng').value = pos ? pos.coords.longitude.toFixed(6) : '';
        $('geoAcc').value = pos ? Math.round(pos.coords.accuracy) : '';
    }

    function errorText(err) {
        switch (err && err.code) {
            case 1: return 'Izin lokasi ditolak. Buka pengaturan browser &rsaquo; Izin situs &rsaquo; <strong>Lokasi</strong> &rsaquo; Izinkan, lalu tekan Coba Lagi.';
            case 2: return 'Lokasi tidak terbaca. Pastikan <strong>GPS / Lokasi HP aktif</strong>, lalu tekan Coba Lagi.';
            case 3: return 'Waktu membaca lokasi habis. Pindah ke tempat terbuka / dekat jendela, lalu tekan Coba Lagi.';
            default: return 'Browser ini tidak bisa membaca lokasi.';
        }
    }

    async function verify() {
        const params = new URLSearchParams({ lat: $('geoLat').value, lng: $('geoLng').value, acc: $('geoAcc').value });
        const res = await fetch(`${checkUrl}?${params}`, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } });
        if (res.redirected || res.status === 401 || res.status === 403) {
            window.location.reload();   // sesi kiosk habis
            return null;
        }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
    }

    function done(ok) {
        reading = false;
        allowed = ok || mode === 'wifi';
        lock(!allowed);
        if (allowed && submitAfterRead) {
            submitAfterRead = false;
            form.requestSubmit ? form.requestSubmit() : form.submit();
        }
        submitAfterRead = false;
    }

    function read() {
        if (reading) return;
        reading = true;
        if (mode === 'gps') lock(true);

        if (!('geolocation' in navigator)) {
            if (mode === 'gps') show('danger', '⛔', errorText(null), false);
            done(false);
            return;
        }

        if (mode === 'gps') show('secondary', '📍', '<span class="spinner-border spinner-border-sm me-1"></span> Membaca lokasi Anda…', false);

        navigator.geolocation.getCurrentPosition(async (pos) => {
            setInputs(pos);
            fixAt = Date.now();
            if (mode === 'wifi') {                       // Wi-Fi site: cukup dicatat
                done(true);
                return;
            }
            try {
                const r = await verify();
                if (!r) return;
                if (r.allowed) {
                    show('success', '✅', esc(r.message), false);
                    done(true);
                } else {
                    show('danger', '⛔', esc(r.message), true);
                    done(false);
                }
            } catch (e) {
                console.error('[AKP] Cek lokasi gagal', e);
                // Server tetap mengecek saat dikirim, jadi jangan kunci karyawan karena jaringan sesaat
                show('warning', '⚠️', 'Lokasi terbaca, tetapi gagal dicek ke server. Lokasi akan dicek ulang saat dikirim.', true);
                done(true);
            }
        }, (err) => {
            setInputs(null);
            if (mode === 'gps') show('danger', '⛔', errorText(err), true);
            done(false);
        }, { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 });
    }

    // Posisi basi (> 3 menit) dibaca ulang dulu sebelum form benar-benar dikirim
    form.addEventListener('submit', (e) => {
        if (mode !== 'gps' || skip()) return;
        if (!allowed || Date.now() - fixAt > MAX_AGE_MS) {
            e.preventDefault();
            e.stopImmediatePropagation();
            submitAfterRead = allowed;   // kirim otomatis setelah lokasi terbaca ulang & lolos
            read();
        }
    }, true);

    window.AKP_LOCATION_SKIP = (value) => {
        form.dataset.locationSkip = value ? '1' : '0';
        box.classList.toggle('d-none', !!value);
        lock(!allowed);
    };

    $('locationRetry').addEventListener('click', read);
    read();
})();
