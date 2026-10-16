/* Daftar Pemeriksaan Kesehatan (server-side DataTables) */
(function () {
    'use strict';
    const cfg = window.FTW_CHECKUP;
    const $id = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');
    const filter = $id('filter');
    let lastFiltered = 0;

    const table = jQuery('#checkupTable').DataTable({
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
                $id('summary').textContent = json.recordsFiltered ? `${fmt(json.recordsFiltered)} pemeriksaan · ${fmt(json.karyawan)} karyawan` : '';
                return json.data;
            },
            error: (xhr) => {
                if (xhr.status === 401 || xhr.status === 403 || (xhr.responseText || '').includes('<form')) { window.location.reload(); return; }
                toastr.error('Gagal memuat data pemeriksaan.');
            }
        },
        columns: [
            { data: 'waktu', className: 'nowrap' },
            { data: 'nip', render: esc, className: 'nowrap' },
            { data: 'nama', render: (v, t, r) => `<a href="${cfg.recordUrl}${r.employeeId}" class="fw-semibold text-decoration-none">${esc(v)}</a>` +
                (r.followUp ? ` <span class="badge ${esc(r.followUp.badge)}" title="Follow up ${esc(r.followUp.tanggal || '')}">FU ${esc(r.followUp.label)}</span>` : '') +
                (r.lampiran ? ` <span title="${r.lampiran} lampiran">📎</span>` : '') },
            { data: 'department', render: esc },
            { data: 'skor', className: 'text-end fw-semibold', render: (v, t, r) => v == null ? '<span class="text-muted">-</span>'
                : `${v}` + (!r.skorLengkap && r.skorInfo ? ` <span class="text-muted small" title="${esc(r.skorInfo)}">*</span>` : '') },
            { data: 'risiko', render: (v, t, r) => v ? `<span class="badge ${esc(r.risikoClass)}">${esc(v)}</span>` : '<span class="text-muted">-</span>' },
            { data: 'temuan', orderable: false, render: (list) => list && list.length
                ? list.map((x) => `<span class="badge ${esc(x.cls)} fw-normal me-1 mb-1" title="${esc(x.kriteria || '')}">${esc(x.label)}: ${esc(x.nilai)}</span>`).join('')
                : '<span class="text-muted">-</span>' },
            { data: null, orderable: false, className: 'text-end', render: (r) => `<a class="btn btn-sm btn-outline-secondary" href="${cfg.detailUrl}${r.id}">Detail</a>` }
        ],
        language: {
            search: 'Cari:', searchPlaceholder: 'NIP / nama / keluhan…', lengthMenu: 'Tampilkan _MENU_ data',
            info: 'Menampilkan _START_ - _END_ dari _TOTAL_ pemeriksaan', infoEmpty: 'Tidak ada data',
            infoFiltered: '(disaring dari _MAX_ total)', zeroRecords: 'Tidak ada pemeriksaan yang cocok',
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
