/*
 * Data Karyawan
 * - Tabel pakai DataTables mode serverSide: tiap ganti halaman / cari / sortir,
 *   browser minta 1 halaman saja ke /Admin/Employees/Data (bukan semua data sekaligus).
 * - Tambah & edit karyawan lewat modal, simpan via AJAX tanpa reload halaman.
 */
(function () {
    'use strict';

    const cfg = window.FTW_EMPLOYEES;
    const $id = (id) => document.getElementById(id);
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const cell = (s) => s ? esc(s) : '<span class="text-muted">-</span>';

    toastr.options = { closeButton: true, progressBar: true, positionClass: 'toast-top-right', timeOut: 4000 };

    // POST form-urlencoded + token anti-forgery; hasilnya JSON (termasuk saat error 400/404)
    async function postForm(url, data) {
        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'RequestVerificationToken': token,
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'Accept': 'application/json'
            },
            credentials: 'same-origin',
            body: new URLSearchParams(data)
        });
        let body = {};
        try { body = await res.json(); } catch { /* respons bukan JSON, mis. halaman login */ }
        return { ok: res.ok, status: res.status, body };
    }

    // ---------------- Tabel (server-side) ----------------
    const table = jQuery('#employeesTable').DataTable({
        serverSide: true,
        processing: true,
        searchDelay: 400,          // tunggu user berhenti mengetik 0,4 detik sebelum request
        pageLength: 25,
        lengthMenu: [10, 25, 50, 100],
        order: [[1, 'asc']],       // default urut Nama A-Z
        search: { search: cfg.initialSearch || '' },
        ajax: {
            url: cfg.dataUrl,
            data: (d) => {
                d.status = $id('statusFilter').value; // filter tambahan di luar kotak search
            },
            error: (xhr) => {
                if (xhr.status === 401 || xhr.status === 403 || (xhr.responseText || '').includes('<form')) {
                    window.location.reload(); // sesi login habis
                    return;
                }
                toastr.error('Gagal memuat data karyawan. Coba muat ulang halaman.');
            }
        },
        columns: [
            { data: 'nip', render: (v) => `<span class="fw-semibold">${esc(v)}</span>` },
            { data: 'nama', render: esc },
            { data: 'division', render: cell },
            { data: 'department', render: cell },
            { data: 'section', render: cell },
            { data: 'position', render: cell },
            { data: 'nomorHp', render: cell },
            {
                data: 'isActive',
                render: (v) => v
                    ? '<span class="badge bg-success">Aktif</span>'
                    : '<span class="badge bg-secondary">Nonaktif</span>'
            },
            {
                data: null,
                orderable: false,
                searchable: false,
                className: 'text-end text-nowrap',
                render: (row) => `
                    <button type="button" class="btn btn-sm btn-outline-primary js-detail" data-id="${row.id}">Detail</button>
                    <button type="button" class="btn btn-sm btn-outline-secondary js-edit" data-id="${row.id}">Edit</button>
                    <button type="button" class="btn btn-sm ${row.isActive ? 'btn-outline-danger' : 'btn-outline-success'} js-toggle"
                            data-id="${row.id}" data-activate="${!row.isActive}">
                        ${row.isActive ? 'Nonaktifkan' : 'Aktifkan'}
                    </button>`
            }
        ],
        language: {
            search: 'Cari:',
            searchPlaceholder: 'NIP / nama / section / jabatan…',
            lengthMenu: 'Tampilkan _MENU_ data',
            info: 'Menampilkan _START_ - _END_ dari _TOTAL_ karyawan',
            infoEmpty: 'Tidak ada data',
            infoFiltered: '(disaring dari _MAX_ total karyawan)',
            zeroRecords: 'Karyawan tidak ditemukan',
            processing: '<div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…',
            paginate: { previous: '‹', next: '›' }
        }
    });

    $id('statusFilter').addEventListener('change', () => table.ajax.reload());

    // Download Excel: status, kata kunci pencarian & urutan yang sedang aktif ikut dikirim
    $id('btnExportEmployees').addEventListener('click', () => {
        const order = table.order()[0] || [1, 'asc'];
        const params = new URLSearchParams({
            status: $id('statusFilter').value,
            search: table.search(),
            sort: order[0],
            dir: order[1]
        });
        window.location.href = `${cfg.exportUrl}?${params.toString()}`;
        toastr.info('Menyiapkan file Excel data karyawan…', null, { timeOut: 2500 });
    });

    // reload(null, false) = ambil ulang data tapi tetap di halaman yang sama
    const refreshTable = () => table.ajax.reload(null, false);

    // ---------------- Modal form ----------------
    const modalEl = $id('employeeModal');
    const modal = new bootstrap.Modal(modalEl);
    const form = $id('employeeForm');
    const btnSave = $id('btnSaveEmployee');
    const formError = $id('employeeFormError');

    function clearErrors() {
        formError.classList.add('d-none');
        formError.textContent = '';
        form.querySelectorAll('.is-invalid').forEach((el) => el.classList.remove('is-invalid'));
        form.querySelectorAll('[data-error-for]').forEach((el) => { el.textContent = ''; });
    }

    function showErrors(message, errors) {
        let firstInvalid = null;
        Object.entries(errors || {}).forEach(([field, messages]) => {
            const input = form.elements[field];
            const feedback = form.querySelector(`[data-error-for="${field}"]`);
            if (input && feedback) {
                input.classList.add('is-invalid');
                feedback.textContent = messages.join(' ');
                firstInvalid = firstInvalid || input;
            }
        });
        // Error yang tidak punya field (atau field-nya tidak tampil) muncul di kotak merah atas
        if (!firstInvalid || !errors) {
            formError.textContent = message || 'Terjadi kesalahan.';
            formError.classList.remove('d-none');
        }
        (firstInvalid || formError).focus?.();
    }

    function fillForm(data) {
        form.elements['Id'].value = data.id || 0;
        form.elements['NIP'].value = data.nip || '';
        form.elements['Nama'].value = data.nama || '';
        form.elements['NomorHp'].value = data.nomorHp || '';
        form.elements['DivisionId'].value = data.divisionId ?? '';
        form.elements['DepartmentId'].value = data.departmentId ?? '';
        form.elements['SectionId'].value = data.sectionId ?? '';
        form.elements['PositionId'].value = data.positionId ?? '';
        akpSetDate(form.elements['TanggalLahir'], data.tanggalLahir || '');
        form.elements['JenisKelamin'].value = data.jenisKelamin || '';
        form.elements['IsActive'].checked = data.isActive ?? true;
    }

    function setSaving(saving) {
        btnSave.disabled = saving;
        btnSave.querySelector('.spinner-border').classList.toggle('d-none', !saving);
    }

    function openAdd() {
        clearErrors();
        form.reset();
        fillForm({ id: 0, isActive: true });
        $id('employeeModalTitle').textContent = 'Tambah Karyawan';
        modal.show();
    }

    async function openEdit(id) {
        clearErrors();
        try {
            const res = await fetch(`${cfg.getUrl}/${id}`, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
            const data = await res.json();
            if (!res.ok) {
                toastr.error(data.message || 'Data karyawan tidak bisa dibuka.');
                refreshTable();
                return;
            }
            fillForm(data);
            $id('employeeModalTitle').textContent = `Edit Karyawan — ${data.nama}`;
            modal.show();
        } catch (err) {
            console.error(err);
            toastr.error('Gagal mengambil data karyawan.');
        }
    }

    modalEl.addEventListener('shown.bs.modal', () => form.elements['NIP'].focus());

    // Hapus tanda merah begitu user memperbaiki isian
    form.addEventListener('input', (e) => {
        if (e.target.classList.contains('is-invalid')) e.target.classList.remove('is-invalid');
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        clearErrors();

        // Validasi cepat di browser (server tetap validasi ulang)
        const required = { NIP: 'NIP wajib diisi', Nama: 'Nama wajib diisi', SectionId: 'Section wajib dipilih' };
        const localErrors = {};
        Object.entries(required).forEach(([f, msg]) => {
            if (!String(form.elements[f].value).trim()) localErrors[f] = [msg];
        });
        if (Object.keys(localErrors).length) {
            showErrors(null, localErrors);
            return;
        }

        const data = new FormData(form);
        // Checkbox yang tidak dicentang tidak ikut terkirim, jadi nilai true/false di-set manual
        data.set('IsActive', form.elements['IsActive'].checked ? 'true' : 'false');

        setSaving(true);
        try {
            const { ok, status, body } = await postForm(cfg.saveUrl, data);
            if (ok) {
                modal.hide();
                toastr.success(body.message || 'Data tersimpan.');
                refreshTable();
            } else if (status === 400) {
                showErrors(body.message, body.errors);
            } else if (status === 404) {
                modal.hide();
                toastr.error(body.message || 'Karyawan tidak ditemukan.');
                refreshTable();
            } else {
                showErrors(body.message || `Gagal menyimpan (HTTP ${status}). Coba muat ulang halaman.`);
            }
        } catch (err) {
            console.error(err);
            showErrors('Tidak bisa terhubung ke server. Periksa koneksi lalu coba lagi.');
        } finally {
            setSaving(false);
        }
    });

    $id('btnAddEmployee').addEventListener('click', openAdd);

    // ---------------- Modal detail ----------------
    // Isi modal = HTML dari server (partial _DetailContent), jadi hak akses & format tanggal tetap diatur server
    const detailEl = $id('employeeDetailModal');
    const detailModal = new bootstrap.Modal(detailEl);
    const detailBody = $id('employeeDetailBody');
    let detailId = null;

    async function openDetail(id) {
        detailId = id;
        $id('employeeDetailTitle').textContent = 'Detail Karyawan';
        detailBody.innerHTML = '<div class="text-center text-muted py-5"><div class="spinner-border spinner-border-sm text-primary me-2"></div>Memuat…</div>';
        detailModal.show();
        try {
            const res = await fetch(`${cfg.detailUrl}/${id}`, { credentials: 'same-origin', headers: { 'Accept': 'text/html' } });
            const html = await res.text();
            if (res.status === 401 || res.status === 403 || html.includes('name="Password"')) { window.location.reload(); return; }
            if (!res.ok) {
                detailBody.innerHTML = `<div class="alert alert-warning mb-0">${esc(html || 'Data karyawan tidak bisa dibuka.')}</div>`;
                return;
            }
            if (String(detailId) !== String(id)) return;   // user sudah membuka karyawan lain
            detailBody.innerHTML = html;
            const nama = detailBody.querySelector('h5');
            if (nama) $id('employeeDetailTitle').textContent = `Detail Karyawan — ${nama.textContent.trim()}`;
        } catch (err) {
            console.error(err);
            detailBody.innerHTML = '<div class="alert alert-danger mb-0">Gagal memuat detail karyawan. Periksa koneksi lalu coba lagi.</div>';
        }
    }

    // Reset password Riwayat Saya (tombol ada di isi modal detail, jadi pakai event delegation)
    detailBody.addEventListener('click', async (e) => {
        const btn = e.target.closest('.js-reset-portal');
        if (!btn) return;
        if (!window.confirm(`Reset password Riwayat Saya ${btn.dataset.nama} ke tanggal lahir?\n\nKaryawan wajib membuat password baru saat login berikutnya.`)) return;
        btn.disabled = true;
        const { ok, body } = await postForm(`${cfg.resetPortalUrl}/${btn.dataset.id}`, {});
        if (ok) {
            toastr.success(body.message, '', { timeOut: 8000 });
            openDetail(btn.dataset.id);   // muat ulang status password
        } else {
            toastr.error(body.message || 'Gagal me-reset password.');
            btn.disabled = false;
        }
    });

    $id('btnDetailEdit').addEventListener('click', () => {
        if (!detailId) return;
        const id = detailId;
        detailEl.addEventListener('hidden.bs.modal', () => openEdit(id), { once: true });
        detailModal.hide();
    });

    // /Admin/Employees?edit=5 langsung membuka modal edit
    if (cfg.editId) {
        openEdit(cfg.editId);
        history.replaceState(null, '', window.location.pathname);
    }

    // ---------------- Tombol di tiap baris (event delegation) ----------------
    // Baris tabel dibuat ulang setiap ganti halaman, jadi event dipasang di <table>, bukan di tombolnya.
    $id('employeesTable').addEventListener('click', async (e) => {
        const btnDetail = e.target.closest('.js-detail');
        if (btnDetail) {
            openDetail(btnDetail.dataset.id);
            return;
        }

        const btnEdit = e.target.closest('.js-edit');
        if (btnEdit) {
            openEdit(btnEdit.dataset.id);
            return;
        }

        const btnToggle = e.target.closest('.js-toggle');
        if (btnToggle) {
            btnToggle.disabled = true;
            const { ok, body } = await postForm(`${cfg.setActiveUrl}/${btnToggle.dataset.id}`, {
                activate: btnToggle.dataset.activate
            });
            if (ok) toastr.success(body.message);
            else toastr.error(body.message || 'Gagal mengubah status karyawan.');
            refreshTable();
        }
    });
})();
