/*
 * Riwayat Pengisian
 * - DataTables serverSide: hanya 1 halaman yang diambil dari /Admin/History/Data
 * - Filter (tanggal, section, jadwal, kesiapan) ikut dikirim di setiap request
 * - Tombol Detail membuka modal bersama (submission-detail.js)
 */
(function () {
    'use strict';

    const cfg = window.FTW_HISTORY;
    const $id = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');
    const badge = (isFit) => isFit
        ? '<span class="badge bg-success">FIT</span>'
        : '<span class="badge bg-danger">TIDAK FIT</span>';

    const filter = $id('historyFilter');
    let lastFiltered = 0;   // jumlah data hasil filter terakhir (dipakai sebelum export)

    // ---------------- Tabel ----------------
    const table = jQuery('#historyTable').DataTable({
        serverSide: true,
        processing: true,
        searchDelay: 400,
        pageLength: 25,
        lengthMenu: [10, 25, 50, 100],
        order: [[0, 'desc']],                    // terbaru di atas
        ajax: {
            url: cfg.dataUrl,
            data: (d) => {
                // Semua isian filter ikut dikirim bersama parameter DataTables
                new FormData(filter).forEach((value, key) => { d[key] = value; });
            },
            dataSrc: (json) => {
                lastFiltered = json.recordsFiltered || 0;
                const tidakFit = json.tidakFit || 0;
                $id('historySummary').innerHTML = json.recordsFiltered
                    ? `${fmt(json.recordsFiltered)} pengisian · <span class="${tidakFit ? 'text-danger fw-semibold' : ''}">${fmt(tidakFit)} tidak fit</span>`
                    : '&nbsp;';
                return json.data;
            },
            error: (xhr) => {
                if (xhr.status === 401 || xhr.status === 403 || (xhr.responseText || '').includes('<form')) {
                    window.location.reload();
                    return;
                }
                toastr.error('Gagal memuat riwayat. Coba muat ulang halaman.');
            }
        },
        columns: [
            { data: 'waktu', className: 'text-nowrap' },
            { data: 'nip', render: esc },
            { data: 'nama', render: (v) => `<span class="fw-semibold">${esc(v)}</span>` },
            { data: 'section', render: esc },
            { data: 'jadwalKerja', render: (v) => `<span class="small">${esc(v)}</span>` },
            { data: 'isFit', render: badge },
            {
                data: null, orderable: false, searchable: false, className: 'text-end',
                render: (row) => `<button type="button" class="btn btn-sm btn-outline-secondary" data-submission-detail="${row.id}">Detail</button>`
            }
        ],
        language: {
            search: 'Cari:',
            searchPlaceholder: 'NIP / nama / section…',
            lengthMenu: 'Tampilkan _MENU_ data',
            info: 'Menampilkan _START_ - _END_ dari _TOTAL_ pengisian',
            infoEmpty: 'Tidak ada data',
            infoFiltered: '(disaring dari _MAX_ total pengisian)',
            zeroRecords: 'Tidak ada pengisian yang cocok dengan filter',
            processing: '<div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…',
            paginate: { previous: '‹', next: '›' }
        }
    });

    // Setiap filter berubah -> muat ulang dari halaman 1
    filter.addEventListener('change', () => table.ajax.reload());

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
    // Filter, kata kunci pencarian, dan urutan kolom yang sedang aktif ikut dikirim,
    // jadi isi file Excel = isi tabel di layar (semua halaman, bukan cuma halaman yang tampil).
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

        // Browser langsung men-download file karena server membalas dengan Content-Disposition: attachment
        window.location.href = `${cfg.exportUrl}?${params.toString()}`;

        // Tombol dikunci sebentar supaya tidak diklik berkali-kali selama server menyiapkan file
        btnExport.disabled = true;
        btnExport.querySelector('.spinner-border').classList.remove('d-none');
        toastr.info(`Menyiapkan file Excel (${fmt(lastFiltered)} baris)…`, null, { timeOut: 3000 });
        setTimeout(() => {
            btnExport.disabled = false;
            btnExport.querySelector('.spinner-border').classList.add('d-none');
        }, Math.min(3000 + lastFiltered / 10, 15000));
    });

    // Tombol Detail ditangani oleh submission-detail.js (atribut data-submission-detail)
})();
