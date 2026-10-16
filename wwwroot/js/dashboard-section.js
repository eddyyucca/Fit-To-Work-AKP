/*
 * Dashboard Fit To Work - Pengisian per Section
 * - Data dari /Admin/DashboardSection?period=day|week|month
 * - Bar chart Target vs Sudah Isi + garis Achievement %, tabel bisa diurutkan (default achievement terendah)
 * - Klik baris -> modal daftar karyawan yang belum isi (/Admin/DashboardSectionDetail)
 * - Ikut refresh realtime saat ada pengisian FTW baru (event SignalR), fallback tiap 60 detik
 * - Data terakhir dibagikan ke dashboard-pdf.js lewat window.FTW_SECTION
 */
(function () {
    'use strict';

    const cfg = window.FTW_DASHBOARD;
    const LEVEL_COLOR = { hijau: '#1baf7a', kuning: '#eda100', merah: '#e34948' };
    const TARGET_COLOR = '#dee2e6';
    const LINE_COLOR = '#1f3864';

    let period = 'day';
    let data = null;                              // respons terakhir
    let sort = { key: 'persen', dir: 'asc' };     // default: section yang tertinggal di atas
    let chart = null;
    let refreshTimer = null;
    let detailModal = null;

    const $ = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');
    const pct = (n) => Number(n || 0).toLocaleString('id-ID', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + '%';

    // ---------- chart ----------
    function buildChart() {
        chart = new Chart($('chartSection'), {
            type: 'bar',
            data: {
                labels: [],
                datasets: [
                    {
                        label: 'Target (jumlah karyawan)',
                        data: [],
                        backgroundColor: TARGET_COLOR,
                        borderRadius: 3,
                        maxBarThickness: 28,
                        order: 2
                    },
                    {
                        label: 'Sudah isi',
                        data: [],
                        backgroundColor: [],
                        borderRadius: 3,
                        maxBarThickness: 28,
                        order: 1
                    },
                    {
                        type: 'line',
                        label: 'Achievement %',
                        data: [],
                        yAxisID: 'y1',
                        borderColor: LINE_COLOR,
                        backgroundColor: LINE_COLOR,
                        borderWidth: 2,
                        pointRadius: 3,
                        tension: 0.2,
                        order: 0
                    }
                ]
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { position: 'bottom', labels: { usePointStyle: true } },
                    tooltip: {
                        callbacks: {
                            title: (items) => {
                                const r = currentRows()[items[0].dataIndex];
                                return r ? `${r.section} · ${r.department}` : items[0].label;
                            },
                            label: (ctx) => ctx.dataset.yAxisID === 'y1'
                                ? ' Achievement: ' + pct(ctx.parsed.y)
                                : ' ' + ctx.dataset.label + ': ' + fmt(ctx.parsed.y)
                        }
                    },
                    datalabels: { display: false }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        ticks: {
                            autoSkip: false,
                            maxRotation: 60,
                            minRotation: 0,
                            font: { size: 10 },
                            // Nama section panjang dipotong supaya chart tetap rapi (nama lengkap ada di tooltip)
                            callback: function (v) {
                                const s = this.getLabelForValue(v);
                                return s.length > 16 ? s.slice(0, 15) + '…' : s;
                            }
                        }
                    },
                    y: { beginAtZero: true, grace: '8%', grid: { color: 'rgba(0,0,0,.06)' }, ticks: { precision: 0 } },
                    y1: {
                        position: 'right',
                        min: 0,
                        max: 100,
                        grid: { display: false },
                        ticks: { callback: (v) => v + '%' }
                    }
                },
                onClick: (evt, elements) => {
                    if (!elements.length) return;
                    const r = currentRows()[elements[0].index];
                    if (r) openDetail(r);
                }
            }
        });
    }

    // ---------- urutkan ----------
    function currentRows() {
        if (!data) return [];
        const k = sort.key;
        const dir = sort.dir === 'asc' ? 1 : -1;
        return [...data.items].sort((a, b) => {
            const va = a[k], vb = b[k];
            const c = typeof va === 'string'
                ? va.localeCompare(vb, 'id', { sensitivity: 'base' })
                : (va - vb);
            return c !== 0 ? c * dir : a.section.localeCompare(b.section, 'id');
        });
    }

    function updateSortHeaders() {
        document.querySelectorAll('#sectionTable th[data-sort]').forEach((th) => {
            th.classList.remove('asc', 'desc');
            if (th.dataset.sort === sort.key) th.classList.add(sort.dir);
        });
    }

    // ---------- render ----------
    function achBadge(persen, level) {
        return `<span class="badge ftw-ach ${esc(level)}">${pct(persen)}</span>`;
    }

    function render() {
        if (!data) return;
        const rows = currentRows();
        const multiDay = data.period !== 'day';

        $('sectionLabel').textContent = data.label;
        $('sectionNote').textContent = multiDay
            ? `Target = jumlah karyawan × ${data.hariOperasional} hari operasional (hari yang ada pengisiannya). 1 karyawan dihitung 1x per hari.`
            : 'Target = jumlah karyawan aktif di section. 1 karyawan dihitung 1x per hari (status terakhir).';

        // Chart mengikuti urutan tabel
        chart.data.labels = rows.map((r) => r.section);
        chart.data.datasets[0].label = multiDay ? 'Target (karyawan × hari)' : 'Target (jumlah karyawan)';
        chart.data.datasets[0].data = rows.map((r) => r.target);
        chart.data.datasets[1].data = rows.map((r) => r.sudahIsi);
        chart.data.datasets[1].backgroundColor = rows.map((r) => LEVEL_COLOR[r.level] || LEVEL_COLOR.merah);
        chart.data.datasets[2].data = rows.map((r) => r.persen);
        chart.update();

        if (!rows.length) {
            $('sectionBody').innerHTML = '<tr><td colspan="9" class="text-center text-muted py-4">Belum ada karyawan aktif</td></tr>';
            $('sectionFoot').innerHTML = '';
            return;
        }

        // Mingguan/bulanan: jumlah karyawan + target (karyawan × hari) supaya angkanya bisa dibandingkan
        const jumlahCell = (r) => multiDay
            ? `${fmt(r.jumlahKaryawan)}<div class="small text-muted">target ${fmt(r.target)}</div>`
            : fmt(r.jumlahKaryawan);

        $('sectionBody').innerHTML = rows.map((r) => `
            <tr data-section-id="${r.sectionId}" title="Klik untuk melihat karyawan yang belum isi">
                <td class="small">${esc(r.department)}</td>
                <td class="fw-semibold">${esc(r.section)}</td>
                <td class="text-end">${jumlahCell(r)}</td>
                <td class="text-end">${fmt(r.sudahIsi)}</td>
                <td class="text-end">${r.belumIsi > 0 ? fmt(r.belumIsi) : '<span class="text-muted">0</span>'}</td>
                <td class="text-end text-success">${fmt(r.fit)}</td>
                <td class="text-end">${r.tidakFit > 0 ? '<span class="badge bg-danger-subtle text-danger-emphasis">' + fmt(r.tidakFit) + '</span>' : '<span class="text-muted">0</span>'}</td>
                <td class="text-end" style="min-width:120px">
                    ${achBadge(r.persen, r.level)}
                    <div class="ftw-ach-bar mt-1"><div style="width:${Math.min(r.persen, 100)}%;background:${LEVEL_COLOR[r.level]}"></div></div>
                </td>
                <td class="text-end"><i class="bi bi-chevron-right text-muted"></i></td>
            </tr>`).join('');

        const t = data.total;
        $('sectionFoot').innerHTML = `
            <tr>
                <td colspan="2">Total (${fmt(rows.length)} section)</td>
                <td class="text-end">${fmt(t.jumlahKaryawan)}${multiDay ? `<div class="small text-muted fw-normal">target ${fmt(t.target)}</div>` : ''}</td>
                <td class="text-end">${fmt(t.sudahIsi)}</td>
                <td class="text-end">${fmt(t.belumIsi)}</td>
                <td class="text-end">${fmt(t.fit)}</td>
                <td class="text-end">${fmt(t.tidakFit)}</td>
                <td class="text-end">${achBadge(t.persen, t.level)}</td>
                <td></td>
            </tr>`;
        updateSortHeaders();
    }

    // ---------- drill-down ----------
    async function openDetail(r) {
        $('sectionDetailTitle').textContent = `Belum Isi — ${r.section}`;
        $('sectionDetailSub').textContent = `${r.department} · ${data.label}`;
        $('sectionDetailBody').innerHTML = '<div class="text-center text-muted py-5"><div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…</div>';
        detailModal.show();
        try {
            const url = `${cfg.sectionDetailUrl}?sectionId=${encodeURIComponent(r.sectionId)}&period=${encodeURIComponent(period)}`;
            const res = await fetch(url, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
            if (res.redirected || res.status === 401 || res.status === 403) { window.location.reload(); return; }
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const d = await res.json();
            const multiDay = d.period !== 'day';

            if (!d.items.length) {
                $('sectionDetailBody').innerHTML = `<div class="alert alert-success m-3 mb-3">Semua ${fmt(d.jumlahKaryawan)} karyawan sudah isi${multiDay ? ' setiap hari operasional' : ''}. 👍</div>`;
                return;
            }
            $('sectionDetailBody').innerHTML = `
                <div class="px-3 pt-3 small text-muted">
                    ${fmt(d.items.length)} dari ${fmt(d.jumlahKaryawan)} karyawan ${multiDay ? `belum isi lengkap (${d.hariOperasional} hari operasional)` : 'belum isi hari ini'}
                </div>
                <div class="table-responsive">
                    <table class="table table-sm mb-0 align-middle">
                        <thead class="table-light"><tr>
                            <th class="ps-3">Nama</th><th>Jabatan</th>
                            ${multiDay ? '<th class="text-end">Hari Isi</th>' : ''}
                            <th class="text-end pe-3">Pengisian Terakhir</th>
                        </tr></thead>
                        <tbody>${d.items.map((e) => `
                            <tr>
                                <td class="ps-3"><div class="fw-semibold">${esc(e.nama)}</div><div class="small text-muted">${esc(e.nip)}</div></td>
                                <td>${esc(e.jabatan)}</td>
                                ${multiDay ? `<td class="text-end">${fmt(e.hariIsi)} / ${d.hariOperasional}</td>` : ''}
                                <td class="text-end pe-3 small text-muted">${esc(e.terakhir)}</td>
                            </tr>`).join('')}
                        </tbody>
                    </table>
                </div>`;
        } catch (err) {
            console.error('[FTW] Gagal memuat detail section', err);
            $('sectionDetailBody').innerHTML = '<div class="alert alert-danger m-3">Gagal memuat daftar karyawan. Periksa koneksi lalu coba lagi.</div>';
        }
    }

    // ---------- data ----------
    async function load() {
        try {
            const res = await fetch(cfg.sectionUrl + '?period=' + encodeURIComponent(period), {
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });
            if (res.redirected || res.status === 401 || res.status === 403) return;   // dashboard.js yang me-reload halaman
            if (!res.ok) throw new Error('HTTP ' + res.status);
            data = await res.json();
            render();
        } catch (err) {
            console.error('[FTW] Gagal memuat pengisian per section', err);
        }
    }

    function scheduleRefresh() {
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(load, 1500);
    }

    // ---------- init ----------
    document.addEventListener('DOMContentLoaded', () => {
        if (!$('chartSection')) return;   // admin tanpa hak dashboard FTW
        buildChart();
        detailModal = new bootstrap.Modal($('sectionDetailModal'));

        window.FTW_SECTION = { getData: () => data, getRows: currentRows, chart };

        document.querySelectorAll('[data-section-period]').forEach((btn) => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('[data-section-period]').forEach((b) => b.classList.remove('active'));
                btn.classList.add('active');
                period = btn.dataset.sectionPeriod;
                load();
            });
        });

        document.querySelectorAll('#sectionTable th[data-sort]').forEach((th) => {
            th.addEventListener('click', () => {
                const key = th.dataset.sort;
                const isText = key === 'section' || key === 'department';
                sort = sort.key === key
                    ? { key, dir: sort.dir === 'asc' ? 'desc' : 'asc' }
                    : { key, dir: isText || key === 'persen' ? 'asc' : 'desc' };
                render();
            });
        });

        $('sectionBody').addEventListener('click', (e) => {
            const tr = e.target.closest('tr[data-section-id]');
            if (!tr || !data) return;
            const r = data.items.find((x) => String(x.sectionId) === tr.dataset.sectionId);
            if (r) openDetail(r);
        });

        if (window.AKP_HUB) {
            AKP_HUB.on('FtwRegistered', scheduleRefresh);
            AKP_HUB.on('DashboardRefresh', scheduleRefresh);
            AKP_HUB.onReconnected(load);
        }

        load();
        setInterval(load, 60000);
    });
})();
