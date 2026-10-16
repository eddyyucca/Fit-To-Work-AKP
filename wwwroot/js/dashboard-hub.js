/*
 * Koneksi realtime (SignalR) bersama untuk Dashboard AKP Sehat.
 * - Satu koneksi dipakai bagian Fit To Work, Klinik dan Gym.
 * - Server hanya mengirim event ke bagian yang boleh dilihat role admin ini.
 *   FtwRegistered  : ada submit Fit To Work (membawa detail untuk toast)
 *   GymChanged     : ada check-in gym ({ nama, jam })
 *   ClinicChanged  : kunjungan klinik baru / dibatalkan
 *   DashboardRefresh : data karyawan berubah / data direset -> semua bagian muat ulang
 * - Pakai: AKP_HUB.on('GymChanged', fn); AKP_HUB.onReconnected(fn)
 * - Status koneksi tampil di elemen [data-akp-live] (titik hijau = realtime aktif).
 */
(function () {
    'use strict';

    const url = window.AKP_HUB_URL;
    const handlers = {};
    const reconnectHandlers = [];

    // API tetap tersedia walau SignalR gagal dimuat, supaya script dashboard lain tidak error
    window.AKP_HUB = {
        on(eventName, fn) { (handlers[eventName] = handlers[eventName] || []).push(fn); },
        onReconnected(fn) { reconnectHandlers.push(fn); },
        connected: false
    };

    function setLive(state, text) {
        document.querySelectorAll('[data-akp-live]').forEach((el) => {
            const dot = el.querySelector('.ftw-live-dot');
            if (dot) {
                dot.classList.remove('on', 'off');
                if (state) dot.classList.add(state);
            }
            const t = el.querySelector('[data-akp-live-text]');
            if (t) t.textContent = text;
        });
        window.AKP_HUB.connected = state === 'on';
    }

    if (!url || !window.signalR) {
        setLive('off', 'Realtime tidak tersedia');
        return;
    }

    const conn = new signalR.HubConnectionBuilder()
        .withUrl(url)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    ['FtwRegistered', 'GymChanged', 'ClinicChanged', 'DashboardRefresh'].forEach((name) => {
        conn.on(name, (payload) => (handlers[name] || []).forEach((fn) => {
            try { fn(payload); } catch (e) { console.error('[AKP] handler ' + name, e); }
        }));
    });

    // Setelah tersambung lagi, data yang terlewat selama putus diambil ulang
    const afterReconnect = () => reconnectHandlers.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });

    conn.onreconnecting(() => setLive('off', 'Menyambung ulang…'));
    conn.onreconnected(() => { setLive('on', 'Realtime aktif'); afterReconnect(); });
    conn.onclose(() => {
        setLive('off', 'Realtime terputus');
        setTimeout(start, 10000);   // coba terus
    });

    let everConnected = false;
    async function start() {
        try {
            await conn.start();
            setLive('on', 'Realtime aktif');
            if (everConnected) afterReconnect();
            everConnected = true;
        } catch (err) {
            console.warn('[AKP] SignalR gagal connect, coba lagi 10 detik', err);
            setLive('off', 'Realtime terputus');
            setTimeout(start, 10000);
        }
    }

    setLive(null, 'Menghubungkan…');
    // Mulai setelah semua script dashboard selesai mendaftarkan handler
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => setTimeout(start, 0));
    else setTimeout(start, 0);
})();
