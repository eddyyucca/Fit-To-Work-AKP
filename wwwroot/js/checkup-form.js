/*
 * Form input pemeriksaan kesehatan (parameter custom + skor risiko).
 * Konfigurasi dari window.CHECKUP_CONFIG (parameter, kriteria, kategori risiko) yang diatur di menu
 * Parameter & Skor Pemkes. Perhitungan di sini hanya PRATINJAU; server menghitung ulang saat disimpan
 * (Services/CheckupScoring.cs) dengan aturan yang sama.
 */
(function () {
    'use strict';

    const cfg = window.CHECKUP_CONFIG;
    const form = document.getElementById('checkupForm');
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = (n) => Number(n).toLocaleString('id-ID', { maximumFractionDigits: 3 });
    const badgeClass = { Normal: 'bg-success', Perhatian: 'bg-warning text-dark', Risiko: 'bg-danger', Info: 'bg-info text-dark' };
    const round = (v, d) => { const f = Math.pow(10, Math.max(0, Math.min(d || 0, 3))); return Math.round(v * f) / f; };
    const setDate = window.akpSetDate || ((el, v) => { el.value = v || ''; });

    // Data karyawan terpilih (umur & jenis kelamin) untuk parameter otomatis
    let umur = form.dataset.umur !== '' && form.dataset.umur != null ? Number(form.dataset.umur) : null;
    let jk = form.dataset.jk || null;
    form.querySelector('.employee-picker').addEventListener('employee:selected', (e) => {
        umur = e.detail && e.detail.umur != null ? Number(e.detail.umur) : null;
        jk = e.detail ? e.detail.jenisKelamin || null : null;
        update();
    });

    const numberInput = (name) => { const el = form.elements[name]; return el && el.value !== '' ? Number(el.value) : null; };

    function matchNumber(p, v) {
        return p.criteria.find((c) =>
            (c.forSex == null || c.forSex === jk) && (c.min == null || v >= c.min) && (c.max == null || v <= c.max)) || null;
    }

    // Nilai satu parameter: { text, match } atau null kalau belum diisi
    function evaluate(p) {
        switch (p.type) {
            case 'Number': {
                const el = form.elements[`Values[${p.id}]`];
                if (!el || el.value === '') return null;
                const v = round(Number(el.value), p.decimals);
                if (Number.isNaN(v)) return null;
                return { text: fmt(v) + (p.unit ? ' ' + p.unit : ''), match: matchNumber(p, v) };
            }
            case 'Choice': {
                const el = form.querySelector(`input[name="Values[${p.id}]"]:checked`);
                if (!el) return null;
                const c = p.criteria.find((x) => String(x.id) === el.value) || null;
                return c ? { text: c.label, match: c } : null;
            }
            case 'Age': {
                const el = form.querySelector(`[data-param="${p.id}"] [data-auto="age"]`);
                if (el) el.textContent = umur != null ? `${umur} tahun` : 'tanggal lahir belum diisi';
                return umur != null ? { text: `${umur} tahun`, match: matchNumber(p, umur) } : null;
            }
            case 'Sex': {
                const el = form.querySelector(`[data-param="${p.id}"] [data-auto="sex"]`);
                const text = jk === 'L' ? 'Laki-laki' : jk === 'P' ? 'Perempuan' : null;
                if (el) el.textContent = text || 'belum diisi di data karyawan';
                return text ? { text, match: p.criteria.find((c) => c.match === jk) || null } : null;
            }
            case 'Bmi': {
                const bb = numberInput('BeratBadan'), tb = numberInput('TinggiBadan');
                const el = form.querySelector(`[data-param="${p.id}"] [data-auto="bmi"]`);
                if (!bb || !tb) { if (el) el.textContent = '-'; return null; }
                const v = round(bb / Math.pow(tb / 100, 2), p.decimals || 1);
                if (el) el.textContent = fmt(v);
                return { text: fmt(v), match: matchNumber(p, v) };
            }
        }
        return null;
    }

    // ---------- Temuan & follow up ----------
    const adaTemuan = document.getElementById('AdaTemuan');
    const fuFields = document.getElementById('followUpFields');
    const fuDate = form.elements['FollowUpDate'];
    const abnormalHint = document.getElementById('abnormalHint');
    let temuanTouched = adaTemuan.checked;   // kalau medic sudah memilih sendiri, jangan diubah otomatis

    const isoDate = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const suggestFollowUpDate = () => {
        if (fuDate.value) return;
        const base = form.elements['ExaminedAt'].value ? new Date(form.elements['ExaminedAt'].value) : new Date();
        base.setDate(base.getDate() + (cfg.followUpHari || 30));
        setDate(fuDate, isoDate(base));
    };
    const syncFollowUp = () => {
        fuFields.classList.toggle('d-none', !adaTemuan.checked);
        if (adaTemuan.checked) suggestFollowUpDate();
    };
    adaTemuan.addEventListener('change', () => { temuanTouched = true; syncFollowUp(); });

    // ---------- Hitung ulang semua ----------
    function update() {
        let total = 0, filled = 0, scoredCount = 0, abnormal = 0;

        cfg.parameters.forEach((p) => {
            if (p.scored) scoredCount++;
            const r = evaluate(p);
            const box = form.querySelector(`[data-result="${p.id}"]`);
            if (!box) return;
            if (!r) { box.innerHTML = ''; return; }

            if (!r.match) {
                box.innerHTML = '<span class="badge bg-secondary">di luar kriteria</span>';
                return;
            }
            const cls = badgeClass[r.match.level] || 'bg-secondary';
            if (['Perhatian', 'Risiko', 'Info'].includes(r.match.level)) abnormal++;
            if (p.scored) { total += r.match.points; filled++; }
            box.innerHTML = `<span class="badge ${cls}">${esc(r.match.label)}</span>` +
                (p.scored ? ` <span class="small text-muted text-nowrap">${r.match.points} poin</span>` : '');
        });

        const risk = filled > 0 ? cfg.riskLevels.find((x) => total >= x.min && (x.max == null || total <= x.max)) : null;
        document.getElementById('scoreTotal').textContent = filled > 0 ? total : '-';
        const riskEl = document.getElementById('scoreRisk');
        riskEl.className = 'badge fs-6 ' + (risk ? risk.cls : '');
        riskEl.textContent = risk ? risk.label : '';
        document.getElementById('scoreDesc').textContent = risk && risk.description ? risk.description : '';
        document.getElementById('scoreFilled').textContent = scoredCount ? `${filled} dari ${scoredCount} parameter berpoin terisi` : '';

        // Ada nilai di luar normal / risiko tinggi -> sarankan follow up (selama medic belum memilih sendiri)
        const needFollowUp = abnormal > 0 || (risk && ['bg-orange', 'bg-danger'].includes(risk.cls));
        abnormalHint.textContent = abnormal ? `${abnormal} nilai di luar normal` : '';
        abnormalHint.className = abnormal ? 'small text-danger fw-semibold' : 'small text-muted';
        if (!temuanTouched) { adaTemuan.checked = !!needFollowUp; syncFollowUp(); }
    }

    form.querySelectorAll('.param-input').forEach((el) => {
        el.addEventListener('input', update);
        el.addEventListener('change', update);
    });
    form.querySelectorAll('.param-clear').forEach((btn) => btn.addEventListener('click', () => {
        btn.closest('.param-row').querySelectorAll('input[type="radio"]').forEach((r) => { r.checked = false; });
        update();
    }));
    form.elements['ExaminedAt'].addEventListener('change', update);

    update();
    syncFollowUp();

    form.addEventListener('submit', (e) => {
        if (!form.elements['EmployeeId'].value) {
            e.preventDefault();
            toastr.warning('Pilih karyawan terlebih dahulu.');
            return;
        }
        if (adaTemuan.checked && (!form.elements['TemuanKeterangan'].value.trim() || !fuDate.value)) {
            e.preventDefault();
            toastr.warning('Isi keterangan temuan dan tanggal follow up.');
        }
    });
})();
