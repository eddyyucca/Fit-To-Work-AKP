/*
 * Modal detail pengisian (dipakai Dashboard & Riwayat Pengisian).
 * Butuh partial Views/Shared/_SubmissionDetailModal.cshtml di halaman yang sama.
 *
 * Tombol apa pun dengan atribut data-submission-detail="<id>" otomatis membuka modal ini,
 * termasuk tombol yang baru dibuat belakangan (baris tabel DataTables, update realtime, dll)
 * karena event dipasang sekali di document (event delegation).
 */
(function () {
    'use strict';

    const modalEl = document.getElementById('submissionDetailModal');
    if (!modalEl) {
        console.warn('[FTW] Partial _SubmissionDetailModal belum dipasang di halaman ini.');
        return;
    }

    // URL dari partial berakhiran "/0" -> dibuang, nanti diganti id yang diklik
    const jsonUrl = modalEl.dataset.jsonUrl.replace(/\/0$/, '');
    const pageUrl = modalEl.dataset.pageUrl.replace(/\/0$/, '');
    const $ = (id) => document.getElementById(id);
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    const FIELDS = [
        ['Waktu Pengisian', 'waktu'],
        ['NIP', 'nip'],
        ['Section', 'section'],
        ['Nomor HP', 'nomorHp'],
        ['Jadwal Kerja', 'jadwalKerja'],
        ['Area Kerja', 'areaKerja'],
        ['Jam Tidur', 'jamTidur'],
        ['Kualitas Tidur', 'kualitasTidur'],
        ['Gejala Kesehatan', 'gejalaKesehatan'],
        ['Kondisi Fokus & Pikiran', 'kondisiFokus'],
        ['Konsumsi Obat 12 Jam Terakhir', 'konsumsiObat']
    ];

    let modal = null;
    let currentId = null;

    async function open(id) {
        currentId = String(id);
        modal = modal || new bootstrap.Modal(modalEl);

        $('submissionDetailTitle').textContent = 'Memuat…';
        $('submissionDetailSubtitle').textContent = '';
        $('submissionDetailStatus').innerHTML = '';
        $('submissionDetailBody').innerHTML =
            '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div>Memuat detail…</div>';
        $('submissionDetailFullLink').href = `${pageUrl}/${id}`;
        modal.show();

        try {
            const res = await fetch(`${jsonUrl}/${id}`, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
            if (res.redirected || res.status === 401 || res.status === 403) {
                window.location.reload();   // sesi login habis
                return;
            }
            const d = await res.json();
            if (!res.ok) throw new Error(d.message || 'HTTP ' + res.status);
            if (currentId !== String(id)) return;   // user sudah klik detail lain sebelum yang ini selesai dimuat

            $('submissionDetailTitle').textContent = d.nama;
            $('submissionDetailSubtitle').textContent = `${d.nip} · ${d.section}`;
            $('submissionDetailStatus').innerHTML = d.isFit
                ? `<div class="alert alert-success mb-0 py-2"><strong>FIT</strong> — ${esc(d.kesiapanKerja)}</div>`
                : `<div class="alert alert-danger mb-0 py-2"><strong>TIDAK FIT</strong> — ${esc(d.kesiapanKerja)}</div>`;
            const lokasi = !d.lokasi ? '<span class="text-muted">-</span>'
                : d.mapsUrl ? `<a href="${esc(d.mapsUrl)}" target="_blank" rel="noopener">${esc(d.lokasi)}</a>` : esc(d.lokasi);
            $('submissionDetailBody').innerHTML = FIELDS.map(([label, key]) => `
                <dt class="col-sm-5">${esc(label)}</dt>
                <dd class="col-sm-7">${d[key] ? esc(d[key]) : '<span class="text-muted">-</span>'}</dd>`).join('') + `
                <dt class="col-sm-5">Lokasi Pengisian</dt>
                <dd class="col-sm-7">${lokasi}</dd>`;
        } catch (err) {
            console.error('[FTW] Gagal memuat detail', err);
            $('submissionDetailTitle').textContent = 'Detail Pengisian';
            $('submissionDetailBody').innerHTML = `<div class="alert alert-danger mb-0">Gagal memuat detail: ${esc(err.message)}</div>`;
        }
    }

    document.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-submission-detail]');
        if (!btn) return;
        e.preventDefault();
        open(btn.dataset.submissionDetail);
    });

    window.FtwSubmissionDetail = { open };
})();
