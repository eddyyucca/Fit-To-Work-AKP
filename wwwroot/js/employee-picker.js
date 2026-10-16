/*
 * Pemilih karyawan (autocomplete NIP / nama) untuk form Pemeriksaan & Kunjungan Klinik.
 * Setelah dipilih, event "employee:selected" dikirim ke elemen .employee-picker dengan detail data karyawan.
 */
(function () {
    'use strict';

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    document.querySelectorAll('.employee-picker').forEach((root) => {
        const input = root.querySelector('.ep-input');
        const hidden = root.querySelector('.ep-id');
        const results = root.querySelector('.ep-results');
        const info = root.querySelector('.ep-info');
        const clearBtn = root.querySelector('.ep-clear');
        let timer = null;
        let seq = 0;

        const close = () => { results.innerHTML = ''; };

        const select = (e) => {
            hidden.value = e.id;
            input.value = `${e.nip} - ${e.nama}`;
            input.readOnly = true;
            clearBtn.classList.remove('d-none');
            info.innerHTML = [e.department, e.section, e.position,
                e.jenisKelamin === 'L' ? 'Laki-laki' : e.jenisKelamin === 'P' ? 'Perempuan' : null,
                e.umur != null ? `${e.umur} tahun` : null].filter(Boolean).map(esc).join(' · ');
            close();
            root.dispatchEvent(new CustomEvent('employee:selected', { detail: e }));
        };

        clearBtn.addEventListener('click', () => {
            hidden.value = '';
            input.value = '';
            input.readOnly = false;
            info.textContent = '';
            clearBtn.classList.add('d-none');
            input.focus();
            root.dispatchEvent(new CustomEvent('employee:selected', { detail: null }));
        });

        input.addEventListener('input', () => {
            clearTimeout(timer);
            const q = input.value.trim();
            if (q.length < 2) { close(); return; }
            timer = setTimeout(async () => {
                const my = ++seq;
                try {
                    const url = new URL(root.dataset.searchUrl, window.location.origin);   // url bisa sudah punya ?includeMitra=true
                    url.searchParams.set('q', q);
                    const res = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                    if (!res.ok) throw new Error(res.status);
                    const list = await res.json();
                    if (my !== seq) return;
                    results.innerHTML = list.length
                        ? list.map((e, i) => `<button type="button" class="list-group-item list-group-item-action" data-i="${i}">
                                <div class="fw-semibold">${esc(e.nama)} <span class="text-muted fw-normal">· ${esc(e.nip)}</span>${e.mitra ? ' <span class="badge bg-info text-dark">Mitra</span>' : ''}</div>
                                <div class="small text-muted">${esc([e.department, e.section].filter(Boolean).join(' · '))}</div>
                           </button>`).join('')
                        : '<div class="list-group-item text-muted small">Karyawan aktif tidak ditemukan</div>';
                    results.querySelectorAll('[data-i]').forEach((b) => b.addEventListener('click', () => select(list[Number(b.dataset.i)])));
                } catch {
                    results.innerHTML = '<div class="list-group-item text-danger small">Gagal mencari karyawan</div>';
                }
            }, 250);
        });

        input.addEventListener('keydown', (ev) => {
            if (ev.key === 'Enter') {
                ev.preventDefault();   // jangan submit form saat memilih karyawan
                const first = results.querySelector('[data-i]');
                if (first) first.click();
            }
            if (ev.key === 'Escape') close();
        });

        document.addEventListener('click', (ev) => { if (!root.contains(ev.target)) close(); });
    });
})();
