/*
 * Riwayat Aktivitas Kebugaran (gym, lari, hiking, dll)
 * - DataTables serverSide: hanya 1 halaman yang diambil dari /Admin/Gym/Data
 * - Filter (tanggal, aktivitas, status, department, jabatan) ikut dikirim di setiap request
 * - Tombol peta di kolom Jarak membuka rute GPS (route-map.js + _RouteModal)
 * - Download Excel memakai filter, pencarian & urutan yang sama dengan tabel
 */
(function () {
    'use strict';

    const cfg = window.FTW_GYM;
    const $id = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');
    const fmtKg = (n) => Number(n || 0).toLocaleString('id-ID', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

    const filter = $id('gymFilter');
    let lastFiltered = 0;

    // ---------------- Tabel ----------------
    const table = jQuery('#gymTable').DataTable({
        serverSide: true,
        processing: true,
        searchDelay: 400,
        pageLength: 25,
        lengthMenu: [10, 25, 50, 100],
        order: [[0, 'desc']],
        ajax: {
            url: cfg.dataUrl,
            data: (d) => {
                new FormData(filter).forEach((value, key) => { d[key] = value; });
            },
            dataSrc: (json) => {
                lastFiltered = json.recordsFiltered || 0;
                const parts = [`${fmt(json.recordsFiltered)} sesi`, `${fmt(json.karyawan)} karyawan`];
                if (json.rataDurasi) parts.push(`rata-rata ${esc(json.rataDurasi)}`);
                if (json.totalJarak) parts.push(`total ${esc(json.totalJarak)}`);
                if (json.tidakCheckout) parts.push(`<span class="text-warning-emphasis">${fmt(json.tidakCheckout)} tidak diselesaikan</span>`);
                $id('gymSummary').innerHTML = json.recordsFiltered ? parts.join(' · ') : '&nbsp;';
                $id('gymLive').textContent = `🟢 ${fmt(json.sedangDiGym)} orang sedang beraktivitas`;
                return json.data;
            },
            error: (xhr) => {
                if (xhr.status === 401 || xhr.status === 403 || (xhr.responseText || '').includes('<form')) {
                    window.location.reload();
                    return;
                }
                toastr.error('Gagal memuat riwayat aktivitas. Coba muat ulang halaman.');
            }
        },
        columns: [
            { data: 'waktu', className: 'text-nowrap' },
            { data: 'nip', render: esc },
            { data: 'nama', render: (v) => `<span class="fw-semibold">${esc(v)}</span>` },
            { data: 'department', render: (v, t, row) => `${esc(v || '-')}<div class="small text-muted">${esc(row.jabatan || '')}</div>` },
            { data: 'aktivitas', className: 'text-nowrap', render: (v, t, row) => `<span aria-hidden="true">${esc(row.ikon)}</span> ${esc(v)}` },
            { data: 'checkOut', className: 'text-nowrap', render: (v) => v ? esc(v) : '<span class="text-muted">-</span>' },
            { data: 'durasi', className: 'text-nowrap', render: (v) => v ? `<span class="fw-semibold">${esc(v)}</span>` : '<span class="text-muted">-</span>' },
            {
                data: 'jarak', className: 'text-nowrap',
                render: (v, t, row) => {
                    if (!v) return row.gps && row.status === 'Selesai' ? '<span class="text-muted small">tanpa GPS</span>' : '<span class="text-muted">-</span>';
                    const pace = row.pace ? `<div class="small text-muted">${esc(row.pace)}</div>` : '';
                    const map = row.rute
                        ? ` <button type="button" class="btn btn-link btn-sm p-0 align-baseline" data-route-id="${esc(row.id)}" title="Lihat rute di peta"><i class="bi bi-map"></i></button>`
                        : '';
                    return `<span class="fw-semibold">${esc(v)}</span>${map}${pace}`;
                }
            },
            { data: 'status', className: 'text-nowrap', render: (v, t, row) => `<span class="badge ${esc(row.statusBadge)}">${esc(v)}</span>` },
            { data: 'beratBadan', className: 'text-end', render: (v) => v == null ? '<span class="text-muted">-</span>' : esc(fmtKg(v)) },
            {
                data: 'lokasi', className: 'small',
                render: (v, t, row) => !v ? '<span class="text-muted">-</span>'
                    : row.mapsUrl ? `<a href="${esc(row.mapsUrl)}" target="_blank" rel="noopener" title="Buka di Google Maps">${esc(v)}</a>` : esc(v)
            }
        ],
        language: {
            search: 'Cari:',
            searchPlaceholder: 'NIK / nama / aktivitas…',
            lengthMenu: 'Tampilkan _MENU_ data',
            info: 'Menampilkan _START_ - _END_ dari _TOTAL_ sesi',
            infoEmpty: 'Tidak ada data',
            infoFiltered: '(disaring dari _MAX_ total sesi)',
            zeroRecords: 'Tidak ada aktivitas yang cocok dengan filter',
            processing: '<div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…',
            paginate: { previous: '‹', next: '›' }
        }
    });

    filter.addEventListener('change', () => table.ajax.reload());

    // Peta rute (modal)
    if (window.AKP_ROUTE && cfg.routeUrl) AKP_ROUTE.bindModal(cfg.routeUrl);

    // ---------------- Rentang cepat ----------------
    const pad = (n) => String(n).padStart(2, '0');
    const toIso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

    filter.querySelectorAll('[data-range]').forEach((btn) => {
        btn.addEventListener('click', () => {
            const range = btn.dataset.range;
            if (range === 'all') {
                akpSetDate($id('fFrom'), '');
                akpSetDate($id('fTo'), '');
            } else {
                // Hitung dari tanggal "hari ini" versi server (WITA), bukan jam komputer admin
                const to = new Date(cfg.defaultTo + 'T00:00:00');
                const from = new Date(to);
                from.setDate(from.getDate() - Number(range));
                akpSetDate($id('fFrom'), toIso(from));
                akpSetDate($id('fTo'), toIso(to));
            }
            table.ajax.reload();
        });
    });

    $id('btnResetFilter').addEventListener('click', () => {
        filter.reset();
        akpSetDate($id('fFrom'), cfg.defaultFrom);
        akpSetDate($id('fTo'), cfg.defaultTo);
        table.search('').order([[0, 'desc']]).ajax.reload();
    });

    // ---------------- Download Excel ----------------
    const btnExport = $id('btnExportExcel');

    btnExport.addEventListener('click', () => {
        if (!lastFiltered) {
            toastr.info('Tidak ada data untuk di-download dengan filter ini.');
            return;
        }
        if (lastFiltered > cfg.maxExportRows) {
            toastr.warning(
                `Hasil filter ${fmt(lastFiltered)} baris, maksimal ${fmt(cfg.maxExportRows)} baris per file. ` +
                'Persempit rentang tanggal lalu coba lagi.', null, { timeOut: 8000 });
            return;
        }

        const params = new URLSearchParams(new FormData(filter));
        const order = table.order()[0] || [0, 'desc'];
        params.set('search', table.search());
        params.set('sort', order[0]);
        params.set('dir', order[1]);

        window.location.href = `${cfg.exportUrl}?${params.toString()}`;

        btnExport.disabled = true;
        btnExport.querySelector('.spinner-border').classList.remove('d-none');
        toastr.info(`Menyiapkan file Excel (${fmt(lastFiltered)} baris)…`, null, { timeOut: 3000 });
        setTimeout(() => {
            btnExport.disabled = false;
            btnExport.querySelector('.spinner-border').classList.add('d-none');
        }, Math.min(3000 + lastFiltered / 10, 15000));
    });
})();
