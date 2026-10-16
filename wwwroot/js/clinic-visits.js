/* Daftar Kunjungan Klinik (server-side DataTables) */
(function () {
    'use strict';
    const cfg = window.FTW_VISIT;
    const $id = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');
    const cut = (s, n) => { s = String(s ?? ''); return s.length > n ? s.slice(0, n) + '…' : s; };

    const filter = $id('filter');
    let lastFiltered = 0;

    const table = jQuery('#visitTable').DataTable({
        serverSide: true,
        processing: true,
        searchDelay: 400,
        pageLength: 25,
        order: [[0, 'desc']],
        ajax: {
            url: cfg.dataUrl,
            data: (d) => { new FormData(filter).forEach((v, k) => { d[k] = v; }); },
            dataSrc: (json) => {
                lastFiltered = json.recordsFiltered || 0;
                $id('summary').textContent = json.recordsFiltered ? `${fmt(json.recordsFiltered)} kunjungan · ${fmt(json.karyawan)} karyawan` : '';
                return json.data;
            },
            error: (xhr) => {
                if (xhr.status === 401 || xhr.status === 403 || (xhr.responseText || '').includes('<form')) { window.location.reload(); return; }
                toastr.error('Gagal memuat data kunjungan.');
            }
        },
        createdRow: (tr, row) => { if (row.isCancelled) tr.classList.add('text-muted', 'text-decoration-line-through'); },
        columns: [
            { data: 'waktu', className: 'text-nowrap' },
            { data: 'nip', render: esc },
            { data: 'nama', render: (v, t, r) => `<a href="${cfg.recordUrl}${r.employeeId}" class="fw-semibold text-decoration-none">${esc(v)}</a>` },
            { data: 'department', render: esc },
            { data: 'keluhan', render: (v) => `<span title="${esc(v)}">${esc(cut(v, 60))}</span>` },
            { data: 'diagnosa', render: (v) => esc(v || '-') },
            { data: 'rekomendasi', render: (v, t, r) => r.isCancelled ? '<span class="badge bg-secondary">Dibatalkan</span>' : `<span class="badge ${esc(r.rekomendasiClass)}">${esc(v)}</span>` },
            { data: null, orderable: false, className: 'text-end', render: (r) => `<a class="btn btn-sm btn-outline-secondary" href="${cfg.detailUrl}${r.id}">Detail</a>` }
        ],
        language: {
            search: 'Cari:', searchPlaceholder: 'NIP / nama / keluhan / penyakit…', lengthMenu: 'Tampilkan _MENU_ data',
            info: 'Menampilkan _START_ - _END_ dari _TOTAL_ kunjungan', infoEmpty: 'Tidak ada data',
            infoFiltered: '(disaring dari _MAX_ total)', zeroRecords: 'Tidak ada kunjungan yang cocok',
            processing: '<div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…', paginate: { previous: '‹', next: '›' }
        }
    });

    filter.addEventListener('change', () => table.ajax.reload());

    $id('btnExportExcel').addEventListener('click', () => {
        if (!lastFiltered) { toastr.info('Tidak ada data untuk di-download.'); return; }
        const p = new URLSearchParams(new FormData(filter));
        const o = table.order()[0] || [0, 'desc'];
        p.set('search', table.search()); p.set('sort', o[0]); p.set('dir', o[1]);
        window.location.href = `${cfg.exportUrl}?${p}`;
    });
})();
