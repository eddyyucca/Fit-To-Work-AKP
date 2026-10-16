/*
 * Dashboard AKP Sehat - bagian Klinik & Aktivitas Kebugaran (dulu Gym)
 * - Data dari /Admin/DashboardClinic dan /Admin/DashboardGym (?period=day|week|month)
 * - Tombol periode masing-masing bagian terpisah dari tombol periode Fit To Work
 * - Realtime lewat SignalR (dashboard-hub.js): event ClinicChanged / GymChanged / DashboardRefresh
 */
(function () {
    'use strict';

    const cfg = window.AKP_DASH_EXTRA || {};
    if (!cfg.clinicUrl && !cfg.gymUrl) return;

    // Warna dari logo AKP Sehat (satu seri = satu warna; jam tersibuk ditandai navy)
    const C = {
        blue: '#1a8fd8',
        navy: '#1b3a6b',
        teal: '#22b3a4',
        green: '#4caf50',
        track: '#e3e8ee',
        grid: 'rgba(0,0,0,.06)',
        text: '#52514e'
    };

    const $ = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');
    const fmtDec = (n) => Number(n || 0).toLocaleString('id-ID', { maximumFractionDigits: 1 });

    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.color = C.text;

    const noLabels = { datalabels: { display: false } };   // aman walau plugin datalabels tidak dimuat

    const baseBarOptions = (horizontal) => ({
        responsive: true,
        maintainAspectRatio: false,
        indexAxis: horizontal ? 'y' : 'x',
        interaction: { mode: 'index', intersect: false },
        plugins: {
            legend: { display: false },
            ...noLabels
        },
        scales: {
            x: {
                grid: { display: horizontal, color: C.grid },
                beginAtZero: true,
                ticks: horizontal ? { precision: 0 } : { maxRotation: 0, autoSkip: true }
            },
            y: {
                grid: { display: !horizontal, color: C.grid },
                beginAtZero: true,
                ticks: horizontal ? { autoSkip: false } : { precision: 0 }
            }
        }
    });

    async function getJson(url, period) {
        const res = await fetch(`${url}?period=${encodeURIComponent(period)}`, {
            headers: { 'Accept': 'application/json' },
            credentials: 'same-origin'
        });
        if (res.redirected || res.status === 401 || res.status === 403) {
            window.location.reload();
            return null;
        }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
    }

    function bindPeriod(attr, onChange) {
        const buttons = document.querySelectorAll(`[${attr}]`);
        buttons.forEach((btn) => btn.addEventListener('click', () => {
            buttons.forEach((b) => b.classList.remove('active'));
            btn.classList.add('active');
            onChange(btn.getAttribute(attr));
        }));
    }

    // ======================= Klinik =======================
    const clinic = { period: 'day', trend: null, disease: null };

    function renderClinic(d) {
        const s = d.summary;
        $('clinicLabel').textContent = d.label;
        $('clTotal').textContent = fmt(s.totalKunjungan);
        $('clKaryawan').textContent = `${fmt(s.karyawanUnik)} karyawan berbeda`;
        $('clKembali').textContent = fmt(s.kembaliBekerja);
        $('clTerbatas').textContent = `+ ${fmt(s.kerjaTerbatas)} kerja terbatas`;
        $('clIstirahat').textContent = fmt(s.istirahat);
        $('clHari').textContent = `Total ${fmt(s.totalHariIstirahat)} hari istirahat`;
        $('clRujuk').textContent = fmt(s.rujuk);
        $('clObat').textContent = `${fmt(s.obatKeluar)} unit obat diberikan`;
        $('clTrendNote').textContent = d.period === 'day'
            ? 'Jumlah kunjungan per jam hari ini'
            : 'Jumlah kunjungan per hari · ' + d.label;

        // Riwayat kunjungan (per jam / per hari)
        const labels = d.trend.map((t) => t.label);
        const values = d.trend.map((t) => t.jumlah);
        if (!clinic.trend) {
            clinic.trend = new Chart($('chartClinicTrend'), {
                type: 'bar',
                data: { labels, datasets: [{ label: 'Sesi', data: values, backgroundColor: C.blue, borderRadius: 4, maxBarThickness: 28 }] },
                options: baseBarOptions(false)
            });
        } else {
            clinic.trend.data.labels = labels;
            clinic.trend.data.datasets[0].data = values;
            clinic.trend.update();
        }

        // Top 10 jenis penyakit (bar horizontal, diurutkan terbanyak di atas)
        const dl = d.topDiseases.map((x) => x.nama);
        const dv = d.topDiseases.map((x) => x.jumlah);
        const diseaseOpts = baseBarOptions(true);
        diseaseOpts.plugins.tooltip = {
            callbacks: {
                afterLabel: (ctx) => `${fmt(d.topDiseases[ctx.dataIndex]?.karyawan)} karyawan`
            }
        };
        if (!clinic.disease) {
            clinic.disease = new Chart($('chartTopDisease'), {
                type: 'bar',
                data: { labels: dl, datasets: [{ label: 'Kasus', data: dv, backgroundColor: C.teal, borderRadius: 4, maxBarThickness: 22 }] },
                options: diseaseOpts
            });
        } else {
            clinic.disease.data.labels = dl;
            clinic.disease.data.datasets[0].data = dv;
            clinic.disease.options.plugins.tooltip = diseaseOpts.plugins.tooltip;
            clinic.disease.update();
        }
        setEmpty(clinic.disease, dl.length === 0, 'Belum ada kunjungan pada periode ini');

        // Department terbanyak (tabel + bar kecil)
        const max = Math.max(1, ...d.topDepartments.map((x) => x.jumlah));
        $('clDeptBody').innerHTML = d.topDepartments.length
            ? d.topDepartments.map((x, i) => `
                <tr>
                    <td class="text-center text-muted">${i + 1}</td>
                    <td class="fw-semibold">${esc(x.nama)}</td>
                    <td class="text-end">${fmt(x.jumlah)}</td>
                    <td class="text-end text-muted">${fmt(x.karyawan)}</td>
                    <td><div class="progress" style="height:6px" role="presentation">
                        <div class="progress-bar" style="width:${(x.jumlah / max * 100).toFixed(1)}%;background:${C.blue}"></div></div></td>
                </tr>`).join('')
            : '<tr><td colspan="5" class="text-center text-muted py-4">Belum ada kunjungan pada periode ini</td></tr>';
    }

    async function loadClinic() {
        try {
            const d = await getJson(cfg.clinicUrl, clinic.period);
            if (d) renderClinic(d);
        } catch (e) {
            console.error('[AKP] Gagal memuat dashboard klinik', e);
        }
    }

    // ======================= Aktivitas Kebugaran =======================
    const gym = { period: 'day', hour: null, share: null };

    function renderGym(d) {
        $('gymLabel').textContent = d.label;
        $('gmTotal').textContent = fmt(d.totalKunjungan);
        $('gmRata').textContent = d.karyawanUnik
            ? `rata-rata ${fmtDec(d.rataKunjungan)}x per karyawan` + (d.rataDurasi ? ` · durasi ±${d.rataDurasi}` : '')
            : ' ';
        $('gmLive').textContent = `🟢 ${fmt(d.sedangDiGym)} orang sedang beraktivitas`;
        $('gmTidakCheckout').textContent = `${fmt(d.tidakCheckout)} tidak diselesaikan`;
        $('gmTidakCheckout').classList.toggle('d-none', !d.tidakCheckout);
        $('gmUnik').textContent = fmt(d.karyawanUnik);
        $('gmDari').textContent = `dari ${fmt(d.totalKaryawan)} karyawan aktif`;
        $('gmPersen').innerHTML = `${fmtDec(d.persen)}<sup class="fs-5">%</sup>`;
        $('gmBelum').textContent = `${fmt(d.belumKeGym)} karyawan belum berolahraga`;
        $('gmPeak').textContent = d.jamTersibuk || '-';
        $('gmPeakJumlah').textContent = d.jamTersibuk ? `${fmt(d.jamTersibukJumlah)} sesi pada jam ini` : 'Belum ada aktivitas';

        // Per jam: jam tersibuk berwarna navy, lainnya hijau
        const labels = d.perHour.map((h) => h.label);
        const values = d.perHour.map((h) => h.jumlah);
        const peakValue = Math.max(0, ...values);
        const colors = values.map((v) => (v > 0 && v === peakValue ? C.navy : C.green));
        if (!gym.hour) {
            gym.hour = new Chart($('chartGymHour'), {
                type: 'bar',
                data: { labels, datasets: [{ label: 'Sesi', data: values, backgroundColor: colors, borderRadius: 4, maxBarThickness: 26 }] },
                options: baseBarOptions(false)
            });
        } else {
            gym.hour.data.datasets[0].data = values;
            gym.hour.data.datasets[0].backgroundColor = colors;
            gym.hour.update();
        }

        // Partisipasi: ke gym vs belum
        const shareData = [d.karyawanUnik, d.belumKeGym];
        if (!gym.share) {
            gym.share = new Chart($('chartGymShare'), {
                type: 'doughnut',
                data: {
                    labels: ['Berolahraga', 'Belum'],
                    datasets: [{ data: shareData, backgroundColor: [C.teal, C.track], borderColor: '#fff', borderWidth: 2 }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '68%',
                    plugins: {
                        legend: { position: 'bottom' },
                        tooltip: { callbacks: { label: (ctx) => `${ctx.label}: ${fmt(ctx.parsed)} karyawan` } },
                        ...noLabels
                    }
                },
                plugins: [{
                    // Persentase di tengah donat
                    id: 'akpCenter',
                    afterDraw(chart) {
                        const { ctx, chartArea: a } = chart;
                        if (!a) return;
                        const pct = chart.$akpPct ?? 0;
                        ctx.save();
                        ctx.textAlign = 'center';
                        ctx.textBaseline = 'middle';
                        ctx.fillStyle = C.navy;
                        ctx.font = '700 24px ' + Chart.defaults.font.family;
                        ctx.fillText(fmtDec(pct) + '%', (a.left + a.right) / 2, (a.top + a.bottom) / 2 - 8);
                        ctx.fillStyle = '#6c757d';
                        ctx.font = '12px ' + Chart.defaults.font.family;
                        ctx.fillText('berolahraga', (a.left + a.right) / 2, (a.top + a.bottom) / 2 + 14);
                        ctx.restore();
                    }
                }]
            });
        } else {
            gym.share.data.datasets[0].data = shareData;
        }
        gym.share.$akpPct = d.persen;
        gym.share.update();

        // Per jenis aktivitas
        $('gmJarakTotal').textContent = d.totalJarak ? `Total jarak GPS: ${d.totalJarak}` : 'Belum ada jarak GPS pada periode ini';
        $('gmActBody').innerHTML = (d.perActivity || []).length
            ? d.perActivity.map((a) => `
                <tr>
                    <td class="text-nowrap"><span aria-hidden="true">${esc(a.ikon)}</span> <span class="fw-semibold">${esc(a.nama)}</span></td>
                    <td class="text-end">${fmt(a.jumlah)}</td>
                    <td class="text-end">${fmt(a.karyawan)}</td>
                    <td class="text-end text-nowrap">${esc(a.rataDurasi || '-')}</td>
                    <td class="text-end text-nowrap">${esc(a.totalJarak || '-')}</td>
                </tr>`).join('')
            : '<tr><td colspan="5" class="text-center text-muted py-4">Belum ada aktivitas pada periode ini</td></tr>';

        // Top 10
        $('gmTopBody').innerHTML = d.top.length
            ? d.top.map((t) => `
                <tr>
                    <td class="text-center"><span class="ftw-rank ${t.rank <= 3 ? 'r' + t.rank : ''}">${t.rank}</span></td>
                    <td><span class="fw-semibold">${esc(t.nama)}</span><div class="small text-muted">${esc(t.nip)}</div></td>
                    <td>${esc(t.department)}</td>
                    <td class="text-end fw-semibold">${fmt(t.jumlah)}x</td>
                    <td class="text-end text-nowrap">${esc(t.totalDurasi || '-')}</td>
                    <td class="text-end text-nowrap">${esc(t.totalJarak || '-')}</td>
                    <td class="text-end">${t.beratTerakhir != null ? fmtDec(t.beratTerakhir) + ' kg' : '-'}</td>
                </tr>`).join('')
            : '<tr><td colspan="7" class="text-center text-muted py-4">Belum ada aktivitas pada periode ini</td></tr>';
    }

    async function loadGym() {
        try {
            const d = await getJson(cfg.gymUrl, gym.period);
            if (d) renderGym(d);
        } catch (e) {
            console.error('[AKP] Gagal memuat dashboard aktivitas', e);
        }
    }

    // Tampilkan keterangan "belum ada data" di chart kosong (dan hilangkan lagi kalau data sudah ada)
    function setEmpty(chart, isEmpty, text) {
        chart.options.plugins.subtitle = { display: isEmpty, text, color: '#6c757d', padding: { top: 40 } };
        chart.update();
    }

    // Tanggal lokal saat halaman dibuka; kalau sudah ganti hari, periode "Hari ini" perlu dimuat ulang
    let loadedDay = new Date().toDateString();
    function dayChanged() {
        const today = new Date().toDateString();
        if (today === loadedDay) return false;
        loadedDay = today;
        return true;
    }

    document.addEventListener('DOMContentLoaded', () => {
        if (cfg.clinicUrl) {
            bindPeriod('data-clinic-period', (p) => { clinic.period = p; loadClinic(); });
            loadClinic();
        }
        if (cfg.gymUrl) {
            bindPeriod('data-gym-period', (p) => { gym.period = p; loadGym(); });
            loadGym();
        }
        // Realtime lewat SignalR (dashboard-hub.js). Beberapa event beruntun digabung jadi 1 kali muat ulang.
        const timers = {};
        const debounce = (key, fn) => { clearTimeout(timers[key]); timers[key] = setTimeout(fn, 1200); };

        if (window.AKP_HUB) {
            if (cfg.clinicUrl) AKP_HUB.on('ClinicChanged', () => debounce('clinic', loadClinic));
            if (cfg.gymUrl) {
                AKP_HUB.on('GymChanged', (e) => {
                    if (e && e.nama && window.toastr) toastr.info(`${esc(e.nama)} ${esc(e.aksi || 'mulai aktivitas')} pukul ${esc(e.jam)}`, '🏃 Aktivitas Kebugaran', { timeOut: 5000 });
                    debounce('gym', loadGym);
                });
            }
            AKP_HUB.on('DashboardRefresh', () => {
                if (cfg.clinicUrl) debounce('clinic', loadClinic);
                if (cfg.gymUrl) debounce('gym', loadGym);
            });
            AKP_HUB.onReconnected(() => {
                if (cfg.clinicUrl) loadClinic();
                if (cfg.gymUrl) loadGym();
            });
        }

        // Cadangan: muat ulang tiap 5 menit hanya kalau realtime sedang putus (dan untuk pergantian hari)
        setInterval(() => {
            if (document.hidden) return;
            if (window.AKP_HUB && AKP_HUB.connected && !dayChanged()) return;
            if (cfg.clinicUrl) loadClinic();
            if (cfg.gymUrl) loadGym();
        }, 300000);
    });
})();
