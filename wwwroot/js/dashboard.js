/*
 * Dashboard Fit To Work - realtime
 * - Ambil semua data dari /Admin/DashboardData
 * - Dengarkan event "FtwRegistered" dari SignalR hub -> toastr + refresh data
 * - Kalau koneksi realtime putus, tetap refresh otomatis tiap 60 detik (fallback)
 */
(function () {
    'use strict';

    const cfg = window.FTW_DASHBOARD;
    const COLORS = {
        nonShift: '#2a78d6',   // biru
        shiftPagi: '#eb6834',  // oranye
        shiftMalam: '#4a3aa7', // ungu tua (malam)
        total: '#2a78d6',
        sudah: '#1baf7a',
        belum: '#dee2e6',
        grid: 'rgba(0,0,0,.06)',
        text: '#52514e'
    };

    let period = 'day';
    let lastData = null;   // respons terakhir dari DashboardData (dipakai juga oleh export PDF)
    let trendChart, shiftChart, coverageChart;
    let refreshTimer = null;
    let lastIds = new Set();

    // Jam realtime: selisih jam browser vs server, offset zona waktu site, dan tanggal data terakhir
    const clock = { skewMs: 0, offsetMin: null, zona: '', todayKey: null, timer: null };

    Chart.register(ChartDataLabels);
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.color = COLORS.text;
    Chart.defaults.plugins.datalabels.display = false; // nyalakan per chart saja

    // ---------- util ----------
    const $ = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');

    function setText(id, value) {
        const el = $(id);
        if (el && el.textContent !== String(value)) {
            el.textContent = value;
            el.classList.remove('ftw-flash');
            void el.offsetWidth; // restart animasi
            el.classList.add('ftw-flash');
        }
    }

    // ---------- charts ----------
    function buildCharts() {
        trendChart = new Chart($('chartTrend'), {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Total register',
                    data: [],
                    borderColor: COLORS.total,
                    backgroundColor: 'rgba(42,120,214,.10)',
                    borderWidth: 2,
                    pointRadius: 3,
                    pointHoverRadius: 6,
                    pointBackgroundColor: COLORS.total,
                    fill: true,
                    tension: 0.3,
                    cubicInterpolationMode: 'monotone'
                }]
            },
            options: {
                maintainAspectRatio: false,
                layout: { padding: { top: 22 } },
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            title: (items) => 'Tanggal ' + items[0].label,
                            label: (ctx) => ' Total: ' + fmt(ctx.parsed.y) + ' karyawan'
                        }
                    },
                    datalabels: {
                        display: (ctx) => ctx.dataset.data[ctx.dataIndex] > 0,
                        align: 'top',
                        anchor: 'end',
                        offset: 2,
                        color: '#212529',
                        font: { size: 10, weight: '600' },
                        formatter: (v) => fmt(v)
                    }
                },
                scales: {
                    x: { grid: { display: false } },
                    y: { beginAtZero: true, grace: '8%', grid: { color: COLORS.grid }, ticks: { precision: 0 } }
                }
            }
        });

        const shiftDs = (label, color) => ({
            label, data: [], backgroundColor: color,
            borderColor: '#ffffff', borderWidth: { top: 0, right: 0, bottom: 1, left: 0 },
            borderRadius: 3, borderSkipped: false, maxBarThickness: 36
        });

        shiftChart = new Chart($('chartShift'), {
            type: 'bar',
            data: {
                labels: [],
                datasets: [
                    shiftDs('Non Shift', COLORS.nonShift),
                    shiftDs('Shift Pagi', COLORS.shiftPagi),
                    shiftDs('Shift Malam', COLORS.shiftMalam)
                ]
            },
            options: {
                maintainAspectRatio: false,
                layout: { padding: { top: 16 } },
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { position: 'bottom', labels: { usePointStyle: true, pointStyle: 'rectRounded' } },
                    tooltip: {
                        callbacks: {
                            title: (items) => 'Tanggal ' + items[0].label,
                            label: (ctx) => ' ' + ctx.dataset.label + ': ' + fmt(ctx.parsed.y),
                            footer: (items) => 'Total: ' + fmt(items.reduce((a, i) => a + i.parsed.y, 0))
                        }
                    },
                    datalabels: {
                        display: (ctx) => ctx.dataset.data[ctx.dataIndex] > 0,
                        color: '#ffffff',
                        font: { size: 10, weight: '600' },
                        anchor: 'center',
                        align: 'center',
                        clamp: true,
                        formatter: (v) => fmt(v)
                    }
                },
                scales: {
                    x: { stacked: true, grid: { display: false } },
                    y: { stacked: true, beginAtZero: true, grace: '8%', grid: { color: COLORS.grid }, ticks: { precision: 0 } }
                }
            }
        });

        // Teks persentase di tengah doughnut
        const centerText = {
            id: 'centerText',
            afterDraw(chart) {
                const meta = chart.getDatasetMeta(0);
                if (!meta || !meta.data.length) return;
                const { x, y } = meta.data[0];
                const pct = chart.$pct ?? 0;
                const ctx = chart.ctx;
                ctx.save();
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillStyle = '#212529';
                ctx.font = '700 26px ' + Chart.defaults.font.family;
                ctx.fillText(pct.toLocaleString('id-ID') + '%', x, y - 8);
                ctx.fillStyle = '#6c757d';
                ctx.font = '12px ' + Chart.defaults.font.family;
                ctx.fillText('sudah register', x, y + 14);
                ctx.restore();
            }
        };

        coverageChart = new Chart($('chartCoverage'), {
            type: 'doughnut',
            data: {
                labels: ['Sudah register', 'Belum register'],
                datasets: [{
                    data: [0, 0],
                    backgroundColor: [COLORS.sudah, COLORS.belum],
                    borderColor: '#ffffff',
                    borderWidth: 2,
                    hoverOffset: 6
                }]
            },
            options: {
                maintainAspectRatio: false,
                cutout: '66%',
                plugins: {
                    legend: { position: 'bottom', labels: { usePointStyle: true } },
                    tooltip: {
                        callbacks: {
                            label: (ctx) => {
                                const total = ctx.dataset.data.reduce((a, b) => a + b, 0) || 1;
                                return ' ' + ctx.label + ': ' + fmt(ctx.parsed) + ' (' + (ctx.parsed * 100 / total).toFixed(1) + '%)';
                            }
                        }
                    },
                    datalabels: {
                        display: (ctx) => ctx.dataset.data[ctx.dataIndex] > 0,
                        color: (ctx) => ctx.dataIndex === 0 ? '#ffffff' : '#212529',
                        font: { size: 12, weight: '700' },
                        formatter: (v) => fmt(v)
                    }
                }
            },
            plugins: [centerText]
        });
    }

    function setBarMode(stacked) {
        shiftChart.options.scales.x.stacked = stacked;
        shiftChart.options.scales.y.stacked = stacked;
        shiftChart.options.plugins.datalabels.anchor = stacked ? 'center' : 'end';
        shiftChart.options.plugins.datalabels.align = stacked ? 'center' : 'top';
        shiftChart.options.plugins.datalabels.color = stacked ? '#ffffff' : '#212529';
        shiftChart.options.plugins.datalabels.font = { size: stacked ? 10 : 9, weight: '600' };
        shiftChart.update();
    }

    // ---------- jam realtime ----------
    const pad = (n) => String(n).padStart(2, '0');

    // Waktu site sekarang = jam browser + koreksi selisih ke server + offset zona waktu site.
    // Hasilnya dibaca pakai getUTC*() supaya tidak terpengaruh zona waktu komputer admin.
    function siteNow() {
        return new Date(Date.now() + clock.skewMs + clock.offsetMin * 60000);
    }

    function syncClock(data, requestStartedAt) {
        // Perkiraan waktu server saat respons sampai = waktu server + setengah durasi request
        const latency = (Date.now() - requestStartedAt) / 2;
        clock.skewMs = (data.serverTimeUtcMs + latency) - Date.now();
        clock.offsetMin = data.utcOffsetMinutes;
        clock.zona = data.zonaWaktu;
        clock.todayKey = data.todayKey;
        if (!clock.timer) tick();
    }

    function tick() {
        const d = siteNow();
        $('ftwClock').innerHTML =
            `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}<small>${esc(clock.zona)}</small>`;

        // Lewat tengah malam -> ambil data hari yang baru
        const key = `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
        if (clock.todayKey && key !== clock.todayKey) {
            clock.todayKey = key;
            load();
        }

        // Jadwalkan tepat di awal detik berikutnya supaya detiknya tidak loncat/tersendat
        clock.timer = setTimeout(tick, 1000 - (d.getUTCMilliseconds()) + 5);
    }

    // ---------- render ----------
    function renderToday(t, generatedAt) {
        $('ftwTanggal').textContent = t.tanggal;
        $('ftwUpdatedAt').textContent = generatedAt;
        setText('kpiTotal', fmt(t.totalKaryawan));
        setText('kpiSudah', fmt(t.sudahIsi));
        setText('kpiFit', fmt(t.fit));
        setText('kpiTidakFit', fmt(t.tidakFit));
        setText('kpiBelum', fmt(t.belumIsi));
        $('kpiPersen').textContent = t.persen.toLocaleString('id-ID') + '% dari karyawan aktif';
    }

    function renderTrend(trend) {
        const labels = trend.map((d) => d.label);
        trendChart.data.labels = labels;
        trendChart.data.datasets[0].data = trend.map((d) => d.total);
        trendChart.update();

        shiftChart.data.labels = labels;
        shiftChart.data.datasets[0].data = trend.map((d) => d.nonShift);
        shiftChart.data.datasets[1].data = trend.map((d) => d.shiftPagi);
        shiftChart.data.datasets[2].data = trend.map((d) => d.shiftMalam);
        shiftChart.update();
    }

    function renderCoverage(c) {
        coverageChart.data.datasets[0].data = [c.sudahIsi, c.belumIsi];
        coverageChart.$pct = c.persen;
        coverageChart.update();
        $('coverageLabel').textContent = c.label;
        $('coverageNote').textContent = c.period === 'day'
            ? fmt(c.sudahIsi) + ' dari ' + fmt(c.totalKaryawan) + ' karyawan aktif sudah isi'
            : fmt(c.totalKaryawan) + ' karyawan × ' + c.hariOperasional + ' hari operasional = target ' + fmt(c.target) + ' pengisian';
    }

    function renderTop(top) {
        if (!top.length) {
            $('topBody').innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">Belum ada data</td></tr>';
            return;
        }
        $('topBody').innerHTML = top.map((t) => `
            <tr>
                <td class="text-center"><span class="ftw-rank ${t.rank <= 3 ? 'r' + t.rank : ''}">${t.rank}</span></td>
                <td><div class="fw-semibold">${esc(t.nama)}</div><div class="small text-muted">${esc(t.nip)}</div></td>
                <td>${esc(t.jabatan)}</td>
                <td>${esc(t.section)}</td>
                <td class="text-end fw-semibold">${fmt(t.jumlahHari)}</td>
                <td class="text-end">${t.tidakFit > 0 ? '<span class="badge bg-danger-subtle text-danger-emphasis">' + fmt(t.tidakFit) + '</span>' : '<span class="text-muted">0</span>'}</td>
                <td class="text-end small text-muted">${esc(t.terakhir)}</td>
            </tr>`).join('');
    }

    function statusBadge(isFit) {
        return isFit
            ? '<span class="badge bg-success">✔ FIT</span>'
            : '<span class="badge bg-danger">✖ TIDAK FIT</span>';
    }

    function renderRecent(recent) {
        if (!recent.length) {
            $('recentBody').innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">Belum ada pengisian hari ini</td></tr>';
            lastIds = new Set();
            return;
        }
        const firstLoad = lastIds.size === 0;
        $('recentBody').innerHTML = recent.map((r) => `
            <tr class="${!firstLoad && !lastIds.has(r.id) ? 'ftw-flash' : ''}">
                <td>${esc(r.jam)}</td>
                <td><div class="fw-semibold">${esc(r.nama)}</div><div class="small text-muted">${esc(r.nip)}</div></td>
                <td>${esc(r.jabatan)}</td>
                <td>${esc(r.section)}</td>
                <td><span class="small">${esc(r.jadwalKerja)}</span></td>
                <td>${statusBadge(r.isFit)}</td>
                <td><button type="button" class="btn btn-sm btn-outline-secondary" data-submission-detail="${r.id}">Detail</button></td>
            </tr>`).join('');
        lastIds = new Set(recent.map((r) => r.id));
    }

    // ---------- data ----------
    async function load() {
        try {
            const startedAt = Date.now();
            const res = await fetch(cfg.dataUrl + '?period=' + encodeURIComponent(period), {
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });
            if (res.redirected || res.status === 401 || res.status === 403) {
                window.location.reload(); // sesi login habis
                return;
            }
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const data = await res.json();
            lastData = data;
            syncClock(data, startedAt);
            renderToday(data.today, data.generatedAt);
            renderTrend(data.trend);
            renderCoverage(data.coverage);
            renderTop(data.top);
            renderRecent(data.recent);
        } catch (err) {
            console.error('[FTW] Gagal memuat dashboard', err);
        }
    }

    // Banyak karyawan bisa submit hampir bersamaan (jam masuk shift),
    // jadi refresh dikumpulkan dulu 1,5 detik supaya tidak spam request.
    function scheduleRefresh() {
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(load, 1500);
    }

    // ---------- toastr ----------
    toastr.options = {
        closeButton: true,
        newestOnTop: true,
        progressBar: true,
        positionClass: 'toast-top-right',
        preventDuplicates: false,
        showDuration: 250,
        hideDuration: 400,
        timeOut: 7000,
        extendedTimeOut: 3000,
        escapeHtml: false // isi sudah di-escape manual di bawah
    };

    function showToast(e) {
        const body =
            `<div class="fw-semibold">${esc(e.nama)}</div>` +
            `<div>${esc(e.jabatan)}</div>` +
            `<div class="ftw-toast-meta">${esc(e.section)} · ${esc(e.jadwalKerja)} · ${esc(e.jam)}</div>`;

        if (e.isFit) {
            toastr.success(body, '✔ FIT — Register Fit To Work');
        } else {
            // Tidak fit ditahan lebih lama supaya tidak terlewat
            toastr.error(body, '✖ TIDAK FIT — Perlu perhatian', { timeOut: 20000, extendedTimeOut: 10000 });
        }
    }

    // ---------- realtime ----------
    // Koneksi SignalR dipegang dashboard-hub.js (dipakai bersama bagian Klinik & Gym)
    function connect() {
        AKP_HUB.on('FtwRegistered', (e) => {
            showToast(e);
            scheduleRefresh();
        });
        AKP_HUB.on('DashboardRefresh', scheduleRefresh);
        AKP_HUB.onReconnected(load);
    }

    // ---------- init ----------
    document.addEventListener('DOMContentLoaded', async () => {
        buildCharts();

        // Dibagikan ke dashboard-pdf.js (export PDF) tanpa membuat variabel global lain
        window.FTW_DASH = {
            getData: () => lastData,
            charts: { trend: trendChart, shift: shiftChart, coverage: coverageChart },
            siteNowText: () => {
                const d = siteNow();
                return `${pad(d.getUTCDate())}/${pad(d.getUTCMonth() + 1)}/${d.getUTCFullYear()} ` +
                       `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())} ${clock.zona}`;
            },
            siteNowStamp: () => {
                const d = siteNow();
                return `${d.getUTCFullYear()}${pad(d.getUTCMonth() + 1)}${pad(d.getUTCDate())}_` +
                       `${pad(d.getUTCHours())}${pad(d.getUTCMinutes())}`;
            }
        };

        document.querySelectorAll('[data-period]').forEach((btn) => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('[data-period]').forEach((b) => b.classList.remove('active'));
                btn.classList.add('active');
                period = btn.dataset.period;
                load();
            });
        });

        document.querySelectorAll('[data-bar-mode]').forEach((btn) => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('[data-bar-mode]').forEach((b) => b.classList.remove('active'));
                btn.classList.add('active');
                setBarMode(btn.dataset.barMode === 'stacked');
            });
        });

        connect();
        await load();

        // Fallback + pergantian hari: refresh tiap 60 detik walau tidak ada event
        setInterval(load, 60000);
    });
})();
