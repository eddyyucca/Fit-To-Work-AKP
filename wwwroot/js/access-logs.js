/*
 * Log Akses (admin)
 * - DataTables serverSide dari /Admin/AccessLogs/Data
 * - Filter tanggal, user, jenis kejadian; export Excel sesuai filter
 */
(function () {
    'use strict';

    const cfg = window.FTW_LOG;
    const $id = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');

    // Warna badge per jenis kejadian
    const badgeClass = {
        Login: 'bg-success',
        LoginGagal: 'bg-danger',
        Logout: 'bg-secondary',
        AksesDitolak: 'bg-danger',
        Lihat: 'text-bg-light border',
        Export: 'bg-info text-dark',
        Ubah: 'bg-warning text-dark',
        PortalLogin: 'bg-primary',
        PortalLoginGagal: 'bg-danger',
        ResetData: 'bg-dark'
    };

    const filter = $id('logFilter');
    let lastFiltered = 0;

    const table = jQuery('#logTable').DataTable({
        serverSide: true,
        processing: true,
        searchDelay: 400,
        pageLength: 25,
        lengthMenu: [10, 25, 50, 100],
        order: [[0, 'desc']],
        ajax: {
            url: cfg.dataUrl,
            data: (d) => { new FormData(filter).forEach((value, key) => { d[key] = value; }); },
            dataSrc: (json) => {
                lastFiltered = json.recordsFiltered || 0;
                const gagal = json.gagal || 0;
                $id('logSummary').innerHTML = json.recordsFiltered
                    ? `${fmt(json.recordsFiltered)} kejadian · <span class="${gagal ? 'text-danger fw-semibold' : ''}">${fmt(gagal)} gagal/ditolak</span>`
                    : '&nbsp;';
                return json.data;
            },
            error: (xhr) => {
                if (xhr.status === 401 || xhr.status === 403 || (xhr.responseText || '').includes('<form')) {
                    window.location.reload();
                    return;
                }
                toastr.error('Gagal memuat log akses. Coba muat ulang halaman.');
            }
        },
        columns: [
            { data: 'waktu', className: 'text-nowrap' },
            { data: 'username', render: (v) => `<span class="fw-semibold">${esc(v)}</span>` },
            { data: 'eventLabel', render: (v, t, row) => `<span class="badge ${badgeClass[row.eventType] || 'bg-secondary'}">${esc(v)}</span>` },
            {
                data: 'activity',
                render: (v, t, row) => `${esc(v)}` +
                    (row.path ? `<div class="log-path">${esc(row.method || '')} ${esc(row.path)}${row.status ? ' · ' + esc(row.status) : ''}</div>` : '')
            },
            { data: 'ip', className: 'text-nowrap small', render: (v, t, row) => `<span title="${esc(row.userAgent)}">${esc(v || '-')}</span>` }
        ],
        language: {
            search: 'Cari:',
            searchPlaceholder: 'user / aktivitas / IP…',
            lengthMenu: 'Tampilkan _MENU_ data',
            info: 'Menampilkan _START_ - _END_ dari _TOTAL_ kejadian',
            infoEmpty: 'Tidak ada data',
            infoFiltered: '(disaring dari _MAX_ total kejadian)',
            zeroRecords: 'Tidak ada log yang cocok dengan filter',
            processing: '<div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…',
            paginate: { previous: '‹', next: '›' }
        }
    });

    filter.addEventListener('change', () => table.ajax.reload());

    const pad = (n) => String(n).padStart(2, '0');
    const toIso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

    filter.querySelectorAll('[data-range]').forEach((btn) => {
        btn.addEventListener('click', () => {
            const range = btn.dataset.range;
            if (range === 'all') {
                akpSetDate($id('fFrom'), '');
                akpSetDate($id('fTo'), '');
            } else {
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
        $id('fUser').value = '';
        $id('fEvent').value = '';
        akpSetDate($id('fFrom'), cfg.defaultFrom);
        akpSetDate($id('fTo'), cfg.defaultTo);
        table.search('').order([[0, 'desc']]).ajax.reload();
    });

    const btnExport = $id('btnExportExcel');
    btnExport.addEventListener('click', () => {
        if (!lastFiltered) {
            toastr.info('Tidak ada data untuk di-download dengan filter ini.');
            return;
        }
        if (lastFiltered > cfg.maxExportRows) {
            toastr.warning(`Hasil filter ${fmt(lastFiltered)} baris, maksimal ${fmt(cfg.maxExportRows)} baris per file. Persempit rentang tanggal.`,
                null, { timeOut: 8000 });
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
        setTimeout(() => {
            btnExport.disabled = false;
            btnExport.querySelector('.spinner-border').classList.add('d-none');
        }, Math.min(3000 + lastFiltered / 10, 15000));
    });
})();
