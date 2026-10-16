/*
 * Fit To Work › Pengaturan Pengisian (admin)
 * - Pilih cara reset: "Jeda" (jam + menit sejak pengisian terakhir) atau "JamTetap" (daftar jam reset).
 * - Tambah / hapus jam reset (maks. cfg.maxJam).
 * - Pratinjau langsung "isi pukul X → bisa isi lagi Y" dengan logika yang sama seperti FtwRules.NextOpenLocal
 *   (server tetap sumber kebenaran; ini hanya bantuan visual sebelum disimpan).
 */
(function () {
    'use strict';

    const cfg = window.AKP_FTW_SETTINGS || { maxJam: 6 };
    const form = document.getElementById('ftwSettingsForm');
    if (!form) return;

    const list = document.getElementById('jamResetList');
    const addBtn = document.getElementById('addJamReset');
    const tpl = document.getElementById('jamResetTemplate');
    const jedaJam = form.querySelector('[name="JedaJam"]');
    const jedaMenit = form.querySelector('[name="JedaMenit"]');
    const previewTime = document.getElementById('previewTime');
    const previewResult = document.getElementById('previewResult');
    const previewTable = document.getElementById('previewTable');
    const SAMPLES = ['06:10', '07:45', '17:55', '18:30', '23:40'];
    const MIN_JEDA = 30, MAX_JEDA = 72 * 60;

    const pad = (n) => String(n).padStart(2, '0');
    const toMin = (v) => {
        const m = /^(\d{1,2}):(\d{2})/.exec(v || '');
        if (!m) return null;
        const h = +m[1], mi = +m[2];
        return h <= 23 && mi <= 59 ? h * 60 + mi : null;
    };
    const fmt = (min) => pad(Math.floor(min / 60) % 24) + ':' + pad(min % 60);

    const mode = () => (form.querySelector('[name="Mode"]:checked') || {}).value || 'Jeda';
    const jamInputs = () => Array.from(list.querySelectorAll('input[name="JamReset"]'));
    const jedaTotal = () => (parseInt(jedaJam.value, 10) || 0) * 60 + (parseInt(jedaMenit.value, 10) || 0);

    /** Menit-sejak-tengah-malam pengisian → { min, hari } kapan dibuka lagi (hari 0 = hari yang sama). */
    function nextOpen(isiMin) {
        if (mode() === 'JamTetap') {
            const times = jamInputs().map((i) => toMin(i.value)).filter((v) => v !== null).sort((a, b) => a - b);
            if (times.length) {
                const same = times.find((t) => t > isiMin);
                return same !== undefined ? { min: same, hari: 0 } : { min: times[0], hari: 1 };
            }
        }
        const jeda = Math.min(Math.max(jedaTotal(), MIN_JEDA), MAX_JEDA);
        const total = isiMin + jeda;
        return { min: total % 1440, hari: Math.floor(total / 1440) };
    }

    function label(r) {
        if (r.hari === 0) return 'pukul ' + fmt(r.min);
        if (r.hari === 1) return 'besok pukul ' + fmt(r.min);
        return r.hari + ' hari lagi pukul ' + fmt(r.min);
    }

    function render() {
        const isJeda = mode() !== 'JamTetap';
        form.querySelectorAll('[data-mode-panel]').forEach((p) => {
            const active = p.dataset.modePanel === (isJeda ? 'Jeda' : 'JamTetap');
            p.style.opacity = active ? '' : '.5';
        });
        // Jam reset hanya wajib diisi kalau modenya JamTetap (panel lain tidak boleh menghalangi Simpan)
        jamInputs().forEach((i) => { i.required = !isJeda; });
        addBtn.disabled = jamInputs().length >= cfg.maxJam;

        const isi = toMin(previewTime.value);
        previewResult.textContent = isi === null ? '-' : 'Bisa mengisi lagi ' + label(nextOpen(isi)) + '.';

        previewTable.replaceChildren(...SAMPLES.map((s) => {
            const tr = document.createElement('tr');
            const a = document.createElement('td');
            const b = document.createElement('td');
            a.textContent = s;
            b.textContent = label(nextOpen(toMin(s)));
            tr.append(a, b);
            return tr;
        }));
    }

    addBtn.addEventListener('click', () => {
        if (jamInputs().length >= cfg.maxJam) return;
        list.appendChild(tpl.content.cloneNode(true));
        const inputs = jamInputs();
        inputs[inputs.length - 1].focus();
        render();
    });

    list.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-remove-jam]');
        if (!btn) return;
        if (jamInputs().length <= 1) {
            const only = jamInputs()[0];
            if (only) only.value = '';
        } else {
            btn.closest('.jam-reset-item').remove();
        }
        render();
    });

    form.addEventListener('input', render);
    form.addEventListener('change', render);
    previewTime.addEventListener('input', render);
    render();
})();
